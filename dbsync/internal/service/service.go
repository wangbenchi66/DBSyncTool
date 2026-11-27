// Package service 是 GUI 与未来 CLI 共用的编排层；不包含第二份比对逻辑。
package service

import (
	"context"
	"crypto/rand"
	"database/sql"
	"encoding/hex"
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"sync"

	"gitee.com/wangbenchi66/dbsync/internal/data"
	"gitee.com/wangbenchi66/dbsync/internal/depgraph"
	"gitee.com/wangbenchi66/dbsync/internal/dialect/ddl"
	"gitee.com/wangbenchi66/dbsync/internal/dialect/mysql"
	"gitee.com/wangbenchi66/dbsync/internal/dialect/postgres"
	"gitee.com/wangbenchi66/dbsync/internal/dialect/sqlite"
	"gitee.com/wangbenchi66/dbsync/internal/dialect/sqlserver"
	"gitee.com/wangbenchi66/dbsync/internal/diff"
	"gitee.com/wangbenchi66/dbsync/internal/library"
	"gitee.com/wangbenchi66/dbsync/internal/model"
	"gitee.com/wangbenchi66/dbsync/internal/plan"
	"gitee.com/wangbenchi66/dbsync/internal/report"
	"gitee.com/wangbenchi66/dbsync/internal/rules"
	"gitee.com/wangbenchi66/dbsync/internal/snapshot"
)

type Endpoint struct {
	Kind   string `json:"kind"`   // mysql | snapshot
	DSN    string `json:"dsn"`    // mysql
	Path   string `json:"path"`   // snapshot
	Schema string `json:"schema"` // 库名；空则用连接默认库/快照 manifest
}

type CompareRequest struct {
	Source           Endpoint `json:"source"`
	Target           Endpoint `json:"target"`
	CompareData      bool     `json:"compareData"`
	AllowDestructive bool     `json:"allowDestructive"`
	ApplyRules       bool     `json:"applyRules"`
}

type MultiRequest struct {
	Source           Endpoint   `json:"source"`
	Targets          []Endpoint `json:"targets"`
	CompareData      bool       `json:"compareData"`
	AllowDestructive bool       `json:"allowDestructive"`
	ApplyRules       bool       `json:"applyRules"`
}

type MultiTarget struct {
	Name  string       `json:"name"`
	RunID string       `json:"runId"`
	Error string       `json:"error,omitempty"`
	View  *CompareView `json:"view,omitempty"`
}

type MultiView struct {
	Source  string        `json:"source"`
	Targets []MultiTarget `json:"targets"`
}

type CompareView struct {
	RunID        string         `json:"runId"`
	SourceName   string         `json:"sourceName"`
	TargetName   string         `json:"targetName"`
	Tables       []TableBrief   `json:"tables"`
	Views        []ObjectBrief  `json:"views"`
	Routines     []ObjectBrief  `json:"routines"`
	Triggers     []ObjectBrief  `json:"triggers"`
	Added        int            `json:"added"`
	Removed      int            `json:"removed"`
	Changed      int            `json:"changed"`
	Same         int            `json:"same"`
	HasData      bool           `json:"hasData"`
	Dependencies depgraph.Graph `json:"dependencies"`
}

type TableBrief struct {
	Name        string            `json:"name"`
	Status      string            `json:"status"`
	Columns     []ColumnBrief     `json:"columns"`
	Indexes     []IndexBrief      `json:"indexes"`
	Constraints []ConstraintBrief `json:"constraints"`
	Data        *DataBrief        `json:"data,omitempty"`
	Collation   string            `json:"collation,omitempty"`
}

type ColumnBrief struct {
	Name          string   `json:"name"`
	Status        string   `json:"status"`
	SourceType    string   `json:"sourceType"`
	TargetType    string   `json:"targetType"`
	SourceNull    bool     `json:"sourceNull"`
	TargetNull    bool     `json:"targetNull"`
	SourceDefault string   `json:"sourceDefault"`
	TargetDefault string   `json:"targetDefault"`
	Reasons       []string `json:"reasons"`
}

type IndexBrief struct {
	Name   string `json:"name"`
	Status string `json:"status"`
}

type ConstraintBrief struct {
	Name   string `json:"name"`
	Kind   string `json:"kind"`
	Status string `json:"status"`
}

type ObjectBrief struct {
	Name   string `json:"name"`
	Kind   string `json:"kind"`
	Status string `json:"status"`
}

type DataBrief struct {
	SourceCount    int        `json:"sourceCount"`
	TargetCount    int        `json:"targetCount"`
	Inserts        int        `json:"inserts"`
	Updates        int        `json:"updates"`
	Deletes        int        `json:"deletes"`
	UnsupportedKey bool       `json:"unsupportedKey"`
	Rows           []RowBrief `json:"rows"`
	Truncated      bool       `json:"truncated"`
}

type RowBrief struct {
	Kind  string      `json:"kind"` // insert/update/delete
	Key   string      `json:"key"`
	Cells []CellBrief `json:"cells"`
}

type CellBrief struct {
	Name   string `json:"name"`
	Source string `json:"source"`
	Target string `json:"target"`
}

type PlanView struct {
	RunID       string       `json:"runId"`
	Blocked     bool         `json:"blocked"`
	Fingerprint string       `json:"fingerprint"`
	Diagnostics []Diagnostic `json:"diagnostics"`
	Phases      []PhaseView  `json:"phases"`
}
type Diagnostic struct {
	Severity string `json:"severity"`
	Code     string `json:"code"`
	Object   string `json:"object"`
	Message  string `json:"message"`
}
type PhaseView struct {
	Phase      int         `json:"phase"`
	Statements []Statement `json:"statements"`
}
type Statement struct {
	SQL         string `json:"sql"`
	Object      string `json:"object"`
	Destructive bool   `json:"destructive"`
}

type ConnInfo struct {
	Version  string `json:"version"`
	Database string `json:"database"`
}

type Service struct {
	mu       sync.Mutex
	sessions map[string]*session
}

type session struct {
	source       Endpoint
	target       Endpoint
	sourceSchema *model.Schema
	targetSchema *model.Schema
	diff         *diff.SchemaDiff
	plan         *plan.Plan
	allowDrop    bool
}

func New() *Service {
	return &Service{sessions: map[string]*session{}}
}

func (s *Service) TestConnection(ctx context.Context, ep Endpoint) (ConnInfo, error) {
	switch ep.Kind {
	case "mysql":
		db, err := mysql.Open(ep.DSN)
		if err != nil {
			return ConnInfo{}, err
		}
		defer db.Close()
		v, err := mysql.Version(ctx, db)
		if err != nil {
			return ConnInfo{}, err
		}
		d, _ := mysql.Database(ctx, db)
		if ep.Schema != "" {
			d = ep.Schema
		}
		return ConnInfo{Version: v, Database: d}, nil
	case "snapshot":
		m, err := snapshot.Inspect(ep.Path)
		if err != nil {
			return ConnInfo{}, err
		}
		return ConnInfo{Version: fmt.Sprintf("snapshot v%d", m.FormatVersion), Database: m.SchemaName}, nil
	case "dir":
		s, err := ddl.ReadDir(ep.Path)
		if err != nil {
			return ConnInfo{}, err
		}
		return ConnInfo{Version: fmt.Sprintf("DDL dir · %d tables", len(s.Tables)), Database: "dir"}, nil
	case "sqlite":
		db, err := sqlite.Open(ep.Path)
		if err != nil {
			return ConnInfo{}, err
		}
		defer db.Close()
		v, err := sqlite.Version(ctx, db)
		if err != nil {
			return ConnInfo{}, err
		}
		return ConnInfo{Version: "SQLite " + v, Database: "main"}, nil
	case "postgres":
		db, err := postgres.Open(ep.DSN)
		if err != nil {
			return ConnInfo{}, err
		}
		defer db.Close()
		v, err := postgres.Version(ctx, db)
		if err != nil {
			return ConnInfo{}, err
		}
		d := ep.Schema
		if d == "" {
			d = "public"
		}
		return ConnInfo{Version: "PostgreSQL " + v, Database: d}, nil
	case "sqlserver":
		db, err := sqlserver.Open(ep.DSN)
		if err != nil {
			return ConnInfo{}, err
		}
		defer db.Close()
		v, err := sqlserver.Version(ctx, db)
		if err != nil {
			return ConnInfo{}, err
		}
		return ConnInfo{Version: v, Database: ep.Schema}, nil
	default:
		return ConnInfo{}, fmt.Errorf("未知端点类型 %q", ep.Kind)
	}
}

func (s *Service) ListDatabases(ctx context.Context, ep Endpoint) ([]string, error) {
	if ep.Kind != "mysql" {
		return nil, fmt.Errorf("只有 mysql 端点支持库列表")
	}
	db, err := mysql.Open(ep.DSN)
	if err != nil {
		return nil, err
	}
	defer db.Close()
	rows, err := db.QueryContext(ctx, "SHOW DATABASES")
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	var out []string
	for rows.Next() {
		var n string
		if err := rows.Scan(&n); err != nil {
			return nil, err
		}
		if n == "information_schema" || n == "performance_schema" || n == "mysql" || n == "sys" {
			continue
		}
		out = append(out, n)
	}
	sort.Strings(out)
	return out, rows.Err()
}

// CompareMulti 一套源对多个目标；单个目标失败不影响其他目标。
func (s *Service) CompareMulti(ctx context.Context, req MultiRequest) (*MultiView, error) {
	if len(req.Targets) == 0 {
		return nil, fmt.Errorf("至少一个目标")
	}
	out := &MultiView{Source: endpointLabel(req.Source), Targets: []MultiTarget{}}
	for _, t := range req.Targets {
		mt := MultiTarget{Name: endpointLabel(t)}
		v, err := s.Compare(ctx, CompareRequest{
			Source: req.Source, Target: t,
			CompareData: req.CompareData, AllowDestructive: req.AllowDestructive,
			ApplyRules: req.ApplyRules,
		})
		if err != nil {
			mt.Error = err.Error()
		} else {
			mt.RunID = v.RunID
			mt.View = v
		}
		out.Targets = append(out.Targets, mt)
	}
	return out, nil
}

func endpointLabel(ep Endpoint) string {
	switch ep.Kind {
	case "snapshot", "sqlite":
		return ep.Kind + ":" + filepath.Base(ep.Path)
	default:
		if ep.Schema != "" {
			return ep.Kind + ":" + ep.Schema
		}
		return ep.Kind
	}
}
func (s *Service) Compare(ctx context.Context, req CompareRequest) (*CompareView, error) {
	so, err := openEndpoint(ctx, req.Source)
	if err != nil {
		return nil, fmt.Errorf("源端: %w", err)
	}
	if so.db != nil {
		defer so.db.Close()
	}
	to, err := openEndpoint(ctx, req.Target)
	if err != nil {
		return nil, fmt.Errorf("目标端: %w", err)
	}
	if to.db != nil {
		defer to.db.Close()
	}
	d := diff.CompareSchema(so.schema, to.schema)
	if req.ApplyRules {
		if rs, err := rules.Load(); err == nil {
			rules.Filter(d, rs)
		}
	}
	if req.CompareData {
		if so.kind == "dir" || to.kind == "dir" {
			return nil, fmt.Errorf("脚本目录没有数据行，不能做数据比对；请只比较结构")
		}
		if err := attachMixed(ctx, so, to, d); err != nil {
			return nil, err
		}
		d.HasData = true
	}
	runID := newID()
	s.mu.Lock()
	s.sessions[runID] = &session{
		source: req.Source, target: req.Target,
		sourceSchema: so.schema, targetSchema: to.schema, diff: d,
		allowDrop: req.AllowDestructive,
	}
	s.mu.Unlock()
	return toCompareView(runID, d, so.schema, to.schema), nil
}

func (s *Service) Plan(ctx context.Context, runID string, allowDestructive bool) (*PlanView, error) {
	s.mu.Lock()
	sess := s.sessions[runID]
	s.mu.Unlock()
	if sess == nil {
		return nil, fmt.Errorf("运行 %s 不存在，请重新比较", runID)
	}
	if sess.source.Kind != "mysql" || sess.target.Kind != "mysql" {
		kind := sess.target.Kind
		if kind == "snapshot" || kind == "dir" {
			kind = sess.source.Kind
		}
		if kind == "snapshot" || kind == "dir" || kind == "" {
			kind = "mysql"
		}
		p := plan.BuildData(sess.diff, sess.sourceSchema, sess.targetSchema, kind, plan.Options{
			AllowDestructive: allowDestructive || sess.allowDrop,
			CompareData:      sess.diff.HasData,
		})
		s.mu.Lock()
		sess.plan = p
		s.mu.Unlock()
		return toPlanView(runID, p), nil
	}
	p := plan.Build(sess.diff, sess.sourceSchema, sess.targetSchema, plan.Options{
		AllowDestructive: allowDestructive || sess.allowDrop,
		CompareData:      sess.diff.HasData,
	})
	s.mu.Lock()
	sess.plan = p
	s.mu.Unlock()
	return toPlanView(runID, p), nil
}

// SnapshotCreate 从端点导出快照；withData=false 时只导结构。快照写入不是数据库变更。
func (s *Service) SnapshotCreate(ctx context.Context, ep Endpoint, output string, withData bool) (snapshot.Manifest, error) {
	if output == "" {
		return snapshot.Manifest{}, fmt.Errorf("输出路径为空")
	}
	o, err := openEndpoint(ctx, ep)
	if err != nil {
		return snapshot.Manifest{}, err
	}
	if o.db != nil {
		defer o.db.Close()
	}
	var dataMap map[string]snapshot.RowSet
	if withData {
		if o.db == nil {
			return snapshot.Manifest{}, fmt.Errorf("只有活库端点支持导出数据快照")
		}
		dataMap = map[string]snapshot.RowSet{}
		for i := range o.schema.Tables {
			t := &o.schema.Tables[i]
			rows, err := data.ReadRows(ctx, o.db, t)
			if err != nil {
				return snapshot.Manifest{}, fmt.Errorf("%s: %w", t.Name, err)
			}
			cols := make([]string, 0, len(t.Columns))
			for _, c := range t.Columns {
				cols = append(cols, c.Name)
			}
			dataMap[t.Name] = snapshot.RowSet{Columns: cols, Rows: rows}
		}
	}
	if err := snapshot.Write(output, o.schema, snapshot.WriteOptions{
		Source: ep.Kind + ":" + ep.Schema,
		Data:   dataMap,
	}); err != nil {
		return snapshot.Manifest{}, err
	}
	return snapshot.Inspect(output)
}

type opened struct {
	kind   string
	schema *model.Schema
	db     *sql.DB
	data   map[string]snapshot.RowSet
}

func openEndpoint(ctx context.Context, ep Endpoint) (*opened, error) {
	switch ep.Kind {
	case "mysql":
		db, err := mysql.Open(ep.DSN)
		if err != nil {
			return nil, err
		}
		schema := ep.Schema
		if schema == "" {
			schema, err = mysql.Database(ctx, db)
			if err != nil || schema == "" {
				db.Close()
				return nil, fmt.Errorf("无法确定 schema，请显式填写")
			}
		}
		s, err := mysql.Introspect(ctx, db, schema)
		if err != nil {
			db.Close()
			return nil, err
		}
		return &opened{kind: "mysql", schema: s, db: db}, nil
	case "sqlite":
		db, err := sqlite.Open(ep.Path)
		if err != nil {
			return nil, err
		}
		s, err := sqlite.Introspect(ctx, db, "main")
		if err != nil {
			db.Close()
			return nil, err
		}
		return &opened{kind: "sqlite", schema: s, db: db}, nil
	case "postgres":
		db, err := postgres.Open(ep.DSN)
		if err != nil {
			return nil, err
		}
		schema := ep.Schema
		if schema == "" {
			schema = "public"
		}
		s, err := postgres.Introspect(ctx, db, schema)
		if err != nil {
			db.Close()
			return nil, err
		}
		return &opened{kind: "postgres", schema: s, db: db}, nil
	case "sqlserver":
		db, err := sqlserver.Open(ep.DSN)
		if err != nil {
			return nil, err
		}
		schema := ep.Schema
		if schema == "" {
			schema = "dbo"
		}
		s, err := sqlserver.Introspect(ctx, db, schema)
		if err != nil {
			db.Close()
			return nil, err
		}
		return &opened{kind: "sqlserver", schema: s, db: db}, nil
	case "snapshot":
		sn, err := snapshot.Read(ep.Path)
		if err != nil {
			return nil, err
		}
		return &opened{kind: "snapshot", schema: sn.Schema, data: sn.Data}, nil
	case "dir":
		s, err := ddl.ReadDir(ep.Path)
		if err != nil {
			return nil, err
		}
		return &opened{kind: "dir", schema: s}, nil
	default:
		return nil, fmt.Errorf("未知端点类型 %q", ep.Kind)
	}
}

func rowsOf(ctx context.Context, o *opened, t *model.Table) ([]diff.Row, error) {
	if o.db != nil {
		return data.ReadRowsKind(ctx, o.db, t, o.kind)
	}
	rs, ok := o.data[t.Name]
	if !ok {
		return nil, fmt.Errorf("快照不含表 %s 的数据；请用带数据的快照", t.Name)
	}
	return rs.Rows, nil
}

func attachMixed(ctx context.Context, so, to *opened, d *diff.SchemaDiff) error {
	for i := range d.Tables {
		td := &d.Tables[i]
		if td.Status == diff.Removed || td.Source == nil {
			continue
		}
		key := data.PKColumns(td.Source)
		if len(key) == 0 && td.Target != nil {
			key = data.PKColumns(td.Target)
		}
		dd := &diff.DataDiff{KeyColumns: key, CommonColumns: data.ComparedColumns(td.Source)}
		td.Data = dd
		if len(key) == 0 {
			dd.UnsupportedKey = true
			continue
		}
		src, err := rowsOf(ctx, so, td.Source)
		if err != nil {
			return err
		}
		var tgt []diff.Row
		if td.Target != nil {
			tgt, err = rowsOf(ctx, to, td.Target)
			if err != nil {
				return err
			}
		}
		dd.SourceCount, dd.TargetCount = len(src), len(tgt)
		data.DiffRows(dd, src, tgt)
	}
	return nil
}

// SnapshotLibrary 列出本地快照库。
func (s *Service) SnapshotLibrary() ([]library.Entry, error) {
	dir, err := library.DefaultDir()
	if err != nil {
		return nil, err
	}
	return library.List(dir)
}

func (s *Service) SnapshotLibraryDir() (string, error) {
	return library.DefaultDir()
}

func (s *Service) SnapshotImport(path string) (library.Entry, error) {
	dir, err := library.DefaultDir()
	if err != nil {
		return library.Entry{}, err
	}
	return library.Import(dir, path)
}

func (s *Service) SnapshotDelete(path string) error {
	dir, err := library.DefaultDir()
	if err != nil {
		return err
	}
	return library.Delete(dir, path)
}

func (s *Service) SnapshotSetMeta(path string, tags []string, note string) error {
	dir, err := library.DefaultDir()
	if err != nil {
		return err
	}
	return library.SetMeta(dir, path, tags, note)
}

func (s *Service) Rules() ([]rules.Rule, error) { return rules.Load() }

func (s *Service) SaveRules(rs []rules.Rule) error { return rules.Save(rs) }

// Report 生成只读差异报告（md/html/json）。
func (s *Service) Report(runID, format string) (string, error) {
	s.mu.Lock()
	sess := s.sessions[runID]
	s.mu.Unlock()
	if sess == nil {
		return "", fmt.Errorf("运行 %s 不存在", runID)
	}
	return report.Render(sess.diff, sess.plan, format)
}

func (s *Service) SaveReport(runID, format, path string) error {
	body, err := s.Report(runID, format)
	if err != nil {
		return err
	}
	return os.WriteFile(path, []byte(body), 0o644)
}

func toCompareView(runID string, d *diff.SchemaDiff, schemas ...*model.Schema) *CompareView {
	v := &CompareView{
		RunID: runID, SourceName: d.SourceName, TargetName: d.TargetName, HasData: d.HasData,
		Tables: []TableBrief{}, Views: []ObjectBrief{}, Routines: []ObjectBrief{}, Triggers: []ObjectBrief{},
		Dependencies: depgraph.Build(schemas...),
	}
	v.Added, v.Removed, v.Changed, v.Same = d.Summary()
	for _, t := range d.Tables {
		tb := TableBrief{
			Name: t.Name, Status: string(t.Status),
			Columns: []ColumnBrief{}, Indexes: []IndexBrief{}, Constraints: []ConstraintBrief{},
		}
		for _, c := range t.Columns {
			cb := ColumnBrief{
				Name: c.Name, Status: string(c.Status),
				SourceNull: c.Source != nil && c.Source.Nullable,
				TargetNull: c.Target != nil && c.Target.Nullable,
				Reasons:    c.Reasons,
			}
			if c.Source != nil {
				cb.SourceType = c.Source.Type.Raw
				if c.Source.Default != nil {
					cb.SourceDefault = *c.Source.Default
				}
			}
			if c.Target != nil {
				cb.TargetType = c.Target.Type.Raw
				if c.Target.Default != nil {
					cb.TargetDefault = *c.Target.Default
				}
			}
			tb.Columns = append(tb.Columns, cb)
		}
		for _, ix := range t.Indexes {
			tb.Indexes = append(tb.Indexes, IndexBrief{Name: ix.Name, Status: string(ix.Status)})
		}
		for _, c := range t.Constraints {
			tb.Constraints = append(tb.Constraints, ConstraintBrief{Name: c.Name, Kind: string(c.Kind), Status: string(c.Status)})
		}
		if t.Data != nil {
			tb.Data = &DataBrief{
				SourceCount: t.Data.SourceCount, TargetCount: t.Data.TargetCount,
				Inserts: len(t.Data.Inserts), Updates: len(t.Data.Updates), Deletes: len(t.Data.Deletes),
				UnsupportedKey: t.Data.UnsupportedKey,
				Rows:           []RowBrief{},
			}
			tb.Data.Rows, tb.Data.Truncated = dataRows(&t)
		}
		v.Tables = append(v.Tables, tb)
	}
	for _, o := range d.Views {
		v.Views = append(v.Views, ObjectBrief{Name: o.Name, Kind: o.Kind, Status: string(o.Status)})
	}
	for _, o := range d.Routines {
		v.Routines = append(v.Routines, ObjectBrief{Name: o.Name, Kind: o.Kind, Status: string(o.Status)})
	}
	for _, o := range d.Triggers {
		v.Triggers = append(v.Triggers, ObjectBrief{Name: o.Name, Kind: o.Kind, Status: string(o.Status)})
	}
	return v
}

func dataRows(t *diff.TableDiff) ([]RowBrief, bool) {
	if t.Data == nil {
		return []RowBrief{}, false
	}
	const limit = 50
	total := t.Data.Changes()
	out := make([]RowBrief, 0, min(total, limit))
	appendRow := func(kind string, key diff.Row, cols []string, source, target diff.Row) bool {
		if len(out) >= limit {
			return false
		}
		rb := RowBrief{Kind: kind, Key: keyText(key, t.Data.KeyColumns), Cells: []CellBrief{}}
		for _, c := range cols {
			sv, sok := source[c]
			tv, tok := target[c]
			cell := CellBrief{Name: c}
			if sok {
				cell.Source = sv.Display()
			}
			if tok {
				cell.Target = tv.Display()
			}
			rb.Cells = append(rb.Cells, cell)
		}
		out = append(out, rb)
		return true
	}
	for _, r := range t.Data.Inserts {
		if t.Source == nil {
			break
		}
		cols := columnNames(t.Source)
		if !appendRow("insert", r, cols, r, nil) {
			return out, true
		}
	}
	for _, u := range t.Data.Updates {
		if !appendRow("update", u.Key, u.Changed, u.After, u.Before) {
			return out, true
		}
	}
	for _, r := range t.Data.Deletes {
		if t.Target == nil {
			break
		}
		cols := columnNames(t.Target)
		if !appendRow("delete", r, cols, nil, r) {
			return out, true
		}
	}
	return out, total > limit
}

func keyText(r diff.Row, pk []string) string {
	parts := make([]string, 0, len(pk))
	for _, c := range pk {
		if v, ok := r[c]; ok {
			parts = append(parts, c+"="+v.Display())
		}
	}
	return strings.Join(parts, ", ")
}

func columnNames(t *model.Table) []string {
	out := make([]string, 0, len(t.Columns))
	for _, c := range t.Columns {
		out = append(out, c.Name)
	}
	return out
}

func toPlanView(runID string, p *plan.Plan) *PlanView {
	v := &PlanView{RunID: runID, Blocked: p.Blocked, Fingerprint: p.Fingerprint, Diagnostics: []Diagnostic{}, Phases: []PhaseView{}}
	for _, d := range p.Diagnostics {
		v.Diagnostics = append(v.Diagnostics, Diagnostic{Severity: string(d.Severity), Code: d.Code, Object: d.Object, Message: d.Message})
	}
	byPhase := map[int][]Statement{}
	for _, st := range p.Statements {
		byPhase[int(st.Phase)] = append(byPhase[int(st.Phase)], Statement{SQL: st.SQL, Object: st.Object, Destructive: st.Destructive})
	}
	var phases []int
	for ph := range byPhase {
		phases = append(phases, ph)
	}
	sort.Ints(phases)
	for _, ph := range phases {
		v.Phases = append(v.Phases, PhaseView{Phase: ph, Statements: byPhase[ph]})
	}
	return v
}

func newID() string {
	var b [8]byte
	_, _ = rand.Read(b[:])
	return hex.EncodeToString(b[:])
}

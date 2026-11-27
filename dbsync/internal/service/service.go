// Package service 是 GUI 与未来 CLI 共用的编排层；不包含第二份比对逻辑。
package service

import (
	"context"
	"crypto/rand"
	"database/sql"
	"encoding/hex"
	"fmt"
	"sort"
	"sync"

	"gitee.com/wangbenchi66/dbsync/internal/data"
	"gitee.com/wangbenchi66/dbsync/internal/dialect/mysql"
	"gitee.com/wangbenchi66/dbsync/internal/diff"
	"gitee.com/wangbenchi66/dbsync/internal/model"
	"gitee.com/wangbenchi66/dbsync/internal/plan"
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
}

type CompareView struct {
	RunID      string        `json:"runId"`
	SourceName string        `json:"sourceName"`
	TargetName string        `json:"targetName"`
	Tables     []TableBrief  `json:"tables"`
	Views      []ObjectBrief `json:"views"`
	Routines   []ObjectBrief `json:"routines"`
	Triggers   []ObjectBrief `json:"triggers"`
	Added      int           `json:"added"`
	Removed    int           `json:"removed"`
	Changed    int           `json:"changed"`
	Same       int           `json:"same"`
	HasData    bool          `json:"hasData"`
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
	SourceCount    int  `json:"sourceCount"`
	TargetCount    int  `json:"targetCount"`
	Inserts        int  `json:"inserts"`
	Updates        int  `json:"updates"`
	Deletes        int  `json:"deletes"`
	UnsupportedKey bool `json:"unsupportedKey"`
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

// Compare 打开两端、读取结构、按需读数据，返回 runId 供后续 Plan/Apply。
func (s *Service) Compare(ctx context.Context, req CompareRequest) (*CompareView, error) {
	ss, sdb, err := openSchema(ctx, req.Source)
	if err != nil {
		return nil, fmt.Errorf("源端: %w", err)
	}
	if sdb != nil {
		defer sdb.Close()
	}
	ts, tdb, err := openSchema(ctx, req.Target)
	if err != nil {
		return nil, fmt.Errorf("目标端: %w", err)
	}
	if tdb != nil {
		defer tdb.Close()
	}
	d := diff.CompareSchema(ss, ts)
	if req.CompareData && sdb != nil && tdb != nil {
		if err := data.Attach(ctx, sdb, tdb, d); err != nil {
			return nil, err
		}
		d.HasData = true
	}
	runID := newID()
	s.mu.Lock()
	s.sessions[runID] = &session{
		source: req.Source, target: req.Target,
		sourceSchema: ss, targetSchema: ts, diff: d,
		allowDrop: req.AllowDestructive,
	}
	s.mu.Unlock()
	return toCompareView(runID, d), nil
}

func (s *Service) Plan(ctx context.Context, runID string, allowDestructive bool) (*PlanView, error) {
	s.mu.Lock()
	sess := s.sessions[runID]
	s.mu.Unlock()
	if sess == nil {
		return nil, fmt.Errorf("运行 %s 不存在，请重新比较", runID)
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

func openSchema(ctx context.Context, ep Endpoint) (*model.Schema, *sql.DB, error) {
	switch ep.Kind {
	case "mysql":
		db, err := mysql.Open(ep.DSN)
		if err != nil {
			return nil, nil, err
		}
		schema := ep.Schema
		if schema == "" {
			schema, err = mysql.Database(ctx, db)
			if err != nil || schema == "" {
				db.Close()
				return nil, nil, fmt.Errorf("无法确定 schema，请显式填写")
			}
		}
		s, err := mysql.Introspect(ctx, db, schema)
		if err != nil {
			db.Close()
			return nil, nil, err
		}
		return s, db, nil
	case "snapshot":
		sn, err := snapshot.Read(ep.Path)
		if err != nil {
			return nil, nil, err
		}
		return sn.Schema, nil, nil
	default:
		return nil, nil, fmt.Errorf("未知端点类型 %q", ep.Kind)
	}
}

func toCompareView(runID string, d *diff.SchemaDiff) *CompareView {
	v := &CompareView{
		RunID: runID, SourceName: d.SourceName, TargetName: d.TargetName, HasData: d.HasData,
		Tables: []TableBrief{}, Views: []ObjectBrief{}, Routines: []ObjectBrief{}, Triggers: []ObjectBrief{},
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
			}
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

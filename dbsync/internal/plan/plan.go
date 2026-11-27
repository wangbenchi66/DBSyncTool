// Package plan 把结构/数据差异编译成按阶段排序的 MySQL 语句。
// M2 只生成计划，不执行；执行器在 execute 包。
package plan

import (
	"crypto/sha256"
	"encoding/hex"
	"fmt"
	"sort"
	"strings"

	"gitee.com/wangbenchi66/dbsync/internal/diff"
	"gitee.com/wangbenchi66/dbsync/internal/model"
	"gitee.com/wangbenchi66/dbsync/internal/value"
)

type Phase int

const (
	PhaseDropDeps    Phase = 1
	PhaseDataCleanup Phase = 2
	PhaseSchema      Phase = 3
	PhaseDataLoad    Phase = 4
	PhaseSchemaDone  Phase = 5
	PhaseObjects     Phase = 6
	PhaseVerify      Phase = 7
)

type Severity string

const (
	Blocking Severity = "blocking"
	Warning  Severity = "warning"
	Info     Severity = "info"
)

type Diagnostic struct {
	Severity Severity
	Code     string
	Object   string
	Message  string
}

type Statement struct {
	Phase       Phase
	SQL         string
	Object      string
	Destructive bool
	Reversible  bool
}

type Plan struct {
	Source      string
	Target      string
	Statements  []Statement
	Diagnostics []Diagnostic
	Blocked     bool
	Fingerprint string
}

func (p *Plan) Add(phase Phase, sql, object string, destructive, reversible bool) {
	p.Statements = append(p.Statements, Statement{
		Phase: phase, SQL: sql, Object: object,
		Destructive: destructive, Reversible: reversible,
	})
}

func (p *Plan) Block(code, object, msg string) {
	p.Blocked = true
	p.Diagnostics = append(p.Diagnostics, Diagnostic{Severity: Blocking, Code: code, Object: object, Message: msg})
}

func (p *Plan) Warn(code, object, msg string) {
	p.Diagnostics = append(p.Diagnostics, Diagnostic{Severity: Warning, Code: code, Object: object, Message: msg})
}

func (p *Plan) Seal() {
	h := sha256.New()
	h.Write([]byte(p.Source))
	h.Write([]byte{0})
	h.Write([]byte(p.Target))
	h.Write([]byte{0})
	for _, s := range p.Statements {
		fmt.Fprintf(h, "%d\x00%s\x00%s\x00", s.Phase, s.SQL, s.Object)
	}
	p.Fingerprint = hex.EncodeToString(h.Sum(nil))
}

type Options struct {
	AllowDestructive bool
	CompareData      bool
}

// Build 生成 MySQL 计划。遇到阻断项仍保留语句供 UI 预览，但 Blocked=true。
func Build(d *diff.SchemaDiff, source, target *model.Schema, opts Options) *Plan {
	p := &Plan{Source: d.SourceName, Target: d.TargetName}
	defer p.Seal()
	changed := d.HasChanges()
	if !changed && !hasDataChanges(d) {
		p.Diagnostics = append(p.Diagnostics, Diagnostic{Severity: Info, Code: "NO_CHANGES", Message: "没有差异"})
		return p
	}

	// 阶段 1：摘依赖。M2 用"全部 FK / 视图 / 触发器重建"换取正确性。
	if changed {
		for i := range d.Tables {
			td := &d.Tables[i]
			t := td.Target
			if t == nil {
				continue
			}
			for _, c := range t.Constraints {
				if c.Kind == model.ConstraintFK {
					p.Add(PhaseDropDeps, "ALTER TABLE "+tableName(t)+" DROP FOREIGN KEY "+quote(c.Name)+";", td.Name, false, true)
				}
				if c.Kind == model.ConstraintCheck {
					p.Add(PhaseDropDeps, "ALTER TABLE "+tableName(t)+" DROP CHECK "+quote(c.Name)+";", td.Name, false, true)
				}
			}
			for _, ix := range td.Indexes {
				if ix.Status == diff.Removed || ix.Status == diff.Changed {
					if ix.Target != nil && ix.Target.Primary {
						p.Block("PK_CHANGE", td.Name, "主键变更需要整表替换，M2 暂不支持")
						continue
					}
					if ix.Target != nil {
						p.Add(PhaseDropDeps, "ALTER TABLE "+tableName(t)+" DROP INDEX "+quote(ix.Name)+";", td.Name, false, true)
					}
				}
			}
		}
		for _, v := range d.Views {
			if v.Status != diff.Same || changed {
				p.Add(PhaseDropDeps, "DROP VIEW IF EXISTS "+quoteQualified(d.TargetName, v.Name)+";", v.Name, false, true)
			}
		}
		for _, tr := range d.Triggers {
			if tr.Status != diff.Same || changed {
				p.Add(PhaseDropDeps, "DROP TRIGGER IF EXISTS "+quoteQualified(d.TargetName, tr.Name)+";", tr.Name, false, true)
			}
		}
	}

	// 阶段 2：删除目标独有行（在结构变更前，避免旧 NOT NULL 列阻塞）。
	for i := range d.Tables {
		td := &d.Tables[i]
		if td.Data == nil || td.Target == nil {
			continue
		}
		if td.Data.UnsupportedKey && (len(td.Data.Inserts)+len(td.Data.Updates)+len(td.Data.Deletes)) > 0 {
			p.Block("NO_PK", td.Name, "表没有主键，无法生成安全的逐行 DML")
			continue
		}
		for _, row := range td.Data.Deletes {
			p.Add(PhaseDataCleanup, "DELETE FROM "+tableName(td.Target)+" WHERE "+wherePK(td.Target, row, td.Data.KeyColumns)+";", td.Name, true, true)
		}
	}

	// 阶段 3：表/列结构。
	for i := range d.Tables {
		td := &d.Tables[i]
		switch td.Status {
		case diff.Removed:
			if td.Target != nil {
				if !opts.AllowDestructive {
					p.Block("DROP_TABLE", td.Name, "删除表需要显式允许破坏性操作")
				}
				p.Add(PhaseSchema, "DROP TABLE "+tableName(td.Target)+";", td.Name, true, false)
			}
			continue
		case diff.Added:
			if td.Source != nil {
				st := *td.Source
				st.Schema = p.Target
				sql, err := createTableSQL(&st)
				if err != nil {
					p.Block("CREATE_TABLE", td.Name, err.Error())
					continue
				}
				p.Add(PhaseSchema, sql, td.Name, false, true)
			}
			continue
		}
		if td.Source == nil || td.Target == nil {
			continue
		}
		// 表默认 collation
		if td.Source.Collation != "" && td.Source.Collation != td.Target.Collation {
			if cs := charsetOf(td.Source.Collation); cs != "" {
				p.Add(PhaseSchema, "ALTER TABLE "+tableName(td.Target)+" DEFAULT CHARACTER SET "+cs+" COLLATE "+quote(td.Source.Collation)+";", td.Name, false, true)
			}
		}
		for _, c := range td.Columns {
			switch c.Status {
			case diff.Removed:
				if !opts.AllowDestructive {
					p.Block("DROP_COLUMN", td.Name+"."+c.Name, "删除列需要显式允许破坏性操作")
				}
				p.Add(PhaseSchema, "ALTER TABLE "+tableName(td.Target)+" DROP COLUMN "+quote(c.Name)+";", td.Name, true, false)
			case diff.Added:
				def, err := columnSQL(c.Source)
				if err != nil {
					p.Block("ADD_COLUMN", td.Name+"."+c.Name, err.Error())
					continue
				}
				p.Add(PhaseSchema, "ALTER TABLE "+tableName(td.Target)+" ADD COLUMN "+def+";", td.Name, false, true)
			case diff.Changed:
				def, err := columnSQL(c.Source)
				if err != nil {
					p.Block("MODIFY_COLUMN", td.Name+"."+c.Name, err.Error())
					continue
				}
				p.Add(PhaseSchema, "ALTER TABLE "+tableName(td.Target)+" MODIFY COLUMN "+def+";", td.Name, false, true)
			}
		}
	}

	// 阶段 4：INSERT/UPDATE。
	for i := range d.Tables {
		td := &d.Tables[i]
		if td.Data == nil || td.Source == nil {
			continue
		}
		if td.Data.UnsupportedKey && (len(td.Data.Inserts)+len(td.Data.Updates)) > 0 {
			continue
		}
		tt := td.Target
		if tt == nil {
			copy := *td.Source
			copy.Schema = p.Target
			tt = &copy
		} else {
			copy := *tt
			copy.Schema = p.Target
			tt = &copy
		}
		for _, row := range td.Data.Inserts {
			sql, err := insertSQL(tt, td.Source.Columns, row)
			if err != nil {
				p.Block("INSERT", td.Name, err.Error())
				continue
			}
			p.Add(PhaseDataLoad, sql, td.Name, false, true)
		}
		for _, up := range td.Data.Updates {
			sql, err := updateSQL(tt, td.Source, up, td.Data.KeyColumns)
			if err != nil {
				p.Block("UPDATE", td.Name, err.Error())
				continue
			}
			p.Add(PhaseDataLoad, sql, td.Name, false, true)
		}
	}

	// 阶段 5：索引/FK/CHECK。
	for i := range d.Tables {
		td := &d.Tables[i]
		if td.Source == nil {
			continue
		}
		var targetTable *model.Table
		var indexes []diff.IndexDiff
		var constraints []diff.ConstraintDiff
		if td.Status == diff.Added {
			copy := *td.Source
			copy.Schema = p.Target
			targetTable = &copy
			for j := range td.Source.Indexes {
				ix := &td.Source.Indexes[j]
				indexes = append(indexes, diff.IndexDiff{Name: ix.Name, Status: diff.Added, Source: ix})
			}
			for j := range td.Source.Constraints {
				c := &td.Source.Constraints[j]
				constraints = append(constraints, diff.ConstraintDiff{Name: c.Name, Kind: c.Kind, Status: diff.Added, Source: c})
			}
		} else {
			targetTable = td.Target
			indexes = td.Indexes
			constraints = td.Constraints
		}
		if targetTable == nil {
			continue
		}
		for _, ix := range indexes {
			if ix.Status == diff.Added || ix.Status == diff.Changed {
				if ix.Source == nil || ix.Source.Primary {
					continue
				}
				p.Add(PhaseSchemaDone, addIndexSQL(targetTable, ix.Source), td.Name, false, true)
			}
		}
		for _, c := range constraints {
			if c.Kind != model.ConstraintFK && c.Kind != model.ConstraintCheck {
				continue
			}
			if c.Kind == model.ConstraintFK && c.Status == diff.Removed {
				continue
			}
			if c.Kind == model.ConstraintCheck && c.Status == diff.Removed {
				continue
			}
			if c.Source == nil {
				continue
			}
			if c.Kind == model.ConstraintFK {
				p.Add(PhaseSchemaDone, addFKSQL(targetTable, c.Source, p.Target), td.Name, false, true)
			} else {
				p.Add(PhaseSchemaDone, "ALTER TABLE "+tableName(targetTable)+" ADD CONSTRAINT "+quote(c.Name)+" CHECK ("+c.Source.CheckExpr+");", td.Name, false, true)
			}
		}
	}

	// 阶段 6：视图/触发器。
	for _, v := range d.Views {
		if v.Source == "" {
			continue
		}
		def := rewriteSchema(v.Source, p.Source, p.Target)
		p.Add(PhaseObjects, "CREATE OR REPLACE VIEW "+quoteQualified(p.Target, v.Name)+" AS "+def+";", v.Name, false, true)
	}
	if changed && source != nil {
		for _, tr := range source.Triggers {
			p.Add(PhaseObjects, "CREATE TRIGGER "+quoteQualified(p.Target, tr.Name)+" "+tr.Timing+" "+tr.Event+" ON "+quoteQualified(p.Target, tr.Table)+" FOR EACH ROW "+tr.Statement+";", tr.Name, false, true)
		}
	}
	for _, r := range d.Routines {
		if r.Status != diff.Same {
			p.Warn("ROUTINE_PLAN", r.Name, "过程/函数 DDL 生成在 M2 暂未实现")
		}
	}
	_ = target
	p.Add(PhaseVerify, "SELECT 1;", "", false, true)
	return p
}

func rewriteSchema(def, source, target string) string {
	if source == "" || target == "" || source == target {
		return def
	}
	def = strings.ReplaceAll(def, "`"+source+"`.", "`"+target+"`.")
	def = strings.ReplaceAll(def, source+".", target+".")
	return def
}

func hasDataChanges(d *diff.SchemaDiff) bool {
	for _, t := range d.Tables {
		if t.Data != nil && !t.Data.IsEmpty() {
			return true
		}
	}
	return false
}

func tableName(t *model.Table) string {
	if t.Schema == "" {
		return quote(t.Name)
	}
	return quoteQualified(t.Schema, t.Name)
}

func quoteQualified(schema, name string) string {
	if schema == "" {
		return quote(name)
	}
	return quote(schema) + "." + quote(name)
}

func quote(s string) string {
	return "`" + strings.ReplaceAll(s, "`", "``") + "`"
}

func charsetOf(collation string) string {
	if i := strings.IndexByte(collation, '_'); i > 0 {
		return collation[:i]
	}
	return ""
}

func createTableSQL(t *model.Table) (string, error) {
	var defs []string
	for i := range t.Columns {
		s, err := columnSQL(&t.Columns[i])
		if err != nil {
			return "", err
		}
		defs = append(defs, s)
	}
	var pk []string
	for _, c := range t.Constraints {
		if c.Kind == model.ConstraintPK {
			for _, col := range c.Columns {
				pk = append(pk, quote(col))
			}
		}
	}
	if len(pk) == 0 {
		for _, ix := range t.Indexes {
			if ix.Primary {
				for _, c := range ix.Columns {
					pk = append(pk, quote(c.Name))
				}
			}
		}
	}
	if len(pk) > 0 {
		defs = append(defs, "PRIMARY KEY ("+strings.Join(pk, ",")+")")
	}
	sql := "CREATE TABLE " + tableName(t) + " (\n  " + strings.Join(defs, ",\n  ") + "\n)"
	if t.Collation != "" {
		if cs := charsetOf(t.Collation); cs != "" {
			sql += " ENGINE=InnoDB DEFAULT CHARSET=" + cs + " COLLATE=" + t.Collation
		}
	}
	if t.Comment != "" {
		sql += " COMMENT=" + strLit(t.Comment)
	}
	return sql + ";", nil
}

func columnSQL(c *model.Column) (string, error) {
	if c == nil {
		return "", fmt.Errorf("列为空")
	}
	if c.Generated {
		return "", fmt.Errorf("生成列 %s 的 DDL 生成在 M2 暂未实现", c.Name)
	}
	s := quote(c.Name) + " " + c.Type.Raw
	if c.Type.Raw == "" {
		return "", fmt.Errorf("列 %s 缺少原始类型", c.Name)
	}
	if c.Type.Charset != "" && (c.Type.ID == model.TypeString || c.Type.ID == model.TypeText || c.Type.ID == model.TypeEnum || c.Type.ID == model.TypeSet) {
		s += " CHARACTER SET " + c.Type.Charset
	}
	if c.Type.Collation != "" && (c.Type.ID == model.TypeString || c.Type.ID == model.TypeText || c.Type.ID == model.TypeEnum || c.Type.ID == model.TypeSet) {
		s += " COLLATE " + quote(c.Type.Collation)
	}
	if c.Nullable {
		s += " NULL"
	} else {
		s += " NOT NULL"
	}
	if c.DefaultSet {
		if c.Default == nil {
			s += " DEFAULT NULL"
		} else if isExprDefault(*c.Default) {
			s += " DEFAULT " + *c.Default
		} else {
			s += " DEFAULT " + literalForType(*c.Default, c.Type)
		}
	}
	if c.Identity {
		s += " AUTO_INCREMENT"
	}
	if c.Comment != "" {
		s += " COMMENT " + strLit(c.Comment)
	}
	return s, nil
}

func isExprDefault(s string) bool {
	u := strings.ToUpper(strings.TrimSpace(s))
	return strings.HasPrefix(u, "CURRENT_TIMESTAMP") || strings.HasPrefix(u, "NOW()") || strings.HasPrefix(u, "UUID")
}

func literalForType(s string, t model.Type) string {
	if t.ID == model.TypeBool {
		if s == "0" || strings.EqualFold(s, "false") {
			return "0"
		}
		return "1"
	}
	return strLit(s)
}

func addIndexSQL(t *model.Table, ix *model.Index) string {
	var cols []string
	for _, c := range ix.Columns {
		x := quote(c.Name)
		if c.Prefix > 0 {
			x += fmt.Sprintf("(%d)", c.Prefix)
		}
		if c.Desc {
			x += " DESC"
		}
		cols = append(cols, x)
	}
	uniq := ""
	if ix.Unique {
		uniq = "UNIQUE "
	}
	return "ALTER TABLE " + tableName(t) + " ADD " + uniq + "INDEX " + quote(ix.Name) + " (" + strings.Join(cols, ",") + ");"
}

func addFKSQL(t *model.Table, c *model.Constraint, targetSchema string) string {
	var cols []string
	for _, s := range c.Columns {
		cols = append(cols, quote(s))
	}
	var refs []string
	for _, s := range c.RefColumns {
		refs = append(refs, quote(s))
	}
	refSchema := targetSchema
	if refSchema == "" {
		refSchema = c.RefSchema
	}
	sql := "ALTER TABLE " + tableName(t) + " ADD CONSTRAINT " + quote(c.Name) +
		" FOREIGN KEY (" + strings.Join(cols, ",") + ") REFERENCES " +
		quoteQualified(refSchema, c.RefTable) + " (" + strings.Join(refs, ",") + ")"
	if c.OnDelete != "" && c.OnDelete != "RESTRICT" {
		sql += " ON DELETE " + c.OnDelete
	}
	if c.OnUpdate != "" && c.OnUpdate != "RESTRICT" {
		sql += " ON UPDATE " + c.OnUpdate
	}
	return sql + ";"
}

func wherePK(t *model.Table, row diff.Row, pk []string) string {
	var parts []string
	for _, name := range pk {
		c := t.Column(name)
		if c == nil {
			parts = append(parts, quote(name)+" = "+literal(row[name], model.Type{}))
			continue
		}
		parts = append(parts, quote(name)+" = "+literal(row[name], c.Type))
	}
	return strings.Join(parts, " AND ")
}

func insertSQL(t *model.Table, cols []model.Column, row diff.Row) (string, error) {
	var names, vals []string
	for _, c := range cols {
		if c.Generated {
			continue
		}
		names = append(names, quote(c.Name))
		vals = append(vals, literal(row[c.Name], c.Type))
	}
	return "INSERT INTO " + tableName(t) + " (" + strings.Join(names, ",") + ") VALUES (" + strings.Join(vals, ",") + ");", nil
}

func updateSQL(t *model.Table, source *model.Table, up diff.RowUpdate, pk []string) (string, error) {
	var sets []string
	for _, name := range up.Changed {
		c := source.Column(name)
		if c == nil || c.Generated {
			continue
		}
		sets = append(sets, quote(name)+" = "+literal(up.After[name], c.Type))
	}
	if len(sets) == 0 {
		return "", fmt.Errorf("空 UPDATE")
	}
	return "UPDATE " + tableName(t) + " SET " + strings.Join(sets, ", ") + " WHERE " + wherePK(t, up.Key, pk) + ";", nil
}

func literal(v value.Value, t model.Type) string {
	switch v.Kind() {
	case value.KindNull:
		return "NULL"
	case value.KindBool:
		if v.Bool() {
			return "1"
		}
		return "0"
	case value.KindInt:
		return fmt.Sprintf("%d", v.Int())
	case value.KindUint:
		return fmt.Sprintf("%d", v.Uint())
	case value.KindFloat:
		return fmt.Sprintf("%v", v.Float())
	case value.KindDecimal:
		return v.StringValue()
	case value.KindString:
		return strLit(v.StringValue())
	case value.KindBytes:
		return "0x" + hex.EncodeToString(v.BytesValue())
	case value.KindTime:
		return strLit(v.TimeValue().UTC().Format("2006-01-02 15:04:05.999999"))
	}
	return "NULL"
}

func strLit(s string) string {
	return "'" + strings.ReplaceAll(strings.ReplaceAll(s, "\\", "\\\\"), "'", "''") + "'"
}

// SortStatements 稳定排序：先阶段，再原始顺序。
func (p *Plan) SortStatements() {
	sort.SliceStable(p.Statements, func(i, j int) bool { return p.Statements[i].Phase < p.Statements[j].Phase })
}

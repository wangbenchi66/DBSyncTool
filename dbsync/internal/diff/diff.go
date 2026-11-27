package diff

import (
	"sort"
	"strings"

	"gitee.com/wangbenchi66/dbsync/internal/model"
	"gitee.com/wangbenchi66/dbsync/internal/value"
)

type Status string

const (
	Same    Status = "same"
	Added   Status = "added"
	Removed Status = "removed"
	Changed Status = "changed"
)

type SchemaDiff struct {
	SourceName string
	TargetName string
	Tables     []TableDiff
	Views      []ObjectDiff
	Routines   []ObjectDiff
	Triggers   []ObjectDiff
	HasData    bool
}

type TableDiff struct {
	Name        string
	Status      Status
	Source      *model.Table
	Target      *model.Table
	Columns     []ColumnDiff
	Indexes     []IndexDiff
	Constraints []ConstraintDiff
	Data        *DataDiff
}

type ColumnDiff struct {
	Name    string
	Status  Status
	Source  *model.Column
	Target  *model.Column
	Reasons []string
}

type IndexDiff struct {
	Name   string
	Status Status
	Source *model.Index
	Target *model.Index
}

type ConstraintDiff struct {
	Name   string
	Kind   model.ConstraintKind
	Status Status
	Source *model.Constraint
	Target *model.Constraint
}

type ObjectDiff struct {
	Kind   string
	Name   string
	Status Status
	Source string
	Target string
}

type DataDiff struct {
	KeyColumns     []string
	CommonColumns  []string
	SourceCount    int
	TargetCount    int
	Inserts        []Row
	Updates        []RowUpdate
	Deletes        []Row
	UnsupportedKey bool
}

type Row map[string]value.Value

type RowUpdate struct {
	Key     Row
	Before  Row
	After   Row
	Changed []string
}

// CompareSchema 只比较结构；数据由 data 包填充。
func CompareSchema(source, target *model.Schema) *SchemaDiff {
	source.Sort()
	target.Sort()
	out := &SchemaDiff{SourceName: source.Name, TargetName: target.Name}
	st := map[string]*model.Table{}
	tt := map[string]*model.Table{}
	for i := range source.Tables {
		st[source.Tables[i].Name] = &source.Tables[i]
	}
	for i := range target.Tables {
		tt[target.Tables[i].Name] = &target.Tables[i]
	}
	names := unionKeys(st, tt)
	for _, name := range names {
		s, t := st[name], tt[name]
		switch {
		case s != nil && t == nil:
			out.Tables = append(out.Tables, TableDiff{Name: name, Status: Added, Source: s})
		case s == nil && t != nil:
			out.Tables = append(out.Tables, TableDiff{Name: name, Status: Removed, Target: t})
		default:
			td := compareTable(name, s, t)
			out.Tables = append(out.Tables, td)
		}
	}
	out.Views = compareObjects("view", source.Views, target.Views, source.Name, target.Name)
	out.Routines = compareRoutines(source.Routines, target.Routines)
	out.Triggers = compareTriggers(source.Triggers, target.Triggers)
	return out
}

func compareTable(name string, s, t *model.Table) TableDiff {
	td := TableDiff{Name: name, Source: s, Target: t}
	sc := map[string]*model.Column{}
	tc := map[string]*model.Column{}
	for i := range s.Columns {
		sc[s.Columns[i].Name] = &s.Columns[i]
	}
	for i := range t.Columns {
		tc[t.Columns[i].Name] = &t.Columns[i]
	}
	for _, n := range unionKeys(sc, tc) {
		cs, ct := sc[n], tc[n]
		switch {
		case cs != nil && ct == nil:
			td.Columns = append(td.Columns, ColumnDiff{Name: n, Status: Added, Source: cs})
		case cs == nil && ct != nil:
			td.Columns = append(td.Columns, ColumnDiff{Name: n, Status: Removed, Target: ct})
		default:
			reasons := columnReasons(cs, ct)
			status := Same
			if len(reasons) > 0 {
				status = Changed
			}
			td.Columns = append(td.Columns, ColumnDiff{Name: n, Status: status, Source: cs, Target: ct, Reasons: reasons})
		}
	}
	td.Indexes = compareIndexes(s, t)
	td.Constraints = compareConstraints(s, t)
	for _, c := range td.Columns {
		if c.Status != Same {
			td.Status = Changed
		}
	}
	for _, ix := range td.Indexes {
		if ix.Status != Same {
			td.Status = Changed
		}
	}
	for _, c := range td.Constraints {
		if c.Status != Same {
			td.Status = Changed
		}
	}
	if s.Collation != t.Collation && s.Collation != "" && t.Collation != "" {
		td.Status = Changed
	}
	return td
}

func columnReasons(s, t *model.Column) []string {
	var r []string
	if !typesEqual(s.Type, t.Type) {
		r = append(r, "类型/长度/collation 不同")
	}
	if s.Nullable != t.Nullable {
		r = append(r, "可空性不同")
	}
	if !defaultsEqual(s, t) {
		r = append(r, "默认值不同")
	}
	if s.Identity != t.Identity {
		r = append(r, "identity 不同")
	}
	if s.Generated != t.Generated || normalizeSQL(s.GenerationExpr) != normalizeSQL(t.GenerationExpr) {
		r = append(r, "生成列不同")
	}
	if s.Comment != t.Comment {
		r = append(r, "注释不同")
	}
	return r
}

func typesEqual(a, b model.Type) bool {
	if a.ID != b.ID || a.Unsigned != b.Unsigned {
		return false
	}
	if a.Length != b.Length || a.Precision != b.Precision || a.Scale != b.Scale {
		return false
	}
	if a.Collation != "" && b.Collation != "" && a.Collation != b.Collation {
		return false
	}
	return true
}

func defaultsEqual(a, b *model.Column) bool {
	if a.DefaultSet != b.DefaultSet {
		return false
	}
	if !a.DefaultSet {
		return true
	}
	da, db := "", ""
	if a.Default != nil {
		da = *a.Default
	}
	if b.Default != nil {
		db = *b.Default
	}
	return normalizeDefault(da) == normalizeDefault(db)
}

func normalizeDefault(s string) string {
	s = strings.TrimSpace(s)
	s = strings.Trim(s, "()")
	if strings.EqualFold(s, "null") {
		return ""
	}
	return s
}

func compareIndexes(s, t *model.Table) []IndexDiff {
	sm := map[string]*model.Index{}
	tm := map[string]*model.Index{}
	for i := range s.Indexes {
		sm[s.Indexes[i].Name] = &s.Indexes[i]
	}
	for i := range t.Indexes {
		tm[t.Indexes[i].Name] = &t.Indexes[i]
	}
	var out []IndexDiff
	for _, n := range unionKeys(sm, tm) {
		a, b := sm[n], tm[n]
		switch {
		case a != nil && b == nil:
			out = append(out, IndexDiff{Name: n, Status: Added, Source: a})
		case a == nil && b != nil:
			out = append(out, IndexDiff{Name: n, Status: Removed, Target: b})
		default:
			st := Same
			if !indexEqual(a, b) {
				st = Changed
			}
			out = append(out, IndexDiff{Name: n, Status: st, Source: a, Target: b})
		}
	}
	return out
}

func indexEqual(a, b *model.Index) bool {
	if a.Unique != b.Unique || a.Primary != b.Primary || len(a.Columns) != len(b.Columns) {
		return false
	}
	for i := range a.Columns {
		if a.Columns[i] != b.Columns[i] {
			return false
		}
	}
	return true
}

func compareConstraints(s, t *model.Table) []ConstraintDiff {
	sm := map[string]*model.Constraint{}
	tm := map[string]*model.Constraint{}
	for i := range s.Constraints {
		sm[s.Constraints[i].Name] = &s.Constraints[i]
	}
	for i := range t.Constraints {
		tm[t.Constraints[i].Name] = &t.Constraints[i]
	}
	var out []ConstraintDiff
	for _, n := range unionKeys(sm, tm) {
		a, b := sm[n], tm[n]
		kind := model.ConstraintKind("")
		if a != nil {
			kind = a.Kind
		} else if b != nil {
			kind = b.Kind
		}
		switch {
		case a != nil && b == nil:
			out = append(out, ConstraintDiff{Name: n, Kind: kind, Status: Added, Source: a})
		case a == nil && b != nil:
			out = append(out, ConstraintDiff{Name: n, Kind: kind, Status: Removed, Target: b})
		default:
			st := Same
			if !constraintEqual(a, b) {
				st = Changed
			}
			out = append(out, ConstraintDiff{Name: n, Kind: kind, Status: st, Source: a, Target: b})
		}
	}
	return out
}

func constraintEqual(a, b *model.Constraint) bool {
	if a.Kind != b.Kind || a.RefSchema != b.RefSchema || a.RefTable != b.RefTable ||
		a.OnUpdate != b.OnUpdate || a.OnDelete != b.OnDelete ||
		normalizeSQL(a.CheckExpr) != normalizeSQL(b.CheckExpr) ||
		len(a.Columns) != len(b.Columns) || len(a.RefColumns) != len(b.RefColumns) {
		return false
	}
	for i := range a.Columns {
		if a.Columns[i] != b.Columns[i] {
			return false
		}
	}
	for i := range a.RefColumns {
		if a.RefColumns[i] != b.RefColumns[i] {
			return false
		}
	}
	return true
}

func compareObjects(kind string, s, t []model.View, sourceSchema, targetSchema string) []ObjectDiff {
	sm := map[string]string{}
	tm := map[string]string{}
	for _, v := range s {
		sm[v.Name] = v.Definition
	}
	for _, v := range t {
		tm[v.Name] = v.Definition
	}
	var out []ObjectDiff
	for _, n := range unionKeys(sm, tm) {
		a, aok := sm[n]
		b, bok := tm[n]
		switch {
		case aok && !bok:
			out = append(out, ObjectDiff{Kind: kind, Name: n, Status: Added, Source: a})
		case !aok && bok:
			out = append(out, ObjectDiff{Kind: kind, Name: n, Status: Removed, Target: b})
		default:
			st := Same
			if normalizeSQL(normalizeView(a, sourceSchema, targetSchema)) != normalizeSQL(b) {
				st = Changed
			}
			out = append(out, ObjectDiff{Kind: kind, Name: n, Status: st, Source: a, Target: b})
		}
	}
	return out
}

func normalizeView(def, from, to string) string {
	if from == "" || to == "" || from == to {
		return def
	}
	def = strings.ReplaceAll(def, "`"+from+"`.", "`"+to+"`.")
	def = strings.ReplaceAll(def, from+".", to+".")
	return def
}

func compareRoutines(s, t []model.Routine) []ObjectDiff {
	sm := map[string]string{}
	tm := map[string]string{}
	for _, v := range s {
		sm[v.Kind+" "+v.Name] = v.Definition
	}
	for _, v := range t {
		tm[v.Kind+" "+v.Name] = v.Definition
	}
	var out []ObjectDiff
	for _, n := range unionKeys(sm, tm) {
		a, aok := sm[n]
		b, bok := tm[n]
		switch {
		case aok && !bok:
			out = append(out, ObjectDiff{Kind: "routine", Name: n, Status: Added, Source: a})
		case !aok && bok:
			out = append(out, ObjectDiff{Kind: "routine", Name: n, Status: Removed, Target: b})
		default:
			st := Same
			if normalizeSQL(a) != normalizeSQL(b) {
				st = Changed
			}
			out = append(out, ObjectDiff{Kind: "routine", Name: n, Status: st, Source: a, Target: b})
		}
	}
	return out
}

func compareTriggers(s, t []model.Trigger) []ObjectDiff {
	sm := map[string]string{}
	tm := map[string]string{}
	for _, v := range s {
		sm[v.Name] = v.Statement
	}
	for _, v := range t {
		tm[v.Name] = v.Statement
	}
	var out []ObjectDiff
	for _, n := range unionKeys(sm, tm) {
		a, aok := sm[n]
		b, bok := tm[n]
		switch {
		case aok && !bok:
			out = append(out, ObjectDiff{Kind: "trigger", Name: n, Status: Added, Source: a})
		case !aok && bok:
			out = append(out, ObjectDiff{Kind: "trigger", Name: n, Status: Removed, Target: b})
		default:
			st := Same
			if normalizeSQL(a) != normalizeSQL(b) {
				st = Changed
			}
			out = append(out, ObjectDiff{Kind: "trigger", Name: n, Status: st, Source: a, Target: b})
		}
	}
	return out
}

func normalizeSQL(s string) string {
	return strings.Join(strings.Fields(s), " ")
}

func unionKeys[V any](a, b map[string]V) []string {
	set := map[string]struct{}{}
	for k := range a {
		set[k] = struct{}{}
	}
	for k := range b {
		set[k] = struct{}{}
	}
	out := make([]string, 0, len(set))
	for k := range set {
		out = append(out, k)
	}
	sort.Strings(out)
	return out
}

// Summary 返回给 UI/报告的计数。
func (d *SchemaDiff) Summary() (added, removed, changed, same int) {
	for _, t := range d.Tables {
		switch t.Status {
		case Added:
			added++
		case Removed:
			removed++
		case Changed:
			changed++
		default:
			same++
		}
	}
	return
}

// HasChanges 用于计划/空态。
func (d *SchemaDiff) HasChanges() bool {
	a, r, c, _ := d.Summary()
	if a+r+c > 0 {
		return true
	}
	for _, o := range d.Views {
		if o.Status != Same {
			return true
		}
	}
	for _, o := range d.Routines {
		if o.Status != Same {
			return true
		}
	}
	for _, o := range d.Triggers {
		if o.Status != Same {
			return true
		}
	}
	return false
}

func (d *DataDiff) IsEmpty() bool {
	return d == nil || (len(d.Inserts) == 0 && len(d.Updates) == 0 && len(d.Deletes) == 0)
}

func (d *DataDiff) Changes() int {
	if d == nil {
		return 0
	}
	return len(d.Inserts) + len(d.Updates) + len(d.Deletes)
}

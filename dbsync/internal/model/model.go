package model

import "sort"

// TypeID 是跨方言的规范类型。Raw 保留方言原始类型用于 DDL。
type TypeID string

const (
	TypeUnknown   TypeID = "unknown"
	TypeBool      TypeID = "bool"
	TypeInt       TypeID = "int"
	TypeUint      TypeID = "uint"
	TypeDecimal   TypeID = "decimal"
	TypeFloat     TypeID = "float"
	TypeString    TypeID = "string"
	TypeText      TypeID = "text"
	TypeBinary    TypeID = "binary"
	TypeDate      TypeID = "date"
	TypeTime      TypeID = "time"
	TypeDateTime  TypeID = "datetime"
	TypeTimestamp TypeID = "timestamp"
	TypeJSON      TypeID = "json"
	TypeEnum      TypeID = "enum"
	TypeSet       TypeID = "set"
)

// Type 描述列类型。Length/Precision/Scale 为 -1 表示未指定。
type Type struct {
	ID        TypeID
	Raw       string
	Length    int64
	Precision int
	Scale     int
	Unsigned  bool
	Charset   string
	Collation string
}

type Column struct {
	Name           string
	Ordinal        int
	Type           Type
	Nullable       bool
	Default        *string
	DefaultSet     bool
	Identity       bool
	Generated      bool
	GenerationExpr string
	Comment        string
}

type IndexColumn struct {
	Name   string
	Desc   bool
	Prefix int
}

type Index struct {
	Name    string
	Unique  bool
	Primary bool
	Type    string
	Columns []IndexColumn
}

type ConstraintKind string

const (
	ConstraintPK     ConstraintKind = "pk"
	ConstraintUnique ConstraintKind = "unique"
	ConstraintFK     ConstraintKind = "fk"
	ConstraintCheck  ConstraintKind = "check"
)

type Constraint struct {
	Name       string
	Kind       ConstraintKind
	Columns    []string
	RefSchema  string
	RefTable   string
	RefColumns []string
	OnUpdate   string
	OnDelete   string
	CheckExpr  string
}

type Table struct {
	Schema      string
	Name        string
	Columns     []Column
	Indexes     []Index
	Constraints []Constraint
	Comment     string
	Collation   string
}

type View struct {
	Schema       string
	Name         string
	Definition   string
	Dependencies []string
}

type Routine struct {
	Schema     string
	Name       string
	Kind       string // PROCEDURE / FUNCTION
	Definition string
}

type Trigger struct {
	Schema    string
	Name      string
	Table     string
	Timing    string // BEFORE / AFTER
	Event     string // INSERT / UPDATE / DELETE
	Statement string
}

type Schema struct {
	Name     string
	Tables   []Table
	Views    []View
	Routines []Routine
	Triggers []Trigger
}

// Sort 统一对象顺序，保证快照与摘要可复现。
func (s *Schema) Sort() {
	sort.Slice(s.Tables, func(i, j int) bool { return s.Tables[i].Name < s.Tables[j].Name })
	for i := range s.Tables {
		t := &s.Tables[i]
		sort.SliceStable(t.Columns, func(a, b int) bool {
			if t.Columns[a].Ordinal != t.Columns[b].Ordinal {
				return t.Columns[a].Ordinal < t.Columns[b].Ordinal
			}
			return t.Columns[a].Name < t.Columns[b].Name
		})
		sort.Slice(t.Indexes, func(a, b int) bool { return t.Indexes[a].Name < t.Indexes[b].Name })
		sort.Slice(t.Constraints, func(a, b int) bool { return t.Constraints[a].Name < t.Constraints[b].Name })
	}
	sort.Slice(s.Views, func(i, j int) bool { return s.Views[i].Name < s.Views[j].Name })
	sort.Slice(s.Routines, func(i, j int) bool {
		if s.Routines[i].Name != s.Routines[j].Name {
			return s.Routines[i].Name < s.Routines[j].Name
		}
		return s.Routines[i].Kind < s.Routines[j].Kind
	})
	sort.Slice(s.Triggers, func(i, j int) bool { return s.Triggers[i].Name < s.Triggers[j].Name })
}

func (t *Table) Column(name string) *Column {
	for i := range t.Columns {
		if t.Columns[i].Name == name {
			return &t.Columns[i]
		}
	}
	return nil
}

func (s *Schema) Table(name string) *Table {
	for i := range s.Tables {
		if s.Tables[i].Name == name {
			return &s.Tables[i]
		}
	}
	return nil
}

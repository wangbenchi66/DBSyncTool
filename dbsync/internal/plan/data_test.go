package plan

import (
	"strings"
	"testing"

	"gitee.com/wangbenchi66/dbsync/internal/diff"
	"gitee.com/wangbenchi66/dbsync/internal/model"
	"gitee.com/wangbenchi66/dbsync/internal/value"
)

func TestBuildDataDialects(t *testing.T) {
	tbl := &model.Table{Name: "users", Schema: "app", Columns: []model.Column{
		{Name: "id", Type: model.Type{ID: model.TypeInt}},
		{Name: "name", Type: model.Type{ID: model.TypeString}},
		{Name: "active", Type: model.Type{ID: model.TypeBool}},
		{Name: "blob", Type: model.Type{ID: model.TypeBinary}},
	}}
	d := &diff.SchemaDiff{Tables: []diff.TableDiff{{
		Name: "users", Source: tbl, Target: tbl,
		Data: &diff.DataDiff{
			KeyColumns: []string{"id"},
			Inserts:    []diff.Row{{"id": value.Int(1), "name": value.String("a"), "active": value.Bool(true), "blob": value.Bytes([]byte{1, 2})}},
		},
	}}}
	cases := map[string]string{"postgres": `TRUE`, "sqlserver": `[app].[users]`, "sqlite": `"app"."users"`}
	for dialect, want := range cases {
		p := BuildData(d, nil, nil, dialect, Options{})
		if len(p.Statements) == 0 || !strings.Contains(p.Statements[0].SQL, want) {
			t.Fatalf("%s: %+v", dialect, p.Statements)
		}
	}
	p := BuildData(d, nil, nil, "postgres", Options{})
	if !strings.Contains(p.Statements[0].SQL, `'\x0102'`) {
		t.Fatalf("pg bytes: %s", p.Statements[0].SQL)
	}
}

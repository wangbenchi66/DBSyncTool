package plan

import (
	"strings"
	"testing"

	"gitee.com/wangbenchi66/dbsync/internal/diff"
	"gitee.com/wangbenchi66/dbsync/internal/model"
)

func TestBuildSQLiteDDL(t *testing.T) {
	col := func(name string) model.Column {
		return model.Column{Name: name, Type: model.Type{ID: model.TypeString}}
	}
	target := &model.Table{Schema: "main", Name: "users", Columns: []model.Column{col("id")}}
	source := &model.Table{Schema: "main", Name: "users", Columns: []model.Column{col("id"), col("email")}}
	d := &diff.SchemaDiff{
		SourceName: "src", TargetName: "main",
		Tables: []diff.TableDiff{{
			Name: "users", Status: diff.Changed, Source: source, Target: target,
			Columns: []diff.ColumnDiff{{Name: "email", Status: diff.Added, Source: &source.Columns[1]}},
			Indexes: []diff.IndexDiff{{Name: "idx_email", Status: diff.Added, Source: &model.Index{Name: "idx_email", Columns: []model.IndexColumn{{Name: "email"}}}}},
		}},
	}
	p := BuildSQLite(d, nil, nil, Options{})
	all := ""
	for _, s := range p.Statements {
		all += s.SQL + "\n"
	}
	if !strings.Contains(all, `ALTER TABLE "main"."users" ADD COLUMN "email" TEXT`) {
		t.Fatalf("缺 ADD COLUMN: %s", all)
	}
	if !strings.Contains(all, `CREATE INDEX "idx_email"`) {
		t.Fatalf("缺 CREATE INDEX: %s", all)
	}

	d.Tables[0].Columns[0].Status = diff.Changed
	d.Tables[0].Columns[0].Source = &source.Columns[0]
	p = BuildSQLite(d, nil, nil, Options{})
	if !p.Blocked {
		t.Fatal("改列应阻断")
	}
}

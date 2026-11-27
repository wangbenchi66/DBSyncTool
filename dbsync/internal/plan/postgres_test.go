package plan

import (
	"strings"
	"testing"

	"gitee.com/wangbenchi66/dbsync/internal/diff"
	"gitee.com/wangbenchi66/dbsync/internal/model"
)

func TestBuildPostgresDDL(t *testing.T) {
	col := func(name string, typ model.TypeID) model.Column {
		return model.Column{Name: name, Type: model.Type{ID: typ}, Nullable: false}
	}
	target := &model.Table{Schema: "public", Name: "users", Columns: []model.Column{col("id", model.TypeInt)}}
	source := &model.Table{Schema: "src", Name: "users", Columns: []model.Column{col("id", model.TypeInt), col("email", model.TypeString)}}
	d := &diff.SchemaDiff{
		SourceName: "src", TargetName: "public",
		Tables: []diff.TableDiff{{
			Name: "users", Status: diff.Changed, Source: source, Target: target,
			Columns: []diff.ColumnDiff{{Name: "email", Status: diff.Added, Source: &source.Columns[1]}},
		}},
	}
	p := BuildPostgres(d, nil, nil, Options{})
	all := ""
	for _, s := range p.Statements {
		all += s.SQL + "\n"
	}
	if !strings.Contains(all, `ALTER TABLE "public"."users" ADD COLUMN "email" TEXT`) {
		t.Fatalf("缺 ADD COLUMN: %s", all)
	}
	if len(p.Diagnostics) == 0 {
		t.Fatal("应提示函数/触发器限制")
	}
}

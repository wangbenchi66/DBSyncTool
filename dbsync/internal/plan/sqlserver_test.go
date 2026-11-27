package plan

import (
	"strings"
	"testing"

	"gitee.com/wangbenchi66/dbsync/internal/diff"
	"gitee.com/wangbenchi66/dbsync/internal/model"
)

func TestBuildSQLServerDDL(t *testing.T) {
	col := func(name string, typ model.TypeID) model.Column {
		return model.Column{Name: name, Type: model.Type{ID: typ}, Nullable: false}
	}
	target := &model.Table{Schema: "dbo", Name: "users", Columns: []model.Column{col("id", model.TypeInt)}}
	source := &model.Table{Schema: "dbo", Name: "users", Columns: []model.Column{col("id", model.TypeInt), col("email", model.TypeString)}}
	d := &diff.SchemaDiff{
		SourceName: "src", TargetName: "dbo",
		Tables: []diff.TableDiff{{
			Name: "users", Status: diff.Changed, Source: source, Target: target,
			Columns: []diff.ColumnDiff{{Name: "email", Status: diff.Added, Source: &source.Columns[1]}},
		}},
	}
	p := BuildSQLServer(d, nil, nil, Options{})
	all := ""
	for _, s := range p.Statements {
		all += s.SQL + "\n"
	}
	if !strings.Contains(all, `ALTER TABLE [dbo].[users] ADD [email] NVARCHAR(MAX)`) {
		t.Fatalf("缺 ADD COLUMN: %s", all)
	}
	if len(p.Diagnostics) == 0 {
		t.Fatal("应有预览提示")
	}
}

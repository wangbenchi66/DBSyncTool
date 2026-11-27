package rules

import (
	"testing"

	"gitee.com/wangbenchi66/dbsync/internal/diff"
	"gitee.com/wangbenchi66/dbsync/internal/model"
)

func TestFilter(t *testing.T) {
	d := &diff.SchemaDiff{
		Tables: []diff.TableDiff{
			{Name: "tmp_a", Source: &model.Table{Schema: "app", Name: "tmp_a"}},
			{Name: "users", Source: &model.Table{Schema: "app", Name: "users"}, Columns: []diff.ColumnDiff{{Name: "legacy"}, {Name: "email"}}},
		},
		Views: []diff.ObjectDiff{{Name: "v_tmp"}},
	}
	rs := []Rule{{Kind: "table", Pattern: "tmp_*"}, {Kind: "column", Pattern: "users.legacy"}, {Kind: "view", Pattern: "v_*"}}
	Filter(d, rs)
	if len(d.Tables) != 1 || d.Tables[0].Name != "users" {
		t.Fatalf("表过滤不符: %+v", d.Tables)
	}
	if len(d.Tables[0].Columns) != 1 || d.Tables[0].Columns[0].Name != "email" {
		t.Fatalf("列过滤不符: %+v", d.Tables[0].Columns)
	}
	if len(d.Views) != 0 {
		t.Fatalf("视图过滤不符: %+v", d.Views)
	}
}

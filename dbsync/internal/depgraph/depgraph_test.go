package depgraph

import (
	"testing"

	"gitee.com/wangbenchi66/dbsync/internal/model"
)

func TestBuild(t *testing.T) {
	s := &model.Schema{Tables: []model.Table{
		{Name: "parent", Schema: "app"},
		{Name: "child", Schema: "app", Constraints: []model.Constraint{{Kind: model.ConstraintFK, Name: "fk_child_parent", RefSchema: "app", RefTable: "parent"}}},
	}}
	g := Build(s)
	if len(g.Nodes) != 2 || len(g.Edges) != 1 {
		t.Fatalf("graph 不符: %+v", g)
	}
	if g.Edges[0].From != "child" || g.Edges[0].To != "parent" {
		t.Fatalf("edge 不符: %+v", g.Edges[0])
	}
}

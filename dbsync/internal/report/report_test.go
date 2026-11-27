package report

import (
	"encoding/json"
	"strings"
	"testing"

	"gitee.com/wangbenchi66/dbsync/internal/diff"
)

func sample() *diff.SchemaDiff {
	return &diff.SchemaDiff{
		SourceName: "src", TargetName: "tgt", HasData: true,
		Tables: []diff.TableDiff{{
			Name: "users", Status: diff.Changed,
			Columns: []diff.ColumnDiff{{Name: "email", Status: diff.Changed}},
			Data: &diff.DataDiff{
				Inserts: make([]diff.Row, 1),
				Updates: make([]diff.RowUpdate, 2),
				Deletes: make([]diff.Row, 3),
			},
		}},
		Views: []diff.ObjectDiff{{Kind: "view", Name: "v", Status: diff.Changed}},
	}
}

func TestRender(t *testing.T) {
	md, err := Render(sample(), nil, "md")
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(md, "users") || !strings.Contains(md, "只读生成") {
		t.Fatalf("markdown 不符: %s", md)
	}
	htmlBody, err := Render(sample(), nil, "html")
	if err != nil || !strings.Contains(htmlBody, "<table>") {
		t.Fatalf("html 不符: %v", err)
	}
	js, err := Render(sample(), nil, "json")
	if err != nil {
		t.Fatal(err)
	}
	var doc Document
	if err := json.Unmarshal([]byte(js), &doc); err != nil {
		t.Fatal(err)
	}
	if doc.Source != "src" || len(doc.Tables) != 1 {
		t.Fatalf("json 不符: %+v", doc)
	}
	sarif, err := Render(sample(), nil, "sarif")
	if err != nil || !strings.Contains(sarif, `"version": "2.1.0"`) {
		t.Fatalf("sarif 不符: %v %s", err, sarif)
	}
}

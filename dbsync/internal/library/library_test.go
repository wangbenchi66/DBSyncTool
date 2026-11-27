package library

import (
	"os"
	"path/filepath"
	"testing"
	"time"

	"gitee.com/wangbenchi66/dbsync/internal/model"
	"gitee.com/wangbenchi66/dbsync/internal/snapshot"
	"gitee.com/wangbenchi66/dbsync/internal/value"
)

func TestListImportDelete(t *testing.T) {
	dir := t.TempDir()
	srcDir := t.TempDir()
	src := filepath.Join(srcDir, "base.dbsync")
	schema := &model.Schema{Name: "app", Tables: []model.Table{{Schema: "app", Name: "t", Columns: []model.Column{{Name: "id", Ordinal: 1, Type: model.Type{ID: model.TypeInt}}}}}}
	if err := snapshot.Write(src, schema, snapshot.WriteOptions{
		CreatedAt: time.Date(2026, 11, 27, 10, 0, 0, 0, time.UTC),
		Data:      map[string]snapshot.RowSet{"t": {Columns: []string{"id"}, Rows: []snapshot.Row{{"id": value.Int(1)}}}},
	}); err != nil {
		t.Fatal(err)
	}
	e, err := Import(dir, src)
	if err != nil {
		t.Fatal(err)
	}
	if e.Name != "base.dbsync" || e.Rows != 1 || !e.HasData {
		t.Fatalf("entry 不符: %+v", e)
	}
	list, err := List(dir)
	if err != nil || len(list) != 1 {
		t.Fatalf("list=%+v err=%v", list, err)
	}
	if err := Delete(dir, e.Path); err != nil {
		t.Fatal(err)
	}
	if _, err := os.Stat(e.Path); !os.IsNotExist(err) {
		t.Fatal("删除失败")
	}
}

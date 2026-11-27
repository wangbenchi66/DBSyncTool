package snapshot

import (
	"bytes"
	"os"
	"path/filepath"
	"testing"
	"time"

	"gitee.com/wangbenchi66/dbsync/internal/model"
)

func testSchema() *model.Schema {
	return &model.Schema{
		Name: "app",
		Tables: []model.Table{{
			Schema: "app",
			Name:   "users",
			Columns: []model.Column{
				{Name: "id", Ordinal: 1, Type: model.Type{ID: model.TypeUint, Raw: "bigint unsigned"}, Identity: true},
				{Name: "email", Ordinal: 2, Type: model.Type{ID: model.TypeString, Raw: "varchar(255)", Length: 255, Collation: "utf8mb4_bin"}, Nullable: false},
			},
			Collation: "utf8mb4_bin",
		}},
		Views:    []model.View{{Schema: "app", Name: "v_users", Definition: "select 1"}},
		Routines: []model.Routine{{Schema: "app", Name: "p", Kind: "PROCEDURE", Definition: "select 1"}},
		Triggers: []model.Trigger{{Schema: "app", Name: "trg", Table: "users", Timing: "BEFORE", Event: "INSERT", Statement: "SET NEW.id=NEW.id"}},
	}
}

func TestWriteReadSchema(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "baseline.dbsync")
	created := time.Date(2026, 11, 27, 10, 0, 0, 0, time.UTC)
	if err := WriteSchema(path, testSchema(), WriteOptions{Source: "mysql://test", CreatedAt: created}); err != nil {
		t.Fatal(err)
	}
	s, err := Read(path)
	if err != nil {
		t.Fatal(err)
	}
	if s.Manifest.SchemaName != "app" || s.Manifest.TableCount != 1 || s.Manifest.HasData {
		t.Fatalf("manifest 不符: %+v", s.Manifest)
	}
	if len(s.Schema.Tables) != 1 || s.Schema.Tables[0].Columns[1].Type.Collation != "utf8mb4_bin" {
		t.Fatalf("schema 不符: %+v", s.Schema)
	}
	if err := Verify(path); err != nil {
		t.Fatal(err)
	}
}

func TestWriteDeterministic(t *testing.T) {
	dir := t.TempDir()
	a := filepath.Join(dir, "a.dbsync")
	b := filepath.Join(dir, "b.dbsync")
	created := time.Date(2026, 11, 27, 10, 0, 0, 0, time.UTC)
	if err := WriteSchema(a, testSchema(), WriteOptions{CreatedAt: created}); err != nil {
		t.Fatal(err)
	}
	if err := WriteSchema(b, testSchema(), WriteOptions{CreatedAt: created}); err != nil {
		t.Fatal(err)
	}
	ab, err := os.ReadFile(a)
	if err != nil {
		t.Fatal(err)
	}
	bb, err := os.ReadFile(b)
	if err != nil {
		t.Fatal(err)
	}
	if !bytes.Equal(ab, bb) {
		t.Fatalf("同样输入应生成字节一致的明文快照")
	}
}

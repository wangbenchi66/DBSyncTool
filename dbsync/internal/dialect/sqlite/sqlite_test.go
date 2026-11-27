package sqlite

import (
	"context"
	"database/sql"
	"path/filepath"
	"testing"

	"gitee.com/wangbenchi66/dbsync/internal/model"

	_ "modernc.org/sqlite"
)

func TestIntrospectSQLite(t *testing.T) {
	path := filepath.Join(t.TempDir(), "test.db")
	db, err := sql.Open("sqlite", path)
	if err != nil {
		t.Fatal(err)
	}
	_, err = db.Exec(`
CREATE TABLE parent (
  id INTEGER PRIMARY KEY,
  name TEXT NOT NULL
);
CREATE TABLE child (
  id INTEGER PRIMARY KEY,
  pid INTEGER,
  CONSTRAINT fk_child_parent FOREIGN KEY (pid) REFERENCES parent(id)
);
CREATE INDEX idx_child_pid ON child(pid);
CREATE VIEW v_parent AS SELECT id, name FROM parent;
CREATE TRIGGER trg_parent AFTER INSERT ON parent BEGIN UPDATE parent SET name = name WHERE id = NEW.id; END;
`)
	if err != nil {
		t.Fatal(err)
	}
	db.Close()

	ro, err := Open(path)
	if err != nil {
		t.Fatal(err)
	}
	defer ro.Close()
	s, err := Introspect(context.Background(), ro, "main")
	if err != nil {
		t.Fatal(err)
	}
	if len(s.Tables) != 2 {
		t.Fatalf("表数=%d", len(s.Tables))
	}
	parent := s.Table("parent")
	if parent == nil || parent.Column("id") == nil {
		t.Fatal("缺 parent/id")
	}
	if !parent.Column("id").Identity || parent.Column("id").Type.ID != model.TypeInt {
		t.Fatalf("id 类型不符: %+v", parent.Column("id"))
	}
	child := s.Table("child")
	if child == nil {
		t.Fatal("缺 child")
	}
	var fk bool
	for _, c := range child.Constraints {
		if c.Kind == model.ConstraintFK && c.RefTable == "parent" {
			fk = true
		}
	}
	if !fk {
		t.Fatalf("外键缺失: %+v", child.Constraints)
	}
	if len(s.Views) != 1 || len(s.Triggers) != 1 {
		t.Fatalf("视图/触发器不符: %+v %+v", s.Views, s.Triggers)
	}
	if len(s.Views[0].Dependencies) != 1 || s.Views[0].Dependencies[0] != "parent" {
		t.Fatalf("视图依赖不符: %+v", s.Views[0])
	}
}

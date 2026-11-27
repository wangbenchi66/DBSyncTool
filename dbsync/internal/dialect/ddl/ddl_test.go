package ddl

import (
	"os"
	"path/filepath"
	"testing"

	"gitee.com/wangbenchi66/dbsync/internal/model"
)

const sample = `
CREATE TABLE users (
  id BIGINT NOT NULL AUTO_INCREMENT,
  email VARCHAR(100) COLLATE utf8mb4_bin NOT NULL,
  amount DECIMAL(10,2) DEFAULT '0.00',
  active TINYINT(1) DEFAULT 1,
  created_at TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (id),
  UNIQUE KEY uq_email (email)
);
CREATE TABLE child (
  id BIGINT PRIMARY KEY,
  uid BIGINT,
  CONSTRAINT fk_child_user FOREIGN KEY (uid) REFERENCES users(id) ON DELETE CASCADE,
  CHECK (uid > 0)
);
CREATE VIEW v_users AS SELECT id, email FROM users;
CREATE TRIGGER trg_users BEFORE INSERT ON users FOR EACH ROW SET @x = 1;
`

func TestParse(t *testing.T) {
	s, err := Parse(sample)
	if err != nil {
		t.Fatal(err)
	}
	if len(s.Tables) != 2 {
		t.Fatalf("表=%d", len(s.Tables))
	}
	u := s.Table("users")
	if u == nil {
		t.Fatal("缺 users")
	}
	if c := u.Column("email"); c == nil || c.Type.ID != model.TypeString || c.Type.Length != 100 || c.Type.Collation != "utf8mb4_bin" {
		t.Fatalf("email: %+v", c)
	}
	if c := u.Column("amount"); c == nil || c.Type.ID != model.TypeDecimal || c.Type.Precision != 10 || c.Type.Scale != 2 {
		t.Fatalf("amount: %+v", c)
	}
	if c := u.Column("active"); c == nil || c.Type.ID != model.TypeBool {
		t.Fatalf("active: %+v", c)
	}
	if c := u.Column("id"); c == nil || !c.Identity || c.Nullable {
		t.Fatalf("id: %+v", c)
	}
	var fk bool
	for _, c := range s.Table("child").Constraints {
		if c.Kind == model.ConstraintFK && c.RefTable == "users" && c.OnDelete == "CASCADE" {
			fk = true
		}
	}
	if !fk {
		t.Fatalf("fk: %+v", s.Table("child").Constraints)
	}
	if len(s.Views) != 1 || len(s.Triggers) != 1 {
		t.Fatalf("views=%d triggers=%d", len(s.Views), len(s.Triggers))
	}
}

func TestReadDir(t *testing.T) {
	dir := t.TempDir()
	if err := os.WriteFile(filepath.Join(dir, "a.sql"), []byte(sample), 0o644); err != nil {
		t.Fatal(err)
	}
	s, err := ReadDir(dir)
	if err != nil {
		t.Fatal(err)
	}
	if len(s.Tables) != 2 {
		t.Fatalf("tables=%d", len(s.Tables))
	}
}

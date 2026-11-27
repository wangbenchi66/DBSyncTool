//go:build integration

package mysql

import (
	"context"
	"database/sql"
	"os"
	"testing"

	drivermysql "github.com/go-sql-driver/mysql"

	"gitee.com/wangbenchi66/dbsync/internal/model"
)

func TestIntrospectMySQL(t *testing.T) {
	base := os.Getenv("DBSYNC_TEST_MYSQL_DSN")
	if base == "" {
		t.Skip("未设置 DBSYNC_TEST_MYSQL_DSN")
	}
	cfg, err := drivermysql.ParseDSN(base)
	if err != nil {
		t.Fatal(err)
	}
	admin, err := sql.Open("mysql", base)
	if err != nil {
		t.Fatal(err)
	}
	defer admin.Close()
	const dbName = "dbsync_m1_it"
	if _, err := admin.Exec("CREATE DATABASE IF NOT EXISTS " + Quote(dbName) + " CHARACTER SET utf8mb4 COLLATE utf8mb4_bin"); err != nil {
		t.Fatal(err)
	}
	cfg.DBName = dbName
	cfg.MultiStatements = true
	cfg.ParseTime = true
	db, err := sql.Open("mysql", cfg.FormatDSN())
	if err != nil {
		t.Fatal(err)
	}
	defer db.Close()
	ctx := context.Background()
	if v, err := Version(ctx, db); err == nil {
		t.Logf("MySQL version: %s", v)
	}
	cleanup := func() {
		stmts := []string{
			"DROP VIEW IF EXISTS v_parent",
			"DROP TRIGGER IF EXISTS trg_parent",
			"DROP PROCEDURE IF EXISTS p_parent",
			"DROP TABLE IF EXISTS child",
			"DROP TABLE IF EXISTS parent",
		}
		for _, s := range stmts {
			_, _ = db.ExecContext(ctx, s)
		}
	}
	cleanup()
	defer cleanup()
	schemaSQL := `
CREATE TABLE parent (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  name VARCHAR(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  amount DECIMAL(12,2) NOT NULL DEFAULT 0.00,
  flag TINYINT(1) NOT NULL DEFAULT 0,
  created_at DATETIME(6) NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uk_parent_name (name),
  CONSTRAINT chk_amount CHECK (amount >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
CREATE TABLE child (
  id BIGINT NOT NULL AUTO_INCREMENT,
  parent_id BIGINT UNSIGNED NOT NULL,
  PRIMARY KEY (id),
  KEY idx_parent (parent_id),
  CONSTRAINT fk_child_parent FOREIGN KEY (parent_id) REFERENCES parent(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE VIEW v_parent AS SELECT id, name FROM parent;
CREATE TRIGGER trg_parent BEFORE INSERT ON parent FOR EACH ROW SET NEW.flag = 0;
CREATE PROCEDURE p_parent() SELECT 1;
`
	if _, err := db.ExecContext(ctx, schemaSQL); err != nil {
		t.Fatal(err)
	}
	s, err := Introspect(ctx, db, dbName)
	if err != nil {
		t.Fatal(err)
	}
	if len(s.Tables) != 2 {
		t.Fatalf("表数=%d, want 2", len(s.Tables))
	}
	parent := s.Table("parent")
	if parent == nil {
		t.Fatal("缺 parent")
	}
	if parent.Collation != "utf8mb4_bin" {
		t.Fatalf("表 collation=%q", parent.Collation)
	}
	id := parent.Column("id")
	if id == nil || !id.Identity || id.Type.ID != model.TypeUint {
		t.Fatalf("id 列不符: %+v", id)
	}
	name := parent.Column("name")
	if name == nil || name.Type.Collation != "utf8mb4_bin" {
		t.Fatalf("name collation 不符: %+v", name)
	}
	amount := parent.Column("amount")
	if amount == nil || amount.Type.ID != model.TypeDecimal || amount.Type.Precision != 12 || amount.Type.Scale != 2 {
		t.Fatalf("amount 类型不符: %+v", amount)
	}
	flag := parent.Column("flag")
	if flag == nil || flag.Type.ID != model.TypeBool {
		t.Fatalf("flag 类型不符: %+v", flag)
	}
	if len(parent.Indexes) < 2 {
		t.Fatalf("索引不足: %+v", parent.Indexes)
	}
	var hasFK, hasCheck bool
	for _, c := range s.Table("child").Constraints {
		if c.Kind == model.ConstraintFK && c.RefTable == "parent" {
			hasFK = true
		}
	}
	for _, c := range parent.Constraints {
		if c.Kind == model.ConstraintCheck && c.CheckExpr != "" {
			hasCheck = true
		}
	}
	if !hasFK || !hasCheck {
		t.Fatalf("约束缺失: fk=%v check=%v", hasFK, hasCheck)
	}
	if len(s.Views) != 1 || s.Views[0].Name != "v_parent" {
		t.Fatalf("视图不符: %+v", s.Views)
	}
	if len(s.Views[0].Dependencies) != 1 || s.Views[0].Dependencies[0] != "parent" {
		t.Fatalf("视图依赖不符: %+v", s.Views[0])
	}
	if len(s.Routines) != 1 || s.Routines[0].Name != "p_parent" {
		t.Fatalf("例程不符: %+v", s.Routines)
	}
	if len(s.Triggers) != 1 || s.Triggers[0].Table != "parent" {
		t.Fatalf("触发器不符: %+v", s.Triggers)
	}
}

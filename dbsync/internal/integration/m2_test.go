//go:build integration

package integration

import (
	"context"
	"database/sql"
	"os"
	"strings"
	"testing"

	drivermysql "github.com/go-sql-driver/mysql"

	"gitee.com/wangbenchi66/dbsync/internal/data"
	"gitee.com/wangbenchi66/dbsync/internal/dialect/mysql"
	"gitee.com/wangbenchi66/dbsync/internal/diff"
	"gitee.com/wangbenchi66/dbsync/internal/plan"
)

func openSchema(t *testing.T, base, name string) *sql.DB {
	t.Helper()
	cfg, err := drivermysql.ParseDSN(base)
	if err != nil {
		t.Fatal(err)
	}
	cfg.DBName = name
	cfg.MultiStatements = true
	cfg.ParseTime = true
	db, err := sql.Open("mysql", cfg.FormatDSN())
	if err != nil {
		t.Fatal(err)
	}
	if err := db.Ping(); err != nil {
		t.Fatal(err)
	}
	return db
}

func TestM2MySQLRoundTrip(t *testing.T) {
	base := os.Getenv("DBSYNC_TEST_MYSQL_DSN")
	if base == "" {
		t.Skip("未设置 DBSYNC_TEST_MYSQL_DSN")
	}
	admin, err := sql.Open("mysql", base)
	if err != nil {
		t.Fatal(err)
	}
	defer admin.Close()
	ctx := context.Background()
	const srcName, tgtName = "dbsync_m2_src", "dbsync_m2_tgt"
	for _, n := range []string{srcName, tgtName} {
		if _, err := admin.ExecContext(ctx, "DROP DATABASE IF EXISTS "+mysql.Quote(n)); err != nil {
			t.Fatal(err)
		}
		if _, err := admin.ExecContext(ctx, "CREATE DATABASE "+mysql.Quote(n)+" CHARACTER SET utf8mb4 COLLATE utf8mb4_bin"); err != nil {
			t.Fatal(err)
		}
	}
	src := openSchema(t, base, srcName)
	defer src.Close()
	tgt := openSchema(t, base, tgtName)
	defer tgt.Close()

	srcSQL := `
CREATE TABLE users (
  id INT NOT NULL,
  email VARCHAR(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  name VARCHAR(50) NULL,
  amount DECIMAL(10,2) NOT NULL DEFAULT 0.00,
  created_at DATETIME(6) NULL,
  flag TINYINT(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (id),
  UNIQUE KEY uk_email (email)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
CREATE VIEW v_users AS SELECT id, email FROM users;
INSERT INTO users (id,email,name,amount,created_at,flag) VALUES
 (1,'a@x','Alice',10.00,'2026-01-01 00:00:00',0),
 (2,'b@x','Bob',20.00,'2026-01-02 00:00:00',1),
 (3,'c@x','Carol',30.00,'2026-01-03 00:00:00',0);
`
	tgtSQL := `
CREATE TABLE users (
  id INT NOT NULL,
  email VARCHAR(80) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  name VARCHAR(50) NULL,
  amount DECIMAL(10,2) NOT NULL DEFAULT 0.00,
  old_col VARCHAR(10) NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uk_email (email)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
CREATE VIEW v_users AS SELECT id FROM users;
INSERT INTO users (id,email,name,amount,old_col) VALUES
 (1,'a@x','Alice',10.00,'x'),
 (2,'b@x','Bobby',20.00,'x'),
 (4,'d@x','Dave',40.00,'x');
`
	if _, err := src.ExecContext(ctx, srcSQL); err != nil {
		t.Fatal(err)
	}
	if _, err := tgt.ExecContext(ctx, tgtSQL); err != nil {
		t.Fatal(err)
	}
	ss, err := mysql.Introspect(ctx, src, srcName)
	if err != nil {
		t.Fatal(err)
	}
	ts, err := mysql.Introspect(ctx, tgt, tgtName)
	if err != nil {
		t.Fatal(err)
	}
	d := diff.CompareSchema(ss, ts)
	if !d.HasChanges() {
		t.Fatal("应检测到结构差异")
	}
	if err := data.Attach(ctx, src, tgt, d); err != nil {
		t.Fatal(err)
	}
	p := plan.Build(d, ss, ts, plan.Options{AllowDestructive: true, CompareData: true})
	if p.Blocked {
		t.Fatalf("预览被阻断: %+v", p.Diagnostics)
	}
	if len(p.Statements) == 0 || p.Fingerprint == "" {
		t.Fatal("预览为空或缺少指纹")
	}
	var hasDropCol, hasUpdate bool
	for _, s := range p.Statements {
		if strings.Contains(s.SQL, "DROP COLUMN `old_col`") {
			hasDropCol = true
		}
		if strings.HasPrefix(s.SQL, "UPDATE ") {
			hasUpdate = true
		}
	}
	if !hasDropCol || !hasUpdate {
		t.Fatalf("预览缺少预期语句: dropCol=%v update=%v", hasDropCol, hasUpdate)
	}

	// 只读边界：生成预览后目标必须完全不变。
	var name string
	if err := tgt.QueryRowContext(ctx, "SELECT name FROM users WHERE id = 2").Scan(&name); err != nil {
		t.Fatal(err)
	}
	if name != "Bobby" {
		t.Fatalf("目标被修改：name=%q，期望 Bobby", name)
	}
	ts2, err := mysql.Introspect(ctx, tgt, tgtName)
	if err != nil {
		t.Fatal(err)
	}
	if ts2.Table("users").Column("old_col") == nil {
		t.Fatal("目标被修改：old_col 已不存在")
	}
	if !diff.CompareSchema(ss, ts2).HasChanges() {
		t.Fatal("目标被修改：结构差异消失了")
	}
}

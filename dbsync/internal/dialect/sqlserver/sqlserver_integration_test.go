//go:build integration

package sqlserver

import (
	"context"
	"database/sql"
	"os"
	"testing"

	_ "github.com/microsoft/go-mssqldb"

	"gitee.com/wangbenchi66/dbsync/internal/model"
)

func TestIntrospectSQLServer(t *testing.T) {
	dsn := os.Getenv("DBSYNC_TEST_MSSQL_DSN")
	if dsn == "" {
		t.Skip("未设置 DBSYNC_TEST_MSSQL_DSN")
	}
	admin, err := sql.Open("sqlserver", dsn)
	if err != nil {
		t.Fatal(err)
	}
	defer admin.Close()
	ctx := context.Background()
	if _, err := admin.ExecContext(ctx, `IF DB_ID('dbsync_mssql_it') IS NOT NULL DROP DATABASE dbsync_mssql_it`); err != nil {
		t.Fatal(err)
	}
	if _, err := admin.ExecContext(ctx, `CREATE DATABASE dbsync_mssql_it`); err != nil {
		t.Fatal(err)
	}
	// 切到目标库
	db, err := sql.Open("sqlserver", dsnWithDB(dsn, "dbsync_mssql_it"))
	if err != nil {
		t.Fatal(err)
	}
	defer db.Close()
	setup := []string{
		`CREATE TABLE dbo.users (
  id BIGINT IDENTITY(1,1) PRIMARY KEY,
  email NVARCHAR(100) COLLATE Latin1_General_100_BIN2 NOT NULL,
  amount DECIMAL(10,2) NOT NULL DEFAULT 0,
  active BIT NOT NULL DEFAULT 1,
  created_at DATETIME2 NULL,
  CONSTRAINT uq_users_email UNIQUE (email),
  CONSTRAINT ck_amount CHECK (amount >= 0)
)`,
		`CREATE TABLE dbo.child (
  id BIGINT PRIMARY KEY,
  uid BIGINT NULL,
  CONSTRAINT fk_child_user FOREIGN KEY (uid) REFERENCES dbo.users(id)
)`,
		`CREATE INDEX idx_child_uid ON dbo.child(uid)`,
		`CREATE VIEW dbo.v_users AS SELECT id, email FROM dbo.users`,
		`CREATE PROCEDURE dbo.p_one AS SELECT 1`,
		`CREATE TRIGGER trg_users ON dbo.users AFTER INSERT AS BEGIN SET NOCOUNT ON; END`,
	}
	for _, s := range setup {
		if _, err := db.ExecContext(ctx, s); err != nil {
			t.Fatal(err)
		}
	}
	ro, err := Open(dsnWithDB(dsn, "dbsync_mssql_it"))
	if err != nil {
		t.Fatal(err)
	}
	defer ro.Close()
	s, err := Introspect(ctx, ro, "dbo")
	if err != nil {
		t.Fatal(err)
	}
	if len(s.Tables) != 2 {
		t.Fatalf("表数=%d", len(s.Tables))
	}
	users := s.Table("users")
	if users == nil {
		t.Fatal("缺 users")
	}
	if c := users.Column("id"); c == nil || !c.Identity || c.Type.ID != model.TypeInt {
		t.Fatalf("id 不符: %+v", c)
	}
	if c := users.Column("email"); c == nil || c.Type.ID != model.TypeString || c.Type.Length != 100 {
		t.Fatalf("email 不符: %+v", c)
	}
	if c := users.Column("amount"); c == nil || c.Type.ID != model.TypeDecimal || c.Type.Precision != 10 || c.Type.Scale != 2 {
		t.Fatalf("amount 不符: %+v", c)
	}
	if c := users.Column("active"); c == nil || c.Type.ID != model.TypeBool {
		t.Fatalf("active 不符: %+v", c)
	}
	var hasFK, hasCheck bool
	for _, c := range s.Table("child").Constraints {
		if c.Kind == model.ConstraintFK && c.RefTable == "users" {
			hasFK = true
		}
	}
	for _, c := range users.Constraints {
		if c.Kind == model.ConstraintCheck && c.CheckExpr != "" {
			hasCheck = true
		}
	}
	if !hasFK || !hasCheck {
		t.Fatalf("约束缺失: fk=%v check=%v", hasFK, hasCheck)
	}
	if len(s.Views) != 1 || len(s.Routines) != 1 || len(s.Triggers) != 1 {
		t.Fatalf("对象不符: views=%d routines=%d triggers=%d", len(s.Views), len(s.Routines), len(s.Triggers))
	}
}

func dsnWithDB(dsn, db string) string {
	// 测试 DSN 固定为 ...?database=master；这里做最小替换。
	if i := indexOf(dsn, "database="); i >= 0 {
		j := i + len("database=")
		k := j
		for k < len(dsn) && dsn[k] != '&' && dsn[k] != ';' {
			k++
		}
		return dsn[:j] + db + dsn[k:]
	}
	return dsn
}

func indexOf(s, sub string) int {
	for i := 0; i+len(sub) <= len(s); i++ {
		if s[i:i+len(sub)] == sub {
			return i
		}
	}
	return -1
}

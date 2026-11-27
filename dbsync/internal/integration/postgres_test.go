//go:build integration

package integration

import (
	"context"
	"database/sql"
	"os"
	"testing"

	_ "github.com/jackc/pgx/v5/stdlib"

	"gitee.com/wangbenchi66/dbsync/internal/service"
)

func TestPostgresCompareReadOnly(t *testing.T) {
	dsn := os.Getenv("DBSYNC_TEST_PG_DSN")
	if dsn == "" {
		dsn = "postgres://postgres:dbsync@127.0.0.1:55432/dbsync?sslmode=disable"
	}
	admin, err := sql.Open("pgx", dsn)
	if err != nil {
		t.Fatal(err)
	}
	defer admin.Close()
	ctx := context.Background()
	if _, err := admin.ExecContext(ctx, `DROP SCHEMA IF EXISTS dbsync_pg_src CASCADE; DROP SCHEMA IF EXISTS dbsync_pg_tgt CASCADE; CREATE SCHEMA dbsync_pg_src; CREATE SCHEMA dbsync_pg_tgt`); err != nil {
		t.Fatal(err)
	}
	if _, err := admin.ExecContext(ctx, `
CREATE TABLE dbsync_pg_src.users (id BIGINT PRIMARY KEY, email TEXT COLLATE "C" NOT NULL, amount NUMERIC(10,2), active BOOLEAN);
CREATE TABLE dbsync_pg_tgt.users (id BIGINT PRIMARY KEY, email TEXT COLLATE "C" NOT NULL, amount NUMERIC(10,2), active BOOLEAN);
INSERT INTO dbsync_pg_src.users VALUES (1,'a@x',10.00,true),(2,'b@x',20.00,false);
INSERT INTO dbsync_pg_tgt.users VALUES (1,'a@x',10.00,true),(3,'c@x',30.00,true);
`); err != nil {
		t.Fatal(err)
	}
	svc := service.New()
	v, err := svc.Compare(ctx, service.CompareRequest{
		Source:      service.Endpoint{Kind: "postgres", DSN: dsn, Schema: "dbsync_pg_src"},
		Target:      service.Endpoint{Kind: "postgres", DSN: dsn, Schema: "dbsync_pg_tgt"},
		CompareData: true,
	})
	if err != nil {
		t.Fatal(err)
	}
	if !v.HasData {
		t.Fatal("缺数据差异")
	}
	var found bool
	for _, tb := range v.Tables {
		if tb.Name == "users" && tb.Data != nil {
			found = true
			if tb.Data.Inserts != 1 || tb.Data.Deletes != 1 || tb.Data.Updates != 0 {
				t.Fatalf("数据差异不符: %+v", tb.Data)
			}
		}
	}
	if !found {
		t.Fatal("缺 users 数据差异")
	}
	p, err := svc.Plan(ctx, v.RunID, false)
	if err != nil {
		t.Fatal(err)
	}
	if len(p.Diagnostics) == 0 {
		t.Fatalf("非 MySQL 应给结构预览限制提示: %+v", p)
	}
	var dml int
	for _, ph := range p.Phases {
		dml += len(ph.Statements)
	}
	if dml == 0 {
		t.Fatalf("应生成 PostgreSQL 数据 DML 预览: %+v", p)
	}
	var name string
	if err := admin.QueryRowContext(ctx, `SELECT email FROM dbsync_pg_tgt.users WHERE id=3`).Scan(&name); err != nil {
		t.Fatal(err)
	}
	if name != "c@x" {
		t.Fatalf("目标被修改: %q", name)
	}
}

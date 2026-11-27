//go:build integration

package integration

import (
	"context"
	"database/sql"
	"os"
	"testing"

	_ "github.com/microsoft/go-mssqldb"

	"gitee.com/wangbenchi66/dbsync/internal/service"
)

func TestSQLServerCompareReadOnly(t *testing.T) {
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
	for _, db := range []string{"dbsync_mssql_src", "dbsync_mssql_tgt"} {
		_, _ = admin.ExecContext(ctx, `ALTER DATABASE [`+db+`] SET SINGLE_USER WITH ROLLBACK IMMEDIATE`)
		_, _ = admin.ExecContext(ctx, `DROP DATABASE [`+db+`]`)
		if _, err := admin.ExecContext(ctx, `CREATE DATABASE [`+db+`]`); err != nil {
			t.Fatal(err)
		}
	}
	mk := func(dbName, values string) string {
		dsnDB := replaceDatabase(dsn, dbName)
		db, err := sql.Open("sqlserver", dsnDB)
		if err != nil {
			t.Fatal(err)
		}
		defer db.Close()
		if _, err := db.ExecContext(ctx, `CREATE TABLE dbo.users (id BIGINT PRIMARY KEY, email NVARCHAR(100) COLLATE Latin1_General_100_BIN2 NOT NULL, amount DECIMAL(10,2), active BIT)`); err != nil {
			t.Fatal(err)
		}
		if _, err := db.ExecContext(ctx, values); err != nil {
			t.Fatal(err)
		}
		return dsnDB
	}
	srcDSN := mk("dbsync_mssql_src", `INSERT INTO dbo.users VALUES (1,'a@x',10.00,1),(2,'b@x',20.00,0)`)
	tgtDSN := mk("dbsync_mssql_tgt", `INSERT INTO dbo.users VALUES (1,'a@x',10.00,1),(3,'c@x',30.00,1)`)

	svc := service.New()
	v, err := svc.Compare(ctx, service.CompareRequest{
		Source:      service.Endpoint{Kind: "sqlserver", DSN: srcDSN, Schema: "dbo"},
		Target:      service.Endpoint{Kind: "sqlserver", DSN: tgtDSN, Schema: "dbo"},
		CompareData: true,
	})
	if err != nil {
		t.Fatal(err)
	}
	var found bool
	for _, tb := range v.Tables {
		if tb.Name == "users" && tb.Data != nil {
			found = true
			if tb.Data.Inserts != 1 || tb.Data.Deletes != 1 {
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
	if len(p.Phases) != 0 || len(p.Diagnostics) == 0 {
		t.Fatalf("非 MySQL 预览应只给提示: %+v", p)
	}
	tgt, _ := sql.Open("sqlserver", tgtDSN)
	defer tgt.Close()
	var email string
	if err := tgt.QueryRowContext(ctx, `SELECT email FROM dbo.users WHERE id=3`).Scan(&email); err != nil {
		t.Fatal(err)
	}
	if email != "c@x" {
		t.Fatalf("目标被修改: %q", email)
	}
}

func replaceDatabase(dsn, db string) string {
	i := indexOf(dsn, "database=")
	if i < 0 {
		return dsn
	}
	j := i + len("database=")
	k := j
	for k < len(dsn) && dsn[k] != '&' && dsn[k] != ';' {
		k++
	}
	return dsn[:j] + db + dsn[k:]
}

func indexOf(s, sub string) int {
	for i := 0; i+len(sub) <= len(s); i++ {
		if s[i:i+len(sub)] == sub {
			return i
		}
	}
	return -1
}

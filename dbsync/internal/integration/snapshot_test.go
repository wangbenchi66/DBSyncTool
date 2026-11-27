//go:build integration

package integration

import (
	"context"
	"database/sql"
	"os"
	"testing"

	drivermysql "github.com/go-sql-driver/mysql"

	"gitee.com/wangbenchi66/dbsync/internal/service"
)

func TestSnapshotWithDataCompare(t *testing.T) {
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
	const srcName, tgtName = "dbsync_snap_src", "dbsync_snap_tgt"
	for _, n := range []string{srcName, tgtName} {
		if _, err := admin.ExecContext(ctx, "DROP DATABASE IF EXISTS `"+n+"`"); err != nil {
			t.Fatal(err)
		}
		if _, err := admin.ExecContext(ctx, "CREATE DATABASE `"+n+"` CHARACTER SET utf8mb4 COLLATE utf8mb4_bin"); err != nil {
			t.Fatal(err)
		}
	}
	src := openSchema(t, base, srcName)
	defer src.Close()
	tgt := openSchema(t, base, tgtName)
	defer tgt.Close()
	if _, err := src.ExecContext(ctx, "CREATE TABLE users (id INT PRIMARY KEY, name VARCHAR(50) NOT NULL) ENGINE=InnoDB"); err != nil {
		t.Fatal(err)
	}
	if _, err := tgt.ExecContext(ctx, "CREATE TABLE users (id INT PRIMARY KEY, name VARCHAR(50) NOT NULL) ENGINE=InnoDB"); err != nil {
		t.Fatal(err)
	}
	if _, err := src.ExecContext(ctx, "INSERT INTO users VALUES (1,'Alice'),(2,'Bob')"); err != nil {
		t.Fatal(err)
	}
	if _, err := tgt.ExecContext(ctx, "INSERT INTO users VALUES (1,'Alicia'),(3,'Carol')"); err != nil {
		t.Fatal(err)
	}

	cfg, err := drivermysql.ParseDSN(base)
	if err != nil {
		t.Fatal(err)
	}
	cfg.DBName = srcName
	cfg.ParseTime = true
	srcDSN := cfg.FormatDSN()
	cfg.DBName = tgtName
	tgtDSN := cfg.FormatDSN()

	svc := service.New()
	path := t.TempDir() + "/src.dbsync"
	m, err := svc.SnapshotCreate(ctx, service.Endpoint{Kind: "mysql", DSN: srcDSN, Schema: srcName}, path, true)
	if err != nil {
		t.Fatal(err)
	}
	if !m.HasData || len(m.Tables) != 1 || m.Tables[0].RowCount != 2 {
		t.Fatalf("快照数据元信息不符: %+v", m)
	}
	v, err := svc.Compare(ctx, service.CompareRequest{
		Source:      service.Endpoint{Kind: "snapshot", Path: path},
		Target:      service.Endpoint{Kind: "mysql", DSN: tgtDSN, Schema: tgtName},
		CompareData: true,
	})
	if err != nil {
		t.Fatal(err)
	}
	var found bool
	for _, tb := range v.Tables {
		if tb.Name == "users" && tb.Data != nil {
			found = true
			if tb.Data.Inserts != 1 || tb.Data.Deletes != 1 || tb.Data.Updates != 1 {
				t.Fatalf("数据差异不符: %+v", tb.Data)
			}
		}
	}
	if !found {
		t.Fatal("没有 users 数据差异")
	}
	var name string
	if err := tgt.QueryRowContext(ctx, "SELECT name FROM users WHERE id=1").Scan(&name); err != nil {
		t.Fatal(err)
	}
	if name != "Alicia" {
		t.Fatalf("目标被修改: %q", name)
	}
}

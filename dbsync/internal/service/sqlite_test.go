package service

import (
	"context"
	"database/sql"
	"path/filepath"
	"testing"

	_ "modernc.org/sqlite"
)

func TestSQLiteMultiTarget(t *testing.T) {
	dir := t.TempDir()
	mk := func(name, rows string) string {
		path := filepath.Join(dir, name)
		db, err := sql.Open("sqlite", path)
		if err != nil {
			t.Fatal(err)
		}
		defer db.Close()
		if _, err := db.Exec(`CREATE TABLE t (id INTEGER PRIMARY KEY, v TEXT NOT NULL); INSERT INTO t VALUES ` + rows); err != nil {
			t.Fatal(err)
		}
		return path
	}
	src := mk("src.db", "(1,'a')")
	t1 := mk("t1.db", "(1,'a')")
	t2 := mk("t2.db", "(1,'b'),(2,'c')")
	svc := New()
	v, err := svc.CompareMulti(context.Background(), MultiRequest{
		Source:      Endpoint{Kind: "sqlite", Path: src},
		Targets:     []Endpoint{{Kind: "sqlite", Path: t1}, {Kind: "sqlite", Path: t2}},
		CompareData: true,
	})
	if err != nil {
		t.Fatal(err)
	}
	if len(v.Targets) != 2 {
		t.Fatalf("targets=%d", len(v.Targets))
	}
	if v.Targets[0].Error != "" || v.Targets[1].Error != "" {
		t.Fatalf("errors: %+v", v.Targets)
	}
	if v.Targets[0].View.Changed != 0 {
		t.Fatalf("t1 应无结构差异: %+v", v.Targets[0].View)
	}
}

func TestSQLiteCompareAndReadOnlyPreview(t *testing.T) {
	dir := t.TempDir()
	srcPath := filepath.Join(dir, "src.db")
	tgtPath := filepath.Join(dir, "tgt.db")
	mk := func(path, sqlText string) {
		db, err := sql.Open("sqlite", path)
		if err != nil {
			t.Fatal(err)
		}
		defer db.Close()
		if _, err := db.Exec(sqlText); err != nil {
			t.Fatal(err)
		}
	}
	mk(srcPath, `CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL); INSERT INTO users VALUES (1,'Alice'),(2,'Bob');`)
	mk(tgtPath, `CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL); INSERT INTO users VALUES (1,'Alicia'),(3,'Carol');`)

	svc := New()
	v, err := svc.Compare(context.Background(), CompareRequest{
		Source:      Endpoint{Kind: "sqlite", Path: srcPath},
		Target:      Endpoint{Kind: "sqlite", Path: tgtPath},
		CompareData: true,
	})
	if err != nil {
		t.Fatal(err)
	}
	if !v.HasData {
		t.Fatal("应检测到数据差异")
	}
	var got bool
	for _, tb := range v.Tables {
		if tb.Name == "users" && tb.Data != nil {
			got = true
			if tb.Data.Inserts != 1 || tb.Data.Deletes != 1 || tb.Data.Updates != 1 {
				t.Fatalf("数据差异不符: %+v", tb.Data)
			}
		}
	}
	if !got {
		t.Fatal("缺 users 数据差异")
	}
	p, err := svc.Plan(context.Background(), v.RunID, false)
	if err != nil {
		t.Fatal(err)
	}
	if len(p.Phases) != 0 || len(p.Diagnostics) == 0 {
		t.Fatalf("非 MySQL 应只给提示: %+v", p)
	}
}

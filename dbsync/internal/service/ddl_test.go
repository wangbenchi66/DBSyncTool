package service

import (
	"context"
	"os"
	"path/filepath"
	"testing"
)

func TestDDLDirCompare(t *testing.T) {
	src := t.TempDir()
	tgt := t.TempDir()
	if err := os.WriteFile(filepath.Join(src, "a.sql"), []byte(`CREATE TABLE users (id BIGINT PRIMARY KEY, name VARCHAR(50) NOT NULL);`), 0o644); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(tgt, "a.sql"), []byte(`CREATE TABLE users (id BIGINT PRIMARY KEY, name VARCHAR(50) NOT NULL, email VARCHAR(100));`), 0o644); err != nil {
		t.Fatal(err)
	}
	svc := New()
	info, err := svc.TestConnection(context.Background(), Endpoint{Kind: "dir", Path: src})
	if err != nil || info.Database != "dir" {
		t.Fatalf("conn: %+v %v", info, err)
	}
	v, err := svc.Compare(context.Background(), CompareRequest{
		Source: Endpoint{Kind: "dir", Path: src},
		Target: Endpoint{Kind: "dir", Path: tgt},
	})
	if err != nil {
		t.Fatal(err)
	}
	if v.Changed != 1 {
		t.Fatalf("应 1 张变更表: %+v", v)
	}
	if _, err := svc.Compare(context.Background(), CompareRequest{
		Source:      Endpoint{Kind: "dir", Path: src},
		Target:      Endpoint{Kind: "dir", Path: tgt},
		CompareData: true,
	}); err == nil {
		t.Fatal("目录数据比对应报错")
	}
}

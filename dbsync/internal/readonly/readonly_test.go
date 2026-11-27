package readonly

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// TestNoWriteSQL 是只读边界的静态闸门：生产代码不允许出现 Exec/ExecContext。
// 测试代码（_test.go）与临时检查目录不在范围内；建库/建表只允许在测试夹具里。
func TestNoWriteSQL(t *testing.T) {
	root, err := filepath.Abs("../..")
	if err != nil {
		t.Fatal(err)
	}
	var hits []string
	err = filepath.WalkDir(root, func(path string, d os.DirEntry, err error) error {
		if err != nil {
			return err
		}
		if d.IsDir() {
			switch d.Name() {
			case ".git", "build", "frontend", "tmpcheck", ".scratch", "dist":
				return filepath.SkipDir
			}
			return nil
		}
		if !strings.HasSuffix(path, ".go") || strings.HasSuffix(path, "_test.go") {
			return nil
		}
		b, err := os.ReadFile(path)
		if err != nil {
			return err
		}
		s := string(b)
		for _, needle := range []string{"ExecContext(", ".Exec("} {
			if strings.Contains(s, needle) {
				rel, _ := filepath.Rel(root, path)
				hits = append(hits, rel+" 含 "+needle)
			}
		}
		return nil
	})
	if err != nil {
		t.Fatal(err)
	}
	if len(hits) > 0 {
		t.Fatalf("只读工具不允许执行写语句，发现：\n%s", strings.Join(hits, "\n"))
	}
}

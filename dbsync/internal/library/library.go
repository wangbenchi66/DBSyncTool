// Package library 管理本地快照库（只读比对用的 .dbsync 文件索引）。
package library

import (
	"encoding/json"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"time"

	"gitee.com/wangbenchi66/dbsync/internal/snapshot"
)

type Entry struct {
	Path      string    `json:"path"`
	Name      string    `json:"name"`
	Schema    string    `json:"schema"`
	CreatedAt time.Time `json:"createdAt"`
	Tables    int       `json:"tables"`
	Rows      int       `json:"rows"`
	HasData   bool      `json:"hasData"`
	Size      int64     `json:"size"`
	Tags      []string  `json:"tags,omitempty"`
	Note      string    `json:"note,omitempty"`
	Error     string    `json:"error,omitempty"`
}

type meta struct {
	Tags []string `json:"tags,omitempty"`
	Note string   `json:"note,omitempty"`
}

const metaFile = "index.json"

func DefaultDir() (string, error) {
	base, err := os.UserConfigDir()
	if err != nil {
		return "", err
	}
	return filepath.Join(base, "DBSync", "snapshots"), nil
}

func EnsureDir(dir string) error { return os.MkdirAll(dir, 0o755) }

func List(dir string) ([]Entry, error) {
	if err := EnsureDir(dir); err != nil {
		return nil, err
	}
	metas := loadMeta(dir)
	entries, err := os.ReadDir(dir)
	if err != nil {
		return nil, err
	}
	var out []Entry
	for _, e := range entries {
		if e.IsDir() || !strings.HasSuffix(strings.ToLower(e.Name()), ".dbsync") {
			continue
		}
		path := filepath.Join(dir, e.Name())
		info, err := e.Info()
		if err != nil {
			continue
		}
		item := Entry{Path: path, Name: e.Name(), Size: info.Size()}
		if m, ok := metas[e.Name()]; ok {
			item.Tags = m.Tags
			item.Note = m.Note
		}
		m, err := snapshot.Inspect(path)
		if err != nil {
			item.Error = err.Error()
			out = append(out, item)
			continue
		}
		item.Schema = m.SchemaName
		item.HasData = m.HasData
		item.Tables = m.TableCount
		item.CreatedAt, _ = time.Parse(time.RFC3339Nano, m.CreatedAt)
		for _, t := range m.Tables {
			item.Rows += t.RowCount
		}
		out = append(out, item)
	}
	sort.Slice(out, func(i, j int) bool {
		if out[i].CreatedAt.Equal(out[j].CreatedAt) {
			return out[i].Name < out[j].Name
		}
		return out[i].CreatedAt.After(out[j].CreatedAt)
	})
	return out, nil
}

// Import 把快照复制进库；已在库内则直接返回。
func Import(dir, src string) (Entry, error) {
	if err := EnsureDir(dir); err != nil {
		return Entry{}, err
	}
	src, err := filepath.Abs(src)
	if err != nil {
		return Entry{}, err
	}
	if _, err := os.Stat(src); err != nil {
		return Entry{}, err
	}
	dir, _ = filepath.Abs(dir)
	rel, err := filepath.Rel(dir, src)
	if err == nil && rel != ".." && !strings.HasPrefix(rel, ".."+string(filepath.Separator)) {
		return entryOf(src)
	}
	dst := filepath.Join(dir, filepath.Base(src))
	for i := 1; ; i++ {
		if _, err := os.Stat(dst); os.IsNotExist(err) {
			break
		}
		ext := filepath.Ext(src)
		dst = filepath.Join(dir, fmt.Sprintf("%s-%d%s", strings.TrimSuffix(filepath.Base(src), ext), i, ext))
	}
	in, err := os.Open(src)
	if err != nil {
		return Entry{}, err
	}
	defer in.Close()
	out, err := os.Create(dst)
	if err != nil {
		return Entry{}, err
	}
	if _, err := io.Copy(out, in); err != nil {
		out.Close()
		os.Remove(dst)
		return Entry{}, err
	}
	if err := out.Close(); err != nil {
		return Entry{}, err
	}
	return entryOf(dst)
}

func Delete(dir, path string) error {
	dir, _ = filepath.Abs(dir)
	path, _ = filepath.Abs(path)
	rel, err := filepath.Rel(dir, path)
	if err != nil || rel == ".." || strings.HasPrefix(rel, ".."+string(filepath.Separator)) {
		return fmt.Errorf("只能删除快照库内的文件")
	}
	if err := os.Remove(path); err != nil {
		return err
	}
	metas := loadMeta(dir)
	delete(metas, filepath.Base(path))
	return saveMeta(dir, metas)
}

// SetMeta 保存快照的标签/备注。
func SetMeta(dir, path string, tags []string, note string) error {
	dir, _ = filepath.Abs(dir)
	path, _ = filepath.Abs(path)
	rel, err := filepath.Rel(dir, path)
	if err != nil || rel == ".." || strings.HasPrefix(rel, ".."+string(filepath.Separator)) {
		return fmt.Errorf("只能标记快照库内的文件")
	}
	metas := loadMeta(dir)
	metas[filepath.Base(path)] = meta{Tags: tags, Note: note}
	return saveMeta(dir, metas)
}

func loadMeta(dir string) map[string]meta {
	out := map[string]meta{}
	b, err := os.ReadFile(filepath.Join(dir, metaFile))
	if err != nil {
		return out
	}
	_ = json.Unmarshal(b, &out)
	if out == nil {
		out = map[string]meta{}
	}
	return out
}

func saveMeta(dir string, metas map[string]meta) error {
	b, err := json.MarshalIndent(metas, "", "  ")
	if err != nil {
		return err
	}
	return os.WriteFile(filepath.Join(dir, metaFile), append(b, '\n'), 0o644)
}

func entryOf(path string) (Entry, error) {
	info, err := os.Stat(path)
	if err != nil {
		return Entry{}, err
	}
	e := Entry{Path: path, Name: filepath.Base(path), Size: info.Size()}
	m, err := snapshot.Inspect(path)
	if err != nil {
		return Entry{}, err
	}
	e.Schema = m.SchemaName
	e.HasData = m.HasData
	e.Tables = m.TableCount
	e.CreatedAt, _ = time.Parse(time.RFC3339Nano, m.CreatedAt)
	for _, t := range m.Tables {
		e.Rows += t.RowCount
	}
	return e, nil
}

// Package rules 管理只读比对的忽略规则（glob pattern）。规则只影响展示/预览，不修改数据库。
package rules

import (
	"encoding/json"
	"os"
	"path/filepath"
	"strings"

	"gitee.com/wangbenchi66/dbsync/internal/diff"
)

type Rule struct {
	Kind    string `json:"kind"` // table/column/view/routine/trigger
	Pattern string `json:"pattern"`
	Note    string `json:"note,omitempty"`
}

func DefaultPath() (string, error) {
	base, err := os.UserConfigDir()
	if err != nil {
		return "", err
	}
	return filepath.Join(base, "DBSync", "rules.json"), nil
}

func Load() ([]Rule, error) {
	path, err := DefaultPath()
	if err != nil {
		return nil, err
	}
	b, err := os.ReadFile(path)
	if os.IsNotExist(err) {
		return []Rule{}, nil
	}
	if err != nil {
		return nil, err
	}
	var rs []Rule
	if err := json.Unmarshal(b, &rs); err != nil {
		return nil, err
	}
	return rs, nil
}

func Save(rs []Rule) error {
	path, err := DefaultPath()
	if err != nil {
		return err
	}
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		return err
	}
	b, err := json.MarshalIndent(rs, "", "  ")
	if err != nil {
		return err
	}
	return os.WriteFile(path, append(b, '\n'), 0o644)
}

// Match 判断 kind/name 是否被规则命中；pattern 支持 * ? []，大小写不敏感。
func (r Rule) Match(kind, name string) bool {
	if !strings.EqualFold(r.Kind, kind) {
		return false
	}
	if r.Pattern == "" {
		return false
	}
	pat := strings.ToLower(r.Pattern)
	n := strings.ToLower(name)
	ok, err := filepath.Match(pat, n)
	if err != nil {
		return pat == n
	}
	return ok
}

func matchAny(rs []Rule, kind string, names ...string) bool {
	for _, r := range rs {
		for _, n := range names {
			if r.Match(kind, n) {
				return true
			}
		}
	}
	return false
}

// Filter 就地移除被忽略的表/列/对象。
func Filter(d *diff.SchemaDiff, rs []Rule) {
	if len(rs) == 0 || d == nil {
		return
	}
	tables := d.Tables[:0]
	for _, t := range d.Tables {
		schema := ""
		if t.Source != nil {
			schema = t.Source.Schema
		} else if t.Target != nil {
			schema = t.Target.Schema
		}
		if matchAny(rs, "table", t.Name, schema+"."+t.Name) {
			continue
		}
		cols := t.Columns[:0]
		for _, c := range t.Columns {
			if matchAny(rs, "column", t.Name+"."+c.Name, schema+"."+t.Name+"."+c.Name) {
				continue
			}
			cols = append(cols, c)
		}
		t.Columns = cols
		tables = append(tables, t)
	}
	d.Tables = tables
	d.Views = filterObjects(d.Views, rs, "view")
	d.Routines = filterObjects(d.Routines, rs, "routine")
	d.Triggers = filterObjects(d.Triggers, rs, "trigger")
}

func filterObjects(objs []diff.ObjectDiff, rs []Rule, kind string) []diff.ObjectDiff {
	out := objs[:0]
	for _, o := range objs {
		if matchAny(rs, kind, o.Name) {
			continue
		}
		out = append(out, o)
	}
	return out
}

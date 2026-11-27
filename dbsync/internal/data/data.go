// Package data 读取 MySQL 行并按主键做差。M2 先支持有主键的表。
package data

import (
	"context"
	"database/sql"
	"encoding/hex"
	"fmt"
	"sort"
	"strconv"
	"strings"
	"time"

	"gitee.com/wangbenchi66/dbsync/internal/diff"
	"gitee.com/wangbenchi66/dbsync/internal/model"
	"gitee.com/wangbenchi66/dbsync/internal/value"
)

type Options struct {
	// CompareData 为 false 时只做结构。
	CompareData bool
}

// Attach 为结构 diff 的每张表补数据差异。只处理有主键的表；无主键标记 UnsupportedKey。
func Attach(ctx context.Context, sourceDB, targetDB *sql.DB, sd *diff.SchemaDiff) error {
	for i := range sd.Tables {
		td := &sd.Tables[i]
		if td.Status == diff.Removed {
			continue
		}
		if td.Status == diff.Added {
			rows, err := ReadRows(ctx, sourceDB, td.Source)
			if err != nil {
				return fmt.Errorf("%s: %w", td.Name, err)
			}
			td.Data = &diff.DataDiff{
				KeyColumns:  PKColumns(td.Source),
				SourceCount: len(rows),
				Inserts:     rows,
			}
			if len(td.Data.KeyColumns) == 0 {
				td.Data.UnsupportedKey = true
			}
			continue
		}
		if td.Source == nil || td.Target == nil {
			continue
		}
		key := PKColumns(td.Source)
		if len(key) == 0 {
			key = PKColumns(td.Target)
		}
		common := ComparedColumns(td.Source)
		dd := &diff.DataDiff{KeyColumns: key, CommonColumns: common}
		td.Data = dd
		if len(key) == 0 {
			dd.UnsupportedKey = true
			continue
		}
		srcRows, err := ReadRows(ctx, sourceDB, td.Source)
		if err != nil {
			return fmt.Errorf("读源 %s: %w", td.Name, err)
		}
		tgtRows, err := ReadRows(ctx, targetDB, td.Target)
		if err != nil {
			return fmt.Errorf("读目标 %s: %w", td.Name, err)
		}
		dd.SourceCount = len(srcRows)
		dd.TargetCount = len(tgtRows)
		DiffRows(dd, srcRows, tgtRows)
	}
	return nil
}

func PKColumns(t *model.Table) []string {
	for _, c := range t.Constraints {
		if c.Kind == model.ConstraintPK {
			return append([]string(nil), c.Columns...)
		}
	}
	for _, ix := range t.Indexes {
		if ix.Primary {
			out := make([]string, 0, len(ix.Columns))
			for _, c := range ix.Columns {
				out = append(out, c.Name)
			}
			return out
		}
	}
	return nil
}

func ComparedColumns(s *model.Table) []string {
	var out []string
	for _, c := range s.Columns {
		if c.Generated {
			continue
		}
		out = append(out, c.Name)
	}
	return out
}

func DiffRows(dd *diff.DataDiff, src, tgt []diff.Row) {
	sm := map[string]diff.Row{}
	tm := map[string]diff.Row{}
	for _, r := range src {
		sm[rowKey(r, dd.KeyColumns)] = r
	}
	for _, r := range tgt {
		tm[rowKey(r, dd.KeyColumns)] = r
	}
	keys := make([]string, 0, len(sm)+len(tm))
	seen := map[string]struct{}{}
	for k := range sm {
		keys = append(keys, k)
		seen[k] = struct{}{}
	}
	for k := range tm {
		if _, ok := seen[k]; !ok {
			keys = append(keys, k)
		}
	}
	sort.Strings(keys)
	for _, k := range keys {
		s, sok := sm[k]
		t, tok := tm[k]
		switch {
		case sok && !tok:
			dd.Inserts = append(dd.Inserts, s)
		case !sok && tok:
			dd.Deletes = append(dd.Deletes, t)
		default:
			var changed []string
			after := diff.Row{}
			for _, col := range dd.CommonColumns {
				sv, sOK := s[col]
				tv, tOK := t[col]
				if !sOK || !tOK || !value.Equal(sv, tv) {
					changed = append(changed, col)
					after[col] = sv
				}
			}
			if len(changed) > 0 {
				dd.Updates = append(dd.Updates, diff.RowUpdate{
					Key:     keyRow(s, dd.KeyColumns),
					Before:  t,
					After:   after,
					Changed: changed,
				})
			}
		}
	}
}

func keyRow(r diff.Row, pk []string) diff.Row {
	out := diff.Row{}
	for _, c := range pk {
		out[c] = r[c]
	}
	return out
}

func rowKey(r diff.Row, pk []string) string {
	var b strings.Builder
	for _, c := range pk {
		v, ok := r[c]
		if !ok {
			b.WriteString("\x00missing")
			continue
		}
		b.WriteString(hex.EncodeToString(v.Canonical()))
		b.WriteByte('|')
	}
	return b.String()
}

func sortStrings(s []string) { sort.Strings(s) }

func quoteIdent(s string) string {
	return "`" + strings.ReplaceAll(s, "`", "``") + "`"
}

func quoteIdentFor(kind, s string) string {
	if kind == "postgres" {
		return `"` + strings.ReplaceAll(s, `"`, `""`) + `"`
	}
	return quoteIdent(s)
}

func isTextType(t model.Type) bool {
	switch t.ID {
	case model.TypeString, model.TypeText, model.TypeEnum, model.TypeSet:
		return true
	default:
		return false
	}
}

// ReadRows 读取整表（MySQL 排序规则）。M2 先物化到内存；后续换流式归并。
func ReadRows(ctx context.Context, db *sql.DB, t *model.Table) ([]diff.Row, error) {
	return ReadRowsKind(ctx, db, t, "mysql")
}

func ReadRowsKind(ctx context.Context, db *sql.DB, t *model.Table, kind string) ([]diff.Row, error) {
	if len(t.Columns) == 0 {
		return nil, nil
	}
	var cols []string
	for _, c := range t.Columns {
		cols = append(cols, quoteIdentFor(kind, c.Name))
	}
	query := "SELECT " + strings.Join(cols, ",") + " FROM " +
		quoteIdentFor(kind, t.Schema) + "." + quoteIdentFor(kind, t.Name)
	pk := PKColumns(t)
	if len(pk) > 0 {
		var order []string
		for _, name := range pk {
			c := t.Column(name)
			if c == nil {
				continue
			}
			expr := quoteIdentFor(kind, name)
			if isTextType(c.Type) {
				switch kind {
				case "mysql":
					expr += " COLLATE utf8mb4_bin"
				case "postgres":
					expr += ` COLLATE "C"`
				}
			}
			order = append(order, expr)
		}
		if len(order) > 0 {
			query += " ORDER BY " + strings.Join(order, ",")
		}
	}
	rows, err := db.QueryContext(ctx, query)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	var out []diff.Row
	for rows.Next() {
		rec, err := scanRow(rows, t.Columns)
		if err != nil {
			return nil, err
		}
		out = append(out, rec)
	}
	return out, rows.Err()
}

func scanRow(rows *sql.Rows, cols []model.Column) (diff.Row, error) {
	raw := make([]any, len(cols))
	ptrs := make([]any, len(cols))
	for i := range raw {
		ptrs[i] = &raw[i]
	}
	if err := rows.Scan(ptrs...); err != nil {
		return nil, err
	}
	out := diff.Row{}
	for i, c := range cols {
		v, err := convert(raw[i], c.Type)
		if err != nil {
			return nil, fmt.Errorf("列 %s: %w", c.Name, err)
		}
		out[c.Name] = v
	}
	return out, nil
}

func convert(raw any, typ model.Type) (value.Value, error) {
	if raw == nil {
		return value.Null(), nil
	}
	switch typ.ID {
	case model.TypeBool:
		switch x := raw.(type) {
		case bool:
			return value.Bool(x), nil
		case int64:
			return value.Bool(x != 0), nil
		case []byte:
			return value.Bool(string(x) != "0"), nil
		case string:
			return value.Bool(x != "0" && x != ""), nil
		}
	case model.TypeInt:
		switch x := raw.(type) {
		case int64:
			return value.Int(x), nil
		case uint64:
			return value.Int(int64(x)), nil
		case []byte:
			n, err := strconv.ParseInt(string(x), 10, 64)
			if err != nil {
				return value.Value{}, err
			}
			return value.Int(n), nil
		}
	case model.TypeUint:
		switch x := raw.(type) {
		case uint64:
			return value.Uint(x), nil
		case int64:
			return value.Uint(uint64(x)), nil
		case []byte:
			n, err := strconv.ParseUint(string(x), 10, 64)
			if err != nil {
				return value.Value{}, err
			}
			return value.Uint(n), nil
		}
	case model.TypeFloat:
		switch x := raw.(type) {
		case float64:
			return value.Float(x), nil
		case []byte:
			n, err := strconv.ParseFloat(string(x), 64)
			if err != nil {
				return value.Value{}, err
			}
			return value.Float(n), nil
		}
	case model.TypeDecimal:
		switch x := raw.(type) {
		case []byte:
			return value.Decimal(string(x))
		case string:
			return value.Decimal(x)
		case int64:
			return value.Decimal(strconv.FormatInt(x, 10))
		case float64:
			return value.Decimal(strconv.FormatFloat(x, 'f', -1, 64))
		}
	case model.TypeString, model.TypeText, model.TypeEnum, model.TypeSet, model.TypeJSON:
		switch x := raw.(type) {
		case string:
			return value.String(x), nil
		case []byte:
			return value.String(string(x)), nil
		}
	case model.TypeBinary:
		switch x := raw.(type) {
		case []byte:
			return value.Bytes(x), nil
		case string:
			return value.Bytes([]byte(x)), nil
		}
	case model.TypeDate, model.TypeTime, model.TypeDateTime, model.TypeTimestamp:
		switch x := raw.(type) {
		case time.Time:
			return value.Time(x), nil
		case []byte:
			t, err := parseTime(string(x))
			if err != nil {
				return value.Value{}, err
			}
			return value.Time(t), nil
		case string:
			t, err := parseTime(x)
			if err != nil {
				return value.Value{}, err
			}
			return value.Time(t), nil
		}
	}
	// 兜底：保留可读文本，避免驱动类型差异导致整表失败。
	switch x := raw.(type) {
	case []byte:
		return value.String(string(x)), nil
	case string:
		return value.String(x), nil
	}
	return value.Value{}, fmt.Errorf("不支持的类型 %T (TypeID=%s)", raw, typ.ID)
}

func parseTime(s string) (time.Time, error) {
	layouts := []string{
		"2006-01-02 15:04:05.999999",
		"2006-01-02 15:04:05",
		"2006-01-02",
		"15:04:05.999999",
		"15:04:05",
	}
	for _, l := range layouts {
		if t, err := time.ParseInLocation(l, s, time.UTC); err == nil {
			return t, nil
		}
	}
	return time.Time{}, fmt.Errorf("无法解析时间 %q", s)
}

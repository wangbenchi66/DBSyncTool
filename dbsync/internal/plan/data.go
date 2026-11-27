package plan

import (
	"encoding/hex"
	"fmt"
	"strings"

	"gitee.com/wangbenchi66/dbsync/internal/diff"
	"gitee.com/wangbenchi66/dbsync/internal/model"
	"gitee.com/wangbenchi66/dbsync/internal/value"
)

// BuildData 为非 MySQL 方言生成只读数据 DML 预览；结构 DDL 仍只支持 MySQL。
// 该函数只拼 SQL 字符串，不连接数据库、不执行。
func BuildData(d *diff.SchemaDiff, source, target *model.Schema, dialect string, opts Options) *Plan {
	p := &Plan{Source: d.SourceName, Target: d.TargetName}
	defer p.Seal()
	if !hasDataChanges(d) {
		p.Diagnostics = append(p.Diagnostics, Diagnostic{Severity: Info, Code: "NO_DATA_CHANGES", Message: "没有数据差异"})
		return p
	}
	for i := range d.Tables {
		td := &d.Tables[i]
		if td.Data == nil {
			continue
		}
		if td.Data.UnsupportedKey && (len(td.Data.Inserts)+len(td.Data.Updates)+len(td.Data.Deletes)) > 0 {
			p.Block("NO_PK", td.Name, "表没有主键，无法生成安全的逐行 DML")
			continue
		}
		tt := td.Target
		if tt == nil && td.Source != nil {
			copy := *td.Source
			copy.Schema = d.TargetName
			tt = &copy
		}
		if tt == nil {
			continue
		}
		tt = withSchema(tt, d.TargetName)
		for _, row := range td.Data.Deletes {
			p.Add(PhaseDataCleanup, "DELETE FROM "+tableSQL(dialect, tt)+" WHERE "+whereSQL(dialect, tt, row, td.Data.KeyColumns)+";", td.Name, true, true)
		}
		for _, row := range td.Data.Inserts {
			if td.Source == nil {
				continue
			}
			p.Add(PhaseDataLoad, insertDataSQL(dialect, tt, td.Source.Columns, row), td.Name, false, true)
		}
		for _, up := range td.Data.Updates {
			if td.Source == nil {
				continue
			}
			if sql := updateDataSQL(dialect, tt, td.Source, up, td.Data.KeyColumns); sql != "" {
				p.Add(PhaseDataLoad, sql, td.Name, false, true)
			}
		}
	}
	p.Warn("STRUCTURE_PREVIEW_MYSQL_ONLY", "", "结构 DDL 预览目前只支持 MySQL；此处仅生成数据 DML，仍然不会执行")
	return p
}

func withSchema(t *model.Table, schema string) *model.Table {
	c := *t
	if schema != "" {
		c.Schema = schema
	}
	return &c
}

func quoteFor(dialect, s string) string {
	switch dialect {
	case "postgres":
		return `"` + strings.ReplaceAll(s, `"`, `""`) + `"`
	case "sqlserver":
		return "[" + strings.ReplaceAll(s, "]", "]]") + "]"
	case "sqlite":
		return `"` + strings.ReplaceAll(s, `"`, `""`) + `"`
	default:
		return "`" + strings.ReplaceAll(s, "`", "``") + "`"
	}
}

func tableSQL(dialect string, t *model.Table) string {
	if t.Schema == "" {
		return quoteFor(dialect, t.Name)
	}
	return quoteFor(dialect, t.Schema) + "." + quoteFor(dialect, t.Name)
}

func whereSQL(dialect string, t *model.Table, row diff.Row, pk []string) string {
	var parts []string
	for _, name := range pk {
		c := t.Column(name)
		typ := model.Type{}
		if c != nil {
			typ = c.Type
		}
		parts = append(parts, quoteFor(dialect, name)+" = "+literalFor(dialect, row[name], typ))
	}
	return strings.Join(parts, " AND ")
}

func insertDataSQL(dialect string, t *model.Table, cols []model.Column, row diff.Row) string {
	var names, vals []string
	for _, c := range cols {
		if c.Generated {
			continue
		}
		names = append(names, quoteFor(dialect, c.Name))
		vals = append(vals, literalFor(dialect, row[c.Name], c.Type))
	}
	return "INSERT INTO " + tableSQL(dialect, t) + " (" + strings.Join(names, ",") + ") VALUES (" + strings.Join(vals, ",") + ");"
}

func updateDataSQL(dialect string, t *model.Table, source *model.Table, up diff.RowUpdate, pk []string) string {
	var sets []string
	for _, name := range up.Changed {
		c := source.Column(name)
		if c == nil || c.Generated {
			continue
		}
		sets = append(sets, quoteFor(dialect, name)+" = "+literalFor(dialect, up.After[name], c.Type))
	}
	if len(sets) == 0 {
		return ""
	}
	return "UPDATE " + tableSQL(dialect, t) + " SET " + strings.Join(sets, ", ") + " WHERE " + whereSQL(dialect, t, up.Key, pk) + ";"
}

func literalFor(dialect string, v value.Value, t model.Type) string {
	switch v.Kind() {
	case value.KindNull:
		return "NULL"
	case value.KindBool:
		if dialect == "postgres" {
			if v.Bool() {
				return "TRUE"
			}
			return "FALSE"
		}
		if v.Bool() {
			return "1"
		}
		return "0"
	case value.KindInt:
		return fmt.Sprintf("%d", v.Int())
	case value.KindUint:
		return fmt.Sprintf("%d", v.Uint())
	case value.KindFloat:
		return fmt.Sprintf("%v", v.Float())
	case value.KindDecimal:
		return v.StringValue()
	case value.KindString:
		return "'" + strings.ReplaceAll(v.StringValue(), "'", "''") + "'"
	case value.KindBytes:
		h := hex.EncodeToString(v.BytesValue())
		switch dialect {
		case "postgres":
			return `'\x` + h + `'`
		case "sqlite":
			return "X'" + h + "'"
		default:
			return "0x" + h
		}
	case value.KindTime:
		return "'" + v.TimeValue().UTC().Format("2006-01-02 15:04:05.999999") + "'"
	}
	return "NULL"
}

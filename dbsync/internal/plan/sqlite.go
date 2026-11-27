package plan

import (
	"strings"

	"gitee.com/wangbenchi66/dbsync/internal/diff"
	"gitee.com/wangbenchi66/dbsync/internal/model"
)

// BuildSQLite 生成 SQLite 的只读预览：数据 DML + 可安全表达的 DDL。
// SQLite 不支持直接 DROP/ALTER COLUMN；这类变更只给阻断诊断，不生成危险 SQL。
func BuildSQLite(d *diff.SchemaDiff, source, target *model.Schema, opts Options) *Plan {
	p := BuildData(d, source, target, "sqlite", opts)
	// BuildData 的“结构仅 MySQL”提示对 SQLite 不成立。
	var keep []Diagnostic
	for _, x := range p.Diagnostics {
		if x.Code != "STRUCTURE_PREVIEW_MYSQL_ONLY" {
			keep = append(keep, x)
		}
	}
	p.Diagnostics = keep

	for i := range d.Tables {
		td := &d.Tables[i]
		switch td.Status {
		case diff.Removed:
			if td.Target != nil {
				if !opts.AllowDestructive {
					p.Block("DROP_TABLE", td.Name, "删除表需要显式允许破坏性操作")
				}
				p.Add(PhaseSchema, "DROP TABLE "+tableSQL("sqlite", td.Target)+";", td.Name, true, false)
			}
			continue
		case diff.Added:
			if td.Source != nil {
				sql := createSQLiteTable(td.Source, d.TargetName)
				p.Add(PhaseSchema, sql, td.Name, false, true)
			}
			continue
		}
		if td.Source == nil || td.Target == nil {
			continue
		}
		for _, c := range td.Columns {
			switch c.Status {
			case diff.Added:
				p.Add(PhaseSchema, "ALTER TABLE "+tableSQL("sqlite", td.Target)+" ADD COLUMN "+sqliteColumn(c.Source)+";", td.Name, false, true)
			case diff.Removed:
				if !opts.AllowDestructive {
					p.Block("SQLITE_DROP_COLUMN", td.Name+"."+c.Name, "SQLite 删除列需要重建表，预览不生成")
				} else {
					p.Block("SQLITE_DROP_COLUMN", td.Name+"."+c.Name, "SQLite 删除列需要重建表，预览不生成")
				}
			case diff.Changed:
				p.Block("SQLITE_ALTER_COLUMN", td.Name+"."+c.Name, "SQLite 修改列需要重建表，预览不生成")
			}
		}
		// 索引增删
		for _, ix := range td.Indexes {
			if ix.Status == diff.Same || ix.Source == nil {
				continue
			}
			if ix.Target != nil && !ix.Target.Primary {
				p.Add(PhaseSchema, "DROP INDEX IF EXISTS "+quoteFor("sqlite", ix.Target.Name)+";", td.Name, false, true)
			}
			p.Add(PhaseSchemaDone, sqliteIndexSQL(td.Source, ix.Source), td.Name, false, true)
		}
		// 主键/外键/唯一/CHECK 变更依赖重建表
		for _, c := range td.Constraints {
			if c.Status == diff.Same {
				continue
			}
			if c.Kind == model.ConstraintPK {
				p.Block("SQLITE_PK_CHANGE", td.Name+"."+c.Name, "SQLite 主键变更需要重建表，预览不生成")
			}
			if c.Kind == model.ConstraintFK || c.Kind == model.ConstraintUnique || c.Kind == model.ConstraintCheck {
				p.Warn("SQLITE_REBUILD", td.Name+"."+c.Name, "SQLite 约束变更通常需要重建表；预览只提示，不生成危险 SQL")
			}
		}
	}

	// 视图/触发器：删目标、建源端定义。
	for _, v := range d.Views {
		if v.Source == "" {
			continue
		}
		if v.Status == diff.Changed || v.Status == diff.Removed {
			p.Add(PhaseDropDeps, "DROP VIEW IF EXISTS "+quoteFor("sqlite", v.Name)+";", v.Name, false, true)
		}
		if v.Status == diff.Added || v.Status == diff.Changed {
			p.Add(PhaseObjects, "CREATE VIEW "+quoteFor("sqlite", v.Name)+" AS "+strings.TrimSpace(v.Source)+";", v.Name, false, true)
		}
	}
	for _, tr := range d.Triggers {
		if tr.Source == "" {
			continue
		}
		if tr.Status == diff.Changed || tr.Status == diff.Removed {
			p.Add(PhaseDropDeps, "DROP TRIGGER IF EXISTS "+quoteFor("sqlite", tr.Name)+";", tr.Name, false, true)
		}
		if tr.Status == diff.Added || tr.Status == diff.Changed {
			p.Add(PhaseObjects, strings.TrimSpace(tr.Source)+";", tr.Name, false, true)
		}
	}
	p.Seal()
	return p
}

func createSQLiteTable(t *model.Table, schema string) string {
	var lines []string
	var pk []string
	for _, c := range t.Constraints {
		if c.Kind == model.ConstraintPK {
			pk = c.Columns
		}
	}
	for _, c := range t.Columns {
		if c.Generated {
			continue
		}
		def := sqliteColumn(&c)
		if len(pk) == 1 && pk[0] == c.Name && c.Identity {
			def = quoteFor("sqlite", c.Name) + " INTEGER PRIMARY KEY AUTOINCREMENT"
		}
		lines = append(lines, def)
	}
	inlinePK := false
	if len(pk) == 1 {
		for _, c := range t.Columns {
			if c.Name == pk[0] && c.Identity {
				inlinePK = true
			}
		}
	}
	if !inlinePK && len(pk) > 0 {
		lines = append(lines, "PRIMARY KEY ("+joinQuoted(pk)+")")
	}
	for _, c := range t.Constraints {
		switch c.Kind {
		case model.ConstraintUnique:
			lines = append(lines, "UNIQUE ("+joinQuoted(c.Columns)+")")
		case model.ConstraintFK:
			ref := quoteFor("sqlite", c.RefTable)
			if c.RefSchema != "" && c.RefSchema != schema {
				ref = quoteFor("sqlite", c.RefSchema) + "." + ref
			}
			lines = append(lines, "FOREIGN KEY ("+joinQuoted(c.Columns)+") REFERENCES "+ref+" ("+joinQuoted(c.RefColumns)+")")
		case model.ConstraintCheck:
			expr := strings.TrimSpace(c.CheckExpr)
			if !strings.HasPrefix(strings.ToUpper(expr), "CHECK") {
				expr = "CHECK (" + expr + ")"
			}
			lines = append(lines, expr)
		}
	}
	name := quoteFor("sqlite", t.Name)
	if schema != "" {
		name = quoteFor("sqlite", schema) + "." + name
	}
	return "CREATE TABLE " + name + " (\n  " + strings.Join(lines, ",\n  ") + "\n);"
}

func sqliteColumn(c *model.Column) string {
	var b strings.Builder
	b.WriteString(quoteFor("sqlite", c.Name))
	b.WriteString(" ")
	b.WriteString(sqliteType(c.Type))
	if c.Identity {
		b.WriteString(" PRIMARY KEY")
	}
	if !c.Nullable {
		b.WriteString(" NOT NULL")
	}
	if c.DefaultSet && c.Default != nil {
		d := strings.TrimSpace(*c.Default)
		if d != "" && !strings.HasPrefix(strings.ToUpper(d), "CURRENT_") && !strings.HasPrefix(d, "'") && !isNumeric(d) {
			d = "'" + strings.ReplaceAll(d, "'", "''") + "'"
		}
		b.WriteString(" DEFAULT " + d)
	}
	return b.String()
}

func sqliteType(t model.Type) string {
	switch t.ID {
	case model.TypeInt, model.TypeUint, model.TypeBool:
		return "INTEGER"
	case model.TypeFloat:
		return "REAL"
	case model.TypeDecimal:
		return "NUMERIC"
	case model.TypeBinary:
		return "BLOB"
	default:
		return "TEXT"
	}
}

func sqliteIndexSQL(t *model.Table, ix *model.Index) string {
	unique := ""
	if ix.Unique {
		unique = "UNIQUE "
	}
	var cols []string
	for _, c := range ix.Columns {
		cols = append(cols, quoteFor("sqlite", c.Name))
	}
	return "CREATE " + unique + "INDEX " + quoteFor("sqlite", ix.Name) + " ON " + tableSQL("sqlite", t) + " (" + strings.Join(cols, ",") + ");"
}

func joinQuoted(cols []string) string {
	var out []string
	for _, c := range cols {
		out = append(out, quoteFor("sqlite", c))
	}
	return strings.Join(out, ",")
}

func isNumeric(s string) bool {
	if s == "" {
		return false
	}
	for _, c := range s {
		if (c < '0' || c > '9') && c != '.' && c != '-' && c != '+' {
			return false
		}
	}
	return true
}

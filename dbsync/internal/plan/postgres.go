package plan

import (
	"fmt"
	"strings"

	"gitee.com/wangbenchi66/dbsync/internal/diff"
	"gitee.com/wangbenchi66/dbsync/internal/model"
)

// BuildPostgres 生成 PostgreSQL 的只读预览：数据 DML + 可表达的 DDL。
// 复杂对象（函数体/触发器依赖）暂不生成，只给提示。
func BuildPostgres(d *diff.SchemaDiff, source, target *model.Schema, opts Options) *Plan {
	p := BuildData(d, source, target, "postgres", opts)
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
				p.Add(PhaseSchema, "DROP TABLE "+pgTable(td.Target)+";", td.Name, true, false)
			}
			continue
		case diff.Added:
			if td.Source != nil {
				p.Add(PhaseSchema, createPGTable(td.Source, d.TargetName), td.Name, false, true)
			}
			continue
		}
		if td.Source == nil || td.Target == nil {
			continue
		}
		for _, c := range td.Columns {
			switch c.Status {
			case diff.Added:
				p.Add(PhaseSchema, "ALTER TABLE "+pgTable(td.Target)+" ADD COLUMN "+pgColumn(c.Source, false)+";", td.Name, false, true)
			case diff.Removed:
				if !opts.AllowDestructive {
					p.Block("DROP_COLUMN", td.Name+"."+c.Name, "删除列需要显式允许破坏性操作")
				}
				p.Add(PhaseSchema, "ALTER TABLE "+pgTable(td.Target)+" DROP COLUMN "+pgQuote(c.Name)+";", td.Name, true, false)
			case diff.Changed:
				if c.Target != nil && c.Target.Identity {
					p.Block("PG_IDENTITY_CHANGE", td.Name+"."+c.Name, "PostgreSQL identity 变更需要重建/重设序列，预览不生成")
					continue
				}
				typ := pgType(c.Source.Type)
				p.Add(PhaseSchema, "ALTER TABLE "+pgTable(td.Target)+" ALTER COLUMN "+pgQuote(c.Name)+" TYPE "+typ+" USING "+pgQuote(c.Name)+"::"+typ+";", td.Name, false, true)
				if c.Source.Nullable != c.Target.Nullable {
					act := "SET NOT NULL"
					if c.Source.Nullable {
						act = "DROP NOT NULL"
					}
					p.Add(PhaseSchema, "ALTER TABLE "+pgTable(td.Target)+" ALTER COLUMN "+pgQuote(c.Name)+" "+act+";", td.Name, false, true)
				}
				if c.Source.DefaultSet != c.Target.DefaultSet || deref(c.Source.Default) != deref(c.Target.Default) {
					if c.Source.DefaultSet && c.Source.Default != nil {
						p.Add(PhaseSchema, "ALTER TABLE "+pgTable(td.Target)+" ALTER COLUMN "+pgQuote(c.Name)+" SET DEFAULT "+pgDefault(c.Source)+";", td.Name, false, true)
					} else {
						p.Add(PhaseSchema, "ALTER TABLE "+pgTable(td.Target)+" ALTER COLUMN "+pgQuote(c.Name)+" DROP DEFAULT;", td.Name, false, true)
					}
				}
			}
		}
		for _, ix := range td.Indexes {
			if ix.Status == diff.Same {
				continue
			}
			if ix.Target != nil {
				p.Add(PhaseSchema, "DROP INDEX IF EXISTS "+pgQuote(td.Target.Schema)+"."+pgQuote(ix.Target.Name)+";", td.Name, false, true)
			}
			if ix.Source != nil {
				p.Add(PhaseSchemaDone, pgIndex(td.Target, ix.Source), td.Name, false, true)
			}
		}
		for _, c := range td.Constraints {
			if c.Status == diff.Same {
				continue
			}
			if c.Target != nil {
				p.Add(PhaseSchema, "ALTER TABLE "+pgTable(td.Target)+" DROP CONSTRAINT IF EXISTS "+pgQuote(c.Target.Name)+";", td.Name, false, true)
			}
			if c.Source != nil {
				p.Add(PhaseSchemaDone, pgConstraint(td.Target, c.Source), td.Name, false, true)
			}
		}
	}
	for _, v := range d.Views {
		if v.Source == "" {
			continue
		}
		if v.Status == diff.Removed || v.Status == diff.Changed {
			p.Add(PhaseDropDeps, "DROP VIEW IF EXISTS "+pgQuote(d.TargetName)+"."+pgQuote(v.Name)+";", v.Name, false, true)
		}
		if v.Status == diff.Added || v.Status == diff.Changed {
			p.Add(PhaseObjects, "CREATE OR REPLACE VIEW "+pgQuote(d.TargetName)+"."+pgQuote(v.Name)+" AS "+strings.TrimSpace(v.Source)+";", v.Name, false, true)
		}
	}
	if len(d.Routines) > 0 {
		p.Warn("PG_ROUTINE_PREVIEW", "", "PostgreSQL 函数/过程 DDL 预览暂未生成；只显示差异")
	}
	if len(d.Triggers) > 0 {
		p.Warn("PG_TRIGGER_PREVIEW", "", "PostgreSQL 触发器 DDL 预览暂未生成；只显示差异")
	}
	p.Warn("PG_PREVIEW_NOTE", "", "PostgreSQL 表/索引/视图 DDL 已生成预览；函数/触发器 DDL 暂不生成，且不会执行")
	p.Seal()
	return p
}

func pgQuote(s string) string { return `"` + strings.ReplaceAll(s, `"`, `""`) + `"` }

func pgTable(t *model.Table) string {
	if t.Schema == "" {
		return pgQuote(t.Name)
	}
	return pgQuote(t.Schema) + "." + pgQuote(t.Name)
}

func pgType(t model.Type) string {
	switch t.ID {
	case model.TypeInt, model.TypeUint:
		return "BIGINT"
	case model.TypeBool:
		return "BOOLEAN"
	case model.TypeFloat:
		return "DOUBLE PRECISION"
	case model.TypeDecimal:
		if t.Precision > 0 {
			if t.Scale >= 0 {
				return fmt.Sprintf("NUMERIC(%d,%d)", t.Precision, t.Scale)
			}
			return fmt.Sprintf("NUMERIC(%d)", t.Precision)
		}
		return "NUMERIC"
	case model.TypeString:
		if t.Length > 0 {
			return fmt.Sprintf("VARCHAR(%d)", t.Length)
		}
		return "TEXT"
	case model.TypeBinary:
		return "BYTEA"
	case model.TypeDate:
		return "DATE"
	case model.TypeTime:
		return "TIME"
	case model.TypeDateTime:
		return "TIMESTAMP"
	case model.TypeTimestamp:
		return "TIMESTAMPTZ"
	case model.TypeJSON:
		return "JSONB"
	default:
		return "TEXT"
	}
}

func pgColumn(c *model.Column, inline bool) string {
	if c == nil {
		return ""
	}
	var b strings.Builder
	b.WriteString(pgQuote(c.Name))
	b.WriteString(" ")
	b.WriteString(pgType(c.Type))
	if c.Identity {
		b.WriteString(" GENERATED BY DEFAULT AS IDENTITY")
	}
	if c.Type.Collation != "" && c.Type.Collation != "default" {
		b.WriteString(" COLLATE " + pgQuote(c.Type.Collation))
	}
	if !c.Nullable {
		b.WriteString(" NOT NULL")
	}
	if c.DefaultSet && c.Default != nil {
		b.WriteString(" DEFAULT " + pgDefault(c))
	}
	return b.String()
}

func pgDefault(c *model.Column) string {
	d := strings.TrimSpace(deref(c.Default))
	if d == "" {
		return "NULL"
	}
	up := strings.ToUpper(d)
	if strings.HasPrefix(up, "NEXTVAL(") || strings.HasPrefix(up, "CURRENT_") || strings.HasPrefix(up, "NOW()") || strings.HasPrefix(d, "'") || isNumeric(d) {
		return d
	}
	return "'" + strings.ReplaceAll(d, "'", "''") + "'"
}

func pgIndex(t *model.Table, ix *model.Index) string {
	unique := ""
	if ix.Unique {
		unique = "UNIQUE "
	}
	var cols []string
	for _, c := range ix.Columns {
		cols = append(cols, pgQuote(c.Name))
	}
	return "CREATE " + unique + "INDEX " + pgQuote(ix.Name) + " ON " + pgTable(t) + " USING btree (" + strings.Join(cols, ",") + ");"
}

func pgConstraint(t *model.Table, c *model.Constraint) string {
	name := pgQuote(c.Name)
	switch c.Kind {
	case model.ConstraintPK:
		return "ALTER TABLE " + pgTable(t) + " ADD CONSTRAINT " + name + " PRIMARY KEY (" + pgJoin(c.Columns) + ");"
	case model.ConstraintUnique:
		return "ALTER TABLE " + pgTable(t) + " ADD CONSTRAINT " + name + " UNIQUE (" + pgJoin(c.Columns) + ");"
	case model.ConstraintFK:
		sql := "ALTER TABLE " + pgTable(t) + " ADD CONSTRAINT " + name + " FOREIGN KEY (" + pgJoin(c.Columns) + ") REFERENCES " + pgQuote(c.RefSchema) + "." + pgQuote(c.RefTable) + " (" + pgJoin(c.RefColumns) + ")"
		if c.OnDelete != "" && c.OnDelete != "NO ACTION" {
			sql += " ON DELETE " + c.OnDelete
		}
		if c.OnUpdate != "" && c.OnUpdate != "NO ACTION" {
			sql += " ON UPDATE " + c.OnUpdate
		}
		return sql + ";"
	case model.ConstraintCheck:
		expr := strings.TrimSpace(c.CheckExpr)
		if !strings.HasPrefix(strings.ToUpper(expr), "CHECK") {
			expr = "CHECK (" + expr + ")"
		}
		return "ALTER TABLE " + pgTable(t) + " ADD CONSTRAINT " + name + " " + expr + ";"
	default:
		return ""
	}
}

func pgJoin(cols []string) string {
	var out []string
	for _, c := range cols {
		out = append(out, pgQuote(c))
	}
	return strings.Join(out, ",")
}

func createPGTable(t *model.Table, schema string) string {
	var lines []string
	for _, c := range t.Columns {
		if c.Generated {
			continue
		}
		lines = append(lines, pgColumn(&c, false))
	}
	for _, c := range t.Constraints {
		switch c.Kind {
		case model.ConstraintPK:
			lines = append(lines, "PRIMARY KEY ("+pgJoin(c.Columns)+")")
		case model.ConstraintUnique:
			lines = append(lines, "UNIQUE ("+pgJoin(c.Columns)+")")
		case model.ConstraintFK:
			lines = append(lines, "FOREIGN KEY ("+pgJoin(c.Columns)+") REFERENCES "+pgQuote(c.RefSchema)+"."+pgQuote(c.RefTable)+" ("+pgJoin(c.RefColumns)+")")
		case model.ConstraintCheck:
			expr := strings.TrimSpace(c.CheckExpr)
			if !strings.HasPrefix(strings.ToUpper(expr), "CHECK") {
				expr = "CHECK (" + expr + ")"
			}
			lines = append(lines, expr)
		}
	}
	name := pgQuote(t.Name)
	if schema != "" {
		name = pgQuote(schema) + "." + name
	}
	return "CREATE TABLE " + name + " (\n  " + strings.Join(lines, ",\n  ") + "\n);"
}

func deref(s *string) string {
	if s == nil {
		return ""
	}
	return *s
}

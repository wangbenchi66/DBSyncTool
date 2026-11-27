package plan

import (
	"fmt"
	"strings"

	"gitee.com/wangbenchi66/dbsync/internal/diff"
	"gitee.com/wangbenchi66/dbsync/internal/model"
)

// BuildSQLServer 生成 SQL Server 的只读预览：数据 DML + 表/列/索引/约束/视图 DDL。
// 触发器与默认约束的复杂变更只提示，不生成危险 SQL。
func BuildSQLServer(d *diff.SchemaDiff, source, target *model.Schema, opts Options) *Plan {
	p := BuildData(d, source, target, "sqlserver", opts)
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
				p.Add(PhaseSchema, "DROP TABLE "+msTable(td.Target)+";", td.Name, true, false)
			}
			continue
		case diff.Added:
			if td.Source != nil {
				p.Add(PhaseSchema, createMSTable(td.Source, d.TargetName), td.Name, false, true)
			}
			continue
		}
		if td.Source == nil || td.Target == nil {
			continue
		}
		for _, c := range td.Columns {
			switch c.Status {
			case diff.Added:
				p.Add(PhaseSchema, "ALTER TABLE "+msTable(td.Target)+" ADD "+msColumn(c.Source)+";", td.Name, false, true)
			case diff.Removed:
				if !opts.AllowDestructive {
					p.Block("DROP_COLUMN", td.Name+"."+c.Name, "删除列需要显式允许破坏性操作")
				}
				p.Add(PhaseSchema, "ALTER TABLE "+msTable(td.Target)+" DROP COLUMN "+msQuote(c.Name)+";", td.Name, true, false)
			case diff.Changed:
				if c.Source.DefaultSet {
					p.Warn("MSSQL_DEFAULT_CHANGE", td.Name+"."+c.Name, "SQL Server 默认约束变更需要先删除旧约束；预览只提示")
				}
				p.Add(PhaseSchema, "ALTER TABLE "+msTable(td.Target)+" ALTER COLUMN "+msQuote(c.Name)+" "+msType(c.Source.Type)+" "+nullability(c.Source)+";", td.Name, false, true)
			}
		}
		for _, ix := range td.Indexes {
			if ix.Status == diff.Same {
				continue
			}
			if ix.Target != nil {
				p.Add(PhaseSchema, "DROP INDEX "+msQuote(ix.Target.Name)+" ON "+msTable(td.Target)+";", td.Name, false, true)
			}
			if ix.Source != nil {
				p.Add(PhaseSchemaDone, msIndex(td.Target, ix.Source), td.Name, false, true)
			}
		}
		for _, c := range td.Constraints {
			if c.Status == diff.Same {
				continue
			}
			if c.Target != nil {
				p.Add(PhaseSchema, "ALTER TABLE "+msTable(td.Target)+" DROP CONSTRAINT "+msQuote(c.Target.Name)+";", td.Name, false, true)
			}
			if c.Source != nil {
				p.Add(PhaseSchemaDone, msConstraint(td.Target, c.Source), td.Name, false, true)
			}
		}
	}
	for _, v := range d.Views {
		if v.Source == "" {
			continue
		}
		if v.Status == diff.Changed || v.Status == diff.Removed {
			p.Add(PhaseDropDeps, "DROP VIEW IF EXISTS "+msTable(&model.Table{Schema: d.TargetName, Name: v.Name})+";", v.Name, false, true)
		}
		if v.Status == diff.Added || v.Status == diff.Changed {
			p.Add(PhaseObjects, "CREATE OR ALTER VIEW "+msTable(&model.Table{Schema: d.TargetName, Name: v.Name})+" AS "+strings.TrimSpace(v.Source)+";", v.Name, false, true)
		}
	}
	if len(d.Triggers) > 0 {
		p.Warn("MSSQL_TRIGGER_PREVIEW", "", "SQL Server 触发器 DDL 预览暂未生成；只显示差异")
	}
	p.Warn("MSSQL_PREVIEW_NOTE", "", "SQL Server 表/列/索引/约束/视图 DDL 已生成预览；不会执行")
	p.Seal()
	return p
}

func msQuote(s string) string { return "[" + strings.ReplaceAll(s, "]", "]]") + "]" }

func msTable(t *model.Table) string {
	if t.Schema == "" {
		return msQuote(t.Name)
	}
	return msQuote(t.Schema) + "." + msQuote(t.Name)
}

func msType(t model.Type) string {
	switch t.ID {
	case model.TypeInt, model.TypeUint:
		return "BIGINT"
	case model.TypeBool:
		return "BIT"
	case model.TypeFloat:
		return "FLOAT"
	case model.TypeDecimal:
		if t.Precision > 0 {
			return fmt.Sprintf("DECIMAL(%d,%d)", t.Precision, t.Scale)
		}
		return "DECIMAL(18,2)"
	case model.TypeString:
		if t.Length > 0 {
			return fmt.Sprintf("NVARCHAR(%d)", t.Length)
		}
		return "NVARCHAR(MAX)"
	case model.TypeBinary:
		return "VARBINARY(MAX)"
	case model.TypeDate:
		return "DATE"
	case model.TypeTime:
		return "TIME"
	case model.TypeDateTime:
		return "DATETIME2"
	case model.TypeTimestamp:
		return "DATETIMEOFFSET"
	case model.TypeJSON:
		return "NVARCHAR(MAX)"
	default:
		return "NVARCHAR(MAX)"
	}
}

func nullability(c *model.Column) string {
	if c.Nullable {
		return "NULL"
	}
	return "NOT NULL"
}

func msColumn(c *model.Column) string {
	if c == nil {
		return ""
	}
	var b strings.Builder
	b.WriteString(msQuote(c.Name))
	b.WriteString(" ")
	b.WriteString(msType(c.Type))
	if c.Identity {
		b.WriteString(" IDENTITY(1,1)")
	}
	b.WriteString(" " + nullability(c))
	if c.DefaultSet && c.Default != nil {
		d := strings.TrimSpace(*c.Default)
		if d != "" && !isNumeric(d) && !strings.HasPrefix(d, "'") && !strings.HasPrefix(strings.ToUpper(d), "GET") {
			d = "'" + strings.ReplaceAll(d, "'", "''") + "'"
		}
		b.WriteString(" DEFAULT " + d)
	}
	return b.String()
}

func msIndex(t *model.Table, ix *model.Index) string {
	unique := ""
	if ix.Unique {
		unique = "UNIQUE "
	}
	var cols []string
	for _, c := range ix.Columns {
		cols = append(cols, msQuote(c.Name))
	}
	return "CREATE " + unique + "INDEX " + msQuote(ix.Name) + " ON " + msTable(t) + " (" + strings.Join(cols, ",") + ");"
}

func msConstraint(t *model.Table, c *model.Constraint) string {
	name := msQuote(c.Name)
	switch c.Kind {
	case model.ConstraintPK:
		return "ALTER TABLE " + msTable(t) + " ADD CONSTRAINT " + name + " PRIMARY KEY (" + msJoin(c.Columns) + ");"
	case model.ConstraintUnique:
		return "ALTER TABLE " + msTable(t) + " ADD CONSTRAINT " + name + " UNIQUE (" + msJoin(c.Columns) + ");"
	case model.ConstraintFK:
		sql := "ALTER TABLE " + msTable(t) + " ADD CONSTRAINT " + name + " FOREIGN KEY (" + msJoin(c.Columns) + ") REFERENCES " + msQuote(c.RefSchema) + "." + msQuote(c.RefTable) + " (" + msJoin(c.RefColumns) + ")"
		if c.OnDelete != "" && !strings.EqualFold(c.OnDelete, "NO_ACTION") {
			sql += " ON DELETE " + strings.ReplaceAll(c.OnDelete, "_", " ")
		}
		if c.OnUpdate != "" && !strings.EqualFold(c.OnUpdate, "NO_ACTION") {
			sql += " ON UPDATE " + strings.ReplaceAll(c.OnUpdate, "_", " ")
		}
		return sql + ";"
	case model.ConstraintCheck:
		expr := strings.TrimSpace(c.CheckExpr)
		if !strings.HasPrefix(strings.ToUpper(expr), "CHECK") {
			expr = "CHECK (" + expr + ")"
		}
		return "ALTER TABLE " + msTable(t) + " ADD CONSTRAINT " + name + " " + expr + ";"
	default:
		return ""
	}
}

func msJoin(cols []string) string {
	var out []string
	for _, c := range cols {
		out = append(out, msQuote(c))
	}
	return strings.Join(out, ",")
}

func createMSTable(t *model.Table, schema string) string {
	var lines []string
	for _, c := range t.Columns {
		lines = append(lines, msColumn(&c))
	}
	for _, c := range t.Constraints {
		switch c.Kind {
		case model.ConstraintPK:
			lines = append(lines, "CONSTRAINT "+msQuote(c.Name)+" PRIMARY KEY ("+msJoin(c.Columns)+")")
		case model.ConstraintUnique:
			lines = append(lines, "CONSTRAINT "+msQuote(c.Name)+" UNIQUE ("+msJoin(c.Columns)+")")
		case model.ConstraintFK:
			lines = append(lines, "CONSTRAINT "+msQuote(c.Name)+" FOREIGN KEY ("+msJoin(c.Columns)+") REFERENCES "+msQuote(c.RefSchema)+"."+msQuote(c.RefTable)+" ("+msJoin(c.RefColumns)+")")
		case model.ConstraintCheck:
			expr := strings.TrimSpace(c.CheckExpr)
			if !strings.HasPrefix(strings.ToUpper(expr), "CHECK") {
				expr = "CHECK (" + expr + ")"
			}
			lines = append(lines, "CONSTRAINT "+msQuote(c.Name)+" "+expr)
		}
	}
	name := msQuote(t.Name)
	if schema != "" {
		name = msQuote(schema) + "." + name
	}
	return "CREATE TABLE " + name + " (\n  " + strings.Join(lines, ",\n  ") + "\n);"
}

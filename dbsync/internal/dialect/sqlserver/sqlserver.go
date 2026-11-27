// Package sqlserver 读取 SQL Server 的结构 IR。只读：连接加 ApplicationIntent=ReadOnly。
package sqlserver

import (
	"context"
	"database/sql"
	"fmt"
	"strings"

	_ "github.com/microsoft/go-mssqldb"

	"gitee.com/wangbenchi66/dbsync/internal/model"
)

func Open(dsn string) (*sql.DB, error) {
	// 不强制改写调用方 DSN；建议使用只读账号，并在 DSN 中加 ApplicationIntent=ReadOnly。
	db, err := sql.Open("sqlserver", dsn)
	if err != nil {
		return nil, err
	}
	if err := db.Ping(); err != nil {
		db.Close()
		return nil, err
	}
	return db, nil
}

func Version(ctx context.Context, db *sql.DB) (string, error) {
	var v string
	if err := db.QueryRowContext(ctx, "SELECT @@VERSION").Scan(&v); err != nil {
		return "", err
	}
	if i := strings.IndexByte(v, '\n'); i >= 0 {
		v = v[:i]
	}
	return strings.TrimSpace(v), nil
}

func Introspect(ctx context.Context, db *sql.DB, schema string) (*model.Schema, error) {
	if schema == "" {
		schema = "dbo"
	}
	out := &model.Schema{Name: schema}
	rows, err := db.QueryContext(ctx, `
SELECT t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
WHERE s.name=@p1 ORDER BY t.name`, schema)
	if err != nil {
		return nil, fmt.Errorf("读取表: %w", err)
	}
	var tables []string
	for rows.Next() {
		var n string
		if err := rows.Scan(&n); err != nil {
			rows.Close()
			return nil, err
		}
		tables = append(tables, n)
	}
	rows.Close()
	if err := rows.Err(); err != nil {
		return nil, err
	}
	for _, name := range tables {
		t := model.Table{Schema: schema, Name: name}
		if err := readColumns(ctx, db, &t); err != nil {
			return nil, fmt.Errorf("%s: %w", name, err)
		}
		if err := readIndexes(ctx, db, &t); err != nil {
			return nil, err
		}
		if err := readConstraints(ctx, db, &t); err != nil {
			return nil, err
		}
		out.Tables = append(out.Tables, t)
	}
	if out.Views, err = readViews(ctx, db, schema); err != nil {
		return nil, err
	}
	if out.Routines, err = readRoutines(ctx, db, schema); err != nil {
		return nil, err
	}
	if out.Triggers, err = readTriggers(ctx, db, schema); err != nil {
		return nil, err
	}
	out.Sort()
	return out, nil
}

func readColumns(ctx context.Context, db *sql.DB, t *model.Table) error {
	const q = `
SELECT c.name, c.column_id, ty.name, c.max_length, c.precision, c.scale,
       c.is_nullable, c.is_identity, c.is_computed,
       dc.definition, cc.definition, c.collation_name
FROM sys.columns c
JOIN sys.tables t ON t.object_id=c.object_id
JOIN sys.schemas s ON s.schema_id=t.schema_id
JOIN sys.types ty ON ty.user_type_id=c.user_type_id
LEFT JOIN sys.default_constraints dc ON dc.parent_object_id=c.object_id AND dc.parent_column_id=c.column_id
LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id
WHERE s.name=@p1 AND t.name=@p2
ORDER BY c.column_id`
	rows, err := db.QueryContext(ctx, q, t.Schema, t.Name)
	if err != nil {
		return err
	}
	defer rows.Close()
	for rows.Next() {
		var name, typeName string
		var nullable, identity, computed bool
		var ord int
		var maxLen, precision, scale int64
		var def, gen, collation sql.NullString
		if err := rows.Scan(&name, &ord, &typeName, &maxLen, &precision, &scale,
			&nullable, &identity, &computed, &def, &gen, &collation); err != nil {
			return err
		}
		c := model.Column{
			Name: name, Ordinal: ord,
			Type:           mapType(typeName, maxLen, precision, scale, collation.String),
			Nullable:       nullable,
			Identity:       identity,
			Generated:      computed,
			GenerationExpr: gen.String,
		}
		if def.Valid {
			v := def.String
			c.Default = &v
			c.DefaultSet = true
		}
		t.Columns = append(t.Columns, c)
	}
	return rows.Err()
}

func mapType(name string, maxLen, precision, scale int64, collation string) model.Type {
	n := strings.ToLower(name)
	typ := model.Type{ID: model.TypeUnknown, Raw: n, Length: -1, Precision: int(precision), Scale: int(scale), Collation: collation}
	switch n {
	case "bit":
		typ.ID = model.TypeBool
	case "tinyint", "smallint", "int", "bigint":
		typ.ID = model.TypeInt
	case "decimal", "numeric", "money", "smallmoney":
		typ.ID = model.TypeDecimal
		typ.Raw = fmt.Sprintf("%s(%d,%d)", n, precision, scale)
	case "float", "real":
		typ.ID = model.TypeFloat
	case "char", "varchar", "nchar", "nvarchar":
		typ.ID = model.TypeString
		if maxLen == -1 {
			typ.Length = -1
			typ.Raw = n + "(max)"
		} else {
			l := maxLen
			if n == "nchar" || n == "nvarchar" {
				l /= 2
			}
			typ.Length = l
			typ.Raw = fmt.Sprintf("%s(%d)", n, l)
		}
	case "text", "ntext":
		typ.ID = model.TypeText
	case "binary", "varbinary", "image":
		typ.ID = model.TypeBinary
		if maxLen > 0 {
			typ.Length = maxLen
		}
	case "date":
		typ.ID = model.TypeDate
	case "time":
		typ.ID = model.TypeTime
	case "datetime", "datetime2", "smalldatetime":
		typ.ID = model.TypeDateTime
	case "datetimeoffset":
		typ.ID = model.TypeTimestamp
	case "uniqueidentifier", "xml":
		typ.ID = model.TypeString
	default:
		typ.ID = model.TypeString
	}
	return typ
}

func readIndexes(ctx context.Context, db *sql.DB, t *model.Table) error {
	const q = `
SELECT i.name, i.is_unique, i.is_primary_key, c.name, ic.key_ordinal, ic.is_descending_key
FROM sys.indexes i
JOIN sys.tables t ON t.object_id=i.object_id
JOIN sys.schemas s ON s.schema_id=t.schema_id
JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.is_included_column=0
JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
WHERE s.name=@p1 AND t.name=@p2 AND i.index_id>0
ORDER BY i.name, ic.key_ordinal`
	rows, err := db.QueryContext(ctx, q, t.Schema, t.Name)
	if err != nil {
		return err
	}
	defer rows.Close()
	ixs := map[string]*model.Index{}
	var order []string
	for rows.Next() {
		var name string
		var unique, primary, desc bool
		var col string
		var ord int
		if err := rows.Scan(&name, &unique, &primary, &col, &ord, &desc); err != nil {
			return err
		}
		ix := ixs[name]
		if ix == nil {
			ix = &model.Index{Name: name, Unique: unique, Primary: primary, Type: "btree"}
			ixs[name] = ix
			order = append(order, name)
		}
		ix.Columns = append(ix.Columns, model.IndexColumn{Name: col, Desc: desc})
	}
	for _, n := range order {
		t.Indexes = append(t.Indexes, *ixs[n])
	}
	return rows.Err()
}

func readConstraints(ctx context.Context, db *sql.DB, t *model.Table) error {
	const kq = `
SELECT kc.name, kc.type_desc, c.name, ic.key_ordinal
FROM sys.key_constraints kc
JOIN sys.tables t ON t.object_id=kc.parent_object_id
JOIN sys.schemas s ON s.schema_id=t.schema_id
JOIN sys.index_columns ic ON ic.object_id=kc.parent_object_id AND ic.index_id=kc.unique_index_id
JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
WHERE s.name=@p1 AND t.name=@p2
ORDER BY kc.name, ic.key_ordinal`
	rows, err := db.QueryContext(ctx, kq, t.Schema, t.Name)
	if err != nil {
		return err
	}
	cs := map[string]*model.Constraint{}
	var order []string
	for rows.Next() {
		var name, typ, col string
		var ord int
		if err := rows.Scan(&name, &typ, &col, &ord); err != nil {
			rows.Close()
			return err
		}
		c := cs[name]
		if c == nil {
			c = &model.Constraint{Name: name, Kind: keyKind(typ)}
			cs[name] = c
			order = append(order, name)
		}
		c.Columns = append(c.Columns, col)
	}
	rows.Close()
	if err := rows.Err(); err != nil {
		return err
	}
	const fq = `
SELECT fk.name, pc.name, rs.name, rt.name, rc.name,
       fk.delete_referential_action_desc, fk.update_referential_action_desc, fkc.constraint_column_id
FROM sys.foreign_keys fk
JOIN sys.tables t ON t.object_id=fk.parent_object_id
JOIN sys.schemas s ON s.schema_id=t.schema_id
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
JOIN sys.columns pc ON pc.object_id=fkc.parent_object_id AND pc.column_id=fkc.parent_column_id
JOIN sys.tables rt ON rt.object_id=fkc.referenced_object_id
JOIN sys.schemas rs ON rs.schema_id=rt.schema_id
JOIN sys.columns rc ON rc.object_id=fkc.referenced_object_id AND rc.column_id=fkc.referenced_column_id
WHERE s.name=@p1 AND t.name=@p2
ORDER BY fk.name, fkc.constraint_column_id`
	frows, err := db.QueryContext(ctx, fq, t.Schema, t.Name)
	if err != nil {
		return err
	}
	defer frows.Close()
	fks := map[string]*model.Constraint{}
	var forder []string
	for frows.Next() {
		var name, col, refSchema, refTable, refCol, del, upd string
		var ord int
		if err := frows.Scan(&name, &col, &refSchema, &refTable, &refCol, &del, &upd, &ord); err != nil {
			return err
		}
		c := fks[name]
		if c == nil {
			c = &model.Constraint{Name: name, Kind: model.ConstraintFK, RefSchema: refSchema, RefTable: refTable, OnDelete: del, OnUpdate: upd}
			fks[name] = c
			forder = append(forder, name)
		}
		c.Columns = append(c.Columns, col)
		c.RefColumns = append(c.RefColumns, refCol)
	}
	for _, n := range order {
		t.Constraints = append(t.Constraints, *cs[n])
	}
	for _, n := range forder {
		t.Constraints = append(t.Constraints, *fks[n])
	}
	if err := frows.Err(); err != nil {
		return err
	}
	const cq = `
SELECT cc.name, cc.definition FROM sys.check_constraints cc
JOIN sys.tables t ON t.object_id=cc.parent_object_id
JOIN sys.schemas s ON s.schema_id=t.schema_id
WHERE s.name=@p1 AND t.name=@p2 ORDER BY cc.name`
	crows, err := db.QueryContext(ctx, cq, t.Schema, t.Name)
	if err != nil {
		return err
	}
	defer crows.Close()
	for crows.Next() {
		var name, def string
		if err := crows.Scan(&name, &def); err != nil {
			return err
		}
		t.Constraints = append(t.Constraints, model.Constraint{Name: name, Kind: model.ConstraintCheck, CheckExpr: def})
	}
	return crows.Err()
}

func keyKind(s string) model.ConstraintKind {
	if strings.Contains(strings.ToUpper(s), "PRIMARY") {
		return model.ConstraintPK
	}
	return model.ConstraintUnique
}

func readViews(ctx context.Context, db *sql.DB, schema string) ([]model.View, error) {
	const q = `
SELECT v.name, m.definition FROM sys.views v
JOIN sys.schemas s ON s.schema_id=v.schema_id
LEFT JOIN sys.sql_modules m ON m.object_id=v.object_id
WHERE s.name=@p1 ORDER BY v.name`
	rows, err := db.QueryContext(ctx, q, schema)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	var out []model.View
	for rows.Next() {
		var n string
		var d sql.NullString
		if err := rows.Scan(&n, &d); err != nil {
			return nil, err
		}
		out = append(out, model.View{Schema: schema, Name: n, Definition: d.String})
	}
	return out, rows.Err()
}

func readRoutines(ctx context.Context, db *sql.DB, schema string) ([]model.Routine, error) {
	const q = `
SELECT o.name, o.type_desc, m.definition FROM sys.objects o
JOIN sys.schemas s ON s.schema_id=o.schema_id
LEFT JOIN sys.sql_modules m ON m.object_id=o.object_id
WHERE s.name=@p1 AND o.type IN ('P','FN','IF','TF')
ORDER BY o.name, o.type_desc`
	rows, err := db.QueryContext(ctx, q, schema)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	var out []model.Routine
	for rows.Next() {
		var n, kind string
		var d sql.NullString
		if err := rows.Scan(&n, &kind, &d); err != nil {
			return nil, err
		}
		out = append(out, model.Routine{Schema: schema, Name: n, Kind: kind, Definition: d.String})
	}
	return out, rows.Err()
}

func readTriggers(ctx context.Context, db *sql.DB, schema string) ([]model.Trigger, error) {
	const q = `
SELECT tr.name, OBJECT_NAME(tr.parent_id), m.definition
FROM sys.triggers tr
JOIN sys.tables t ON t.object_id=tr.parent_id
JOIN sys.schemas s ON s.schema_id=t.schema_id
LEFT JOIN sys.sql_modules m ON m.object_id=tr.object_id
WHERE s.name=@p1 ORDER BY tr.name`
	rows, err := db.QueryContext(ctx, q, schema)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	var out []model.Trigger
	for rows.Next() {
		var n, table string
		var d sql.NullString
		if err := rows.Scan(&n, &table, &d); err != nil {
			return nil, err
		}
		out = append(out, model.Trigger{Schema: schema, Name: n, Table: table, Statement: d.String})
	}
	return out, rows.Err()
}

func Quote(s string) string { return `[` + strings.ReplaceAll(s, `]`, `]]`) + `]` }

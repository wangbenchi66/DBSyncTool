// Package mysql 读取 MySQL / MariaDB 的结构 IR。它不做比对，也不生成 SQL。
// 兼容策略：以 MySQL 8.0.16 为基线，CHECK/生成列等元数据按版本回退；排序统一用 utf8mb4_bin。
package mysql

import (
	"context"
	"database/sql"
	"fmt"
	"strings"

	_ "github.com/go-sql-driver/mysql"

	"gitee.com/wangbenchi66/dbsync/internal/model"
)

// Open 打开 MySQL 连接。**只读用途**：调用方只能做查询/内省，禁止执行 DDL/DML。
// 生产环境必须使用只读账号；工具内的写语句由 internal/readonly 测试静态拦截。
func Open(dsn string) (*sql.DB, error) {
	db, err := sql.Open("mysql", dsn)
	if err != nil {
		return nil, err
	}
	db.SetMaxOpenConns(8)
	db.SetMaxIdleConns(4)
	if err := db.Ping(); err != nil {
		_ = db.Close()
		return nil, err
	}
	return db, nil
}

// Introspect 读取一个 schema 的表、列、索引、约束、视图、过程/函数、触发器。
// 兼容 MySQL 5.7+/8.0 与常见 MariaDB；不读取权限、分区、事件。
// 旧版本缺少 CHECK_CONSTRAINTS/GENERATION_EXPRESSION 时自动退回旧查询。
func Introspect(ctx context.Context, db *sql.DB, schema string) (*model.Schema, error) {
	if schema == "" {
		return nil, fmt.Errorf("schema 不能为空")
	}
	out := &model.Schema{Name: schema}
	tables, err := readTables(ctx, db, schema)
	if err != nil {
		return nil, err
	}
	out.Tables = tables
	if err := readColumns(ctx, db, schema, out); err != nil {
		return nil, err
	}
	if err := readIndexes(ctx, db, schema, out); err != nil {
		return nil, err
	}
	if err := readConstraints(ctx, db, schema, out); err != nil {
		return nil, err
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

func readTables(ctx context.Context, db *sql.DB, schema string) ([]model.Table, error) {
	const q = `
SELECT TABLE_NAME, TABLE_COLLATION, TABLE_COMMENT
FROM information_schema.TABLES
WHERE TABLE_SCHEMA = ? AND TABLE_TYPE = 'BASE TABLE'
ORDER BY TABLE_NAME`
	rows, err := db.QueryContext(ctx, q, schema)
	if err != nil {
		return nil, fmt.Errorf("读取表: %w", err)
	}
	defer rows.Close()
	var out []model.Table
	for rows.Next() {
		var name string
		var collation, comment sql.NullString
		if err := rows.Scan(&name, &collation, &comment); err != nil {
			return nil, err
		}
		out = append(out, model.Table{
			Schema:    schema,
			Name:      name,
			Collation: collation.String,
			Comment:   comment.String,
		})
	}
	return out, rows.Err()
}

func readColumns(ctx context.Context, db *sql.DB, schema string, out *model.Schema) error {
	const q = `
SELECT TABLE_NAME, COLUMN_NAME, ORDINAL_POSITION, COLUMN_DEFAULT, IS_NULLABLE,
       DATA_TYPE, COLUMN_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION,
       NUMERIC_SCALE, DATETIME_PRECISION, CHARACTER_SET_NAME, COLLATION_NAME,
       EXTRA, COLUMN_COMMENT, GENERATION_EXPRESSION
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = ?
ORDER BY TABLE_NAME, ORDINAL_POSITION`
	const qNoGen = `
SELECT TABLE_NAME, COLUMN_NAME, ORDINAL_POSITION, COLUMN_DEFAULT, IS_NULLABLE,
       DATA_TYPE, COLUMN_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION,
       NUMERIC_SCALE, DATETIME_PRECISION, CHARACTER_SET_NAME, COLLATION_NAME,
       EXTRA, COLUMN_COMMENT, NULL
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = ?
ORDER BY TABLE_NAME, ORDINAL_POSITION`
	rows, err := db.QueryContext(ctx, q, schema)
	noGen := false
	if err != nil {
		// MySQL 5.7 之前的 information_schema 没有 GENERATION_EXPRESSION；退回旧列集。
		rows, err = db.QueryContext(ctx, qNoGen, schema)
		noGen = true
	}
	if err != nil {
		return fmt.Errorf("读取列: %w", err)
	}
	defer rows.Close()
	for rows.Next() {
		var tableName, name, dataType, columnType, isNullable, extra, comment, genExpr string
		var ordinal int
		var def, charset, collation sql.NullString
		var charLen, numPrec, numScale, dtPrec sql.NullInt64
		if noGen {
			if err := rows.Scan(&tableName, &name, &ordinal, &def, &isNullable,
				&dataType, &columnType, &charLen, &numPrec, &numScale, &dtPrec,
				&charset, &collation, &extra, &comment, &genExpr); err != nil {
				return err
			}
		} else if err := rows.Scan(&tableName, &name, &ordinal, &def, &isNullable,
			&dataType, &columnType, &charLen, &numPrec, &numScale, &dtPrec,
			&charset, &collation, &extra, &comment, &genExpr); err != nil {
			return err
		}
		t := out.Table(tableName)
		if t == nil {
			continue
		}
		typ := mapType(dataType, columnType, charLen, numPrec, numScale, dtPrec, charset.String, collation.String)
		c := model.Column{
			Name:           name,
			Ordinal:        ordinal,
			Type:           typ,
			Nullable:       strings.EqualFold(isNullable, "YES"),
			Identity:       strings.Contains(strings.ToLower(extra), "auto_increment"),
			Generated:      strings.Contains(strings.ToUpper(extra), "GENERATED"),
			GenerationExpr: genExpr,
			Comment:        comment,
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

func mapType(dataType, columnType string, charLen, numPrec, numScale, dtPrec sql.NullInt64, charset, collation string) model.Type {
	dt := strings.ToLower(dataType)
	raw := columnType
	unsigned := strings.Contains(strings.ToLower(columnType), "unsigned")
	typ := model.Type{
		ID:        model.TypeUnknown,
		Raw:       raw,
		Length:    -1,
		Precision: -1,
		Scale:     -1,
		Unsigned:  unsigned,
		Charset:   charset,
		Collation: collation,
	}
	if charLen.Valid {
		typ.Length = charLen.Int64
	}
	if numPrec.Valid {
		typ.Precision = int(numPrec.Int64)
	}
	if numScale.Valid {
		typ.Scale = int(numScale.Int64)
	}
	switch dt {
	case "tinyint":
		if strings.HasPrefix(strings.ToLower(columnType), "tinyint(1)") && !unsigned {
			typ.ID = model.TypeBool
		} else if unsigned {
			typ.ID = model.TypeUint
		} else {
			typ.ID = model.TypeInt
		}
	case "smallint", "mediumint", "int", "integer", "bigint":
		if unsigned {
			typ.ID = model.TypeUint
		} else {
			typ.ID = model.TypeInt
		}
	case "decimal", "numeric":
		typ.ID = model.TypeDecimal
	case "float", "double", "real":
		typ.ID = model.TypeFloat
	case "char", "varchar":
		typ.ID = model.TypeString
	case "tinytext", "text", "mediumtext", "longtext":
		typ.ID = model.TypeText
	case "binary", "varbinary", "tinyblob", "blob", "mediumblob", "longblob", "bit":
		typ.ID = model.TypeBinary
	case "date":
		typ.ID = model.TypeDate
	case "time":
		typ.ID = model.TypeTime
	case "datetime":
		typ.ID = model.TypeDateTime
		if dtPrec.Valid {
			typ.Scale = int(dtPrec.Int64)
		}
	case "timestamp":
		typ.ID = model.TypeTimestamp
		if dtPrec.Valid {
			typ.Scale = int(dtPrec.Int64)
		}
	case "year":
		typ.ID = model.TypeInt
	case "json":
		typ.ID = model.TypeJSON
	case "enum":
		typ.ID = model.TypeEnum
	case "set":
		typ.ID = model.TypeSet
	}
	return typ
}

func readIndexes(ctx context.Context, db *sql.DB, schema string, out *model.Schema) error {
	const q = `
SELECT TABLE_NAME, INDEX_NAME, NON_UNIQUE, SEQ_IN_INDEX, COLUMN_NAME, SUB_PART, INDEX_TYPE
FROM information_schema.STATISTICS
WHERE TABLE_SCHEMA = ?
ORDER BY TABLE_NAME, INDEX_NAME, SEQ_IN_INDEX`
	rows, err := db.QueryContext(ctx, q, schema)
	if err != nil {
		return fmt.Errorf("读取索引: %w", err)
	}
	defer rows.Close()
	type key struct{ table, index string }
	idx := map[key]*model.Index{}
	for rows.Next() {
		var tableName, indexName, indexType string
		var nonUnique int
		var seq int
		var col sql.NullString
		var sub sql.NullInt64
		if err := rows.Scan(&tableName, &indexName, &nonUnique, &seq, &col, &sub, &indexType); err != nil {
			return err
		}
		k := key{tableName, indexName}
		ix := idx[k]
		if ix == nil {
			ix = &model.Index{
				Name:    indexName,
				Unique:  nonUnique == 0,
				Primary: indexName == "PRIMARY",
				Type:    indexType,
			}
			idx[k] = ix
		}
		if col.Valid && col.String != "" {
			ic := model.IndexColumn{Name: col.String}
			if sub.Valid {
				ic.Prefix = int(sub.Int64)
			}
			ix.Columns = append(ix.Columns, ic)
		}
	}
	if err := rows.Err(); err != nil {
		return err
	}
	for k, ix := range idx {
		if t := out.Table(k.table); t != nil {
			t.Indexes = append(t.Indexes, *ix)
		}
	}
	return nil
}

func readConstraints(ctx context.Context, db *sql.DB, schema string, out *model.Schema) error {
	const q = `
SELECT tc.TABLE_NAME, tc.CONSTRAINT_NAME, tc.CONSTRAINT_TYPE,
       kcu.COLUMN_NAME, kcu.ORDINAL_POSITION,
       kcu.REFERENCED_TABLE_SCHEMA, kcu.REFERENCED_TABLE_NAME, kcu.REFERENCED_COLUMN_NAME,
       rc.UPDATE_RULE, rc.DELETE_RULE, cc.CHECK_CLAUSE
FROM information_schema.TABLE_CONSTRAINTS tc
LEFT JOIN information_schema.KEY_COLUMN_USAGE kcu
  ON kcu.CONSTRAINT_SCHEMA = tc.CONSTRAINT_SCHEMA
 AND kcu.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
 AND kcu.TABLE_SCHEMA = tc.TABLE_SCHEMA
 AND kcu.TABLE_NAME = tc.TABLE_NAME
LEFT JOIN information_schema.REFERENTIAL_CONSTRAINTS rc
  ON rc.CONSTRAINT_SCHEMA = tc.CONSTRAINT_SCHEMA
 AND rc.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
 AND rc.TABLE_NAME = tc.TABLE_NAME
LEFT JOIN information_schema.CHECK_CONSTRAINTS cc
  ON cc.CONSTRAINT_SCHEMA = tc.CONSTRAINT_SCHEMA
 AND cc.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
WHERE tc.CONSTRAINT_SCHEMA = ? AND tc.TABLE_SCHEMA = ?
ORDER BY tc.TABLE_NAME, tc.CONSTRAINT_NAME, kcu.ORDINAL_POSITION`
	// CHECK_CONSTRAINTS 在 MySQL 5.7 / 旧 MariaDB 上可能不存在；退回无 CHECK 的查询。
	const qNoCheck = `
SELECT tc.TABLE_NAME, tc.CONSTRAINT_NAME, tc.CONSTRAINT_TYPE,
       kcu.COLUMN_NAME, kcu.ORDINAL_POSITION,
       kcu.REFERENCED_TABLE_SCHEMA, kcu.REFERENCED_TABLE_NAME, kcu.REFERENCED_COLUMN_NAME,
       rc.UPDATE_RULE, rc.DELETE_RULE, NULL
FROM information_schema.TABLE_CONSTRAINTS tc
LEFT JOIN information_schema.KEY_COLUMN_USAGE kcu
  ON kcu.CONSTRAINT_SCHEMA = tc.CONSTRAINT_SCHEMA
 AND kcu.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
 AND kcu.TABLE_SCHEMA = tc.TABLE_SCHEMA
 AND kcu.TABLE_NAME = tc.TABLE_NAME
LEFT JOIN information_schema.REFERENTIAL_CONSTRAINTS rc
  ON rc.CONSTRAINT_SCHEMA = tc.CONSTRAINT_SCHEMA
 AND rc.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
 AND rc.TABLE_NAME = tc.TABLE_NAME
WHERE tc.CONSTRAINT_SCHEMA = ? AND tc.TABLE_SCHEMA = ?
ORDER BY tc.TABLE_NAME, tc.CONSTRAINT_NAME, kcu.ORDINAL_POSITION`
	rows, err := db.QueryContext(ctx, q, schema, schema)
	if err != nil {
		rows, err = db.QueryContext(ctx, qNoCheck, schema, schema)
	}
	if err != nil {
		return fmt.Errorf("读取约束: %w", err)
	}
	defer rows.Close()
	type key struct{ table, name string }
	cs := map[key]*model.Constraint{}
	cols := map[key][]string{}
	for rows.Next() {
		var tableName, name, ctype string
		var col, refSchema, refTable, refCol, upd, del, check sql.NullString
		var ord sql.NullInt64
		if err := rows.Scan(&tableName, &name, &ctype, &col, &ord,
			&refSchema, &refTable, &refCol, &upd, &del, &check); err != nil {
			return err
		}
		k := key{tableName, name}
		c := cs[k]
		if c == nil {
			c = &model.Constraint{Name: name, Kind: constraintKind(ctype)}
			cs[k] = c
		}
		if col.Valid {
			cols[k] = append(cols[k], col.String)
		}
		if refSchema.Valid {
			c.RefSchema = refSchema.String
		}
		if refTable.Valid {
			c.RefTable = refTable.String
		}
		if refCol.Valid {
			c.RefColumns = append(c.RefColumns, refCol.String)
		}
		if upd.Valid {
			c.OnUpdate = upd.String
		}
		if del.Valid {
			c.OnDelete = del.String
		}
		if check.Valid {
			c.CheckExpr = check.String
		}
	}
	if err := rows.Err(); err != nil {
		return err
	}
	for k, c := range cs {
		c.Columns = cols[k]
		if t := out.Table(k.table); t != nil {
			t.Constraints = append(t.Constraints, *c)
		}
	}
	return nil
}

func constraintKind(s string) model.ConstraintKind {
	switch s {
	case "PRIMARY KEY":
		return model.ConstraintPK
	case "UNIQUE":
		return model.ConstraintUnique
	case "FOREIGN KEY":
		return model.ConstraintFK
	case "CHECK":
		return model.ConstraintCheck
	default:
		return model.ConstraintKind(strings.ToLower(s))
	}
}

func readViews(ctx context.Context, db *sql.DB, schema string) ([]model.View, error) {
	const q = `SELECT TABLE_NAME, VIEW_DEFINITION FROM information_schema.VIEWS WHERE TABLE_SCHEMA = ? ORDER BY TABLE_NAME`
	rows, err := db.QueryContext(ctx, q, schema)
	if err != nil {
		return nil, fmt.Errorf("读取视图: %w", err)
	}
	var out []model.View
	for rows.Next() {
		var name string
		var def sql.NullString
		if err := rows.Scan(&name, &def); err != nil {
			rows.Close()
			return nil, err
		}
		out = append(out, model.View{Schema: schema, Name: name, Definition: def.String})
	}
	rows.Close()
	if err := rows.Err(); err != nil {
		return nil, err
	}
	// MySQL 可直接从 VIEW_TABLE_USAGE 得到视图 -> 表依赖。
	deps := map[string][]string{}
	dr, err := db.QueryContext(ctx, `SELECT VIEW_NAME, TABLE_NAME FROM information_schema.VIEW_TABLE_USAGE WHERE VIEW_SCHEMA = ? AND TABLE_SCHEMA = ? ORDER BY VIEW_NAME, TABLE_NAME`, schema, schema)
	if err == nil {
		for dr.Next() {
			var v, t string
			if err := dr.Scan(&v, &t); err == nil {
				deps[v] = append(deps[v], t)
			}
		}
		dr.Close()
	}
	for i := range out {
		out[i].Dependencies = deps[out[i].Name]
	}
	return out, nil
}

func readRoutines(ctx context.Context, db *sql.DB, schema string) ([]model.Routine, error) {
	const q = `
SELECT ROUTINE_NAME, ROUTINE_TYPE, ROUTINE_DEFINITION
FROM information_schema.ROUTINES
WHERE ROUTINE_SCHEMA = ?
ORDER BY ROUTINE_NAME, ROUTINE_TYPE`
	rows, err := db.QueryContext(ctx, q, schema)
	if err != nil {
		return nil, fmt.Errorf("读取过程/函数: %w", err)
	}
	defer rows.Close()
	var out []model.Routine
	for rows.Next() {
		var name, kind string
		var def sql.NullString
		if err := rows.Scan(&name, &kind, &def); err != nil {
			return nil, err
		}
		out = append(out, model.Routine{Schema: schema, Name: name, Kind: kind, Definition: def.String})
	}
	return out, rows.Err()
}

func readTriggers(ctx context.Context, db *sql.DB, schema string) ([]model.Trigger, error) {
	const q = `
SELECT TRIGGER_NAME, EVENT_OBJECT_TABLE, ACTION_TIMING, EVENT_MANIPULATION, ACTION_STATEMENT
FROM information_schema.TRIGGERS
WHERE TRIGGER_SCHEMA = ?
ORDER BY TRIGGER_NAME`
	rows, err := db.QueryContext(ctx, q, schema)
	if err != nil {
		return nil, fmt.Errorf("读取触发器: %w", err)
	}
	defer rows.Close()
	var out []model.Trigger
	for rows.Next() {
		var name, table, timing, event, stmt string
		if err := rows.Scan(&name, &table, &timing, &event, &stmt); err != nil {
			return nil, err
		}
		out = append(out, model.Trigger{
			Schema: schema, Name: name, Table: table,
			Timing: timing, Event: event, Statement: stmt,
		})
	}
	return out, rows.Err()
}

// Quote 引用 MySQL 标识符。
func Quote(s string) string {
	return "`" + strings.ReplaceAll(s, "`", "``") + "`"
}

// Version 读取服务端版本，供测试/诊断使用。
func Version(ctx context.Context, db *sql.DB) (string, error) {
	var v string
	if err := db.QueryRowContext(ctx, "SELECT VERSION()").Scan(&v); err != nil {
		return "", err
	}
	return v, nil
}

// Database 返回当前连接默认库。
func Database(ctx context.Context, db *sql.DB) (string, error) {
	var v sql.NullString
	if err := db.QueryRowContext(ctx, "SELECT DATABASE()").Scan(&v); err != nil {
		return "", err
	}
	return v.String, nil
}

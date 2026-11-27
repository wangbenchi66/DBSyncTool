// Package postgres 读取 PostgreSQL 的结构 IR。只读：连接设置 default_transaction_read_only=on。
package postgres

import (
	"context"
	"database/sql"
	"fmt"
	"strings"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/stdlib"

	"gitee.com/wangbenchi66/dbsync/internal/model"
)

func Open(dsn string) (*sql.DB, error) {
	cfg, err := pgx.ParseConfig(dsn)
	if err != nil {
		return nil, err
	}
	if cfg.RuntimeParams == nil {
		cfg.RuntimeParams = map[string]string{}
	}
	cfg.RuntimeParams["default_transaction_read_only"] = "on"
	cfg.RuntimeParams["application_name"] = "dbsync"
	db := sql.OpenDB(stdlib.GetConnector(*cfg))
	if err := db.Ping(); err != nil {
		db.Close()
		return nil, err
	}
	return db, nil
}

func Version(ctx context.Context, db *sql.DB) (string, error) {
	var v string
	if err := db.QueryRowContext(ctx, "SHOW server_version").Scan(&v); err != nil {
		return "", err
	}
	return v, nil
}

func Introspect(ctx context.Context, db *sql.DB, schema string) (*model.Schema, error) {
	if schema == "" {
		schema = "public"
	}
	out := &model.Schema{Name: schema}
	rows, err := db.QueryContext(ctx, `SELECT table_name FROM information_schema.tables WHERE table_schema=$1 AND table_type='BASE TABLE' ORDER BY table_name`, schema)
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
		if err := readChecks(ctx, db, &t); err != nil {
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
SELECT column_name, ordinal_position, column_default, is_nullable, data_type, udt_name,
       character_maximum_length, numeric_precision, numeric_scale, datetime_precision,
       collation_name, is_identity, is_generated, generation_expression
FROM information_schema.columns
WHERE table_schema=$1 AND table_name=$2
ORDER BY ordinal_position`
	rows, err := db.QueryContext(ctx, q, t.Schema, t.Name)
	if err != nil {
		return err
	}
	defer rows.Close()
	for rows.Next() {
		var name, dataType, udt, nullable, isIdentity, isGenerated string
		var ordinal int
		var def, genExpr, collation sql.NullString
		var charLen, numPrec, numScale, dtPrec sql.NullInt64
		if err := rows.Scan(&name, &ordinal, &def, &nullable, &dataType, &udt,
			&charLen, &numPrec, &numScale, &dtPrec, &collation,
			&isIdentity, &isGenerated, &genExpr); err != nil {
			return err
		}
		c := model.Column{
			Name: name, Ordinal: ordinal,
			Type:           mapType(dataType, udt, charLen, numPrec, numScale, dtPrec, collation.String),
			Nullable:       strings.EqualFold(nullable, "YES"),
			Identity:       strings.EqualFold(isIdentity, "YES"),
			Generated:      strings.EqualFold(isGenerated, "ALWAYS"),
			GenerationExpr: genExpr.String,
			Comment:        "",
		}
		if def.Valid {
			v := def.String
			c.Default = &v
			c.DefaultSet = true
			if strings.Contains(strings.ToLower(v), "nextval(") {
				c.Identity = true
			}
		}
		t.Columns = append(t.Columns, c)
	}
	return rows.Err()
}

func mapType(dataType, udt string, charLen, numPrec, numScale, dtPrec sql.NullInt64, collation string) model.Type {
	dt := strings.ToLower(dataType)
	typ := model.Type{ID: model.TypeUnknown, Raw: udt, Length: -1, Precision: -1, Scale: -1, Collation: collation}
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
	case "boolean":
		typ.ID = model.TypeBool
	case "smallint", "integer", "bigint":
		typ.ID = model.TypeInt
	case "numeric", "decimal":
		typ.ID = model.TypeDecimal
	case "real", "double precision":
		typ.ID = model.TypeFloat
	case "character varying", "character":
		typ.ID = model.TypeString
	case "text":
		typ.ID = model.TypeText
	case "bytea":
		typ.ID = model.TypeBinary
	case "date":
		typ.ID = model.TypeDate
	case "time without time zone", "time with time zone":
		typ.ID = model.TypeTime
	case "timestamp without time zone":
		typ.ID = model.TypeDateTime
	case "timestamp with time zone":
		typ.ID = model.TypeTimestamp
	case "json", "jsonb":
		typ.ID = model.TypeJSON
	case "uuid":
		typ.ID = model.TypeString
	default:
		typ.ID = model.TypeString
	}
	switch typ.ID {
	case model.TypeString:
		if charLen.Valid {
			typ.Raw = fmt.Sprintf("varchar(%d)", charLen.Int64)
		}
	case model.TypeDecimal:
		if numPrec.Valid {
			typ.Raw = fmt.Sprintf("numeric(%d,%d)", numPrec.Int64, numScale.Int64)
		}
	}
	if typ.Raw == "" {
		typ.Raw = udt
	}
	return typ
}

func readIndexes(ctx context.Context, db *sql.DB, t *model.Table) error {
	const q = `
SELECT i.relname, ix.indisunique, ix.indisprimary, a.attname, k.ord
FROM pg_index ix
JOIN pg_class t ON t.oid = ix.indrelid
JOIN pg_namespace n ON n.oid = t.relnamespace
JOIN pg_class i ON i.oid = ix.indexrelid
CROSS JOIN LATERAL unnest(ix.indkey) WITH ORDINALITY AS k(attnum, ord)
LEFT JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum
WHERE n.nspname=$1 AND t.relname=$2
ORDER BY i.relname, k.ord`
	rows, err := db.QueryContext(ctx, q, t.Schema, t.Name)
	if err != nil {
		return err
	}
	defer rows.Close()
	ixs := map[string]*model.Index{}
	var order []string
	for rows.Next() {
		var name string
		var unique, primary bool
		var col sql.NullString
		var ord int
		if err := rows.Scan(&name, &unique, &primary, &col, &ord); err != nil {
			return err
		}
		ix := ixs[name]
		if ix == nil {
			ix = &model.Index{Name: name, Unique: unique, Primary: primary, Type: "btree"}
			ixs[name] = ix
			order = append(order, name)
		}
		if col.Valid {
			ix.Columns = append(ix.Columns, model.IndexColumn{Name: col.String})
		}
	}
	for _, n := range order {
		t.Indexes = append(t.Indexes, *ixs[n])
	}
	return rows.Err()
}

func readConstraints(ctx context.Context, db *sql.DB, t *model.Table) error {
	const q = `
SELECT tc.constraint_name, tc.constraint_type,
       kcu.column_name, kcu.ordinal_position,
       ccu.table_schema, ccu.table_name, ccu.column_name,
       rc.update_rule, rc.delete_rule
FROM information_schema.table_constraints tc
LEFT JOIN information_schema.key_column_usage kcu
  ON kcu.constraint_schema=tc.constraint_schema AND kcu.constraint_name=tc.constraint_name
 AND kcu.table_schema=tc.table_schema AND kcu.table_name=tc.table_name
LEFT JOIN information_schema.constraint_column_usage ccu
  ON ccu.constraint_schema=tc.constraint_schema AND ccu.constraint_name=tc.constraint_name
LEFT JOIN information_schema.referential_constraints rc
  ON rc.constraint_schema=tc.constraint_schema AND rc.constraint_name=tc.constraint_name
WHERE tc.table_schema=$1 AND tc.table_name=$2
  AND tc.constraint_type IN ('PRIMARY KEY','UNIQUE','FOREIGN KEY')
ORDER BY tc.constraint_name, kcu.ordinal_position`
	rows, err := db.QueryContext(ctx, q, t.Schema, t.Name)
	if err != nil {
		return err
	}
	defer rows.Close()
	cs := map[string]*model.Constraint{}
	var order []string
	for rows.Next() {
		var name, ctype string
		var col, refSchema, refTable, refCol, upd, del sql.NullString
		var ord sql.NullInt64
		if err := rows.Scan(&name, &ctype, &col, &ord, &refSchema, &refTable, &refCol, &upd, &del); err != nil {
			return err
		}
		c := cs[name]
		if c == nil {
			c = &model.Constraint{Name: name, Kind: kindOf(ctype)}
			cs[name] = c
			order = append(order, name)
		}
		if col.Valid {
			c.Columns = append(c.Columns, col.String)
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
	}
	for _, n := range order {
		t.Constraints = append(t.Constraints, *cs[n])
	}
	return rows.Err()
}

func readChecks(ctx context.Context, db *sql.DB, t *model.Table) error {
	const q = `
SELECT con.conname, pg_get_constraintdef(con.oid)
FROM pg_constraint con
JOIN pg_class c ON c.oid=con.conrelid
JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE n.nspname=$1 AND c.relname=$2 AND con.contype='c'
ORDER BY con.conname`
	rows, err := db.QueryContext(ctx, q, t.Schema, t.Name)
	if err != nil {
		return err
	}
	defer rows.Close()
	for rows.Next() {
		var name, def string
		if err := rows.Scan(&name, &def); err != nil {
			return err
		}
		t.Constraints = append(t.Constraints, model.Constraint{Name: name, Kind: model.ConstraintCheck, CheckExpr: def})
	}
	return rows.Err()
}

func kindOf(s string) model.ConstraintKind {
	switch s {
	case "PRIMARY KEY":
		return model.ConstraintPK
	case "UNIQUE":
		return model.ConstraintUnique
	case "FOREIGN KEY":
		return model.ConstraintFK
	default:
		return model.ConstraintKind(strings.ToLower(s))
	}
}

func readViews(ctx context.Context, db *sql.DB, schema string) ([]model.View, error) {
	rows, err := db.QueryContext(ctx, `SELECT viewname, definition FROM pg_views WHERE schemaname=$1 ORDER BY viewname`, schema)
	if err != nil {
		return nil, err
	}
	var out []model.View
	for rows.Next() {
		var n, d string
		if err := rows.Scan(&n, &d); err != nil {
			rows.Close()
			return nil, err
		}
		out = append(out, model.View{Schema: schema, Name: n, Definition: d})
	}
	rows.Close()
	if err := rows.Err(); err != nil {
		return nil, err
	}
	deps := map[string][]string{}
	dr, err := db.QueryContext(ctx, `
SELECT DISTINCT v.relname, t.relname
FROM pg_depend d
JOIN pg_rewrite r ON r.oid=d.objid
JOIN pg_class v ON v.oid=r.ev_class
JOIN pg_class t ON t.oid=d.refobjid
JOIN pg_namespace nv ON nv.oid=v.relnamespace
WHERE d.classid='pg_rewrite'::regclass
  AND d.refclassid='pg_class'::regclass
  AND v.relkind='v'
  AND t.relkind IN ('r','v')
  AND t.oid <> v.oid
  AND nv.nspname=$1
ORDER BY 1,2`, schema)
	if err == nil {
		for dr.Next() {
			var v, t string
			if err := dr.Scan(&v, &t); err == nil {
				deps[v] = appendUnique(deps[v], t)
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
	rows, err := db.QueryContext(ctx, `SELECT routine_name, routine_type, routine_definition FROM information_schema.routines WHERE routine_schema=$1 ORDER BY routine_name, routine_type`, schema)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	var out []model.Routine
	for rows.Next() {
		var n, kind string
		var def sql.NullString
		if err := rows.Scan(&n, &kind, &def); err != nil {
			return nil, err
		}
		out = append(out, model.Routine{Schema: schema, Name: n, Kind: kind, Definition: def.String})
	}
	return out, rows.Err()
}

func readTriggers(ctx context.Context, db *sql.DB, schema string) ([]model.Trigger, error) {
	const q = `
SELECT t.tgname, c.relname, pg_get_triggerdef(t.oid)
FROM pg_trigger t
JOIN pg_class c ON c.oid=t.tgrelid
JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE n.nspname=$1 AND NOT t.tgisinternal
ORDER BY t.tgname`
	rows, err := db.QueryContext(ctx, q, schema)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	var out []model.Trigger
	for rows.Next() {
		var name, table, def string
		if err := rows.Scan(&name, &table, &def); err != nil {
			return nil, err
		}
		out = append(out, model.Trigger{Schema: schema, Name: name, Table: table, Statement: def})
	}
	return out, rows.Err()
}

func Quote(s string) string { return `"` + strings.ReplaceAll(s, `"`, `""`) + `"` }

func appendUnique(xs []string, v string) []string {
	for _, x := range xs {
		if x == v {
			return xs
		}
	}
	return append(xs, v)
}

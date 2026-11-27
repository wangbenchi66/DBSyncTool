// Package sqlite 读取 SQLite 的结构 IR。只读：Open 使用 mode=ro + query_only。
package sqlite

import (
	"context"
	"database/sql"
	"fmt"
	"net/url"
	"regexp"
	"strconv"
	"strings"

	_ "modernc.org/sqlite"

	"gitee.com/wangbenchi66/dbsync/internal/model"
)

var viewRef = regexp.MustCompile(`(?is)\b(?:from|join)\s+["\\x60\\[]?([A-Za-z_][A-Za-z0-9_$]*)`)

func Open(path string) (*sql.DB, error) {
	u := "file:" + url.PathEscape(path) + "?mode=ro&_pragma=query_only(1)"
	db, err := sql.Open("sqlite", u)
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
	if err := db.QueryRowContext(ctx, "SELECT sqlite_version()").Scan(&v); err != nil {
		return "", err
	}
	return v, nil
}

func Introspect(ctx context.Context, db *sql.DB, schema string) (*model.Schema, error) {
	if schema == "" {
		schema = "main"
	}
	out := &model.Schema{Name: schema}
	rows, err := db.QueryContext(ctx, `SELECT name, sql FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name`)
	if err != nil {
		return nil, fmt.Errorf("读取表: %w", err)
	}
	for rows.Next() {
		var name string
		var create sql.NullString
		if err := rows.Scan(&name, &create); err != nil {
			rows.Close()
			return nil, err
		}
		t := model.Table{Schema: schema, Name: name}
		if err := readColumns(ctx, db, &t); err != nil {
			rows.Close()
			return nil, err
		}
		if err := readIndexes(ctx, db, &t); err != nil {
			rows.Close()
			return nil, err
		}
		if err := readFKs(ctx, db, &t); err != nil {
			rows.Close()
			return nil, err
		}
		out.Tables = append(out.Tables, t)
	}
	rows.Close()
	if err := rows.Err(); err != nil {
		return nil, err
	}
	if out.Views, err = readViews(ctx, db); err != nil {
		return nil, err
	}
	if out.Triggers, err = readTriggers(ctx, db); err != nil {
		return nil, err
	}
	out.Sort()
	return out, nil
}

func readColumns(ctx context.Context, db *sql.DB, t *model.Table) error {
	q := `PRAGMA table_xinfo(` + quote(t.Name) + `)`
	rows, err := db.QueryContext(ctx, q)
	if err != nil {
		return err
	}
	defer rows.Close()
	for rows.Next() {
		var cid, notnull, pk, hidden int
		var name, typ string
		var def sql.NullString
		if err := rows.Scan(&cid, &name, &typ, &notnull, &def, &pk, &hidden); err != nil {
			return err
		}
		c := model.Column{
			Name:      name,
			Ordinal:   cid + 1,
			Type:      mapType(typ),
			Nullable:  notnull == 0,
			Identity:  pk > 0 && strings.EqualFold(typ, "INTEGER"),
			Generated: hidden == 2 || hidden == 3,
		}
		if def.Valid {
			v := def.String
			c.Default = &v
			c.DefaultSet = true
		}
		t.Columns = append(t.Columns, c)
	}
	// PRIMARY 约束按 pk 顺序收集
	var pkCols []string
	rows2, err := db.QueryContext(ctx, `PRAGMA table_info(`+quote(t.Name)+`)`)
	if err != nil {
		return err
	}
	defer rows2.Close()
	type pkCol struct {
		seq  int
		name string
	}
	var pcs []pkCol
	for rows2.Next() {
		var cid, notnull, pk int
		var name, typ string
		var def sql.NullString
		if err := rows2.Scan(&cid, &name, &typ, &notnull, &def, &pk); err != nil {
			return err
		}
		if pk > 0 {
			pcs = append(pcs, pkCol{pk, name})
		}
	}
	for i := 1; i <= len(pcs); i++ {
		for _, p := range pcs {
			if p.seq == i {
				pkCols = append(pkCols, p.name)
			}
		}
	}
	if len(pkCols) > 0 {
		t.Constraints = append(t.Constraints, model.Constraint{Name: "PRIMARY", Kind: model.ConstraintPK, Columns: pkCols})
		ix := model.Index{Name: "PRIMARY", Primary: true, Unique: true, Type: "btree"}
		for _, c := range pkCols {
			ix.Columns = append(ix.Columns, model.IndexColumn{Name: c})
		}
		t.Indexes = append(t.Indexes, ix)
	}
	return rows.Err()
}

func readIndexes(ctx context.Context, db *sql.DB, t *model.Table) error {
	rows, err := db.QueryContext(ctx, `PRAGMA index_list(`+quote(t.Name)+`)`)
	if err != nil {
		return err
	}
	defer rows.Close()
	var names []string
	var uniques []bool
	for rows.Next() {
		var seq, unique, partial int
		var name, origin string
		if err := rows.Scan(&seq, &name, &unique, &origin, &partial); err != nil {
			return err
		}
		if name == "PRIMARY" || strings.HasPrefix(name, "sqlite_autoindex") {
			continue
		}
		names = append(names, name)
		uniques = append(uniques, unique != 0)
	}
	for i, name := range names {
		ix := model.Index{Name: name, Unique: uniques[i], Type: "btree"}
		ir, err := db.QueryContext(ctx, `PRAGMA index_info(`+quote(name)+`)`)
		if err != nil {
			return err
		}
		for ir.Next() {
			var seqno, cid int
			var col string
			if err := ir.Scan(&seqno, &cid, &col); err != nil {
				ir.Close()
				return err
			}
			ix.Columns = append(ix.Columns, model.IndexColumn{Name: col})
		}
		ir.Close()
		t.Indexes = append(t.Indexes, ix)
	}
	return rows.Err()
}

func readFKs(ctx context.Context, db *sql.DB, t *model.Table) error {
	rows, err := db.QueryContext(ctx, `PRAGMA foreign_key_list(`+quote(t.Name)+`)`)
	if err != nil {
		return err
	}
	defer rows.Close()
	fks := map[int]*model.Constraint{}
	var order []int
	for rows.Next() {
		var id, seq int
		var refTable, from, to, onUpdate, onDelete, match string
		if err := rows.Scan(&id, &seq, &refTable, &from, &to, &onUpdate, &onDelete, &match); err != nil {
			return err
		}
		c := fks[id]
		if c == nil {
			c = &model.Constraint{Name: "fk_" + t.Name + "_" + strconv.Itoa(id), Kind: model.ConstraintFK, RefSchema: "main", RefTable: refTable, OnUpdate: onUpdate, OnDelete: onDelete}
			fks[id] = c
			order = append(order, id)
		}
		c.Columns = append(c.Columns, from)
		c.RefColumns = append(c.RefColumns, to)
	}
	for _, id := range order {
		t.Constraints = append(t.Constraints, *fks[id])
	}
	return rows.Err()
}

func readViews(ctx context.Context, db *sql.DB) ([]model.View, error) {
	rows, err := db.QueryContext(ctx, `SELECT name, sql FROM sqlite_master WHERE type='view' ORDER BY name`)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	var out []model.View
	for rows.Next() {
		var name string
		var def sql.NullString
		if err := rows.Scan(&name, &def); err != nil {
			return nil, err
		}
		out = append(out, model.View{Schema: "main", Name: name, Definition: def.String, Dependencies: refs(def.String)})
	}
	return out, rows.Err()
}

func refs(definition string) []string {
	var out []string
	seen := map[string]bool{}
	for _, m := range viewRef.FindAllStringSubmatch(definition, -1) {
		n := m[1]
		if n == "" || strings.EqualFold(n, "select") || seen[n] {
			continue
		}
		seen[n] = true
		out = append(out, n)
	}
	return out
}

func readTriggers(ctx context.Context, db *sql.DB) ([]model.Trigger, error) {
	rows, err := db.QueryContext(ctx, `SELECT name, tbl_name, sql FROM sqlite_master WHERE type='trigger' ORDER BY name`)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	var out []model.Trigger
	for rows.Next() {
		var name, table string
		var def sql.NullString
		if err := rows.Scan(&name, &table, &def); err != nil {
			return nil, err
		}
		out = append(out, model.Trigger{Schema: "main", Name: name, Table: table, Statement: def.String})
	}
	return out, rows.Err()
}

func mapType(declared string) model.Type {
	raw := declared
	u := strings.ToUpper(declared)
	t := model.Type{Raw: raw, Length: -1, Precision: -1, Scale: -1}
	switch {
	case u == "":
		t.ID = model.TypeBinary
	case strings.Contains(u, "INT"):
		t.ID = model.TypeInt
	case strings.Contains(u, "CHAR"), strings.Contains(u, "CLOB"), strings.Contains(u, "TEXT"):
		t.ID = model.TypeString
	case strings.Contains(u, "BLOB"):
		t.ID = model.TypeBinary
	case strings.Contains(u, "REAL"), strings.Contains(u, "FLOA"), strings.Contains(u, "DOUB"):
		t.ID = model.TypeFloat
	case strings.Contains(u, "DEC"), strings.Contains(u, "NUM"):
		t.ID = model.TypeDecimal
	case strings.Contains(u, "DATE"), strings.Contains(u, "TIME"):
		t.ID = model.TypeDateTime
	default:
		t.ID = model.TypeString
	}
	return t
}

func quote(s string) string { return `"` + strings.ReplaceAll(s, `"`, `""`) + `"` }

// Quote 供测试/调用方使用。
func Quote(s string) string { return quote(s) }

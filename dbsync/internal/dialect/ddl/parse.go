package ddl

import (
	"fmt"
	"os"
	"path/filepath"
	"regexp"
	"sort"
	"strconv"
	"strings"

	"gitee.com/wangbenchi66/dbsync/internal/model"
)

func ReadDir(dir string) (*model.Schema, error) {
	entries, err := os.ReadDir(dir)
	if err != nil {
		return nil, err
	}
	var files []string
	for _, e := range entries {
		if e.IsDir() {
			continue
		}
		switch strings.ToLower(filepath.Ext(e.Name())) {
		case ".sql", ".ddl":
			files = append(files, filepath.Join(dir, e.Name()))
		}
	}
	sort.Strings(files)
	if len(files) == 0 {
		return nil, fmt.Errorf("目录中没有 .sql/.ddl 文件")
	}
	out := &model.Schema{Name: "dir"}
	for _, f := range files {
		b, err := os.ReadFile(f)
		if err != nil {
			return nil, err
		}
		s, err := Parse(string(b))
		if err != nil {
			return nil, fmt.Errorf("%s: %w", filepath.Base(f), err)
		}
		out.Tables = append(out.Tables, s.Tables...)
		out.Views = append(out.Views, s.Views...)
		out.Triggers = append(out.Triggers, s.Triggers...)
	}
	out.Sort()
	return out, nil
}

func Parse(text string) (*model.Schema, error) {
	out := &model.Schema{Name: "dir"}
	for _, stmt := range splitStatements(text) {
		s := strings.TrimSpace(stmt)
		if s == "" {
			continue
		}
		up := strings.ToUpper(s)
		switch {
		case strings.Contains(up, "CREATE TABLE"):
			t, err := parseTable(s)
			if err != nil {
				return nil, err
			}
			if t != nil {
				out.Tables = append(out.Tables, *t)
			}
		case strings.Contains(up, "CREATE") && strings.Contains(up, "VIEW"):
			if v := parseView(s); v != nil {
				out.Views = append(out.Views, *v)
			}
		case strings.Contains(up, "CREATE") && strings.Contains(up, "TRIGGER"):
			if tr := parseTrigger(s); tr != nil {
				out.Triggers = append(out.Triggers, *tr)
			}
		}
	}
	out.Sort()
	return out, nil
}

func parseTable(s string) (*model.Table, error) {
	i := strings.Index(strings.ToUpper(s), "CREATE TABLE")
	rest := strings.TrimSpace(s[i+len("CREATE TABLE"):])
	rest = strings.TrimSpace(strings.TrimPrefix(strings.TrimSpace(rest), "IF NOT EXISTS"))
	open := strings.IndexByte(rest, '(')
	if open < 0 {
		return nil, fmt.Errorf("CREATE TABLE 缺少列定义")
	}
	name := cleanIdent(strings.TrimSpace(rest[:open]))
	if name == "" {
		return nil, fmt.Errorf("CREATE TABLE 缺少表名")
	}
	close := matchingParen(rest, open)
	if close < 0 {
		return nil, fmt.Errorf("CREATE TABLE 括号不匹配")
	}
	body := rest[open+1 : close]
	t := &model.Table{Schema: "dir", Name: name}
	for _, item := range splitTopLevel(body) {
		item = strings.TrimSpace(item)
		if item == "" {
			continue
		}
		up := strings.ToUpper(item)
		switch {
		case strings.HasPrefix(up, "PRIMARY KEY"):
			cols := parenColumns(item)
			t.Constraints = append(t.Constraints, model.Constraint{Name: "PRIMARY", Kind: model.ConstraintPK, Columns: cols})
			t.Indexes = append(t.Indexes, model.Index{Name: "PRIMARY", Primary: true, Unique: true, Type: "btree", Columns: indexCols(cols)})
		case strings.HasPrefix(up, "UNIQUE"):
			nm, cols := namedKey(item)
			t.Constraints = append(t.Constraints, model.Constraint{Name: nm, Kind: model.ConstraintUnique, Columns: cols})
			t.Indexes = append(t.Indexes, model.Index{Name: nm, Unique: true, Type: "btree", Columns: indexCols(cols)})
		case strings.HasPrefix(up, "KEY "), strings.HasPrefix(up, "INDEX "):
			nm, cols := namedKey(item)
			t.Indexes = append(t.Indexes, model.Index{Name: nm, Type: "btree", Columns: indexCols(cols)})
		case strings.HasPrefix(up, "CONSTRAINT"), strings.HasPrefix(up, "FOREIGN KEY"):
			if c := parseFK(item); c != nil {
				t.Constraints = append(t.Constraints, *c)
			}
		case strings.HasPrefix(up, "CHECK"):
			t.Constraints = append(t.Constraints, model.Constraint{Name: "check_" + strconv.Itoa(len(t.Constraints)+1), Kind: model.ConstraintCheck, CheckExpr: item})
		default:
			c, err := parseColumn(item, len(t.Columns)+1)
			if err != nil {
				return nil, fmt.Errorf("%s: %w", name, err)
			}
			t.Columns = append(t.Columns, *c)
		}
	}
	return t, nil
}

func parseColumn(item string, ordinal int) (*model.Column, error) {
	name, rest := firstToken(item)
	if name == "" {
		return nil, fmt.Errorf("列定义为空")
	}
	name = cleanIdent(name)
	if name == "" {
		return nil, fmt.Errorf("列名为空")
	}
	attr := findAttr(rest)
	typeText := strings.TrimSpace(rest[:attr])
	attrs := strings.TrimSpace(rest[attr:])
	c := &model.Column{Name: name, Ordinal: ordinal, Type: parseType(typeText)}
	c.Nullable = !strings.Contains(strings.ToUpper(attrs), "NOT NULL")
	c.Identity = strings.Contains(strings.ToUpper(attrs), "AUTO_INCREMENT")
	c.Generated = strings.Contains(strings.ToUpper(attrs), "GENERATED") || strings.Contains(strings.ToUpper(typeText), " AS ")
	c.Type.Collation = attrValue(attrs, "COLLATE")
	if d, ok := defaultValue(attrs); ok {
		c.Default = &d
		c.DefaultSet = true
	}
	return c, nil
}

func parseType(s string) model.Type {
	raw := strings.TrimSpace(s)
	u := strings.ToUpper(raw)
	t := model.Type{ID: model.TypeString, Raw: raw, Length: -1, Precision: -1, Scale: -1}
	base := u
	if i := strings.IndexByte(u, '('); i >= 0 {
		base = strings.TrimSpace(u[:i])
	}
	args := ""
	if i := strings.IndexByte(raw, '('); i >= 0 {
		if j := matchingParen(raw, i); j > i {
			args = raw[i+1 : j]
		}
	}
	switch {
	case base == "TINYINT" && strings.TrimSpace(args) == "1", base == "BOOL", base == "BOOLEAN":
		t.ID = model.TypeBool
	case strings.Contains(base, "INT"):
		t.ID = model.TypeInt
	case base == "DECIMAL", base == "NUMERIC":
		t.ID = model.TypeDecimal
		parts := strings.Split(args, ",")
		if len(parts) > 0 {
			t.Precision, _ = strconv.Atoi(strings.TrimSpace(parts[0]))
		}
		if len(parts) > 1 {
			t.Scale, _ = strconv.Atoi(strings.TrimSpace(parts[1]))
		}
	case base == "FLOAT", base == "DOUBLE", base == "REAL":
		t.ID = model.TypeFloat
	case base == "CHAR", base == "VARCHAR":
		t.ID = model.TypeString
		t.Length, _ = strconv.ParseInt(strings.TrimSpace(args), 10, 64)
	case strings.Contains(base, "TEXT"):
		t.ID = model.TypeText
	case strings.Contains(base, "BLOB"), strings.Contains(base, "BINARY"):
		t.ID = model.TypeBinary
	case base == "DATE":
		t.ID = model.TypeDate
	case base == "TIME":
		t.ID = model.TypeTime
	case base == "DATETIME":
		t.ID = model.TypeDateTime
	case base == "TIMESTAMP":
		t.ID = model.TypeTimestamp
	case base == "JSON":
		t.ID = model.TypeJSON
	case base == "ENUM":
		t.ID = model.TypeEnum
	case base == "SET":
		t.ID = model.TypeSet
	}
	return t
}

func parseView(s string) *model.View {
	re := regexp.MustCompile(`(?is)\bCREATE\b.*?\bVIEW\s+([^\s(]+)\s+AS\s+(.*)$`)
	m := re.FindStringSubmatch(s)
	if len(m) != 3 {
		return nil
	}
	return &model.View{Schema: "dir", Name: cleanIdent(m[1]), Definition: strings.TrimSpace(m[2])}
}

func parseTrigger(s string) *model.Trigger {
	re := regexp.MustCompile(`(?is)\bCREATE\b.*?\bTRIGGER\s+([^\s]+)\s+.*?\bON\s+([^\s]+)`)
	m := re.FindStringSubmatch(s)
	if len(m) != 3 {
		return nil
	}
	return &model.Trigger{Schema: "dir", Name: cleanIdent(m[1]), Table: cleanIdent(m[2]), Statement: strings.TrimSpace(s)}
}

func parseFK(item string) *model.Constraint {
	up := strings.ToUpper(item)
	fk := strings.Index(up, "FOREIGN KEY")
	if fk < 0 {
		return nil
	}
	name := "fk_" + strconv.Itoa(len(item))
	if strings.HasPrefix(up, "CONSTRAINT") {
		parts := strings.Fields(item)
		if len(parts) > 1 {
			name = cleanIdent(parts[1])
		}
	}
	cols := parenColumns(item[fk:])
	ri := strings.Index(up, "REFERENCES")
	if ri < 0 {
		return nil
	}
	rest := strings.TrimSpace(item[ri+len("REFERENCES"):])
	refTable, rest := firstToken(rest)
	if i := strings.IndexByte(refTable, '('); i >= 0 {
		rest = refTable[i:] + " " + rest
		refTable = refTable[:i]
	}
	c := &model.Constraint{Name: name, Kind: model.ConstraintFK, RefSchema: "dir", RefTable: cleanIdent(refTable), Columns: cols}
	c.RefColumns = parenColumns(rest)
	if i := strings.Index(strings.ToUpper(rest), "ON DELETE"); i >= 0 {
		c.OnDelete = firstWord(rest[i+len("ON DELETE"):])
	}
	if i := strings.Index(strings.ToUpper(rest), "ON UPDATE"); i >= 0 {
		c.OnUpdate = firstWord(rest[i+len("ON UPDATE"):])
	}
	return c
}

package ddl

import (
	"strings"

	"gitee.com/wangbenchi66/dbsync/internal/model"
)

func splitStatements(s string) []string {
	var out []string
	start := 0
	inS, inD, inB := false, false, false
	line, block := false, false
	for i := 0; i < len(s); i++ {
		c := s[i]
		if line {
			if c == '\n' {
				line = false
			}
			continue
		}
		if block {
			if c == '*' && i+1 < len(s) && s[i+1] == '/' {
				block = false
				i++
			}
			continue
		}
		if inS || inD || inB {
			if c == '\\' {
				i++
				continue
			}
			if inS && c == '\'' {
				if i+1 < len(s) && s[i+1] == '\'' {
					i++
					continue
				}
				inS = false
			}
			if inD && c == '"' {
				inD = false
			}
			if inB && c == '`' {
				inB = false
			}
			continue
		}
		switch c {
		case '\'':
			inS = true
		case '"':
			inD = true
		case '`':
			inB = true
		case '#':
			line = true
		case '-':
			if i+1 < len(s) && s[i+1] == '-' {
				line = true
				i++
			}
		case '/':
			if i+1 < len(s) && s[i+1] == '*' {
				block = true
				i++
			}
		case ';':
			out = append(out, strings.TrimSpace(s[start:i]))
			start = i + 1
		}
	}
	if strings.TrimSpace(s[start:]) != "" {
		out = append(out, strings.TrimSpace(s[start:]))
	}
	return out
}

func matchingParen(s string, open int) int {
	depth := 0
	inS, inD, inB := false, false, false
	for i := open; i < len(s); i++ {
		c := s[i]
		if inS || inD || inB {
			if c == '\\' {
				i++
				continue
			}
			if inS && c == '\'' {
				inS = false
			}
			if inD && c == '"' {
				inD = false
			}
			if inB && c == '`' {
				inB = false
			}
			continue
		}
		switch c {
		case '\'':
			inS = true
		case '"':
			inD = true
		case '`':
			inB = true
		case '(':
			depth++
		case ')':
			depth--
			if depth == 0 {
				return i
			}
		}
	}
	return -1
}

func splitTopLevel(s string) []string {
	var out []string
	start, depth := 0, 0
	inS, inD, inB := false, false, false
	for i := 0; i < len(s); i++ {
		c := s[i]
		if inS || inD || inB {
			if c == '\\' {
				i++
				continue
			}
			if inS && c == '\'' {
				inS = false
			}
			if inD && c == '"' {
				inD = false
			}
			if inB && c == '`' {
				inB = false
			}
			continue
		}
		switch c {
		case '\'':
			inS = true
		case '"':
			inD = true
		case '`':
			inB = true
		case '(':
			depth++
		case ')':
			depth--
		case ',':
			if depth == 0 {
				out = append(out, strings.TrimSpace(s[start:i]))
				start = i + 1
			}
		}
	}
	out = append(out, strings.TrimSpace(s[start:]))
	return out
}

func cleanIdent(s string) string {
	s = strings.TrimSpace(s)
	if i := strings.LastIndexByte(s, '.'); i >= 0 {
		s = s[i+1:]
	}
	s = strings.Trim(s, "`\"[] ")
	return s
}

func firstToken(s string) (string, string) {
	s = strings.TrimSpace(s)
	if s == "" {
		return "", ""
	}
	if s[0] == '`' || s[0] == '"' || s[0] == '[' {
		close := byte('`')
		if s[0] == '"' {
			close = '"'
		}
		if s[0] == '[' {
			close = ']'
		}
		if i := strings.IndexByte(s[1:], close); i >= 0 {
			return s[:i+2], s[i+2:]
		}
	}
	for i, c := range s {
		if c == ' ' || c == '\t' || c == '\n' || c == '\r' {
			return s[:i], s[i:]
		}
	}
	return s, ""
}

func findAttr(s string) int {
	up := strings.ToUpper(s)
	attrs := []string{" NOT ", " NULL", " DEFAULT", " AUTO_INCREMENT", " PRIMARY", " UNIQUE", " COLLATE", " CHARACTER", " CHARSET", " COMMENT", " GENERATED", " ON ", " REFERENCES", " CHECK", " AS "}
	best := len(s)
	for _, a := range attrs {
		if i := strings.Index(up, a); i >= 0 && i < best {
			best = i
		}
	}
	return best
}

func attrValue(s, key string) string {
	fs := strings.Fields(s)
	for i, f := range fs {
		if strings.EqualFold(f, key) && i+1 < len(fs) {
			return cleanIdent(fs[i+1])
		}
	}
	return ""
}

func defaultValue(s string) (string, bool) {
	up := strings.ToUpper(s)
	i := strings.Index(up, "DEFAULT")
	if i < 0 {
		return "", false
	}
	rest := strings.TrimSpace(s[i+len("DEFAULT"):])
	// 截到下一个属性
	for _, a := range []string{" NOT ", " NULL", " AUTO_INCREMENT", " PRIMARY", " UNIQUE", " COLLATE", " COMMENT", " ON ", " REFERENCES", " CHECK"} {
		if j := strings.Index(strings.ToUpper(rest), a); j >= 0 {
			rest = rest[:j]
		}
	}
	rest = strings.TrimSpace(rest)
	rest = strings.Trim(rest, "'\"")
	return rest, rest != ""
}

func namedKey(s string) (string, []string) {
	up := strings.ToUpper(s)
	name := "idx_" + strconvItoa(len(s))
	for _, p := range []string{"UNIQUE KEY ", "UNIQUE INDEX ", "UNIQUE ", "KEY ", "INDEX "} {
		if strings.HasPrefix(up, p) {
			rest := strings.TrimSpace(s[len(p):])
			if strings.HasPrefix(strings.ToLower(rest), "`") || !strings.HasPrefix(rest, "(") {
				n, _ := firstToken(rest)
				if n != "" && !strings.HasPrefix(n, "(") {
					name = cleanIdent(n)
				}
			}
			break
		}
	}
	return name, parenColumns(s)
}

func parenColumns(s string) []string {
	i := strings.IndexByte(s, '(')
	if i < 0 {
		return nil
	}
	j := matchingParen(s, i)
	if j < 0 {
		return nil
	}
	var out []string
	for _, p := range splitTopLevel(s[i+1 : j]) {
		p = strings.TrimSpace(p)
		if p == "" {
			continue
		}
		// 去掉前缀长度/排序方向
		name, _ := firstToken(p)
		out = append(out, cleanIdent(name))
	}
	return out
}

func indexCols(cols []string) []model.IndexColumn {
	out := make([]model.IndexColumn, 0, len(cols))
	for _, c := range cols {
		out = append(out, model.IndexColumn{Name: c})
	}
	return out
}

func firstWord(s string) string {
	w, _ := firstToken(strings.TrimSpace(s))
	return cleanIdent(w)
}

func strconvItoa(n int) string {
	if n == 0 {
		return "0"
	}
	var b [20]byte
	i := len(b)
	for n > 0 {
		i--
		b[i] = byte('0' + n%10)
		n /= 10
	}
	return string(b[i:])
}

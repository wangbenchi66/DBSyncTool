// Package report 把只读差异渲染为 Markdown / HTML / JSON。它不执行、不连接数据库。
package report

import (
	"encoding/json"
	"fmt"
	"html"
	"strings"
	"time"

	"gitee.com/wangbenchi66/dbsync/internal/diff"
	"gitee.com/wangbenchi66/dbsync/internal/plan"
)

type TableReport struct {
	Name        string `json:"name"`
	Status      string `json:"status"`
	Columns     int    `json:"columnsChanged"`
	Indexes     int    `json:"indexesChanged"`
	Constraints int    `json:"constraintsChanged"`
	Inserts     int    `json:"inserts"`
	Updates     int    `json:"updates"`
	Deletes     int    `json:"deletes"`
}

type ObjectReport struct {
	Kind   string `json:"kind"`
	Name   string `json:"name"`
	Status string `json:"status"`
}

type Diagnostic struct {
	Severity string `json:"severity"`
	Code     string `json:"code"`
	Object   string `json:"object"`
	Message  string `json:"message"`
}

type Document struct {
	GeneratedAt string         `json:"generatedAt"`
	Source      string         `json:"source"`
	Target      string         `json:"target"`
	Added       int            `json:"added"`
	Removed     int            `json:"removed"`
	Changed     int            `json:"changed"`
	Same        int            `json:"same"`
	HasData     bool           `json:"hasData"`
	Tables      []TableReport  `json:"tables"`
	Objects     []ObjectReport `json:"objects"`
	Diagnostics []Diagnostic   `json:"diagnostics"`
}

func Build(d *diff.SchemaDiff, p *plan.Plan) Document {
	doc := Document{
		GeneratedAt: time.Now().UTC().Format(time.RFC3339),
		Source:      d.SourceName, Target: d.TargetName, HasData: d.HasData,
		Tables: []TableReport{}, Objects: []ObjectReport{}, Diagnostics: []Diagnostic{},
	}
	doc.Added, doc.Removed, doc.Changed, doc.Same = d.Summary()
	for _, t := range d.Tables {
		tr := TableReport{Name: t.Name, Status: string(t.Status)}
		for _, c := range t.Columns {
			if c.Status != diff.Same {
				tr.Columns++
			}
		}
		for _, x := range t.Indexes {
			if x.Status != diff.Same {
				tr.Indexes++
			}
		}
		for _, x := range t.Constraints {
			if x.Status != diff.Same {
				tr.Constraints++
			}
		}
		if t.Data != nil {
			tr.Inserts, tr.Updates, tr.Deletes = len(t.Data.Inserts), len(t.Data.Updates), len(t.Data.Deletes)
		}
		doc.Tables = append(doc.Tables, tr)
	}
	for _, o := range d.Views {
		doc.Objects = append(doc.Objects, ObjectReport{Kind: "view", Name: o.Name, Status: string(o.Status)})
	}
	for _, o := range d.Routines {
		doc.Objects = append(doc.Objects, ObjectReport{Kind: "routine", Name: o.Name, Status: string(o.Status)})
	}
	for _, o := range d.Triggers {
		doc.Objects = append(doc.Objects, ObjectReport{Kind: "trigger", Name: o.Name, Status: string(o.Status)})
	}
	if p != nil {
		for _, x := range p.Diagnostics {
			doc.Diagnostics = append(doc.Diagnostics, Diagnostic{Severity: string(x.Severity), Code: x.Code, Object: x.Object, Message: x.Message})
		}
	}
	return doc
}

func Render(d *diff.SchemaDiff, p *plan.Plan, format string) (string, error) {
	doc := Build(d, p)
	switch strings.ToLower(format) {
	case "json":
		b, err := json.MarshalIndent(doc, "", "  ")
		return string(b) + "\n", err
	case "html":
		return renderHTML(doc), nil
	case "md", "markdown", "":
		return renderMarkdown(doc), nil
	default:
		return "", fmt.Errorf("不支持的报告格式 %q", format)
	}
}

func renderMarkdown(doc Document) string {
	var b strings.Builder
	fmt.Fprintf(&b, "# DBSync 差异报告\n\n")
	fmt.Fprintf(&b, "- 源：`%s`\n- 目标：`%s`\n- 生成时间：%s\n", doc.Source, doc.Target, doc.GeneratedAt)
	fmt.Fprintf(&b, "- 表：新增 %d / 删除 %d / 变更 %d / 相同 %d\n", doc.Added, doc.Removed, doc.Changed, doc.Same)
	if doc.HasData {
		fmt.Fprintf(&b, "- 数据比较：已启用\n")
	}
	b.WriteString("\n## 表差异\n\n| 表 | 状态 | 列 | 索引 | 约束 | INSERT | UPDATE | DELETE |\n| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: |\n")
	for _, t := range doc.Tables {
		fmt.Fprintf(&b, "| `%s` | %s | %d | %d | %d | %d | %d | %d |\n", t.Name, t.Status, t.Columns, t.Indexes, t.Constraints, t.Inserts, t.Updates, t.Deletes)
	}
	if len(doc.Objects) > 0 {
		b.WriteString("\n## 对象差异\n\n| 类型 | 名称 | 状态 |\n| :--- | :--- | :--- |\n")
		for _, o := range doc.Objects {
			fmt.Fprintf(&b, "| %s | `%s` | %s |\n", o.Kind, o.Name, o.Status)
		}
	}
	if len(doc.Diagnostics) > 0 {
		b.WriteString("\n## 风险\n\n")
		for _, d := range doc.Diagnostics {
			fmt.Fprintf(&b, "- **%s** `%s` %s\n", d.Severity, d.Code, d.Message)
		}
	}
	b.WriteString("\n> 本报告只读生成，工具未执行任何 DDL/DML。\n")
	return b.String()
}

func renderHTML(doc Document) string {
	var b strings.Builder
	b.WriteString("<!doctype html><html lang=\"zh-CN\"><meta charset=\"utf-8\"><title>DBSync 差异报告</title>")
	b.WriteString("<style>body{font:14px/1.5 system-ui;margin:32px;color:#1d2433}table{border-collapse:collapse;width:100%;margin:12px 0}th,td{border:1px solid #e3e7ee;padding:6px 8px;text-align:left}th{background:#f7f8fa}code{font-family:Consolas,monospace}</style>")
	fmt.Fprintf(&b, "<h1>DBSync 差异报告</h1><p>源：<code>%s</code><br>目标：<code>%s</code><br>生成：%s</p>", html.EscapeString(doc.Source), html.EscapeString(doc.Target), html.EscapeString(doc.GeneratedAt))
	fmt.Fprintf(&b, "<p>表：新增 %d / 删除 %d / 变更 %d / 相同 %d</p>", doc.Added, doc.Removed, doc.Changed, doc.Same)
	b.WriteString("<h2>表差异</h2><table><tr><th>表</th><th>状态</th><th>列</th><th>索引</th><th>约束</th><th>INSERT</th><th>UPDATE</th><th>DELETE</th></tr>")
	for _, t := range doc.Tables {
		fmt.Fprintf(&b, "<tr><td><code>%s</code></td><td>%s</td><td>%d</td><td>%d</td><td>%d</td><td>%d</td><td>%d</td><td>%d</td></tr>", html.EscapeString(t.Name), html.EscapeString(t.Status), t.Columns, t.Indexes, t.Constraints, t.Inserts, t.Updates, t.Deletes)
	}
	b.WriteString("</table>")
	if len(doc.Diagnostics) > 0 {
		b.WriteString("<h2>风险</h2><ul>")
		for _, d := range doc.Diagnostics {
			fmt.Fprintf(&b, "<li><b>%s</b> <code>%s</code> %s</li>", html.EscapeString(d.Severity), html.EscapeString(d.Code), html.EscapeString(d.Message))
		}
		b.WriteString("</ul>")
	}
	b.WriteString("<p>本报告只读生成，工具未执行任何 DDL/DML。</p></html>")
	return b.String()
}

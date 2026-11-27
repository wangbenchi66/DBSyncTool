// Package depgraph 构建表级依赖图（外键方向）。只读，不解析视图/过程 SQL。
package depgraph

import (
	"sort"

	"gitee.com/wangbenchi66/dbsync/internal/model"
)

type Node struct {
	Name   string `json:"name"`
	Schema string `json:"schema,omitempty"`
	Kind   string `json:"kind,omitempty"`
}

type Edge struct {
	From       string `json:"from"`
	To         string `json:"to"`
	Constraint string `json:"constraint"`
	Schema     string `json:"schema,omitempty"`
	Kind       string `json:"kind,omitempty"`
}

type Graph struct {
	Nodes  []Node     `json:"nodes"`
	Edges  []Edge     `json:"edges"`
	Cycles [][]string `json:"cycles"`
}

func Build(schemas ...*model.Schema) Graph {
	g := Graph{Nodes: []Node{}, Edges: []Edge{}, Cycles: [][]string{}}
	seenNode := map[string]bool{}
	seenEdge := map[string]bool{}
	for _, s := range schemas {
		if s == nil {
			continue
		}
		for _, t := range s.Tables {
			key := "table:" + t.Schema + "." + t.Name
			if !seenNode[key] {
				seenNode[key] = true
				g.Nodes = append(g.Nodes, Node{Name: t.Name, Schema: t.Schema, Kind: "table"})
			}
			for _, c := range t.Constraints {
				if c.Kind != model.ConstraintFK || c.RefTable == "" {
					continue
				}
				fk := c.Name
				ek := key + "->" + c.RefSchema + "." + c.RefTable + "#" + fk
				if seenEdge[ek] {
					continue
				}
				seenEdge[ek] = true
				g.Edges = append(g.Edges, Edge{From: t.Name, To: c.RefTable, Constraint: fk, Schema: t.Schema, Kind: "table"})
			}
		}
		for _, v := range s.Views {
			key := "view:" + v.Schema + "." + v.Name
			if !seenNode[key] {
				seenNode[key] = true
				g.Nodes = append(g.Nodes, Node{Name: v.Name, Schema: v.Schema, Kind: "view"})
			}
			for _, dep := range v.Dependencies {
				ek := key + "->table:" + dep + "#view"
				if seenEdge[ek] {
					continue
				}
				seenEdge[ek] = true
				g.Edges = append(g.Edges, Edge{From: v.Name, To: dep, Constraint: "view", Schema: v.Schema, Kind: "view"})
			}
		}
	}
	sort.Slice(g.Nodes, func(i, j int) bool {
		if g.Nodes[i].Schema != g.Nodes[j].Schema {
			return g.Nodes[i].Schema < g.Nodes[j].Schema
		}
		return g.Nodes[i].Name < g.Nodes[j].Name
	})
	sort.Slice(g.Edges, func(i, j int) bool {
		if g.Edges[i].From != g.Edges[j].From {
			return g.Edges[i].From < g.Edges[j].From
		}
		return g.Edges[i].To < g.Edges[j].To
	})
	g.Cycles = cycles(g)
	return g
}

func cycles(g Graph) [][]string {
	adj := map[string][]string{}
	for _, e := range g.Edges {
		adj[e.From] = append(adj[e.From], e.To)
	}
	for k := range adj {
		sort.Strings(adj[k])
	}
	var out [][]string
	state := map[string]int{}
	var stack []string
	var dfs func(string)
	dfs = func(n string) {
		state[n] = 1
		stack = append(stack, n)
		for _, m := range adj[n] {
			switch state[m] {
			case 0:
				dfs(m)
			case 1:
				i := 0
				for i < len(stack) && stack[i] != m {
					i++
				}
				c := append([]string(nil), stack[i:]...)
				c = append(c, m)
				out = append(out, c)
			}
		}
		stack = stack[:len(stack)-1]
		state[n] = 2
	}
	for _, n := range g.Nodes {
		if state[n.Name] == 0 {
			dfs(n.Name)
		}
	}
	return out
}

// Package snapshot 实现 .dbsync v2 容器：ZIP + manifest + schema.ndjson + data/*.ndjson + CHECKSUMS。
// 只读：快照只被读取/比较，不会被工具执行。
package snapshot

import (
	"archive/zip"
	"bytes"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"io"
	"net/url"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"time"

	"gitee.com/wangbenchi66/dbsync/internal/diff"
	"gitee.com/wangbenchi66/dbsync/internal/model"
)

const FormatVersion = 2

type Row = diff.Row

type RowSet struct {
	Columns []string
	Rows    []Row
}

type TableData struct {
	Name     string   `json:"name"`
	File     string   `json:"file"`
	Columns  []string `json:"columns"`
	RowCount int      `json:"rowCount"`
	Hash     string   `json:"hash"`
}

type Manifest struct {
	FormatVersion int         `json:"formatVersion"`
	CreatedAt     string      `json:"createdAt"`
	Source        string      `json:"source"`
	SchemaName    string      `json:"schemaName"`
	SchemaHash    string      `json:"schemaHash"`
	TableCount    int         `json:"tableCount"`
	ViewCount     int         `json:"viewCount"`
	RoutineCount  int         `json:"routineCount"`
	TriggerCount  int         `json:"triggerCount"`
	Tables        []TableData `json:"tables,omitempty"`
	HasData       bool        `json:"hasData"`
	Encrypted     bool        `json:"encrypted"`
}

type Snapshot struct {
	Manifest Manifest
	Schema   *model.Schema
	Data     map[string]RowSet
}

type WriteOptions struct {
	Source    string
	CreatedAt time.Time
	Data      map[string]RowSet
}

func WriteSchema(path string, schema *model.Schema, opts WriteOptions) error {
	opts.Data = nil
	return Write(path, schema, opts)
}

func Write(path string, schema *model.Schema, opts WriteOptions) error {
	if schema == nil {
		return fmt.Errorf("schema 为空")
	}
	schema.Sort()
	schemaBytes, err := json.Marshal(schema)
	if err != nil {
		return fmt.Errorf("编码 schema: %w", err)
	}
	sum := sha256.Sum256(schemaBytes)
	m := Manifest{
		FormatVersion: FormatVersion,
		CreatedAt:     opts.CreatedAt.UTC().Format(time.RFC3339Nano),
		Source:        opts.Source,
		SchemaName:    schema.Name,
		SchemaHash:    hex.EncodeToString(sum[:]),
		TableCount:    len(schema.Tables),
		ViewCount:     len(schema.Views),
		RoutineCount:  len(schema.Routines),
		TriggerCount:  len(schema.Triggers),
		HasData:       len(opts.Data) > 0,
		Encrypted:     false,
	}
	if opts.CreatedAt.IsZero() {
		m.CreatedAt = time.Unix(0, 0).UTC().Format(time.RFC3339Nano)
	}
	files := make([]zipEntry, 0, 3+len(opts.Data))
	var checksums strings.Builder
	schemaFileBytes := append(append([]byte(nil), schemaBytes...), '\n')
	schemaFileSum := sha256.Sum256(schemaFileBytes)
	fmt.Fprintf(&checksums, "%s  schema.ndjson\n", hex.EncodeToString(schemaFileSum[:]))

	names := make([]string, 0, len(opts.Data))
	for name := range opts.Data {
		names = append(names, name)
	}
	sort.Strings(names)
	for _, name := range names {
		rs := opts.Data[name]
		var buf bytes.Buffer
		for _, row := range rs.Rows {
			b, err := json.Marshal(row)
			if err != nil {
				return fmt.Errorf("编码数据 %s: %w", name, err)
			}
			buf.Write(b)
			buf.WriteByte('\n')
		}
		h := sha256.Sum256(buf.Bytes())
		file := "data/" + url.PathEscape(name) + ".ndjson"
		m.Tables = append(m.Tables, TableData{
			Name: name, File: file, Columns: rs.Columns,
			RowCount: len(rs.Rows), Hash: hex.EncodeToString(h[:]),
		})
		fmt.Fprintf(&checksums, "%s  %s\n", hex.EncodeToString(h[:]), file)
		files = append(files, zipEntry{name: file, data: buf.Bytes()})
	}

	manifestBytes, err := json.MarshalIndent(m, "", "  ")
	if err != nil {
		return fmt.Errorf("编码 manifest: %w", err)
	}
	manifestBytes = append(manifestBytes, '\n')
	files = append([]zipEntry{
		{name: "manifest.json", data: manifestBytes},
		{name: "schema.ndjson", data: schemaFileBytes},
		{name: "CHECKSUMS.txt", data: []byte(checksums.String())},
	}, files...)

	var buf bytes.Buffer
	zw := zip.NewWriter(&buf)
	for _, f := range files {
		h := &zip.FileHeader{Name: f.name, Method: zip.Deflate}
		h.Modified = time.Unix(0, 0).UTC()
		h.SetMode(0o644)
		w, err := zw.CreateHeader(h)
		if err != nil {
			return fmt.Errorf("写 %s: %w", f.name, err)
		}
		if _, err := w.Write(f.data); err != nil {
			return fmt.Errorf("写 %s: %w", f.name, err)
		}
	}
	if err := zw.Close(); err != nil {
		return fmt.Errorf("关闭 zip: %w", err)
	}

	dir := filepath.Dir(path)
	if err := os.MkdirAll(dir, 0o755); err != nil {
		return err
	}
	tmp, err := os.CreateTemp(dir, ".dbsync-*.tmp")
	if err != nil {
		return err
	}
	tmpName := tmp.Name()
	defer os.Remove(tmpName)
	if _, err := tmp.Write(buf.Bytes()); err != nil {
		tmp.Close()
		return err
	}
	if err := tmp.Close(); err != nil {
		return err
	}
	return os.Rename(tmpName, path)
}

type zipEntry struct {
	name string
	data []byte
}

func Read(path string) (*Snapshot, error) {
	zr, err := zip.OpenReader(path)
	if err != nil {
		return nil, fmt.Errorf("打开快照: %w", err)
	}
	defer zr.Close()
	files := map[string]*zip.File{}
	for _, f := range zr.File {
		files[f.Name] = f
	}
	manifestFile := files["manifest.json"]
	schemaFile := files["schema.ndjson"]
	if manifestFile == nil || schemaFile == nil {
		return nil, fmt.Errorf("快照缺少 manifest.json 或 schema.ndjson")
	}
	manifestBytes, err := readZipFile(manifestFile)
	if err != nil {
		return nil, err
	}
	var m Manifest
	if err := json.Unmarshal(manifestBytes, &m); err != nil {
		return nil, fmt.Errorf("解析 manifest: %w", err)
	}
	if m.FormatVersion != FormatVersion {
		return nil, fmt.Errorf("不支持的快照版本 %d", m.FormatVersion)
	}
	schemaBytes, err := readZipFile(schemaFile)
	if err != nil {
		return nil, err
	}
	sum := sha256.Sum256(bytes.TrimSpace(schemaBytes))
	if hex.EncodeToString(sum[:]) != m.SchemaHash {
		return nil, fmt.Errorf("schema 摘要不匹配")
	}
	if err := verifyChecksums(files); err != nil {
		return nil, err
	}
	var schema model.Schema
	if err := json.Unmarshal(bytes.TrimSpace(schemaBytes), &schema); err != nil {
		return nil, fmt.Errorf("解析 schema: %w", err)
	}
	schema.Sort()
	s := &Snapshot{Manifest: m, Schema: &schema, Data: map[string]RowSet{}}
	for _, td := range m.Tables {
		f := files[td.File]
		if f == nil {
			return nil, fmt.Errorf("快照缺少数据文件 %s", td.File)
		}
		b, err := readZipFile(f)
		if err != nil {
			return nil, err
		}
		rs := RowSet{Columns: td.Columns}
		for _, line := range bytes.Split(bytes.TrimSpace(b), []byte{'\n'}) {
			if len(bytes.TrimSpace(line)) == 0 {
				continue
			}
			var row Row
			if err := json.Unmarshal(line, &row); err != nil {
				return nil, fmt.Errorf("解析 %s: %w", td.File, err)
			}
			rs.Rows = append(rs.Rows, row)
		}
		s.Data[td.Name] = rs
	}
	return s, nil
}

func Inspect(path string) (Manifest, error) {
	zr, err := zip.OpenReader(path)
	if err != nil {
		return Manifest{}, err
	}
	defer zr.Close()
	for _, f := range zr.File {
		if f.Name != "manifest.json" {
			continue
		}
		b, err := readZipFile(f)
		if err != nil {
			return Manifest{}, err
		}
		var m Manifest
		if err := json.Unmarshal(b, &m); err != nil {
			return Manifest{}, err
		}
		return m, nil
	}
	return Manifest{}, fmt.Errorf("快照缺少 manifest.json")
}

func Verify(path string) error {
	_, err := Read(path)
	return err
}

func readZipFile(f *zip.File) ([]byte, error) {
	rc, err := f.Open()
	if err != nil {
		return nil, err
	}
	defer rc.Close()
	return io.ReadAll(rc)
}

func verifyChecksums(files map[string]*zip.File) error {
	f := files["CHECKSUMS.txt"]
	if f == nil {
		return fmt.Errorf("快照缺少 CHECKSUMS.txt")
	}
	b, err := readZipFile(f)
	if err != nil {
		return err
	}
	lines := strings.Split(strings.TrimSpace(string(b)), "\n")
	if len(lines) == 0 {
		return fmt.Errorf("CHECKSUMS.txt 为空")
	}
	for _, line := range lines {
		fields := strings.Fields(line)
		if len(fields) != 2 {
			return fmt.Errorf("CHECKSUMS.txt 行非法: %q", line)
		}
		name := fields[1]
		zf := files[name]
		if zf == nil {
			return fmt.Errorf("CHECKSUMS.txt 引用了不存在的文件 %s", name)
		}
		data, err := readZipFile(zf)
		if err != nil {
			return err
		}
		sum := sha256.Sum256(data)
		if hex.EncodeToString(sum[:]) != fields[0] {
			return fmt.Errorf("%s 校验失败", name)
		}
	}
	return nil
}

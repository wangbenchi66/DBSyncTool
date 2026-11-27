// Package snapshot 实现 .dbsync v2 容器：ZIP + manifest + schema.ndjson + CHECKSUMS。
// M1 只写结构；数据行在后续里程碑追加 data/<table>.ndjson。
package snapshot

import (
	"archive/zip"
	"bytes"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"time"

	"gitee.com/wangbenchi66/dbsync/internal/model"
)

const FormatVersion = 2

type Manifest struct {
	FormatVersion int    `json:"formatVersion"`
	CreatedAt     string `json:"createdAt"`
	Source        string `json:"source"`
	SchemaName    string `json:"schemaName"`
	SchemaHash    string `json:"schemaHash"`
	TableCount    int    `json:"tableCount"`
	ViewCount     int    `json:"viewCount"`
	RoutineCount  int    `json:"routineCount"`
	TriggerCount  int    `json:"triggerCount"`
	HasData       bool   `json:"hasData"`
	Encrypted     bool   `json:"encrypted"`
}

type Snapshot struct {
	Manifest Manifest
	Schema   *model.Schema
}

type WriteOptions struct {
	Source    string
	CreatedAt time.Time
}

func WriteSchema(path string, schema *model.Schema, opts WriteOptions) error {
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
		HasData:       false,
		Encrypted:     false,
	}
	if opts.CreatedAt.IsZero() {
		m.CreatedAt = time.Unix(0, 0).UTC().Format(time.RFC3339Nano)
	}
	manifestBytes, err := json.MarshalIndent(m, "", "  ")
	if err != nil {
		return fmt.Errorf("编码 manifest: %w", err)
	}
	manifestBytes = append(manifestBytes, '\n')
	checksums := []byte(hex.EncodeToString(sum[:]) + "  schema.ndjson\n")

	var buf bytes.Buffer
	zw := zip.NewWriter(&buf)
	files := []struct {
		name string
		data []byte
	}{
		{"manifest.json", manifestBytes},
		{"schema.ndjson", append(schemaBytes, '\n')},
		{"CHECKSUMS.txt", checksums},
	}
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
	return &Snapshot{Manifest: m, Schema: &schema}, nil
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
	names := make([]string, 0, len(lines))
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
		sum := sha256.Sum256(bytes.TrimSpace(data))
		if hex.EncodeToString(sum[:]) != fields[0] {
			return fmt.Errorf("%s 校验失败", name)
		}
		names = append(names, name)
	}
	sort.Strings(names)
	return nil
}

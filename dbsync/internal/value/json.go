package value

import (
	"encoding/base64"
	"encoding/json"
	"fmt"
	"strconv"
	"time"
)

type jsonValue struct {
	Kind  string          `json:"k"`
	Value json.RawMessage `json:"v,omitempty"`
}

// MarshalJSON 把 Value 编码为带类型标签的对象，避免 JSON number 精度丢失。
func (v Value) MarshalJSON() ([]byte, error) {
	j := jsonValue{Kind: v.kind.String()}
	var raw any
	switch v.kind {
	case KindNull:
	case KindBool:
		raw = v.b
	case KindInt:
		raw = strconv.FormatInt(v.i, 10)
	case KindUint:
		raw = strconv.FormatUint(v.u, 10)
	case KindFloat:
		raw = strconv.FormatFloat(v.f, 'g', -1, 64)
	case KindDecimal:
		raw = v.s
	case KindString:
		raw = v.s
	case KindBytes:
		raw = base64.StdEncoding.EncodeToString(v.bs)
	case KindTime:
		raw = v.t.UTC().Format(time.RFC3339Nano)
	default:
		return nil, fmt.Errorf("未知 Kind %d", v.kind)
	}
	if raw != nil {
		b, err := json.Marshal(raw)
		if err != nil {
			return nil, err
		}
		j.Value = b
	}
	return json.Marshal(j)
}

func (v *Value) UnmarshalJSON(b []byte) error {
	var j jsonValue
	if err := json.Unmarshal(b, &j); err != nil {
		return err
	}
	switch j.Kind {
	case "null":
		*v = Null()
	case "bool":
		var x bool
		if err := json.Unmarshal(j.Value, &x); err != nil {
			return err
		}
		*v = Bool(x)
	case "int":
		s, err := rawString(j.Value)
		if err != nil {
			return err
		}
		n, err := strconv.ParseInt(s, 10, 64)
		if err != nil {
			return err
		}
		*v = Int(n)
	case "uint":
		s, err := rawString(j.Value)
		if err != nil {
			return err
		}
		n, err := strconv.ParseUint(s, 10, 64)
		if err != nil {
			return err
		}
		*v = Uint(n)
	case "float":
		s, err := rawString(j.Value)
		if err != nil {
			return err
		}
		n, err := strconv.ParseFloat(s, 64)
		if err != nil {
			return err
		}
		*v = Float(n)
	case "decimal":
		s, err := rawString(j.Value)
		if err != nil {
			return err
		}
		d, err := Decimal(s)
		if err != nil {
			return err
		}
		*v = d
	case "string":
		s, err := rawString(j.Value)
		if err != nil {
			return err
		}
		*v = String(s)
	case "bytes":
		s, err := rawString(j.Value)
		if err != nil {
			return err
		}
		bs, err := base64.StdEncoding.DecodeString(s)
		if err != nil {
			return err
		}
		*v = Bytes(bs)
	case "time":
		s, err := rawString(j.Value)
		if err != nil {
			return err
		}
		t, err := time.Parse(time.RFC3339Nano, s)
		if err != nil {
			return err
		}
		*v = Time(t)
	default:
		return fmt.Errorf("未知 Kind %q", j.Kind)
	}
	return nil
}

func rawString(raw json.RawMessage) (string, error) {
	if len(raw) == 0 {
		return "", fmt.Errorf("缺少值")
	}
	var s string
	if err := json.Unmarshal(raw, &s); err == nil {
		return s, nil
	}
	// 兼容旧数据：数字直接写在 v 里。
	return string(raw), nil
}

package value

import (
	"fmt"
	"math"
	"math/big"
	"strconv"
	"strings"
	"time"

	"github.com/zeebo/xxh3"
)

// Kind 是 Value 的类型标签。不同 Kind 之间不隐式转换。
type Kind uint8

const (
	KindNull Kind = iota
	KindBool
	KindInt
	KindUint
	KindFloat
	KindDecimal
	KindString
	KindBytes
	KindTime
)

func (k Kind) String() string {
	switch k {
	case KindNull:
		return "null"
	case KindBool:
		return "bool"
	case KindInt:
		return "int"
	case KindUint:
		return "uint"
	case KindFloat:
		return "float"
	case KindDecimal:
		return "decimal"
	case KindString:
		return "string"
	case KindBytes:
		return "bytes"
	case KindTime:
		return "time"
	default:
		return "kind(" + strconv.Itoa(int(k)) + ")"
	}
}

// Value 是同步工具中唯一的数据值表示。禁止用 string 承载所有值。
type Value struct {
	kind Kind
	b    bool
	i    int64
	u    uint64
	f    float64
	s    string
	bs   []byte
	t    time.Time
}

func Null() Value                    { return Value{kind: KindNull} }
func Bool(v bool) Value              { return Value{kind: KindBool, b: v} }
func Int(v int64) Value              { return Value{kind: KindInt, i: v} }
func Uint(v uint64) Value            { return Value{kind: KindUint, u: v} }
func Float(v float64) Value          { return Value{kind: KindFloat, f: v} }
func String(v string) Value          { return Value{kind: KindString, s: v} }
func Bytes(v []byte) Value           { return Value{kind: KindBytes, bs: append([]byte(nil), v...)} }
func Time(v time.Time) Value         { return Value{kind: KindTime, t: v.UTC()} }
func (v Value) Kind() Kind           { return v.kind }
func (v Value) IsNull() bool         { return v.kind == KindNull }
func (v Value) Bool() bool           { return v.b }
func (v Value) Int() int64           { return v.i }
func (v Value) Uint() uint64         { return v.u }
func (v Value) Float() float64       { return v.f }
func (v Value) StringValue() string  { return v.s }
func (v Value) BytesValue() []byte   { return append([]byte(nil), v.bs...) }
func (v Value) TimeValue() time.Time { return v.t }

// Decimal 构造一个规范化十进制。输入必须是十进制字面量，不接受 NaN/Inf。
func Decimal(s string) (Value, error) {
	c, err := CanonicalDecimal(s)
	if err != nil {
		return Value{}, err
	}
	return Value{kind: KindDecimal, s: c}, nil
}

func MustDecimal(s string) Value {
	v, err := Decimal(s)
	if err != nil {
		panic(err)
	}
	return v
}

// CanonicalDecimal 去除正负零、前导零、尾部零，并保留符号。
// 例：+001.2300 -> 1.23；-0.00 -> 0。
func CanonicalDecimal(s string) (string, error) {
	s = strings.TrimSpace(s)
	if s == "" {
		return "", fmt.Errorf("空十进制")
	}
	neg := false
	switch s[0] {
	case '+':
		s = s[1:]
	case '-':
		neg = true
		s = s[1:]
	}
	if s == "" {
		return "", fmt.Errorf("非法十进制 %q", s)
	}
	intPart, fracPart, hasDot := strings.Cut(s, ".")
	if hasDot && strings.Contains(fracPart, ".") {
		return "", fmt.Errorf("非法十进制 %q", s)
	}
	if intPart == "" && fracPart == "" {
		return "", fmt.Errorf("非法十进制 %q", s)
	}
	for _, r := range intPart + fracPart {
		if r < '0' || r > '9' {
			return "", fmt.Errorf("非法十进制 %q", s)
		}
	}
	if intPart == "" {
		intPart = "0"
	}
	intPart = strings.TrimLeft(intPart, "0")
	if intPart == "" {
		intPart = "0"
	}
	fracPart = strings.TrimRight(fracPart, "0")
	if intPart == "0" && fracPart == "" {
		return "0", nil
	}
	out := intPart
	if fracPart != "" {
		out += "." + fracPart
	}
	if neg {
		out = "-" + out
	}
	return out, nil
}

func (v Value) decimalRat() (*big.Rat, bool) {
	switch v.kind {
	case KindInt:
		return new(big.Rat).SetInt64(v.i), true
	case KindUint:
		i := new(big.Int).SetUint64(v.u)
		return new(big.Rat).SetInt(i), true
	case KindFloat:
		if math.IsNaN(v.f) || math.IsInf(v.f, 0) {
			return nil, false
		}
		r := new(big.Rat)
		if r.SetFloat64(v.f) == nil {
			return nil, false
		}
		return r, true
	case KindDecimal:
		r, ok := new(big.Rat).SetString(v.s)
		return r, ok
	default:
		return nil, false
	}
}

// Compare 给出稳定全序；Null 最小。
func Compare(a, b Value) int {
	if a.kind == KindNull || b.kind == KindNull {
		switch {
		case a.kind == KindNull && b.kind == KindNull:
			return 0
		case a.kind == KindNull:
			return -1
		default:
			return 1
		}
	}
	if ar, ok := a.decimalRat(); ok {
		if br, ok := b.decimalRat(); ok {
			return ar.Cmp(br)
		}
	}
	if a.kind != b.kind {
		if a.kind < b.kind {
			return -1
		}
		return 1
	}
	switch a.kind {
	case KindBool:
		if a.b == b.b {
			return 0
		}
		if !a.b {
			return -1
		}
		return 1
	case KindString:
		return strings.Compare(a.s, b.s)
	case KindBytes:
		return bytesCompare(a.bs, b.bs)
	case KindTime:
		switch {
		case a.t.Before(b.t):
			return -1
		case a.t.After(b.t):
			return 1
		default:
			return 0
		}
	}
	return 0
}

func bytesCompare(a, b []byte) int {
	n := len(a)
	if len(b) < n {
		n = len(b)
	}
	for i := 0; i < n; i++ {
		if a[i] < b[i] {
			return -1
		}
		if a[i] > b[i] {
			return 1
		}
	}
	switch {
	case len(a) < len(b):
		return -1
	case len(a) > len(b):
		return 1
	default:
		return 0
	}
}

func Equal(a, b Value) bool { return Compare(a, b) == 0 }

// Canonical 返回哈希与快照共用的编码。禁止用 fmt.Sprintf("%v") 代替。
func (v Value) Canonical() []byte {
	buf := make([]byte, 0, 32)
	buf = append(buf, byte(v.kind))
	switch v.kind {
	case KindNull:
	case KindBool:
		if v.b {
			buf = append(buf, 1)
		} else {
			buf = append(buf, 0)
		}
	case KindInt:
		buf = appendString(buf, strconv.FormatInt(v.i, 10))
	case KindUint:
		buf = appendString(buf, strconv.FormatUint(v.u, 10))
	case KindFloat:
		buf = appendString(buf, strconv.FormatFloat(v.f, 'g', -1, 64))
	case KindDecimal:
		buf = appendString(buf, v.s)
	case KindString:
		buf = appendString(buf, v.s)
	case KindBytes:
		buf = appendUvarint(buf, uint64(len(v.bs)))
		buf = append(buf, v.bs...)
	case KindTime:
		buf = appendString(buf, v.t.UTC().Format(time.RFC3339Nano))
	}
	return buf
}

func (v Value) Hash128() [16]byte {
	return xxh3.Hash128(v.Canonical()).Bytes()
}

func appendString(buf []byte, s string) []byte {
	buf = appendUvarint(buf, uint64(len(s)))
	return append(buf, s...)
}

func appendUvarint(buf []byte, n uint64) []byte {
	for n >= 0x80 {
		buf = append(buf, byte(n)|0x80)
		n >>= 7
	}
	return append(buf, byte(n))
}

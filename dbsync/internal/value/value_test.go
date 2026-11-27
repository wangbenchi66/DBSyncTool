package value

import (
	"bytes"
	"testing"
	"time"
)

func TestCanonicalDecimal(t *testing.T) {
	cases := map[string]string{
		"0":         "0",
		"-0":        "0",
		"+000.00":   "0",
		"001.2300":  "1.23",
		"-001.2300": "-1.23",
		"100":       "100",
		".5":        "0.5",
		"5.":        "5",
	}
	for in, want := range cases {
		got, err := CanonicalDecimal(in)
		if err != nil {
			t.Fatalf("CanonicalDecimal(%q): %v", in, err)
		}
		if got != want {
			t.Fatalf("CanonicalDecimal(%q)=%q, want %q", in, got, want)
		}
	}
	for _, bad := range []string{"", ".", "1.2.3", "abc", "1e3", "--1"} {
		if _, err := CanonicalDecimal(bad); err == nil {
			t.Fatalf("CanonicalDecimal(%q) 期望失败", bad)
		}
	}
}

func TestCompareNumericAcrossKinds(t *testing.T) {
	a := Int(2)
	b := MustDecimal("2.00")
	if Compare(a, b) != 0 {
		t.Fatalf("2 与 2.00 应相等")
	}
	if Compare(MustDecimal("1.5"), Float(1.5)) != 0 {
		t.Fatalf("1.5 与 1.5 应相等")
	}
	if Compare(Int(-1), Uint(1)) >= 0 {
		t.Fatalf("-1 应小于 1")
	}
	if Compare(Null(), Int(0)) >= 0 {
		t.Fatalf("Null 应最小")
	}
}

func TestHashDeterministic(t *testing.T) {
	ts := time.Date(2026, 11, 27, 10, 0, 0, 123, time.FixedZone("CST", 8*3600))
	a := Time(ts)
	b := Time(ts.UTC())
	ha, hb := a.Hash128(), b.Hash128()
	if !bytes.Equal(ha[:], hb[:]) {
		t.Fatalf("同一时刻不同时区应同哈希")
	}
	if Equal(String("a"), String("b")) {
		t.Fatalf("不同值不应相等")
	}
	if String("a").Hash128() == String("b").Hash128() {
		t.Fatalf("不同字符串不应同哈希")
	}
}

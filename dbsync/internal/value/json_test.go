package value

import (
	"encoding/json"
	"testing"
	"time"
)

func TestJSONRoundTrip(t *testing.T) {
	vals := []Value{
		Null(), Bool(true), Int(-42), Uint(42), Float(1.25),
		MustDecimal("-123.4500"), String("中文\n'"), Bytes([]byte{0, 1, 2, 255}),
		Time(time.Date(2026, 11, 27, 10, 0, 0, 123456789, time.UTC)),
	}
	for _, v := range vals {
		b, err := json.Marshal(v)
		if err != nil {
			t.Fatal(err)
		}
		var got Value
		if err := json.Unmarshal(b, &got); err != nil {
			t.Fatalf("%s: %v (%s)", v.Kind(), err, b)
		}
		if Compare(v, got) != 0 {
			t.Fatalf("round-trip 不等: %s -> %s", v.Kind(), got.Kind())
		}
	}
}

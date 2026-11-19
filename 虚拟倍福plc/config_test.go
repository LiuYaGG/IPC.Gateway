package main

import (
	"encoding/binary"
	"encoding/json"
	"testing"
)

func TestConfiguredPointTypes(t *testing.T) {
	cfg := defaultConfig()
	cfg.Points = []PointConfig{
		{Name: "GVL.bool", Type: "Bool", Value: json.RawMessage(`true`)},
		{Name: "GVL.int64", Type: "Int64", Value: json.RawMessage(`-123456789`)},
		{Name: "GVL.uint16", Type: "UInt16", Value: json.RawMessage(`65535`)},
		{Name: "GVL.double", Type: "Double", Value: json.RawMessage(`12.25`)},
		{Name: "GVL.string", Type: "String", Length: 8, Value: json.RawMessage(`"qwer"`)},
		{Name: "GVL.bytes", Type: "ByteArray", Value: json.RawMessage(`[1,2,255]`)},
	}
	store, err := newStore(cfg)
	if err != nil {
		t.Fatal(err)
	}
	if len(store.points) != 6 {
		t.Fatalf("点位数量错误: %d", len(store.points))
	}
	if got := store.points[normalizeName("GVL.bool")].Data[0]; got != 1 {
		t.Fatalf("Bool 编码错误: %d", got)
	}
	if got := binary.LittleEndian.Uint16(store.points[normalizeName("GVL.uint16")].Data); got != 65535 {
		t.Fatalf("UInt16 编码错误: %d", got)
	}
	if got := len(store.points[normalizeName("GVL.string")].Data); got != 9 {
		t.Fatalf("String 应包含结尾零，长度=%d", got)
	}
	if got := len(store.points[normalizeName("GVL.bytes")].Data); got != 3 {
		t.Fatalf("ByteArray 长度错误: %d", got)
	}
}

func TestRejectDuplicateAndInvalidPoint(t *testing.T) {
	cfg := defaultConfig()
	cfg.Points = []PointConfig{
		{Name: "GVL.value", Type: "Int16", Value: json.RawMessage(`1`)},
		{Name: "gvl.VALUE", Type: "Int16", Value: json.RawMessage(`2`)},
	}
	if _, err := newStore(cfg); err == nil {
		t.Fatal("应拒绝不区分大小写的重复点名")
	}
	cfg.Points = []PointConfig{{Name: "GVL.bad", Type: "Int16", Value: json.RawMessage(`99999`)}}
	if _, err := newStore(cfg); err == nil {
		t.Fatal("应拒绝超出 Int16 范围的值")
	}
}

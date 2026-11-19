package main

import (
	"bytes"
	"encoding/binary"
	"encoding/json"
	"fmt"
	"math"
	"os"
	"strconv"
	"strings"
	"sync"
	"unicode"
)

type Config struct {
	Host        string        `json:"host"`
	Port        int           `json:"port"`
	AmsNetID    string        `json:"amsNetId"`
	AdsPort     uint16        `json:"adsPort"`
	AdsState    uint16        `json:"adsState"`
	DeviceState uint16        `json:"deviceState"`
	Verbose     bool          `json:"verbose"`
	Points      []PointConfig `json:"points"`
}

type PointConfig struct {
	Name   string          `json:"name"`
	Type   string          `json:"type"`
	Length int             `json:"length,omitempty"`
	Value  json.RawMessage `json:"value"`
}

type Point struct {
	Name string
	Type string
	Data []byte
}

func (p *Point) cloneData() []byte {
	return append([]byte(nil), p.Data...)
}

type Store struct {
	mu         sync.RWMutex
	points     map[string]*Point
	handles    map[uint32]*Point
	nextHandle uint32
	verbose    bool
}

func defaultConfig() Config {
	return Config{
		Host: "0.0.0.0", Port: 48898, AmsNetID: "127.0.0.1.1.1", AdsPort: 851,
		AdsState: 5, DeviceState: 0, Verbose: false,
		Points: []PointConfig{
			{Name: "GVL.bStart", Type: "Bool", Value: json.RawMessage(`false`)},
			{Name: "GVL.iCounter", Type: "Int16", Value: json.RawMessage(`123`)},
			{Name: "GVL.nSpeed", Type: "Float", Value: json.RawMessage(`25.5`)},
			{Name: "GVL.sText", Type: "String", Length: 80, Value: json.RawMessage(`"hello"`)},
			{Name: "GVL.aValues", Type: "Int32Array", Value: json.RawMessage(`[1,2,3,4]`)},
		},
	}
}

func loadConfig(path string) (Config, error) {
	data, err := os.ReadFile(path)
	if err != nil {
		return Config{}, err
	}
	cfg := defaultConfig()
	cfg.Points = nil
	decoder := json.NewDecoder(bytes.NewReader(data))
	decoder.DisallowUnknownFields()
	if err := decoder.Decode(&cfg); err != nil {
		return Config{}, fmt.Errorf("JSON 格式错误: %w", err)
	}
	if cfg.Host == "" {
		cfg.Host = "0.0.0.0"
	}
	if cfg.Port <= 0 || cfg.Port > 65535 {
		return Config{}, fmt.Errorf("port 必须在 1～65535 之间")
	}
	if cfg.AdsPort == 0 {
		cfg.AdsPort = 851
	}
	if _, err := parseAmsNetID(cfg.AmsNetID); err != nil {
		return Config{}, fmt.Errorf("amsNetId 无效: %w", err)
	}
	if cfg.AdsState == 0 {
		cfg.AdsState = 5
	}
	if len(cfg.Points) == 0 {
		return Config{}, fmt.Errorf("points 至少需要一个点位")
	}
	return cfg, nil
}

func newStore(cfg Config) (*Store, error) {
	s := &Store{
		points: make(map[string]*Point), handles: make(map[uint32]*Point),
		nextHandle: 1, verbose: cfg.Verbose,
	}
	for _, item := range cfg.Points {
		name := strings.TrimSpace(item.Name)
		key := normalizeName(name)
		if key == "" {
			return nil, fmt.Errorf("存在空点名")
		}
		if _, exists := s.points[key]; exists {
			return nil, fmt.Errorf("点名重复: %s", name)
		}
		data, err := encodeInitialValue(item)
		if err != nil {
			return nil, fmt.Errorf("点位 %s: %w", name, err)
		}
		s.points[key] = &Point{Name: name, Type: normalizeType(item.Type), Data: data}
	}
	return s, nil
}

func normalizeName(name string) string {
	return strings.ToLower(strings.TrimSpace(strings.TrimRight(name, "\x00")))
}

func normalizeType(value string) string {
	value = strings.ToLower(strings.TrimSpace(value))
	switch value {
	case "byte":
		return "uint8"
	case "bytearray":
		return "uint8array"
	default:
		return value
	}
}

var scalarSizes = map[string]int{
	"bool": 1, "int8": 1, "uint8": 1, "int16": 2, "uint16": 2,
	"int32": 4, "uint32": 4, "int64": 8, "uint64": 8, "float": 4, "double": 8,
}

func encodeInitialValue(item PointConfig) ([]byte, error) {
	typeName := normalizeType(item.Type)
	if typeName == "string" {
		var value string
		if len(item.Value) == 0 {
			value = ""
		} else if err := json.Unmarshal(item.Value, &value); err != nil {
			return nil, fmt.Errorf("String 的 value 必须是字符串")
		}
		for _, r := range value {
			if r > unicode.MaxASCII {
				return nil, fmt.Errorf("String 仅支持 ASCII 字符")
			}
		}
		capacity := item.Length
		if capacity == 0 {
			capacity = 80
		}
		if capacity < 1 || capacity > 4096 {
			return nil, fmt.Errorf("String 的 length 必须在 1～4096 之间")
		}
		if len(value) > capacity {
			return nil, fmt.Errorf("字符串长度 %d 超过 length=%d", len(value), capacity)
		}
		data := make([]byte, capacity+1)
		copy(data, value)
		return data, nil
	}

	if strings.HasSuffix(typeName, "array") {
		elementType := strings.TrimSuffix(typeName, "array")
		size, ok := scalarSizes[elementType]
		if !ok {
			return nil, fmt.Errorf("不支持的数据类型 %s", item.Type)
		}
		var values []json.RawMessage
		if len(item.Value) == 0 {
			return nil, fmt.Errorf("数组必须配置 value，例如 [1,2,3]")
		}
		if err := json.Unmarshal(item.Value, &values); err != nil {
			return nil, fmt.Errorf("数组 value 格式错误")
		}
		if len(values) == 0 || len(values) > 65536 {
			return nil, fmt.Errorf("数组元素数量必须在 1～65536 之间")
		}
		data := make([]byte, len(values)*size)
		for i, raw := range values {
			if err := encodeScalar(data[i*size:], elementType, raw); err != nil {
				return nil, fmt.Errorf("数组第 %d 项: %w", i+1, err)
			}
		}
		return data, nil
	}

	size, ok := scalarSizes[typeName]
	if !ok {
		return nil, fmt.Errorf("不支持的数据类型 %s", item.Type)
	}
	data := make([]byte, size)
	raw := item.Value
	if len(raw) == 0 {
		raw = json.RawMessage(`0`)
	}
	if err := encodeScalar(data, typeName, raw); err != nil {
		return nil, err
	}
	return data, nil
}

func encodeScalar(dst []byte, typeName string, raw json.RawMessage) error {
	text := strings.TrimSpace(string(raw))
	if typeName == "bool" {
		var value bool
		if err := json.Unmarshal(raw, &value); err != nil {
			if text == "1" {
				value = true
			} else if text == "0" {
				value = false
			} else {
				return fmt.Errorf("Bool 值必须是 true、false、1 或 0")
			}
		}
		if value {
			dst[0] = 1
		}
		return nil
	}

	bits := scalarSizes[typeName] * 8
	if typeName == "float" || typeName == "double" {
		value, err := strconv.ParseFloat(text, bits)
		if err != nil {
			return fmt.Errorf("数值格式错误: %s", text)
		}
		if typeName == "float" {
			binary.LittleEndian.PutUint32(dst, math.Float32bits(float32(value)))
		} else {
			binary.LittleEndian.PutUint64(dst, math.Float64bits(value))
		}
		return nil
	}

	if strings.HasPrefix(typeName, "uint") {
		value, err := strconv.ParseUint(text, 10, bits)
		if err != nil {
			return fmt.Errorf("数值超出范围或格式错误: %s", text)
		}
		switch bits {
		case 8:
			dst[0] = byte(value)
		case 16:
			binary.LittleEndian.PutUint16(dst, uint16(value))
		case 32:
			binary.LittleEndian.PutUint32(dst, uint32(value))
		case 64:
			binary.LittleEndian.PutUint64(dst, value)
		}
		return nil
	}

	value, err := strconv.ParseInt(text, 10, bits)
	if err != nil {
		return fmt.Errorf("数值超出范围或格式错误: %s", text)
	}
	switch bits {
	case 8:
		dst[0] = byte(int8(value))
	case 16:
		binary.LittleEndian.PutUint16(dst, uint16(int16(value)))
	case 32:
		binary.LittleEndian.PutUint32(dst, uint32(int32(value)))
	case 64:
		binary.LittleEndian.PutUint64(dst, uint64(value))
	}
	return nil
}

func (s *Store) createHandle(name string) (uint32, uint32) {
	s.mu.Lock()
	defer s.mu.Unlock()
	point, ok := s.points[normalizeName(name)]
	if !ok {
		return 0, adsErrSymbolNotFound
	}
	handle := s.nextHandle
	s.nextHandle++
	if s.nextHandle == 0 {
		s.nextHandle = 1
	}
	s.handles[handle] = point
	return handle, adsErrNone
}

func (s *Store) releaseHandle(handle uint32) uint32 {
	s.mu.Lock()
	defer s.mu.Unlock()
	if _, ok := s.handles[handle]; !ok {
		return adsErrInvalidOffset
	}
	delete(s.handles, handle)
	return adsErrNone
}

func (s *Store) readHandle(handle uint32, length uint32) ([]byte, uint32) {
	s.mu.RLock()
	defer s.mu.RUnlock()
	point, ok := s.handles[handle]
	if !ok {
		return nil, adsErrInvalidOffset
	}
	if uint64(length) > uint64(len(point.Data)) {
		return nil, adsErrInvalidSize
	}
	data := point.Data[:length]
	return append([]byte(nil), data...), adsErrNone
}

func (s *Store) writeHandle(handle uint32, data []byte) (string, []byte, uint32) {
	s.mu.Lock()
	defer s.mu.Unlock()
	point, ok := s.handles[handle]
	if !ok {
		return "", nil, adsErrInvalidOffset
	}
	if len(data) != len(point.Data) {
		return point.Name, nil, adsErrInvalidSize
	}
	copy(point.Data, data)
	return point.Name, append([]byte(nil), data...), adsErrNone
}

func (s *Store) snapshotHandles() map[uint32]string {
	s.mu.RLock()
	defer s.mu.RUnlock()
	result := make(map[uint32]string, len(s.handles))
	for handle, point := range s.handles {
		result[handle] = point.Name
	}
	return result
}

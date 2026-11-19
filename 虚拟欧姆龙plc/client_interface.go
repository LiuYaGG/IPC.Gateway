package gofins

import (
	"context"
	"encoding/binary"
	"time"
)

// Configuration operations.
type ClientConfig interface {
	SetByteOrder(o binary.ByteOrder)
	SetTimeoutMs(t uint)
	SetReadTimeout(t time.Duration)
}

// Auto-reconnect controls.
type AutoReconnect interface {
	EnableAutoReconnect(maxRetries int, initialDelay time.Duration)
	DisableAutoReconnect()
	IsReconnecting() bool
	EnableDynamicLocalAddress()
	DisableDynamicLocalAddress()
}

// Interceptor/plugin hooks.
type ClientHooks interface {
	SetInterceptor(interceptor Interceptor)
	Use(plugins ...Plugin) error
}

// Lifecycle controls.
type ClientLifecycle interface {
	IsClosed() bool
	Close() error
	Shutdown() error
}

// Read operations.
type ClientReader interface {
	ReadWords(ctx context.Context, memoryArea byte, address uint16, readCount uint16) ([]uint16, error)
	ReadBytes(ctx context.Context, memoryArea byte, address uint16, readCount uint16) ([]byte, error)
	ReadString(ctx context.Context, memoryArea byte, address uint16, readCount uint16) (string, error)
	ReadBits(ctx context.Context, memoryArea byte, address uint16, bitOffset byte, readCount uint16) ([]bool, error)
	ReadClock(ctx context.Context) (*time.Time, error)
}

// Write operations.
type ClientWriter interface {
	WriteWords(ctx context.Context, memoryArea byte, address uint16, data []uint16) error
	WriteString(ctx context.Context, memoryArea byte, address uint16, s string) error
	WriteBytes(ctx context.Context, memoryArea byte, address uint16, b []byte) error
	WriteBits(ctx context.Context, memoryArea byte, address uint16, bitOffset byte, data []bool) error
	SetBit(ctx context.Context, memoryArea byte, address uint16, bitOffset byte) error
	ResetBit(ctx context.Context, memoryArea byte, address uint16, bitOffset byte) error
	ToggleBit(ctx context.Context, memoryArea byte, address uint16, bitOffset byte) error
}

// FINSClient defines the public contract of Client for easier testing/mocking.
type FINSClient interface {
	ClientConfig
	AutoReconnect
	ClientHooks
	ClientLifecycle
	ClientReader
	ClientWriter
}

// Ensure Client implements the interface.
var _ FINSClient = (*Client)(nil)

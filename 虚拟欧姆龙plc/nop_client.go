package gofins

import (
	"context"
	"encoding/binary"
	"time"
)

// NopClient implements FINSClient with no-op behavior.
// Useful for tests or placeholders where a real PLC connection is not required.
type NopClient struct{}

func (NopClient) SetByteOrder(binary.ByteOrder)          {}
func (NopClient) SetTimeoutMs(uint)                      {}
func (NopClient) SetReadTimeout(time.Duration)           {}
func (NopClient) EnableAutoReconnect(int, time.Duration) {}
func (NopClient) DisableAutoReconnect()                  {}
func (NopClient) IsReconnecting() bool                   { return false }
func (NopClient) EnableDynamicLocalAddress()             {}
func (NopClient) DisableDynamicLocalAddress()            {}
func (NopClient) SetInterceptor(Interceptor)             {}
func (NopClient) Use(...Plugin) error                    { return nil }
func (NopClient) IsClosed() bool                         { return false }
func (NopClient) Close() error                           { return nil }
func (NopClient) Shutdown() error                        { return nil }
func (NopClient) ReadWords(context.Context, byte, uint16, uint16) ([]uint16, error) {
	return nil, nil
}
func (NopClient) ReadBytes(context.Context, byte, uint16, uint16) ([]byte, error) {
	return nil, nil
}
func (NopClient) ReadString(context.Context, byte, uint16, uint16) (string, error) {
	return "", nil
}
func (NopClient) ReadBits(context.Context, byte, uint16, byte, uint16) ([]bool, error) {
	return nil, nil
}
func (NopClient) ReadClock(context.Context) (*time.Time, error) {
	return nil, nil
}
func (NopClient) WriteWords(context.Context, byte, uint16, []uint16) error { return nil }
func (NopClient) WriteString(context.Context, byte, uint16, string) error  { return nil }
func (NopClient) WriteBytes(context.Context, byte, uint16, []byte) error   { return nil }
func (NopClient) WriteBits(context.Context, byte, uint16, byte, []bool) error {
	return nil
}
func (NopClient) SetBit(context.Context, byte, uint16, byte) error    { return nil }
func (NopClient) ResetBit(context.Context, byte, uint16, byte) error  { return nil }
func (NopClient) ToggleBit(context.Context, byte, uint16, byte) error { return nil }

var _ FINSClient = NopClient{}

package gofins

import (
	"context"
	"encoding/binary"
	"fmt"
	"math"
	"net"
	"testing"
	"time"

	"github.com/stretchr/testify/assert"
)

// getAvailablePort returns an available port on localhost
func getAvailablePort(t *testing.T) int {
	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("Failed to get available port: %v", err)
	}
	port := listener.Addr().(*net.TCPAddr).Port
	listener.Close()
	return port
}

// getTestAddresses returns a pair of available addresses for testing
func getTestAddresses(t *testing.T) (clientAddr, plcAddr Address) {
	clientPort := getAvailablePort(t)
	plcPort := getAvailablePort(t)

	// Ensure ports are different
	if clientPort == plcPort {
		plcPort = getAvailablePort(t)
	}

	clientAddr = NewAddress("127.0.0.1", clientPort, 0, 2, 0)
	plcAddr = NewAddress("127.0.0.1", plcPort, 0, 10, 0)

	t.Logf("Using client port %d, PLC port %d", clientPort, plcPort)
	return
}

func TestFinsClient(t *testing.T) {
	ctx := context.Background()
	clientAddr, plcAddr := getTestAddresses(t)

	toWrite := []uint16{5, 4, 3, 2, 1}

	s, e := NewPLCSimulator(plcAddr)
	if e != nil {
		panic(e)
	}
	defer s.Close()

	c, e := NewUDPClient(clientAddr, plcAddr)
	if e != nil {
		panic(e)
	}
	defer c.Close()

	// ------------- Test Words
	err := c.WriteWords(ctx, MemoryAreaDMWord, 100, toWrite)
	assert.Nil(t, err)

	vals, err := c.ReadWords(ctx, MemoryAreaDMWord, 100, 5)
	assert.Nil(t, err)
	assert.Equal(t, toWrite, vals)

	// test setting response timeout
	c.SetTimeoutMs(50)
	_, err = c.ReadWords(ctx, MemoryAreaDMWord, 100, 5)
	assert.Nil(t, err)

	// ------------- Test Strings
	err = c.WriteString(ctx, MemoryAreaDMWord, 10, "ф1234")
	assert.Nil(t, err)

	v, err := c.ReadString(ctx, MemoryAreaDMWord, 12, 1)
	assert.Nil(t, err)
	assert.Equal(t, "12", v)

	v, err = c.ReadString(ctx, MemoryAreaDMWord, 10, 3)
	assert.Nil(t, err)
	assert.Equal(t, "ф1234", v)

	v, err = c.ReadString(ctx, MemoryAreaDMWord, 10, 5)
	assert.Nil(t, err)
	assert.Equal(t, "ф1234", v)

	// ------------- Test Bytes
	err = c.WriteBytes(ctx, MemoryAreaDMWord, 10, []byte{0x00, 0x00, 0xC1, 0xA0})
	assert.Nil(t, err)

	b, err := c.ReadBytes(ctx, MemoryAreaDMWord, 10, 2)
	assert.Nil(t, err)
	assert.Equal(t, []byte{0x00, 0x00, 0xC1, 0xA0}, b)

	buf := make([]byte, 8)
	binary.LittleEndian.PutUint64(buf[:], math.Float64bits(-20))
	err = c.WriteBytes(ctx, MemoryAreaDMWord, 10, buf)
	assert.Nil(t, err)

	b, err = c.ReadBytes(ctx, MemoryAreaDMWord, 10, 4)
	assert.Nil(t, err)
	assert.Equal(t, []byte{0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x34, 0xc0}, b)

	// ------------- Test Bits
	err = c.WriteBits(ctx, MemoryAreaDMBit, 10, 2, []bool{true, false, true})
	assert.Nil(t, err)

	bs, err := c.ReadBits(ctx, MemoryAreaDMBit, 10, 2, 3)
	assert.Nil(t, err)
	assert.Equal(t, []bool{true, false, true}, bs)

	bs, err = c.ReadBits(ctx, MemoryAreaDMBit, 10, 1, 5)
	assert.Nil(t, err)
	assert.Equal(t, []bool{false, true, false, true, false}, bs)
}

func TestClientClosed(t *testing.T) {
	ctx := context.Background()
	clientAddr, plcAddr := getTestAddresses(t)

	s, e := NewPLCSimulator(plcAddr)
	if e != nil {
		panic(e)
	}
	defer s.Close()

	c, e := NewUDPClient(clientAddr, plcAddr)
	if e != nil {
		panic(e)
	}

	assert.False(t, c.IsClosed())

	c.Close()

	assert.True(t, c.IsClosed())

	// Operations should return ClientClosedError
	_, err := c.ReadWords(ctx, MemoryAreaDMWord, 100, 5)
	assert.Error(t, err)
	assert.IsType(t, ClientClosedError{}, err)
}

func TestContextCancellation(t *testing.T) {
	clientAddr, plcAddr := getTestAddresses(t)

	s, e := NewPLCSimulator(plcAddr)
	if e != nil {
		panic(e)
	}
	defer s.Close()

	c, e := NewUDPClient(clientAddr, plcAddr)
	if e != nil {
		panic(e)
	}
	defer c.Close()

	// Create a context with timeout
	ctx, cancel := context.WithTimeout(context.Background(), 1*time.Millisecond)
	defer cancel()

	// Wait for context to expire
	time.Sleep(10 * time.Millisecond)

	// Operation should fail with context error
	_, err := c.ReadWords(ctx, MemoryAreaDMWord, 100, 5)
	assert.Error(t, err)
	assert.Equal(t, context.DeadlineExceeded, err)
}

func TestContextCancellationImmediate(t *testing.T) {
	clientAddr, plcAddr := getTestAddresses(t)

	s, e := NewPLCSimulator(plcAddr)
	if e != nil {
		panic(e)
	}
	defer s.Close()

	c, e := NewUDPClient(clientAddr, plcAddr)
	if e != nil {
		panic(e)
	}
	defer c.Close()

	// Create a context and cancel it immediately
	ctx, cancel := context.WithCancel(context.Background())
	cancel()

	// Operation should fail with context.Canceled
	_, err := c.ReadWords(ctx, MemoryAreaDMWord, 100, 5)
	assert.Error(t, err)
	assert.Equal(t, context.Canceled, err)
}

func TestContextWithTimeout(t *testing.T) {
	ctx := context.Background()
	clientAddr, plcAddr := getTestAddresses(t)

	s, e := NewPLCSimulator(plcAddr)
	if e != nil {
		panic(e)
	}
	defer s.Close()

	c, e := NewUDPClient(clientAddr, plcAddr)
	if e != nil {
		panic(e)
	}
	defer c.Close()

	// Create a context with a reasonable timeout
	ctxWithTimeout, cancel := context.WithTimeout(ctx, 100*time.Millisecond)
	defer cancel()

	// Operation should succeed
	toWrite := []uint16{1, 2, 3}
	err := c.WriteWords(ctxWithTimeout, MemoryAreaDMWord, 200, toWrite)
	assert.Nil(t, err)

	vals, err := c.ReadWords(ctxWithTimeout, MemoryAreaDMWord, 200, 3)
	assert.Nil(t, err)
	assert.Equal(t, toWrite, vals)
}

func TestBitOperations(t *testing.T) {
	ctx := context.Background()
	clientAddr, plcAddr := getTestAddresses(t)

	s, e := NewPLCSimulator(plcAddr)
	if e != nil {
		panic(e)
	}
	defer s.Close()

	c, e := NewUDPClient(clientAddr, plcAddr)
	if e != nil {
		panic(e)
	}
	defer c.Close()

	// Test SetBit
	err := c.SetBit(ctx, MemoryAreaDMBit, 50, 3)
	assert.Nil(t, err)

	// Verify bit is set
	bits, err := c.ReadBits(ctx, MemoryAreaDMBit, 50, 3, 1)
	assert.Nil(t, err)
	assert.True(t, bits[0])

	// Test ResetBit
	err = c.ResetBit(ctx, MemoryAreaDMBit, 50, 3)
	assert.Nil(t, err)

	// Verify bit is reset
	bits, err = c.ReadBits(ctx, MemoryAreaDMBit, 50, 3, 1)
	assert.Nil(t, err)
	assert.False(t, bits[0])

	// Test ToggleBit - from false to true
	err = c.ToggleBit(ctx, MemoryAreaDMBit, 50, 3)
	assert.Nil(t, err)

	bits, err = c.ReadBits(ctx, MemoryAreaDMBit, 50, 3, 1)
	assert.Nil(t, err)
	assert.True(t, bits[0])

	// Test ToggleBit - from true to false
	err = c.ToggleBit(ctx, MemoryAreaDMBit, 50, 3)
	assert.Nil(t, err)

	bits, err = c.ReadBits(ctx, MemoryAreaDMBit, 50, 3, 1)
	assert.Nil(t, err)
	assert.False(t, bits[0])
}

func TestSetByteOrder(t *testing.T) {
	ctx := context.Background()
	clientAddr, plcAddr := getTestAddresses(t)

	s, e := NewPLCSimulator(plcAddr)
	if e != nil {
		panic(e)
	}
	defer s.Close()

	c, e := NewUDPClient(clientAddr, plcAddr)
	if e != nil {
		panic(e)
	}
	defer c.Close()

	// Test with LittleEndian
	c.SetByteOrder(binary.LittleEndian)

	toWrite := []uint16{0x1234}
	err := c.WriteWords(ctx, MemoryAreaDMWord, 300, toWrite)
	assert.Nil(t, err)

	vals, err := c.ReadWords(ctx, MemoryAreaDMWord, 300, 1)
	assert.Nil(t, err)
	assert.Equal(t, toWrite, vals)

	// Test with BigEndian (default)
	c.SetByteOrder(binary.BigEndian)

	toWrite2 := []uint16{0x5678}
	err = c.WriteWords(ctx, MemoryAreaDMWord, 301, toWrite2)
	assert.Nil(t, err)

	vals2, err := c.ReadWords(ctx, MemoryAreaDMWord, 301, 1)
	assert.Nil(t, err)
	assert.Equal(t, toWrite2, vals2)
}

func TestSetReadTimeout(t *testing.T) {
	ctx := context.Background()
	clientAddr, plcAddr := getTestAddresses(t)

	s, e := NewPLCSimulator(plcAddr)
	if e != nil {
		panic(e)
	}
	defer s.Close()

	c, e := NewUDPClient(clientAddr, plcAddr)
	if e != nil {
		panic(e)
	}
	defer c.Close()

	// Set custom read timeout
	c.SetReadTimeout(10 * time.Second)

	// Should still work normally
	toWrite := []uint16{99}
	err := c.WriteWords(ctx, MemoryAreaDMWord, 400, toWrite)
	assert.Nil(t, err)

	vals, err := c.ReadWords(ctx, MemoryAreaDMWord, 400, 1)
	assert.Nil(t, err)
	assert.Equal(t, toWrite, vals)
}

func TestInvalidMemoryAreas(t *testing.T) {
	ctx := context.Background()
	clientAddr, plcAddr := getTestAddresses(t)

	s, e := NewPLCSimulator(plcAddr)
	if e != nil {
		panic(e)
	}
	defer s.Close()

	c, e := NewUDPClient(clientAddr, plcAddr)
	if e != nil {
		panic(e)
	}
	defer c.Close()

	// Try to read words from bit area
	_, err := c.ReadWords(ctx, MemoryAreaDMBit, 100, 5)
	assert.Error(t, err)
	assert.IsType(t, IncompatibleMemoryAreaError{}, err)

	// Try to write words to bit area
	err = c.WriteWords(ctx, MemoryAreaDMBit, 100, []uint16{1, 2, 3})
	assert.Error(t, err)
	assert.IsType(t, IncompatibleMemoryAreaError{}, err)

	// Try to read bits from word area
	_, err = c.ReadBits(ctx, MemoryAreaDMWord, 100, 0, 5)
	assert.Error(t, err)
	assert.IsType(t, IncompatibleMemoryAreaError{}, err)

	// Try to write bits to word area
	err = c.WriteBits(ctx, MemoryAreaDMWord, 100, 0, []bool{true, false})
	assert.Error(t, err)
	assert.IsType(t, IncompatibleMemoryAreaError{}, err)

	// Try SetBit on word area
	err = c.SetBit(ctx, MemoryAreaDMWord, 100, 0)
	assert.Error(t, err)
	assert.IsType(t, IncompatibleMemoryAreaError{}, err)
}

func TestErrorMessages(t *testing.T) {
	// Test ResponseTimeoutError
	err := ResponseTimeoutError{duration: 100 * time.Millisecond}
	assert.Contains(t, err.Error(), "100")
	assert.Contains(t, err.Error(), "timeout")

	// Test IncompatibleMemoryAreaError
	err2 := IncompatibleMemoryAreaError{area: 0x82}
	assert.Contains(t, err2.Error(), "0x82")
	assert.Contains(t, err2.Error(), "incompatible")

	// Test ClientClosedError
	err3 := ClientClosedError{}
	assert.Contains(t, err3.Error(), "closed")

	// Test BCDBadDigitError
	err4 := BCDBadDigitError{v: "hi", val: 15}
	assert.Contains(t, err4.Error(), "hi")
	assert.Contains(t, err4.Error(), "15")

	// Test BCDOverflowError
	err5 := BCDOverflowError{}
	assert.Contains(t, err5.Error(), "Overflow")
}

func TestNewLocalAddress(t *testing.T) {
	addr := NewLocalAddress(0, 5, 0)
	assert.Equal(t, byte(0), addr.FinAddress.Network)
	assert.Equal(t, byte(5), addr.FinAddress.Node)
	assert.Equal(t, byte(0), addr.FinAddress.Unit)
	assert.Nil(t, addr.UdpAddress)
}

func TestServerErr(t *testing.T) {
	_, plcAddr := getTestAddresses(t)

	s, e := NewPLCSimulator(plcAddr)
	if e != nil {
		panic(e)
	}
	defer s.Close()

	// Get error channel
	errChan := s.Err()
	assert.NotNil(t, errChan)

	// Verify no errors initially
	select {
	case err := <-errChan:
		t.Fatalf("Unexpected error: %v", err)
	case <-time.After(10 * time.Millisecond):
		// Good, no errors
	}
}

func TestResponseTimeout(t *testing.T) {
	ctx := context.Background()
	clientAddr, plcAddr := getTestAddresses(t)

	// Don't create server - client will timeout
	c, e := NewUDPClient(clientAddr, plcAddr)
	if e != nil {
		panic(e)
	}
	defer c.Close()

	// Set very short timeout
	c.SetTimeoutMs(10)

	// This should timeout since there's no server
	_, err := c.ReadWords(ctx, MemoryAreaDMWord, 100, 5)
	assert.Error(t, err)
	// Should be either timeout or other network error
	assert.NotNil(t, err)
}

func TestDoubleClose(t *testing.T) {
	ctx := context.Background()
	clientAddr, plcAddr := getTestAddresses(t)

	s, e := NewPLCSimulator(plcAddr)
	if e != nil {
		panic(e)
	}

	c, e := NewUDPClient(clientAddr, plcAddr)
	if e != nil {
		panic(e)
	}

	// First close
	err := c.Close()
	assert.Nil(t, err)

	// Second close should not error
	err = c.Close()
	assert.Nil(t, err)

	// Same for server
	err = s.Close()
	assert.Nil(t, err)

	err = s.Close()
	assert.Nil(t, err)

	// Operations after close should fail
	_, err = c.ReadWords(ctx, MemoryAreaDMWord, 100, 5)
	assert.Error(t, err)
	assert.IsType(t, ClientClosedError{}, err)
}

func TestMemoryAreaChecks(t *testing.T) {
	// Test checkIsWordMemoryArea with all valid areas
	assert.True(t, checkIsWordMemoryArea(MemoryAreaDMWord))
	assert.True(t, checkIsWordMemoryArea(MemoryAreaARWord))
	assert.True(t, checkIsWordMemoryArea(MemoryAreaHRWord))
	assert.True(t, checkIsWordMemoryArea(MemoryAreaWRWord))
	assert.False(t, checkIsWordMemoryArea(MemoryAreaDMBit))
	assert.False(t, checkIsWordMemoryArea(0xFF))

	// Test checkIsBitMemoryArea with all valid areas
	assert.True(t, checkIsBitMemoryArea(MemoryAreaDMBit))
	assert.True(t, checkIsBitMemoryArea(MemoryAreaARBit))
	assert.True(t, checkIsBitMemoryArea(MemoryAreaHRBit))
	assert.True(t, checkIsBitMemoryArea(MemoryAreaWRBit))
	assert.False(t, checkIsBitMemoryArea(MemoryAreaDMWord))
	assert.False(t, checkIsBitMemoryArea(0xFF))
}

func TestAutoReconnectDisabledByDefault(t *testing.T) {
	clientAddr, plcAddr := getTestAddresses(t)

	s, e := NewPLCSimulator(plcAddr)
	if e != nil {
		panic(e)
	}
	defer s.Close()

	c, e := NewUDPClient(clientAddr, plcAddr)
	if e != nil {
		panic(e)
	}
	defer c.Close()

	// Auto-reconnect should be disabled by default
	c.reconnectMutex.RLock()
	assert.False(t, c.autoReconnect)
	c.reconnectMutex.RUnlock()

	// Not reconnecting initially
	assert.False(t, c.IsReconnecting())
}

func TestEnableDisableAutoReconnect(t *testing.T) {
	clientAddr, plcAddr := getTestAddresses(t)

	s, e := NewPLCSimulator(plcAddr)
	if e != nil {
		panic(e)
	}
	defer s.Close()

	c, e := NewUDPClient(clientAddr, plcAddr)
	if e != nil {
		panic(e)
	}
	defer c.Close()

	// Enable auto-reconnect
	c.EnableAutoReconnect(5, 100*time.Millisecond)

	c.reconnectMutex.RLock()
	assert.True(t, c.autoReconnect)
	assert.Equal(t, 5, c.maxReconnect)
	assert.Equal(t, 100*time.Millisecond, c.reconnectDelay)
	c.reconnectMutex.RUnlock()

	// Disable auto-reconnect
	c.DisableAutoReconnect()

	c.reconnectMutex.RLock()
	assert.False(t, c.autoReconnect)
	c.reconnectMutex.RUnlock()
}

func TestShutdownStopsReconnection(t *testing.T) {
	clientAddr, plcAddr := getTestAddresses(t)

	s, e := NewPLCSimulator(plcAddr)
	if e != nil {
		panic(e)
	}
	defer s.Close()

	c, e := NewUDPClient(clientAddr, plcAddr)
	if e != nil {
		panic(e)
	}

	// Enable auto-reconnect
	c.EnableAutoReconnect(5, 100*time.Millisecond)

	c.reconnectMutex.RLock()
	assert.True(t, c.autoReconnect)
	c.reconnectMutex.RUnlock()

	// Shutdown should disable auto-reconnect and close
	err := c.Shutdown()
	assert.Nil(t, err)

	// Auto-reconnect should be disabled
	c.reconnectMutex.RLock()
	assert.False(t, c.autoReconnect)
	c.reconnectMutex.RUnlock()

	// Client should be closed
	assert.True(t, c.IsClosed())
}

func TestOperationsWaitForConnection(t *testing.T) {
	ctx := context.Background()
	clientAddr, plcAddr := getTestAddresses(t)

	s, e := NewPLCSimulator(plcAddr)
	if e != nil {
		panic(e)
	}
	defer s.Close()

	c, e := NewUDPClient(clientAddr, plcAddr)
	if e != nil {
		panic(e)
	}
	defer c.Close()

	// Normal operation should work immediately (connection is ready)
	toWrite := []uint16{1, 2, 3}
	err := c.WriteWords(ctx, MemoryAreaDMWord, 100, toWrite)
	assert.Nil(t, err)

	vals, err := c.ReadWords(ctx, MemoryAreaDMWord, 100, 3)
	assert.Nil(t, err)
	assert.Equal(t, toWrite, vals)
}

func TestOperationsRespectContextDuringWait(t *testing.T) {
	clientAddr, plcAddr := getTestAddresses(t)

	s, e := NewPLCSimulator(plcAddr)
	if e != nil {
		panic(e)
	}
	defer s.Close()

	c, e := NewUDPClient(clientAddr, plcAddr)
	if e != nil {
		panic(e)
	}
	defer c.Close()

	// Manually mark as disconnected to simulate reconnection
	c.connectedMutex.Lock()
	c.connected = make(chan struct{}) // Not closed = disconnected
	c.connectedMutex.Unlock()

	// Create context with very short timeout
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Millisecond)
	defer cancel()

	// Operation should fail with context deadline exceeded
	_, err := c.ReadWords(ctx, MemoryAreaDMWord, 100, 5)
	assert.Error(t, err)
	assert.Equal(t, context.DeadlineExceeded, err)
}

func TestWaitForConnectionReturnsOnClientClose(t *testing.T) {
	clientAddr, plcAddr := getTestAddresses(t)

	s, e := NewPLCSimulator(plcAddr)
	if e != nil {
		panic(e)
	}
	defer s.Close()

	c, e := NewUDPClient(clientAddr, plcAddr)
	if e != nil {
		panic(e)
	}

	// Manually mark as disconnected to simulate reconnection
	c.connectedMutex.Lock()
	c.connected = make(chan struct{}) // Not closed = disconnected
	c.connectedMutex.Unlock()

	// Close client in a goroutine after a short delay
	go func() {
		time.Sleep(50 * time.Millisecond)
		c.Close()
	}()

	// Operation should fail with ClientClosedError
	ctx := context.Background()
	_, err := c.ReadWords(ctx, MemoryAreaDMWord, 100, 5)
	assert.Error(t, err)
	assert.IsType(t, ClientClosedError{}, err)
}

func TestInterceptorBasic(t *testing.T) {
	ctx := context.Background()
	clientAddr, plcAddr := getTestAddresses(t)

	s, e := NewPLCSimulator(plcAddr)
	if e != nil {
		panic(e)
	}
	defer s.Close()

	c, e := NewUDPClient(clientAddr, plcAddr)
	if e != nil {
		panic(e)
	}
	defer c.Close()

	// Track interceptor calls
	var calls []OperationType
	c.SetInterceptor(func(ic *InterceptorCtx) (interface{}, error) {
		calls = append(calls, ic.Info().Operation)
		return ic.Invoke(nil)
	})

	// Perform operations
	toWrite := []uint16{1, 2, 3}
	err := c.WriteWords(ctx, MemoryAreaDMWord, 100, toWrite)
	assert.Nil(t, err)

	_, err = c.ReadWords(ctx, MemoryAreaDMWord, 100, 3)
	assert.Nil(t, err)

	// Verify interceptor was called
	assert.Equal(t, 2, len(calls))
	assert.Equal(t, OpWriteWords, calls[0])
	assert.Equal(t, OpReadWords, calls[1])
}

func TestInterceptorMetrics(t *testing.T) {
	ctx := context.Background()
	clientAddr, plcAddr := getTestAddresses(t)

	s, e := NewPLCSimulator(plcAddr)
	if e != nil {
		panic(e)
	}
	defer s.Close()

	c, e := NewUDPClient(clientAddr, plcAddr)
	if e != nil {
		panic(e)
	}
	defer c.Close()

	// Set up metrics collector
	metrics := NewMetricsCollector()
	c.SetInterceptor(metrics.Interceptor())

	// Perform operations
	toWrite := []uint16{1, 2, 3}
	c.WriteWords(ctx, MemoryAreaDMWord, 100, toWrite)
	c.ReadWords(ctx, MemoryAreaDMWord, 100, 3)
	c.ReadWords(ctx, MemoryAreaDMWord, 200, 5)

	// Check metrics
	readCount, _, _ := metrics.GetStats(OpReadWords)
	assert.Equal(t, int64(2), readCount)

	writeCount, _, _ := metrics.GetStats(OpWriteWords)
	assert.Equal(t, int64(1), writeCount)
}

func TestInterceptorChaining(t *testing.T) {
	ctx := context.Background()
	clientAddr, plcAddr := getTestAddresses(t)

	s, e := NewPLCSimulator(plcAddr)
	if e != nil {
		panic(e)
	}
	defer s.Close()

	c, e := NewUDPClient(clientAddr, plcAddr)
	if e != nil {
		panic(e)
	}
	defer c.Close()

	// Track execution order
	var order []string

	interceptor1 := func(ic *InterceptorCtx) (interface{}, error) {
		order = append(order, "interceptor1-before")
		result, err := ic.Invoke(nil)
		order = append(order, "interceptor1-after")
		return result, err
	}

	interceptor2 := func(ic *InterceptorCtx) (interface{}, error) {
		order = append(order, "interceptor2-before")
		result, err := ic.Invoke(nil)
		order = append(order, "interceptor2-after")
		return result, err
	}

	// Chain interceptors
	c.SetInterceptor(ChainInterceptors(interceptor1, interceptor2))

	// Perform operation
	toWrite := []uint16{1, 2, 3}
	c.WriteWords(ctx, MemoryAreaDMWord, 100, toWrite)

	// Verify execution order
	assert.Equal(t, []string{
		"interceptor1-before",
		"interceptor2-before",
		"interceptor2-after",
		"interceptor1-after",
	}, order)
}

func TestInterceptorCanShortCircuit(t *testing.T) {
	ctx := context.Background()
	clientAddr, plcAddr := getTestAddresses(t)

	s, e := NewPLCSimulator(plcAddr)
	if e != nil {
		panic(e)
	}
	defer s.Close()

	c, e := NewUDPClient(clientAddr, plcAddr)
	if e != nil {
		panic(e)
	}
	defer c.Close()

	// Interceptor that blocks writes
	c.SetInterceptor(func(ic *InterceptorCtx) (interface{}, error) {
		if ic.Info().Operation == OpWriteWords {
			return nil, fmt.Errorf("writes are blocked")
		}
		return ic.Invoke(nil)
	})

	// Write should be blocked
	toWrite := []uint16{1, 2, 3}
	err := c.WriteWords(ctx, MemoryAreaDMWord, 100, toWrite)
	assert.Error(t, err)
	assert.Contains(t, err.Error(), "blocked")

	// Read should work
	_, err = c.ReadWords(ctx, MemoryAreaDMWord, 100, 3)
	assert.Nil(t, err)
}

func TestInterceptorWithContext(t *testing.T) {
	ctx := context.Background()
	clientAddr, plcAddr := getTestAddresses(t)

	s, e := NewPLCSimulator(plcAddr)
	if e != nil {
		panic(e)
	}
	defer s.Close()

	c, e := NewUDPClient(clientAddr, plcAddr)
	if e != nil {
		panic(e)
	}
	defer c.Close()

	// Interceptor that adds trace ID from context
	traceKey := "traceID"
	var capturedTraceID string

	c.SetInterceptor(func(ic *InterceptorCtx) (interface{}, error) {
		if id := ic.Context().Value(traceKey); id != nil {
			capturedTraceID = id.(string)
		}
		return ic.Invoke(nil)
	})

	// Perform operation with trace ID
	ctxWithTrace := context.WithValue(ctx, traceKey, "trace-12345")
	toWrite := []uint16{1, 2, 3}
	c.WriteWords(ctxWithTrace, MemoryAreaDMWord, 100, toWrite)

	// Verify trace ID was captured
	assert.Equal(t, "trace-12345", capturedTraceID)
}

func TestAddressHelpers(t *testing.T) {
	udpAddr := NewUDPAddress("127.0.0.1", 9600, 1, 2, 3)
	assert.NotNil(t, udpAddr.UdpAddress)
	assert.Nil(t, udpAddr.TcpAddress)
	assert.Equal(t, byte(1), udpAddr.FinAddress.Network)

	tcpAddr := NewTCPAddress("127.0.0.1", 9601, 4, 5, 6)
	assert.NotNil(t, tcpAddr.TcpAddress)
	assert.Nil(t, tcpAddr.UdpAddress)
	assert.Equal(t, byte(5), tcpAddr.FinAddress.Node)

	both := NewAddress("127.0.0.1", 9602, 7, 8, 9)
	assert.NotNil(t, both.UdpAddress)
	assert.NotNil(t, both.TcpAddress)
	assert.Equal(t, byte(9), both.FinAddress.Unit)
}

func TestTCPClientReadWrite(t *testing.T) {
	ctx := context.Background()
	plcAddr := NewTCPAddress("127.0.0.1", getAvailablePort(t), 0, 10, 0)
	clientAddr := NewTCPAddress("", 0, 0, 2, 0)

	sim, err := NewPLCSimulator(plcAddr, WithTCPTransport())
	assert.NoError(t, err)
	defer sim.Close()

	c, err := NewTCPClient(clientAddr, plcAddr)
	assert.NoError(t, err)
	defer c.Close()

	toWrite := []uint16{9, 8, 7, 6}
	err = c.WriteWords(ctx, MemoryAreaDMWord, 120, toWrite)
	assert.NoError(t, err)

	read, err := c.ReadWords(ctx, MemoryAreaDMWord, 120, uint16(len(toWrite)))
	assert.NoError(t, err)
	assert.Equal(t, toWrite, read)
}

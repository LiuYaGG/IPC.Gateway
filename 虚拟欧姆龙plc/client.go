package gofins

import (
	"bytes"
	"context"
	"encoding/binary"
	"fmt"
	"net"
	"sync"
	"time"
)

const (
	DEFAULT_RESPONSE_TIMEOUT = 1000 // ms
	READ_BUFFER_SIZE         = 2048
	DEFAULT_READ_TIMEOUT     = 5 * time.Second
	MAX_SERVICE_ID_COUNT     = 256 // Maximum service IDs (byte range: 0-255)
	ERROR_CHANNEL_BUFFER     = 1   // Buffer size for error channels
	RESPONSE_CHANNEL_BUFFER  = 1   // Buffer size for response channels
	CLOSE_TIMEOUT            = 1 * time.Second
	DEFAULT_MAX_RECONNECT    = 5 // Default maximum reconnection attempts
	DEFAULT_RECONNECT_DELAY  = 1 * time.Second
	MAX_RECONNECT_DELAY      = 30 * time.Second
)

type transportKind int

const (
	transportUDP transportKind = iota
	transportTCP
)

// Client Omron FINS client
// Thread-safe: all public methods can be called concurrently
type Client struct {
	tr                transport
	tpKind            transportKind
	resp              []chan response
	respMutex         sync.RWMutex // Protects resp slice access
	sidMutex          sync.Mutex   // Protects sid incrementation
	dst               FinsAddress
	src               FinsAddress
	localAddr         *net.UDPAddr // Store for reconnection
	localTCPAddr      *net.TCPAddr
	localAddrMutex    sync.RWMutex
	remoteAddr        *net.UDPAddr // Store for reconnection
	remoteTCPAddr     *net.TCPAddr
	sid               byte
	closed            bool
	closeMutex        sync.RWMutex // Protects closed flag
	responseTimeoutMs time.Duration
	readTimeout       time.Duration
	byteOrder         binary.ByteOrder
	listenErr         chan error // Channel to receive listen loop errors
	done              chan struct{}

	// Auto-reconnect configuration
	autoReconnect       bool
	maxReconnect        int
	reconnectDelay      time.Duration
	useReconnectBackoff bool
	reconnecting        bool
	reconnectMutex      sync.RWMutex
	connected           chan struct{} // Closed when connected, recreated when disconnected
	connectedMutex      sync.Mutex
	dynamicLocal        bool // Re-detect local bind address on reconnect

	// Interceptor for all operations
	interceptor      Interceptor
	interceptorMutex sync.RWMutex

	// Plugin registry
	pm pluginManager
}

// NewUDPClient creates a new Omron FINS client over UDP.
func NewUDPClient(localAddr, plcAddr Address) (*Client, error) {
	c := new(Client)
	c.tpKind = transportUDP
	c.dst = plcAddr.FinAddress
	c.src = localAddr.FinAddress
	c.localAddr = localAddr.UdpAddress
	c.remoteAddr = plcAddr.UdpAddress
	c.responseTimeoutMs = DEFAULT_RESPONSE_TIMEOUT
	c.readTimeout = DEFAULT_READ_TIMEOUT
	c.byteOrder = binary.BigEndian
	c.listenErr = make(chan error, ERROR_CHANNEL_BUFFER)
	c.done = make(chan struct{})
	// Enable dynamic local detection when the caller left host/port unspecified.
	if localAddr.UdpAddress == nil || localAddr.UdpAddress.IP == nil || localAddr.UdpAddress.IP.IsUnspecified() || localAddr.UdpAddress.Port == 0 {
		c.dynamicLocal = true
	}

	// Auto-reconnect defaults (disabled by default)
	c.autoReconnect = false
	c.maxReconnect = DEFAULT_MAX_RECONNECT
	c.reconnectDelay = DEFAULT_RECONNECT_DELAY
	c.useReconnectBackoff = true
	c.connected = make(chan struct{})

	tr, err := newUDPTransport(localAddr.UdpAddress, plcAddr.UdpAddress)
	if err != nil {
		return nil, err
	}
	c.tr = tr
	// Record the actual local address in case the OS chose one.
	if la, ok := tr.LocalAddr().(*net.UDPAddr); ok {
		c.localAddrMutex.Lock()
		c.localAddr = la
		c.localAddrMutex.Unlock()
	}

	c.resp = make([]chan response, MAX_SERVICE_ID_COUNT)
	go c.listenLoop()

	// Signal that we're connected
	close(c.connected)

	// Let plugins know the initial connection is ready.
	if err := c.pm.notifyConnected(c); err != nil {
		return nil, err
	}

	return c, nil
}

// NewClient is kept for backward compatibility.
// Deprecated: use NewUDPClient for UDP and NewTCPClient for TCP.
func NewClient(localAddr, plcAddr Address) (*Client, error) {
	return NewUDPClient(localAddr, plcAddr)
}

// NewTCPClient creates a new Omron FINS client over TCP (FINS/TCP).
func NewTCPClient(localAddr, plcAddr Address) (*Client, error) {
	c := new(Client)
	c.tpKind = transportTCP
	c.dst = plcAddr.FinAddress
	c.src = localAddr.FinAddress
	c.localTCPAddr = localAddr.TcpAddress
	c.remoteTCPAddr = plcAddr.TcpAddress
	c.responseTimeoutMs = DEFAULT_RESPONSE_TIMEOUT
	c.readTimeout = DEFAULT_READ_TIMEOUT
	c.byteOrder = binary.BigEndian
	c.listenErr = make(chan error, ERROR_CHANNEL_BUFFER)
	c.done = make(chan struct{})
	// Auto-reconnect defaults (disabled by default)
	c.autoReconnect = false
	c.maxReconnect = DEFAULT_MAX_RECONNECT
	c.reconnectDelay = DEFAULT_RECONNECT_DELAY
	c.useReconnectBackoff = true
	c.connected = make(chan struct{})

	if c.remoteTCPAddr == nil {
		return nil, fmt.Errorf("remote TCP address is required")
	}

	tr, err := newTCPTransport(context.Background(), c.localTCPAddr, c.remoteTCPAddr)
	if err != nil {
		return nil, err
	}
	c.tr = tr
	// Record the actual local address in case the OS chose one.
	if la, ok := tr.LocalAddr().(*net.TCPAddr); ok {
		c.localAddrMutex.Lock()
		c.localTCPAddr = la
		c.localAddrMutex.Unlock()
	}

	c.resp = make([]chan response, MAX_SERVICE_ID_COUNT)
	go c.listenLoop()

	// Signal that we're connected
	close(c.connected)

	// Let plugins know the initial connection is ready.
	if err := c.pm.notifyConnected(c); err != nil {
		return nil, err
	}

	return c, nil
}

// SetByteOrder sets the byte order for word operations
// Default value: binary.BigEndian
func (c *Client) SetByteOrder(o binary.ByteOrder) {
	c.byteOrder = o
}

// SetTimeoutMs sets the response timeout duration (ms).
// Default value: 20ms.
// A timeout of zero can be used to block indefinitely.
func (c *Client) SetTimeoutMs(t uint) {
	c.responseTimeoutMs = time.Duration(t)
}

// SetReadTimeout sets the network read timeout.
// Default value: 5s.
// This timeout helps ensure graceful shutdown.
// Note: This should be called before starting operations for thread safety.
func (c *Client) SetReadTimeout(t time.Duration) {
	c.closeMutex.Lock()
	defer c.closeMutex.Unlock()
	c.readTimeout = t
}

// EnableAutoReconnect enables automatic reconnection on connection failures.
// maxRetries: maximum number of reconnection attempts (0 = infinite)
// initialDelay: initial delay before first retry (will use exponential backoff)
func (c *Client) EnableAutoReconnect(maxRetries int, initialDelay time.Duration) {
	c.reconnectMutex.Lock()
	defer c.reconnectMutex.Unlock()
	c.autoReconnect = true
	c.maxReconnect = maxRetries
	c.reconnectDelay = initialDelay
	c.useReconnectBackoff = true
}

// DisableAutoReconnect disables automatic reconnection
func (c *Client) DisableAutoReconnect() {
	c.reconnectMutex.Lock()
	defer c.reconnectMutex.Unlock()
	c.autoReconnect = false
}

// DisableReconnectBackoff forces reconnection attempts to use a fixed delay (no exponential backoff).
func (c *Client) DisableReconnectBackoff() {
	c.reconnectMutex.Lock()
	defer c.reconnectMutex.Unlock()
	c.useReconnectBackoff = false
}

// EnableReconnectBackoff restores exponential backoff for reconnection attempts.
func (c *Client) EnableReconnectBackoff() {
	c.reconnectMutex.Lock()
	defer c.reconnectMutex.Unlock()
	c.useReconnectBackoff = true
}

// EnableDynamicLocalAddress enables re-detecting the local bind address on reconnect.
// Useful when network interfaces change (e.g., VPN up/down).
func (c *Client) EnableDynamicLocalAddress() {
	c.reconnectMutex.Lock()
	c.dynamicLocal = true
	c.reconnectMutex.Unlock()
}

// DisableDynamicLocalAddress locks the client to the current local address on reconnect.
func (c *Client) DisableDynamicLocalAddress() {
	c.reconnectMutex.Lock()
	c.dynamicLocal = false
	c.reconnectMutex.Unlock()
}

// IsReconnecting returns true if the client is currently attempting to reconnect
func (c *Client) IsReconnecting() bool {
	c.reconnectMutex.RLock()
	defer c.reconnectMutex.RUnlock()
	return c.reconnecting
}

// SetInterceptor sets an interceptor for all FINS operations
// The interceptor will be called for every Read/Write operation
// Pass nil to disable interception
func (c *Client) SetInterceptor(interceptor Interceptor) {
	c.interceptorMutex.Lock()
	defer c.interceptorMutex.Unlock()
	c.interceptor = interceptor
}

// Use registers plugins (similar to gorm.Use).
// Plugins are initialized in order; duplicate names return an error.
func (c *Client) Use(plugins ...Plugin) error {
	return c.pm.use(c, plugins...)
}

// invoke calls the operation with interceptor if set, otherwise calls it directly
func (c *Client) invoke(ctx context.Context, info *InterceptorInfo, invoker Invoker) (interface{}, error) {
	c.interceptorMutex.RLock()
	interceptor := c.interceptor
	c.interceptorMutex.RUnlock()

	if interceptor != nil {
		return interceptor(&InterceptorCtx{
			ctx:     ctx,
			info:    info,
			invoker: invoker,
		})
	}

	return invoker(ctx)
}

// IsClosed returns true if the client has been closed
func (c *Client) IsClosed() bool {
	c.closeMutex.RLock()
	defer c.closeMutex.RUnlock()
	return c.closed
}

// triggerReconnect marks the client disconnected and kicks off reconnect if enabled.
func (c *Client) triggerReconnect(reason error) {
	if c.IsClosed() {
		return
	}

	c.reconnectMutex.RLock()
	enabled := c.autoReconnect
	already := c.reconnecting
	c.reconnectMutex.RUnlock()

	if !enabled || already {
		return
	}

	// Mark as disconnected so new operations wait.
	c.connectedMutex.Lock()
	c.connected = make(chan struct{})
	c.connectedMutex.Unlock()

	// Notify plugins of the disconnect reason.
	if hookErr := c.pm.notifyDisconnected(c, reason); hookErr != nil {
		select {
		case c.listenErr <- hookErr:
		default:
		}
	}

	go c.reconnect()
}

// Close closes the Omron FINS connection
func (c *Client) Close() error {
	c.closeMutex.Lock()
	if c.closed {
		c.closeMutex.Unlock()
		return nil
	}
	c.closed = true
	c.closeMutex.Unlock()

	close(c.done)
	var err error
	if c.tr != nil {
		err = c.tr.Close()
	}

	// Wait for listen loop to finish or timeout
	select {
	case <-c.listenErr:
	case <-time.After(CLOSE_TIMEOUT):
	}

	return err
}

// Shutdown gracefully shuts down the client, stopping any reconnection attempts
func (c *Client) Shutdown() error {
	// Disable auto-reconnect first
	c.DisableAutoReconnect()

	// Then close the connection
	return c.Close()
}

// waitForConnection waits for the connection to be ready, respecting context
func (c *Client) waitForConnection(ctx context.Context) error {
	if c.IsClosed() {
		return ClientClosedError{}
	}

	c.connectedMutex.Lock()
	connChan := c.connected
	c.connectedMutex.Unlock()

	select {
	case <-connChan:
		// Connection is ready
		return nil
	case <-ctx.Done():
		// Context expired
		return ctx.Err()
	case <-c.done:
		// Client is being closed
		return ClientClosedError{}
	}
}

// isConnected reports whether the connected channel is currently closed.
func (c *Client) isConnected() bool {
	c.connectedMutex.Lock()
	connChan := c.connected
	c.connectedMutex.Unlock()

	select {
	case <-connChan:
		return true
	default:
		return false
	}
}

// reconnect attempts to reconnect to the PLC with exponential backoff
func (c *Client) reconnect() error {
	c.reconnectMutex.Lock()
	if c.reconnecting {
		c.reconnectMutex.Unlock()
		return fmt.Errorf("already reconnecting")
	}
	c.reconnecting = true
	maxRetries := c.maxReconnect
	delay := c.reconnectDelay
	backoff := c.useReconnectBackoff
	if !backoff {
		delay = 0 // immediate retries when backoff is disabled
	}
	c.reconnectMutex.Unlock()

	defer func() {
		c.reconnectMutex.Lock()
		c.reconnecting = false
		c.reconnectMutex.Unlock()
	}()

	var lastErr error
	attempts := 0

	for {
		// Check if client is being closed
		if c.IsClosed() {
			return fmt.Errorf("client closed during reconnection")
		}

		// Check if auto-reconnect was disabled
		c.reconnectMutex.RLock()
		if !c.autoReconnect {
			c.reconnectMutex.RUnlock()
			return fmt.Errorf("auto-reconnect disabled")
		}
		c.reconnectMutex.RUnlock()

		// Check max retries (0 means infinite)
		if maxRetries > 0 && attempts >= maxRetries {
			return fmt.Errorf("max reconnection attempts (%d) reached: %w", maxRetries, lastErr)
		}

		attempts++

		// Wait before retry (exponential backoff)
		if attempts > 1 && delay > 0 {
			time.Sleep(delay)
			if backoff {
				// Exponential backoff with max cap
				delay *= 2
				if delay > MAX_RECONNECT_DELAY {
					delay = MAX_RECONNECT_DELAY
				}
			}
		}

		// Close the existing transport before re-dialing to free the local port.
		// Without this, re-dialing with the same local port will fail with EADDRINUSE,
		// leaving the client stuck until it is restarted.
		if c.tr != nil {
			_ = c.tr.Close()
			c.tr = nil
		}

		switch c.tpKind {
		case transportUDP:
			// Optionally refresh the local bind address (handles interface changes like VPN up/down).
			c.refreshLocalAddr()

			localAddr := c.getLocalAddr()
			tr, err := newUDPTransport(localAddr, c.remoteAddr)
			if err != nil {
				lastErr = err
				continue
			}

			// Update connection
			c.localAddrMutex.Lock()
			c.tr = tr
			if la, ok := tr.LocalAddr().(*net.UDPAddr); ok {
				c.localAddr = la
			}
			c.localAddrMutex.Unlock()

		case transportTCP:
			tr, err := newTCPTransport(context.Background(), c.getLocalTCPAddr(), c.remoteTCPAddr)
			if err != nil {
				lastErr = err
				continue
			}

			c.localAddrMutex.Lock()
			c.tr = tr
			if la, ok := tr.LocalAddr().(*net.TCPAddr); ok {
				c.localTCPAddr = la
			}
			c.localAddrMutex.Unlock()
		default:
			lastErr = fmt.Errorf("unknown transport kind")
			continue
		}

		// Create new connected channel and close it to signal connection is ready
		c.connectedMutex.Lock()
		c.connected = make(chan struct{})
		close(c.connected)
		c.connectedMutex.Unlock()

		// Use a fresh error channel for the new listener
		c.listenErr = make(chan error, ERROR_CHANNEL_BUFFER)

		// Restart listen loop
		go c.listenLoop()

		// Notify plugins
		if err := c.pm.notifyConnected(c); err != nil {
			return fmt.Errorf("plugin connect hook failed: %w", err)
		}

		return nil
	}
}

// ReadWords reads words from the PLC data area
func (c *Client) ReadWords(ctx context.Context, memoryArea byte, address uint16, readCount uint16) ([]uint16, error) {
	info := &InterceptorInfo{
		Operation:  OpReadWords,
		MemoryArea: memoryArea,
		Address:    address,
		Count:      readCount,
	}

	result, err := c.invoke(ctx, info, func(ctx context.Context) (interface{}, error) {
		return c.readWordsRaw(ctx, memoryArea, address, readCount)
	})

	if err != nil {
		return nil, err
	}
	return result.([]uint16), nil
}

// ReadBytes reads bytes from the PLC data area
func (c *Client) ReadBytes(ctx context.Context, memoryArea byte, address uint16, readCount uint16) ([]byte, error) {
	info := &InterceptorInfo{
		Operation:  OpReadBytes,
		MemoryArea: memoryArea,
		Address:    address,
		Count:      readCount,
	}

	result, err := c.invoke(ctx, info, func(ctx context.Context) (interface{}, error) {
		// Wait for connection to be ready
		if err := c.waitForConnection(ctx); err != nil {
			return nil, err
		}

		if checkIsWordMemoryArea(memoryArea) == false {
			return nil, IncompatibleMemoryAreaError{memoryArea}
		}
		command := readCommand(memAddr(memoryArea, address), readCount)
		r, e := c.sendCommand(ctx, command)
		e = checkResponse(r, e)
		if e != nil {
			return nil, e
		}

		return r.data, nil
	})

	if err != nil {
		return nil, err
	}
	return result.([]byte), nil
}

// ReadString reads a string from the PLC data area
func (c *Client) ReadString(ctx context.Context, memoryArea byte, address uint16, readCount uint16) (string, error) {
	data, e := c.ReadBytes(ctx, memoryArea, address, readCount)
	if e != nil {
		return "", e
	}
	n := bytes.IndexByte(data, 0)
	if n == -1 {
		n = len(data)
	}
	return string(data[:n]), nil
}

// ReadBits reads bits from the PLC data area
func (c *Client) ReadBits(ctx context.Context, memoryArea byte, address uint16, bitOffset byte, readCount uint16) ([]bool, error) {
	info := &InterceptorInfo{
		Operation:  OpReadBits,
		MemoryArea: memoryArea,
		Address:    address,
		BitOffset:  bitOffset,
		Count:      readCount,
	}

	result, err := c.invoke(ctx, info, func(ctx context.Context) (interface{}, error) {
		// Wait for connection to be ready
		if err := c.waitForConnection(ctx); err != nil {
			return nil, err
		}

		if checkIsBitMemoryArea(memoryArea) == false {
			return nil, IncompatibleMemoryAreaError{memoryArea}
		}
		command := readCommand(memAddrWithBitOffset(memoryArea, address, bitOffset), readCount)
		r, e := c.sendCommand(ctx, command)
		e = checkResponse(r, e)
		if e != nil {
			return nil, e
		}

		data := make([]bool, readCount)
		for i := 0; i < int(readCount); i++ {
			data[i] = r.data[i]&0x01 > 0
		}

		return data, nil
	})

	if err != nil {
		return nil, err
	}
	return result.([]bool), nil
}

// ReadClock reads the PLC clock
func (c *Client) ReadClock(ctx context.Context) (*time.Time, error) {
	// Wait for connection to be ready
	if err := c.waitForConnection(ctx); err != nil {
		return nil, err
	}

	r, e := c.sendCommand(ctx, clockReadCommand())
	e = checkResponse(r, e)
	if e != nil {
		return nil, e
	}
	year, _ := decodeBCD(r.data[0:1])
	if year < 50 {
		year += 2000
	} else {
		year += 1900
	}
	month, _ := decodeBCD(r.data[1:2])
	day, _ := decodeBCD(r.data[2:3])
	hour, _ := decodeBCD(r.data[3:4])
	minute, _ := decodeBCD(r.data[4:5])
	second, _ := decodeBCD(r.data[5:6])

	t := time.Date(
		int(year), time.Month(month), int(day), int(hour), int(minute), int(second),
		0, // nanosecond
		time.Local,
	)
	return &t, nil
}

// WriteWords writes words to the PLC data area
func (c *Client) WriteWords(ctx context.Context, memoryArea byte, address uint16, data []uint16) error {
	info := &InterceptorInfo{
		Operation:  OpWriteWords,
		MemoryArea: memoryArea,
		Address:    address,
		Data:       data,
	}

	_, err := c.invoke(ctx, info, func(ctx context.Context) (interface{}, error) {
		// Wait for connection to be ready
		if err := c.waitForConnection(ctx); err != nil {
			return nil, err
		}

		if checkIsWordMemoryArea(memoryArea) == false {
			return nil, IncompatibleMemoryAreaError{memoryArea}
		}
		l := uint16(len(data))
		bts := make([]byte, 2*l)
		for i := 0; i < int(l); i++ {
			c.byteOrder.PutUint16(bts[i*2:i*2+2], data[i])
		}
		command := writeCommand(memAddr(memoryArea, address), l, bts)

		return nil, checkResponse(c.sendCommand(ctx, command))
	})

	return err
}

// WriteString writes a string to the PLC data area
func (c *Client) WriteString(ctx context.Context, memoryArea byte, address uint16, s string) error {
	// Wait for connection to be ready
	if err := c.waitForConnection(ctx); err != nil {
		return err
	}

	if checkIsWordMemoryArea(memoryArea) == false {
		return IncompatibleMemoryAreaError{memoryArea}
	}
	bts := make([]byte, 2*len(s))
	copy(bts, s)

	command := writeCommand(memAddr(memoryArea, address), uint16((len(s)+1)/2), bts) //TODO: test on real PLC

	return checkResponse(c.sendCommand(ctx, command))
}

// WriteBytes writes bytes array to the PLC data area
func (c *Client) WriteBytes(ctx context.Context, memoryArea byte, address uint16, b []byte) error {
	// Wait for connection to be ready
	if err := c.waitForConnection(ctx); err != nil {
		return err
	}

	if checkIsWordMemoryArea(memoryArea) == false {
		return IncompatibleMemoryAreaError{memoryArea}
	}
	command := writeCommand(memAddr(memoryArea, address), uint16(len(b)), b)
	return checkResponse(c.sendCommand(ctx, command))
}

// WriteBits writes bits to the PLC data area
func (c *Client) WriteBits(ctx context.Context, memoryArea byte, address uint16, bitOffset byte, data []bool) error {
	info := &InterceptorInfo{
		Operation:  OpWriteBits,
		MemoryArea: memoryArea,
		Address:    address,
		BitOffset:  bitOffset,
		Data:       data,
	}

	_, err := c.invoke(ctx, info, func(ctx context.Context) (interface{}, error) {
		// Wait for connection to be ready
		if err := c.waitForConnection(ctx); err != nil {
			return nil, err
		}

		if checkIsBitMemoryArea(memoryArea) == false {
			return nil, IncompatibleMemoryAreaError{memoryArea}
		}
		l := uint16(len(data))
		bts := make([]byte, l)
		var d byte
		for i := 0; i < int(l); i++ {
			if data[i] {
				d = 0x01
			} else {
				d = 0x00
			}
			bts[i] = d
		}
		command := writeCommand(memAddrWithBitOffset(memoryArea, address, bitOffset), l, bts)

		return nil, checkResponse(c.sendCommand(ctx, command))
	})

	return err
}

// SetBit sets a bit in the PLC data area
func (c *Client) SetBit(ctx context.Context, memoryArea byte, address uint16, bitOffset byte) error {
	return c.bitTwiddle(ctx, memoryArea, address, bitOffset, 0x01)
}

// ResetBit resets a bit in the PLC data area
func (c *Client) ResetBit(ctx context.Context, memoryArea byte, address uint16, bitOffset byte) error {
	return c.bitTwiddle(ctx, memoryArea, address, bitOffset, 0x00)
}

// ToggleBit toggles a bit in the PLC data area
func (c *Client) ToggleBit(ctx context.Context, memoryArea byte, address uint16, bitOffset byte) error {
	b, e := c.ReadBits(ctx, memoryArea, address, bitOffset, 1)
	if e != nil {
		return e
	}
	var t byte
	if b[0] {
		t = 0x00
	} else {
		t = 0x01
	}
	return c.bitTwiddle(ctx, memoryArea, address, bitOffset, t)
}

func (c *Client) bitTwiddle(ctx context.Context, memoryArea byte, address uint16, bitOffset byte, value byte) error {
	// Wait for connection to be ready
	if err := c.waitForConnection(ctx); err != nil {
		return err
	}

	if checkIsBitMemoryArea(memoryArea) == false {
		return IncompatibleMemoryAreaError{memoryArea}
	}
	mem := memoryAddress{memoryArea, address, bitOffset}
	command := writeCommand(mem, 1, []byte{value})

	return checkResponse(c.sendCommand(ctx, command))
}

func checkResponse(r *response, e error) error {
	if e != nil {
		return e
	}
	if r.endCode != EndCodeNormalCompletion {
		return EndCodeError{EndCode: r.endCode}
	}
	return nil
}

// readWordsRaw performs the read without invoking interceptors.
func (c *Client) readWordsRaw(ctx context.Context, memoryArea byte, address uint16, readCount uint16) ([]uint16, error) {
	// Wait for connection to be ready
	if err := c.waitForConnection(ctx); err != nil {
		return nil, err
	}

	if checkIsWordMemoryArea(memoryArea) == false {
		return nil, IncompatibleMemoryAreaError{memoryArea}
	}
	command := readCommand(memAddr(memoryArea, address), readCount)
	r, e := c.sendCommand(ctx, command)
	e = checkResponse(r, e)
	if e != nil {
		return nil, e
	}

	data := make([]uint16, readCount)
	for i := 0; i < int(readCount); i++ {
		data[i] = c.byteOrder.Uint16(r.data[i*2 : i*2+2])
	}

	return data, nil
}

func (c *Client) nextHeader() *Header {
	sid := c.incrementSid()
	header := defaultCommandHeader(c.src, c.dst, sid)
	return &header
}

func (c *Client) incrementSid() byte {
	c.sidMutex.Lock()
	defer c.sidMutex.Unlock()
	c.sid++
	sid := c.sid

	// Thread-safe channel creation
	c.respMutex.Lock()
	c.resp[sid] = make(chan response, RESPONSE_CHANNEL_BUFFER)
	c.respMutex.Unlock()

	return sid
}

func (c *Client) sendCommand(ctx context.Context, command []byte) (*response, error) {
	// Check context before starting
	if err := ctx.Err(); err != nil {
		return nil, err
	}
	if c.tr == nil {
		return nil, fmt.Errorf("transport not initialized")
	}

	header := c.nextHeader()
	bts := encodeHeader(*header)
	bts = append(bts, command...)
	if err := c.tr.Send(ctx, bts); err != nil {
		return nil, err
	}

	// Get the response channel safely
	c.respMutex.RLock()
	respChan := c.resp[header.serviceID]
	c.respMutex.RUnlock()

	// if response timeout is zero, block indefinitely (but still respect context)
	if c.responseTimeoutMs > 0 {
		select {
		case resp := <-respChan:
			return &resp, nil
		case <-time.After(c.responseTimeoutMs * time.Millisecond):
			err := ResponseTimeoutError{c.responseTimeoutMs}
			c.triggerReconnect(err)
			return nil, err
		case <-ctx.Done():
			return nil, ctx.Err()
		case <-c.done:
			return nil, ClientClosedError{}
		}
	} else {
		select {
		case resp := <-respChan:
			return &resp, nil
		case <-ctx.Done():
			return nil, ctx.Err()
		case <-c.done:
			return nil, ClientClosedError{}
		}
	}
}

func (c *Client) listenLoop() {
	errChan := c.listenErr
	defer close(errChan)

	if c.tr == nil {
		errChan <- fmt.Errorf("transport not initialized")
		return
	}

	for {
		// Set read deadline for graceful shutdown
		c.closeMutex.RLock()
		timeout := c.readTimeout
		c.closeMutex.RUnlock()

		readCtx := context.Background()
		var cancel context.CancelFunc
		if timeout > 0 {
			readCtx, cancel = context.WithTimeout(context.Background(), timeout)
		}

		payload, err := c.tr.Recv(readCtx)
		if cancel != nil {
			cancel()
		}
		if err != nil {
			// Check if connection is closed by user
			select {
			case <-c.done:
				// Expected closure, exit gracefully
				return
			default:
				// Mark as disconnected
				c.connectedMutex.Lock()
				c.connected = make(chan struct{}) // Create new channel (not closed = not connected)
				c.connectedMutex.Unlock()

				// Notify plugins about the disconnect
				if hookErr := c.pm.notifyDisconnected(c, err); hookErr != nil {
					errChan <- hookErr
				}

				// Unexpected error - check if auto-reconnect is enabled
				c.reconnectMutex.RLock()
				shouldReconnect := c.autoReconnect
				c.reconnectMutex.RUnlock()

				if shouldReconnect && !c.IsClosed() {
					// Attempt to reconnect
					reconnectErr := c.reconnect()
					if reconnectErr != nil {
						// Reconnection failed, send error
						errChan <- fmt.Errorf("reconnection failed: %w (original error: %v)", reconnectErr, err)
						return
					}
					// Successfully reconnected, exit this listen loop
					// (new listen loop started in reconnect())
					return
				}

				// Auto-reconnect disabled or client closed
				if !c.IsClosed() {
					errChan <- fmt.Errorf("listen loop error: %w", err)
				}
				return
			}
		}

		if len(payload) > 0 {
			ans := decodeResponse(payload)
			// Thread-safe channel send
			c.respMutex.RLock()
			if int(ans.header.serviceID) < len(c.resp) && c.resp[ans.header.serviceID] != nil {
				// Non-blocking send to prevent deadlock
				select {
				case c.resp[ans.header.serviceID] <- ans:
				default:
					// Channel full or no receiver, skip
				}
			}
			c.respMutex.RUnlock()
		}
	}
}

func checkIsWordMemoryArea(memoryArea byte) bool {
	if memoryArea == MemoryAreaDMWord ||
		memoryArea == MemoryAreaARWord ||
		memoryArea == MemoryAreaHRWord ||
		memoryArea == MemoryAreaWRWord {
		return true
	}
	return false
}

func checkIsBitMemoryArea(memoryArea byte) bool {
	if memoryArea == MemoryAreaDMBit ||
		memoryArea == MemoryAreaARBit ||
		memoryArea == MemoryAreaHRBit ||
		memoryArea == MemoryAreaWRBit {
		return true
	}
	return false
}

func (c *Client) refreshLocalAddr() {
	c.reconnectMutex.RLock()
	enabled := c.dynamicLocal
	c.reconnectMutex.RUnlock()
	if !enabled {
		return
	}
	testConn, err := net.DialUDP("udp", nil, c.remoteAddr)
	if err != nil {
		return
	}
	if la, ok := testConn.LocalAddr().(*net.UDPAddr); ok {
		c.localAddrMutex.Lock()
		c.localAddr = la
		c.localAddrMutex.Unlock()
	}
	testConn.Close()
}

func (c *Client) getLocalAddr() *net.UDPAddr {
	c.localAddrMutex.RLock()
	defer c.localAddrMutex.RUnlock()
	return c.localAddr
}

func (c *Client) getLocalTCPAddr() *net.TCPAddr {
	c.localAddrMutex.RLock()
	defer c.localAddrMutex.RUnlock()
	return c.localTCPAddr
}

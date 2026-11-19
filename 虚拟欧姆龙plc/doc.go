/*
package gofins implements the Omron FINS (Factory Interface Network Service) protocol
for communication with Omron PLCs over UDP.

This is an improved version of https://github.com/l1va/gofins with critical bug fixes,
context support, and enhanced reliability for production use.

# Features

  - Full FINS protocol support for reading/writing PLC memory areas
  - Context-based cancellation and timeout control
  - Thread-safe operations with proper synchronization
  - Graceful shutdown with configurable timeouts
  - PLC simulator for testing

# Quick Start

Basic usage example:

	import (
		"context"
		"log"
		"time"
		"github.com/bronystylecrazy/fins"
	)

	func main() {
		// Create addresses
		clientAddr := fins.NewAddress("", 9600, 0, 2, 0)
		plcAddr := fins.NewAddress("192.168.1.100", 9600, 0, 1, 0)

		// Create client
		client, err := fins.NewUDPClient(clientAddr, plcAddr)
		if err != nil {
			log.Fatal(err)
		}
		defer client.Close()

		// Create context with timeout
		ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
		defer cancel()

		// Read words from PLC
		data, err := client.ReadWords(ctx, fins.MemoryAreaDMWord, 100, 5)
		if err != nil {
			log.Printf("Read error: %v", err)
			return
		}
		log.Printf("Data: %v", data)

		// Write words to PLC
		err = client.WriteWords(ctx, fins.MemoryAreaDMWord, 100, []uint16{1, 2, 3})
		if err != nil {
			log.Printf("Write error: %v", err)
		}
	}

# Auto-Reconnect Support

The client supports automatic reconnection with exponential backoff:

	client, _ := fins.NewUDPClient(clientAddr, plcAddr)

	// Enable auto-reconnect with max 5 retries and 1s initial delay
	client.EnableAutoReconnect(5, 1*time.Second)

	// Check if reconnecting
	if client.IsReconnecting() {
		log.Println("Reconnecting...")
	}

	// Graceful shutdown (stops reconnection attempts)
	defer client.Shutdown()

When auto-reconnect is enabled, Read/Write operations will wait for the connection
to be restored. Operations respect context timeouts - if the context expires before
reconnection completes, the operation returns with context.DeadlineExceeded:

	client.EnableAutoReconnect(5, 1*time.Second)

	// This waits up to 10 seconds for connection
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()

	data, err := client.ReadWords(ctx, fins.MemoryAreaDMWord, 100, 5)
	if err == context.DeadlineExceeded {
		log.Println("Operation timed out waiting for connection")
	}

# Interceptors

Interceptors allow you to add custom logic around all FINS operations.
Use them for logging, metrics, tracing, validation, retries, and more:

	// Signature: func(ic *fins.InterceptorCtx) (interface{}, error)
	// - ic.Context() gives you the request context
	// - ic.Info() returns metadata (operation, address, data, etc.)
	// - ic.Invoke(ctx) calls the next handler (ctx nil = reuse stored context)

	// Logging interceptor (zap)
	logger, _ := zap.NewProduction()
	client.SetInterceptor(fins.LoggingInterceptor(logger))

	// Metrics collection
	metrics := fins.NewMetricsCollector()
	client.SetInterceptor(metrics.Interceptor())

	// Custom interceptor
	client.SetInterceptor(func(ic *fins.InterceptorCtx) (interface{}, error) {
		start := time.Now()
		log.Printf("Starting %s", ic.Info().Operation)
		result, err := ic.Invoke(nil)
		log.Printf("Finished %s in %v", ic.Info().Operation, time.Since(start))
		return result, err
	})

	// Chain multiple interceptors
	client.SetInterceptor(fins.ChainInterceptors(
		fins.LoggingInterceptor(logger),
		metrics.Interceptor(),
		fins.ValidationInterceptor(),
	))

Plugins (gorm-style) can register hooks in one place:

	type loggingPlugin struct{}

	func (loggingPlugin) Name() string { return "logging" }
	func (loggingPlugin) Initialize(c *fins.Client) error {
		c.SetInterceptor(fins.LoggingInterceptor(logger))
		return nil
	}

	client.Use(&loggingPlugin{}) // duplicate names error

# Context Support

All read/write operations accept a context.Context parameter for:
  - Timeout control at operation level
  - Request cancellation
  - Request tracing

Example with timeout:

	ctx, cancel := context.WithTimeout(context.Background(), 500*time.Millisecond)
	defer cancel()

	data, err := client.ReadWords(ctx, fins.MemoryAreaDMWord, 100, 5)
	if err == context.DeadlineExceeded {
		log.Println("Operation timed out")
	}

Example with cancellation:

	ctx, cancel := context.WithCancel(context.Background())

	go func() {
		time.Sleep(100 * time.Millisecond)
		cancel() // Cancel the operation
	}()

	_, err := client.ReadWords(ctx, fins.MemoryAreaDMWord, 100, 5)
	if err == context.Canceled {
		log.Println("Operation was cancelled")
	}

# Memory Areas

The package supports various PLC memory areas:

  - MemoryAreaDMWord / MemoryAreaDMBit - Data Memory (DM)
  - MemoryAreaWRWord / MemoryAreaWRBit - Work Area (WR)
  - MemoryAreaHRWord / MemoryAreaHRBit - Holding Area (HR)
  - MemoryAreaARWord / MemoryAreaARBit - Auxiliary Area (AR)
  - MemoryAreaCIOWord / MemoryAreaCIOBit - CIO Area
  - And more...

# Operations

Reading from PLC:

	// Read words
	words, err := client.ReadWords(ctx, fins.MemoryAreaDMWord, address, count)

	// Read bytes
	bytes, err := client.ReadBytes(ctx, fins.MemoryAreaDMWord, address, count)

	// Read bits
	bits, err := client.ReadBits(ctx, fins.MemoryAreaDMBit, address, bitOffset, count)

	// Read string
	str, err := client.ReadString(ctx, fins.MemoryAreaDMWord, address, count)

	// Read PLC clock
	clockTime, err := client.ReadClock(ctx)

Writing to PLC:

	// Write words
	err := client.WriteWords(ctx, fins.MemoryAreaDMWord, address, []uint16{1, 2, 3})

	// Write bytes
	err := client.WriteBytes(ctx, fins.MemoryAreaDMWord, address, []byte{0x01, 0x02})

	// Write bits
	err := client.WriteBits(ctx, fins.MemoryAreaDMBit, address, bitOffset, []bool{true, false})

	// Set/Reset/Toggle single bit
	err := client.SetBit(ctx, fins.MemoryAreaDMBit, address, bitOffset)
	err := client.ResetBit(ctx, fins.MemoryAreaDMBit, address, bitOffset)
	err := client.ToggleBit(ctx, fins.MemoryAreaDMBit, address, bitOffset)

# Error Handling

The package provides specific error types:

  - ClientClosedError - Operations on closed client
  - ResponseTimeoutError - Response timeout exceeded
  - IncompatibleMemoryAreaError - Wrong memory area type for operation
  - BCDOverflowError / BCDBadDigitError - BCD encoding/decoding errors

Errors from the listen loop are sent to a channel:

	client, _ := fins.NewUDPClient(localAddr, plcAddr)

	go func() {
		if err := <-client.Err(); err != nil {
			log.Printf("Client error: %v", err)
		}
	}()

# Thread Safety

The Client is thread-safe and all public methods can be called concurrently.
Internal state is protected by mutexes:
  - respMutex - Protects response channel slice
  - sidMutex - Protects service ID incrementation
  - closeMutex - Protects closed flag

# Configuration

Client can be configured with:

	// Set byte order (default: BigEndian)
	client.SetByteOrder(binary.LittleEndian)

	// Set response timeout in milliseconds (default: 20ms)
	client.SetTimeoutMs(100)

	// Set UDP read timeout (default: 5s)
	client.SetReadTimeout(10 * time.Second)

# Testing with PLC Simulator

The package includes a PLC simulator for testing:

	// Create simulator
	plcAddr := fins.NewAddress("127.0.0.1", 9600, 0, 10, 0)
	simulator, err := fins.NewPLCSimulator(plcAddr)
	if err != nil {
		log.Fatal(err)
	}
	defer simulator.Close()

	// Monitor simulator errors
	go func() {
		if err := <-simulator.Err(); err != nil {
			log.Printf("Simulator error: %v", err)
		}
	}()

	// Now create client and test
	clientAddr := fins.NewAddress("127.0.0.1", 9601, 0, 2, 0)
	client, _ := fins.NewUDPClient(clientAddr, plcAddr)

# Migration from gofins

Breaking change: All operations now require context.Context as first parameter.

	// Before (gofins):
	data, err := client.ReadWords(memoryArea, address, count)

	// After (this package):
	ctx := context.Background()
	data, err := client.ReadWords(ctx, memoryArea, address, count)

# Improvements over gofins

  - Fixed bufio.Reader resource leak
  - Fixed race conditions in response handling
  - Replaced log.Fatal with proper error channels
  - Fixed iota bug in header constants
  - Added graceful shutdown with timeouts
  - Added context support for all operations
  - Added auto-reconnect with exponential backoff
  - Full thread-safety with documented guarantees
  - Better test coverage (66.4%)
*/
package gofins

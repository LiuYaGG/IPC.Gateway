# GOFINS - Improved Omron FINS Protocol Implementation

This is an improved version of [gofins](https://github.com/l1va/gofins) with critical bug fixes, context support, and various enhancements that make it production-ready.

## Features
- Production-ready Omron FINS client with context support, byte-order control, and auto-reconnect
- Built-in PLC simulator (UDP/TCP) plus an in-process inline client for zero-network tests
- Interceptors and plugin hooks for logging, metrics, tracing, validation, and retries
- Graceful shutdown, error channels instead of process exits, and comprehensive tests
- Helpers for reading/writing words, bytes, bits, strings, and PLC clock

## Installation

```bash
go get github.com/bronystylecrazy/gofins@latest
```

## Why This Version Exists

The original [gofins](https://github.com/l1va/gofins) had several critical issues that made it unsuitable for production use:
- Memory leaks that could crash long-running applications
- Race conditions in concurrent operations
- Used `log.Fatal()` which would kill the entire application on errors
- Bugs in protocol constants
- No way to gracefully shutdown

This version fixes all of those issues and adds modern Go patterns like context support.

## Quick Start

```go
package main

import (
    "context"
    "log"
    "time"
    fins "github.com/bronystylecrazy/gofins"
)

func main() {
    clientAddr := fins.NewAddress("", 9600, 0, 2, 0)
    plcAddr := fins.NewAddress("192.168.1.100", 9600, 0, 1, 0)

    client, err := fins.NewUDPClient(clientAddr, plcAddr)
    if err != nil {
        log.Fatal(err)
    }
    defer client.Shutdown()

    // Monitor for errors in a separate goroutine
    go func() {
        if err := <-client.Err(); err != nil {
            log.Printf("Client error: %v", err)
        }
    }()

    // Create context with timeout
    ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
    defer cancel()

    // Read words from PLC
    data, err := client.ReadWords(ctx, fins.MemoryAreaDMWord, 100, 5)
    if err != nil {
        if err == context.DeadlineExceeded {
            log.Printf("Read timeout")
        } else {
            log.Printf("Read error: %v", err)
        }
        return
    }

    log.Printf("Read data: %v", data)

    // Write words to PLC
    err = client.WriteWords(ctx, fins.MemoryAreaDMWord, 100, []uint16{1, 2, 3})
    if err != nil {
        log.Printf("Write error: %v", err)
    }
}
```

## Examples

Run the built-in PLC simulator with configurable addressing:

```bash
go run ./examples/server \
  -host 0.0.0.0 \
  -port 9600 \
  -network 0 \
  -node 10 \
  -unit 0
```

Available flags:
- `-host` host/IP to bind (default `127.0.0.1`)
- `-port` port for the PLC simulator (default `9600`)
- `-network` FINS network number (default `0`)
- `-node` FINS node address (default `10`)
- `-unit` FINS unit address (default `0`)
- `-tcp` serve FINS over TCP instead of UDP (default `false`)

For in-process tests or tools that just need the simulator's memory without network I/O, use the inline client:

```go
plcAddr := fins.NewAddress("127.0.0.1", 9600, 0, 10, 0)
sim, _ := fins.NewPLCSimulator(plcAddr)
defer sim.Close()

inline := sim.InlineClient()
inline.WriteWords(context.Background(), fins.MemoryAreaDMWord, 100, []uint16{1, 2, 3})
data, _ := inline.ReadWords(context.Background(), fins.MemoryAreaDMWord, 100, 3)
```
The inline client implements the same `FINSClient` interface but operates directly on simulator memory; interceptors work, but plugins are only available on the networked client.

Install and control the simulator as a service (cross-platform via `github.com/kardianos/service`):

```bash
# Install with current flags baked into the service
go run ./examples/server install -host 0.0.0.0 -port 9600 -node 10
# Start/stop/restart/uninstall
go run ./examples/server start
go run ./examples/server stop
go run ./examples/server restart
go run ./examples/server uninstall

```

Chat-style interactive client (auto-detects local host/port if unset):

```bash
go run ./examples/client \
  -remote-host 127.0.0.1 \
  -remote-port 9600 \
  -remote-node 10 \
  -local-node 2 \
  -resp-timeout-ms 1000 \
  -auto-reconnect \
  -auto-reconnect-delay 1s

# One-shot command (no prompt)
go run ./examples/client \
  -remote-host 127.0.0.1 \
  -remote-port 9600 \
  -remote-node 10 \
  -exec "readwords dm 100 3" \
  -format json \
  -quiet \
  -resp-timeout-ms 1000

# Loop a single command every 500ms
go run ./examples/client \
  -remote-host 127.0.0.1 \
  -remote-port 9600 \
  -remote-node 10 \
  -exec "readwords dm 100 3" \
  -exec-interval 500ms
``` 

Add `-tcp` to talk FINS/TCP instead of UDP (useful with the simulator's `-tcp` flag).
Use `-no-reconnect-backoff` to retry immediately (delay=0) instead of exponential backoff; combine with `-auto-reconnect-delay` if you want a fixed sleep instead.

Once running, try commands such as:
- `readwords dm 100 3`
- `writewords dm 100 1 2 3`
- `readbits dm 100 0 8`
- `writebits dm 100 0 1 0 1 1`
- `clock`

Interactive client uses `liner` for readline-style editing and history (arrow keys, Ctrl+R/Emacs keys). Use `-format text|json` and `-quiet` to control output formatting/noise.

### TCP transport

FINS/TCP is supported alongside UDP. Use the TCP-specific helpers when you need stream transport (e.g., traversing firewalls/NAT that block UDP):

```go
clientAddr := fins.NewTCPAddress("", 9600, 0, 2, 0)          // local TCP bind (empty = OS chooses)
plcAddr := fins.NewTCPAddress("192.168.1.100", 9600, 0, 1, 0) // remote PLC over TCP

client, err := fins.NewTCPClient(clientAddr, plcAddr)
if err != nil {
    log.Fatal(err)
}
defer client.Shutdown()
```

For testing, enable TCP on the built-in simulator: `fins.NewPLCSimulator(plcAddr, fins.WithTCPTransport())` mirrors the UDP simulator but speaks FINS/TCP (handshake + framed messages).

## Critical Fixes

### 1. Fixed bufio.Reader Resource Leak

The original code was creating a new `bufio.Reader` on every read iteration, causing memory leaks that would eventually crash long-running applications.

**Before:**
```go
for {
    buf := make([]byte, 2048)
    n, err := bufio.NewReader(c.conn).Read(buf) // New reader each time!
}
```

**After:**
```go
reader := bufio.NewReader(c.conn)
buf := make([]byte, READ_BUFFER_SIZE)
for {
    n, err := reader.Read(buf) // Reuse reader
}
```

### 2. Fixed Race Condition in Response Channel Access

The `resp` slice was being accessed from multiple goroutines without any synchronization, causing data races and potential panics.

**Before:**
```go
c.resp[sid] = make(chan response) // No lock!
// ...
c.resp[ans.header.serviceID] <- ans // No lock!
```

**After:**
```go
c.respMutex.Lock()
c.resp[sid] = make(chan response, 1)
c.respMutex.Unlock()
// ...
c.respMutex.RLock()
if c.resp[ans.header.serviceID] != nil {
    c.resp[ans.header.serviceID] <- ans
}
c.respMutex.RUnlock()
```

### 3. Replaced log.Fatal with Proper Error Handling

Using `log.Fatal()` in a library is a terrible idea because it terminates the entire application. Now errors are sent through channels that callers can monitor.

**Before:**
```go
if err != nil {
    if !c.closed {
        log.Fatal(err) // Kills entire application!
    }
}
```

**After:**
```go
if err != nil {
    if !c.IsClosed() {
        c.listenErr <- fmt.Errorf("listen loop error: %w", err)
    }
    return
}

// Usage:
client, _ := fins.NewUDPClient(localAddr, plcAddr)
go func() {
    if err := <-client.Err(); err != nil {
        log.Printf("Client error: %v", err)
    }
}()
```

### 4. Fixed iota Bug in Header Constants

Both `MessageTypeCommand` and `MessageTypeResponse` had the same value (0) due to a redundant `iota` keyword.

**Before:**
```go
const (
    MessageTypeCommand uint8 = iota  // 0
    MessageTypeResponse uint8 = iota // 0 (BUG!)
)
```

**After:**
```go
const (
    MessageTypeCommand uint8 = iota  // 0
    MessageTypeResponse              // 1 (correct)
)
```

### 5. Added Read Timeout and Graceful Shutdown

The listen loop could block forever during shutdown because UDP read operations don't have a default timeout.

**Solution:**
```go
client.SetReadTimeout(5 * time.Second)

// Graceful shutdown with done channel
select {
case <-c.done:
    return // Clean exit
default:
    // Continue processing
}
```

## Auto-Reconnect Support

The client now supports automatic reconnection when the connection to the PLC fails or times out. This is especially useful for production environments where network issues can occur.

### Enabling Auto-Reconnect

```go
client, err := fins.NewUDPClient(clientAddr, plcAddr)
if err != nil {
    log.Fatal(err)
}

// Enable auto-reconnect with 5 max retries and 1 second initial delay
// The delay uses exponential backoff (1s, 2s, 4s, 8s, 16s, max 30s)
client.EnableAutoReconnect(5, 1*time.Second)

// For infinite retries, use 0
client.EnableAutoReconnect(0, 1*time.Second)

// Check if client is currently reconnecting
if client.IsReconnecting() {
    log.Println("Client is reconnecting...")
}

// Disable auto-reconnect
client.DisableAutoReconnect()

// Graceful shutdown (stops reconnection attempts and closes connection)
client.Shutdown()
```

### Monitoring Reconnection Events

```go
go func() {
    for err := range client.Err() {
        if err != nil {
            log.Printf("Client error: %v", err)
            // Check if reconnecting
            if client.IsReconnecting() {
                log.Println("Attempting to reconnect...")
            }
        }
    }
}()
```

### How Operations Behave During Reconnection

When auto-reconnect is enabled and the connection fails:

1. **Read/Write operations will wait** for the connection to be restored
2. Operations respect the **context timeout** - if the context expires before reconnection completes, the operation returns with `context.DeadlineExceeded`
3. If reconnection fails (max retries reached), operations will return an error

```go
client.EnableAutoReconnect(5, 1*time.Second)

// This operation will wait up to 10 seconds for connection to be restored
ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
defer cancel()

data, err := client.ReadWords(ctx, fins.MemoryAreaDMWord, 100, 5)
if err == context.DeadlineExceeded {
    log.Println("Operation timed out waiting for connection")
} else if err != nil {
    log.Printf("Read failed: %v", err)
} else {
    log.Printf("Read successful: %v", data)
}
```

## Interceptors

Interceptors allow you to add custom logic around all FINS operations. Similar to gRPC unary interceptors, you can use them for logging, metrics, tracing, validation, retries, and more.

Interceptor signature: `func(ic *fins.InterceptorCtx) (interface{}, error)`. Access the request context via `ic.Context()`, metadata via `ic.Info()`, and call the next handler with `ic.Invoke(ctx)` (pass `nil` to reuse the stored context).

### Basic Logging Interceptor

```go
client, _ := fins.NewUDPClient(clientAddr, plcAddr)

// Add logging interceptor
logger, _ := zap.NewProduction()
client.SetInterceptor(fins.LoggingInterceptor(logger))

// All operations will now be logged
data, _ := client.ReadWords(ctx, fins.MemoryAreaDMWord, 100, 5)
// Logs (structured):
//  INFO    FINS    starting    {"operation": "ReadWords", "area": "0x82", "address": 100}
//  INFO    FINS    completed   {"operation": "ReadWords", "duration": "5ms"}
```

### Metrics Collection

```go
// Create metrics collector
metrics := fins.NewMetricsCollector()
client.SetInterceptor(metrics.Interceptor())

// Perform operations...
client.ReadWords(ctx, fins.MemoryAreaDMWord, 100, 5)
client.WriteWords(ctx, fins.MemoryAreaDMWord, 200, []uint16{1, 2, 3})

// Get statistics
count, errors, avgDuration := metrics.GetStats(fins.OpReadWords)
log.Printf("ReadWords: %d calls, %d errors, avg duration: %v", count, errors, avgDuration)
```

### Custom Interceptor

```go
// Create custom interceptor
customInterceptor := func(ic *fins.InterceptorCtx) (interface{}, error) {
    start := time.Now()
    log.Printf("Starting %s at address %d", ic.Info().Operation, ic.Info().Address)

    // Call the actual operation
    result, err := ic.Invoke(nil)

    duration := time.Since(start)
    if err != nil {
        log.Printf("Failed %s: %v (took %v)", ic.Info().Operation, err, duration)
    } else {
        log.Printf("Completed %s (took %v)", ic.Info().Operation, duration)
    }

    return result, err
}

client.SetInterceptor(customInterceptor)
```

### Chaining Multiple Interceptors

```go
// Combine logging, metrics, and tracing
client.SetInterceptor(fins.ChainInterceptors(
    fins.LoggingInterceptor(logger),
    metrics.Interceptor(),
    fins.TracingInterceptor("traceID"),
))
```

### Plugins (Use)

Register reusable hooks similar to `gorm.Use`:

```go
type loggingPlugin struct{}

func (loggingPlugin) Name() string { return "logging" }
func (loggingPlugin) Initialize(c *fins.Client) error {
    logger, _ := zap.NewProduction()
    c.SetInterceptor(fins.LoggingInterceptor(logger))
    return nil
}

client.Use(&loggingPlugin{}) // duplicate names return an error
```

Connection lifecycle hooks are also available via `ConnectionPlugin`:

```go
type lifecyclePlugin struct{}

func (lifecyclePlugin) Name() string { return "lifecycle" }
func (lifecyclePlugin) Initialize(c *fins.Client) error { return nil }

func (lifecyclePlugin) OnConnected(c *fins.Client) error {
    log.Println("connected to PLC")
    return nil
}

func (lifecyclePlugin) OnDisconnected(c *fins.Client, err error) error {
    log.Printf("lost connection: %v", err)
    return nil
}

client.Use(&lifecyclePlugin{})
```

Built-in connection watchdog plugin for metrics and structured events:

```go
watchdog := fins.NewConnectionWatchdog(8) // buffered event channel
client.Use(watchdog)

go func() {
    for evt := range watchdog.Events() {
        log.Printf("conn=%s downtime=%v err=%v", evt.Type, evt.Downtime, evt.Err)
    }
}()

stats := watchdog.Stats()
log.Printf("connected=%v total_downtime=%v last_err=%v", stats.Connected, stats.TotalDowntime, stats.LastDisconnectErr)
```

Bulk read coalescer to batch adjacent `ReadWords` requests:

```go
// Coalesce reads arriving within 5ms, up to 32 contiguous words.
coalescer := fins.NewBulkReadCoalescer(5*time.Millisecond, 32).WithMaxBatchSize(16)
// default: interceptors run on the batched read; call WithInterceptors(false) for a raw fast path
client.Use(coalescer)
```

### Validation Interceptor

```go
// Add validation before operations
client.SetInterceptor(fins.ValidationInterceptor())

// This will fail validation
_, err := client.ReadWords(ctx, fins.MemoryAreaDMWord, 100, 0) // count = 0
// Error: invalid read count: 0
```

### Retry Interceptor

```go
// Automatically retry failed operations
client.SetInterceptor(fins.RetryInterceptor(3, 100*time.Millisecond))

// Will retry up to 3 times with 100ms delay between retries
data, err := client.ReadWords(ctx, fins.MemoryAreaDMWord, 100, 5)
```

## Context Support

All Read/Write methods now accept `context.Context` as their first parameter. This gives you:

- **Timeout control** - Set different timeouts for different operations
- **Cancellation** - Cancel operations that are taking too long
- **Request tracing** - Propagate trace IDs through your application

### Timeout Control

```go
ctx, cancel := context.WithTimeout(context.Background(), 500*time.Millisecond)
defer cancel()

data, err := client.ReadWords(ctx, fins.MemoryAreaDMWord, 100, 5)
if err == context.DeadlineExceeded {
    log.Println("Operation timed out")
}
```

### Cancellation

```go
ctx, cancel := context.WithCancel(context.Background())

go func() {
    time.Sleep(100 * time.Millisecond)
    cancel() // Cancel the operation
}()

_, err := client.ReadWords(ctx, fins.MemoryAreaDMWord, 100, 5)
if err == context.Canceled {
    log.Println("Operation was cancelled")
}
```

### Request Tracing

```go
ctx := context.WithValue(context.Background(), "traceID", "12345")
data, err := client.ReadWords(ctx, fins.MemoryAreaDMWord, 100, 5)
```

## Other Improvements

### Thread Safety
- Added proper mutex protection for all shared state
- New `IsClosed()` method with mutex protection
- All public methods check if the client is closed before operating

### Better Error Types
- New `ClientClosedError` for operations on closed clients
- Server provides `Err()` channel for monitoring errors

### Code Quality
- Fixed typo: "axuillary" → "auxiliary"
- Extracted all magic numbers to named constants
- Added buffered channels to prevent goroutine leaks
- Comprehensive package documentation

### Testing
- Tests use dynamic port allocation instead of hardcoded ports
- Can now run tests in parallel without conflicts
- New tests for context cancellation and timeout handling
- 66.5% test coverage (up from ~60%)

## Migration from gofins

The main breaking change is that all Read/Write methods now require a context parameter.

**Before (gofins):**
```go
import "github.com/l1va/gofins"
data, err := client.ReadWords(memoryArea, address, count)
```

**After (this package):**
```go
import (
    "context"
    fins "github.com/bronystylecrazy/gofins"
)

ctx := context.Background() // or context with timeout/cancellation
data, err := client.ReadWords(ctx, memoryArea, address, count)
```

### New Features Available

- `IsClosed() bool` - Check if client/server is closed
- `SetReadTimeout(time.Duration)` - Configure read timeout
- `ClientClosedError` - New error type
- `Server.Err()` - Monitor server errors
- `EnableAutoReconnect(maxRetries, initialDelay)` - Enable automatic reconnection
- `DisableAutoReconnect()` - Disable automatic reconnection
- `IsReconnecting() bool` - Check if client is reconnecting
- `Shutdown()` - Graceful shutdown that stops reconnection attempts
- `SetInterceptor(interceptor)` - Set an interceptor for all operations (logging, metrics, etc.)

## Comparison with gofins

Here's a quick comparison of what changed:

| Issue | gofins | This Package |
|-------|----------|--------------|
| Resource leak | Creates reader per iteration | Reuses single reader |
| Race conditions | Unprotected slice access | Mutex-protected |
| Error handling | Uses log.Fatal | Returns errors via channels |
| Constants bug | Duplicate iota values | Correct values |
| Graceful shutdown | Blocks forever | Timeout support |
| Thread safety | Partially documented | Fully documented and protected |
| Context support | None | Full support |
| Cancellation | Not possible | Via context |
| Timeout control | Global only | Per-operation |
| Auto-reconnect | Not available | Exponential backoff |
| Interceptors | Not available | Full support (logging, metrics, etc.) |
| Client state check | No method | IsClosed() available |
| Test isolation | Hardcoded ports | Dynamic allocation |

## Performance

- Memory leak fix improves long-running stability significantly
- Proper synchronization prevents race conditions without noticeable overhead
- Context checking adds less than 1% overhead
- Buffered channels reduce goroutine blocking

## Compatibility

- Wire protocol is identical - works with the same Omron PLCs
- Not backward compatible due to context parameter requirement
- Drop-in replacement after adding context to method calls

## License

Same as the original [gofins](https://github.com/l1va/gofins) package.

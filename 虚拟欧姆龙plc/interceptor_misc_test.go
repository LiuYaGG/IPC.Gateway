package gofins

import (
	"bytes"
	"context"
	"fmt"
	"log"
	"strings"
	"testing"
	"time"

	"github.com/stretchr/testify/assert"
	"go.uber.org/zap"
	"go.uber.org/zap/zaptest/observer"
)

func TestMetricsInterceptor(t *testing.T) {
	metrics := NewMetricsCollector()
	info := &InterceptorInfo{Operation: OpReadWords}

	// Successful call
	_, err := metrics.Interceptor()(&InterceptorCtx{
		ctx:  context.Background(),
		info: info,
		invoker: func(context.Context) (interface{}, error) {
			return "ok", nil
		},
	})
	assert.NoError(t, err)

	// Failing call
	_, err = metrics.Interceptor()(&InterceptorCtx{
		ctx:  context.Background(),
		info: info,
		invoker: func(context.Context) (interface{}, error) {
			return nil, fmt.Errorf("boom")
		},
	})
	assert.Error(t, err)

	count, errors, avg := metrics.GetStats(OpReadWords)
	assert.Equal(t, int64(2), count)
	assert.Equal(t, int64(1), errors)
	assert.Greater(t, avg, time.Duration(0))

	all := metrics.GetAllStats()
	assert.Contains(t, all, OpReadWords)
	metrics.Reset()
	count, errors, _ = metrics.GetStats(OpReadWords)
	assert.Equal(t, int64(0), count)
	assert.Equal(t, int64(0), errors)
}

func TestRetryInterceptorsMisc(t *testing.T) {
	ctx := context.Background()
	info := &InterceptorInfo{Operation: OpWriteWords}

	attempts := 0
	invoker := func(context.Context) (interface{}, error) {
		attempts++
		if attempts < 3 {
			return nil, fmt.Errorf("fail %d", attempts)
		}
		return "ok", nil
	}

	start := time.Now()
	res, err := RetryInterceptor(3, time.Millisecond)(&InterceptorCtx{ctx: ctx, info: info, invoker: invoker})
	assert.NoError(t, err)
	assert.Equal(t, "ok", res)
	assert.Equal(t, 3, attempts)
	assert.GreaterOrEqual(t, time.Since(start), 2*time.Millisecond)

	// Conditional retry should stop when predicate returns false.
	attempts = 0
	res, err = RetryInterceptorConditional(2, time.Millisecond, func(error) bool { return false })(
		&InterceptorCtx{ctx: ctx, info: info, invoker: invoker},
	)
	assert.Error(t, err)
	assert.Nil(t, res)
	assert.Equal(t, 1, attempts)

	// Backoff caps at maxDelay; ensure we attempted expected retries.
	attempts = 0
	backoffStart := time.Now()
	_, err = RetryInterceptorWithBackoff(2, time.Millisecond, 2*time.Millisecond)(
		&InterceptorCtx{ctx: ctx, info: info, invoker: invoker},
	)
	assert.NoError(t, err)
	assert.Equal(t, 3, attempts)
	assert.GreaterOrEqual(t, time.Since(backoffStart), 2*time.Millisecond)
}

func TestValidationInterceptors(t *testing.T) {
	validate := ValidationInterceptorWithLimits(2, 2)

	// Valid read
	_, err := validate(&InterceptorCtx{
		ctx:  context.Background(),
		info: &InterceptorInfo{Operation: OpReadWords, Count: 1},
		invoker: func(context.Context) (interface{}, error) {
			return "ok", nil
		},
	})
	assert.NoError(t, err)

	// Invalid read count
	_, err = validate(&InterceptorCtx{
		ctx:  context.Background(),
		info: &InterceptorInfo{Operation: OpReadWords, Count: 0},
		invoker: func(context.Context) (interface{}, error) {
			return "ok", nil
		},
	})
	assert.Error(t, err)

	// Invalid write data type
	_, err = validate(&InterceptorCtx{
		ctx:     context.Background(),
		info:    &InterceptorInfo{Operation: OpWriteWords, Data: "nope"},
		invoker: func(context.Context) (interface{}, error) { return "ok", nil },
	})
	assert.Error(t, err)

	// Too many words
	_, err = validate(&InterceptorCtx{
		ctx:     context.Background(),
		info:    &InterceptorInfo{Operation: OpWriteWords, Data: []uint16{1, 2, 3}},
		invoker: func(context.Context) (interface{}, error) { return "ok", nil },
	})
	assert.Error(t, err)
}

func TestAddressRangeAndReadOnly(t *testing.T) {
	validator := AddressRangeValidator(map[byte]struct{ Min, Max uint16 }{
		MemoryAreaDMWord: {Min: 0, Max: 10},
	})
	ro := ReadOnlyInterceptor()

	// Valid range passes.
	_, err := validator(&InterceptorCtx{
		ctx:  context.Background(),
		info: &InterceptorInfo{Operation: OpReadWords, MemoryArea: MemoryAreaDMWord, Address: 5, Count: 2},
		invoker: func(context.Context) (interface{}, error) {
			return "ok", nil
		},
	})
	assert.NoError(t, err)

	// Invalid area
	_, err = validator(&InterceptorCtx{
		ctx:     context.Background(),
		info:    &InterceptorInfo{Operation: OpReadWords, MemoryArea: MemoryAreaARWord, Address: 5, Count: 1},
		invoker: func(context.Context) (interface{}, error) { return "ok", nil },
	})
	assert.Error(t, err)

	// ReadOnly blocks writes
	_, err = ro(&InterceptorCtx{
		ctx:     context.Background(),
		info:    &InterceptorInfo{Operation: OpWriteWords},
		invoker: func(context.Context) (interface{}, error) { return "ok", nil },
	})
	assert.Error(t, err)

	// ReadOnly allows reads
	_, err = ro(&InterceptorCtx{
		ctx:     context.Background(),
		info:    &InterceptorInfo{Operation: OpReadWords},
		invoker: func(context.Context) (interface{}, error) { return "ok", nil },
	})
	assert.NoError(t, err)
}

func TestLoggingAndTracingInterceptors(t *testing.T) {
	// Logging interceptor should emit start and completion.
	core, observed := observer.New(zap.InfoLevel)
	logger := zap.New(core)
	info := &InterceptorInfo{Operation: OpReadWords, MemoryArea: MemoryAreaDMWord, Address: 42}
	_, err := LoggingInterceptor(logger)(&InterceptorCtx{
		ctx:  context.Background(),
		info: info,
		invoker: func(context.Context) (interface{}, error) {
			return "ok", nil
		},
	})
	assert.NoError(t, err)
	assert.GreaterOrEqual(t, observed.Len(), 2) // start + completed

	// Tracing with custom logger should record the trace ID.
	buf := &bytes.Buffer{}
	traceLogger := log.New(buf, "", 0)
	traceKey := "traceID"
	_, err = TracingInterceptorWithLogger(traceKey, traceLogger)(&InterceptorCtx{
		ctx:  context.WithValue(context.Background(), traceKey, "abc-123"),
		info: info,
		invoker: func(context.Context) (interface{}, error) {
			return "ok", nil
		},
	})
	assert.NoError(t, err)
	assert.True(t, strings.Contains(buf.String(), "abc-123"))
}

func TestSimulatorTCPOptionRequiresAddress(t *testing.T) {
	// Using TCP transport without a TCP address should fail fast.
	_, err := NewPLCSimulator(NewUDPAddress("127.0.0.1", 0, 0, 0, 0), WithTCPTransport())
	assert.Error(t, err)
}

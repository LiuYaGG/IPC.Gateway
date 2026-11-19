package gofins

import (
	"context"
	"errors"
	"sync"
	"testing"
	"time"

	"go.uber.org/zap"
	"go.uber.org/zap/zapcore"
	"go.uber.org/zap/zaptest"
	"go.uber.org/zap/zaptest/observer"
)

func TestLoggingInterceptor(t *testing.T) {
	ctx := context.Background()
	info := &InterceptorInfo{
		Operation:  OpReadWords,
		MemoryArea: MemoryAreaDMWord,
		Address:    42,
		Count:      2,
	}

	// Success case
	core, logs := observer.New(zap.InfoLevel)
	logger := zaptest.NewLogger(t, zaptest.WrapOptions(
		zap.WrapCore(func(zapcore.Core) zapcore.Core { return core }),
	))
	_, err := LoggingInterceptor(logger)(&InterceptorCtx{
		ctx:  ctx,
		info: info,
		invoker: func(context.Context) (interface{}, error) {
			return "ok", nil
		},
	})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if logs.Len() != 2 {
		t.Fatalf("expected 2 log entries, got %d", logs.Len())
	}
	entries := logs.All()
	start := entries[0]
	if start.Message != "starting" || fieldString(start.Context, "operation") != "ReadWords" {
		t.Fatalf("unexpected start log: %+v", start)
	}
	end := entries[1]
	if end.Message != "completed" || fieldString(end.Context, "operation") != "ReadWords" {
		t.Fatalf("unexpected completion log: %+v", end)
	}

	// Error case
	logs.TakeAll()
	_, err = LoggingInterceptor(logger)(&InterceptorCtx{
		ctx:  ctx,
		info: info,
		invoker: func(context.Context) (interface{}, error) {
			return nil, errors.New("boom")
		},
	})
	if err == nil {
		t.Fatalf("expected error")
	}
	entries = logs.All()
	if len(entries) != 2 || entries[1].Message != "failed" {
		t.Fatalf("unexpected log output on error: %+v", entries)
	}
	if entries[1].Level != zap.ErrorLevel || fieldError(entries[1].Context, "error") != "boom" {
		t.Fatalf("expected error entry with boom, got %+v", entries[1])
	}
}

func fieldString(fields []zap.Field, key string) string {
	for _, f := range fields {
		if f.Key == key {
			return f.String
		}
	}
	return ""
}

func fieldError(fields []zap.Field, key string) string {
	for _, f := range fields {
		if f.Key == key {
			if err, ok := f.Interface.(error); ok {
				return err.Error()
			}
		}
	}
	return ""
}

func TestMetricsCollectorConcurrency(t *testing.T) {
	ctx := context.Background()
	collector := NewMetricsCollector()
	interceptor := collector.Interceptor()

	var wg sync.WaitGroup
	const successCalls = 10
	for i := 0; i < successCalls; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			_, _ = interceptor(&InterceptorCtx{
				ctx:  ctx,
				info: &InterceptorInfo{Operation: OpReadWords},
				invoker: func(context.Context) (interface{}, error) {
					time.Sleep(1 * time.Millisecond)
					return "ok", nil
				},
			})
		}()
	}

	const errorCalls = 3
	for i := 0; i < errorCalls; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			_, _ = interceptor(&InterceptorCtx{
				ctx:  ctx,
				info: &InterceptorInfo{Operation: OpWriteWords},
				invoker: func(context.Context) (interface{}, error) {
					return nil, errors.New("fail")
				},
			})
		}()
	}

	wg.Wait()

	count, errorsCount, avg := collector.GetStats(OpReadWords)
	if count != successCalls || errorsCount != 0 {
		t.Fatalf("unexpected read stats: count=%d errors=%d", count, errorsCount)
	}
	if avg <= 0 {
		t.Fatalf("expected average duration to be recorded")
	}

	count, errorsCount, _ = collector.GetStats(OpWriteWords)
	if count != errorCalls || errorsCount != errorCalls {
		t.Fatalf("unexpected write stats: count=%d errors=%d", count, errorsCount)
	}

	collector.Reset()
	count, errorsCount, _ = collector.GetStats(OpWriteWords)
	if count != 0 || errorsCount != 0 {
		t.Fatalf("reset should clear metrics, got count=%d errors=%d", count, errorsCount)
	}
}

func TestValidationInterceptorWithLimits(t *testing.T) {
	ctx := context.Background()
	validator := ValidationInterceptorWithLimits(2, 2)

	// Valid read should reach invoker
	called := false
	_, err := validator(&InterceptorCtx{
		ctx: ctx,
		info: &InterceptorInfo{
			Operation:  OpReadWords,
			Count:      1,
			MemoryArea: MemoryAreaDMWord,
		},
		invoker: func(context.Context) (interface{}, error) {
			called = true
			return "ok", nil
		},
	})
	if err != nil || !called {
		t.Fatalf("validator blocked valid operation: called=%v err=%v", called, err)
	}

	tests := []struct {
		name string
		info *InterceptorInfo
	}{
		{"zero read count", &InterceptorInfo{Operation: OpReadWords, Count: 0}},
		{"exceeds read limit", &InterceptorInfo{Operation: OpReadWords, Count: 5}},
		{"invalid write words type", &InterceptorInfo{Operation: OpWriteWords, Data: []byte{1}}},
		{"empty write bytes", &InterceptorInfo{Operation: OpWriteBytes, Data: []byte{}}},
		{"write bytes too large", &InterceptorInfo{Operation: OpWriteBytes, Data: make([]byte, 10), Count: 0, MemoryArea: MemoryAreaDMWord}},
		{"write bits too many", &InterceptorInfo{Operation: OpWriteBits, Data: make([]bool, 33)}},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			_, err := validator(&InterceptorCtx{
				ctx:  ctx,
				info: tt.info,
				invoker: func(context.Context) (interface{}, error) {
					return nil, nil
				},
			})
			if err == nil {
				t.Fatalf("expected validation error for %s", tt.name)
			}
		})
	}
}

func TestAddressRangeAndReadOnlyInterceptors(t *testing.T) {
	ctx := context.Background()
	validator := AddressRangeValidator(map[byte]struct{ Min, Max uint16 }{
		MemoryAreaDMWord: {Min: 0, Max: 10},
	})
	readOnly := ReadOnlyInterceptor()

	// Valid read passes both
	_, err := validator(&InterceptorCtx{
		ctx:     ctx,
		info:    &InterceptorInfo{Operation: OpReadWords, MemoryArea: MemoryAreaDMWord, Address: 5, Count: 1},
		invoker: func(context.Context) (interface{}, error) { return "ok", nil },
	})
	if err != nil {
		t.Fatalf("unexpected error from address validator: %v", err)
	}

	_, err = readOnly(&InterceptorCtx{
		ctx:     ctx,
		info:    &InterceptorInfo{Operation: OpReadWords},
		invoker: func(context.Context) (interface{}, error) { return "ok", nil },
	})
	if err != nil {
		t.Fatalf("read-only should allow reads: %v", err)
	}

	// Invalid area
	_, err = validator(&InterceptorCtx{
		ctx:     ctx,
		info:    &InterceptorInfo{Operation: OpReadWords, MemoryArea: MemoryAreaDMBit, Address: 5, Count: 1},
		invoker: func(context.Context) (interface{}, error) { return "ok", nil },
	})
	if err == nil {
		t.Fatalf("expected area validation error")
	}

	// Address overflow
	_, err = validator(&InterceptorCtx{
		ctx:     ctx,
		info:    &InterceptorInfo{Operation: OpReadWords, MemoryArea: MemoryAreaDMWord, Address: 10, Count: 2},
		invoker: func(context.Context) (interface{}, error) { return "ok", nil },
	})
	if err == nil {
		t.Fatalf("expected address overflow error")
	}

	// Write should be blocked
	_, err = readOnly(&InterceptorCtx{
		ctx:     ctx,
		info:    &InterceptorInfo{Operation: OpWriteWords},
		invoker: func(context.Context) (interface{}, error) { return "ok", nil },
	})
	if err == nil {
		t.Fatalf("expected read-only interceptor to block write")
	}
}

func TestRetryInterceptors(t *testing.T) {
	ctx := context.Background()

	// Basic retry until success
	attempts := 0
	result, err := RetryInterceptor(2, 0)(&InterceptorCtx{ctx: ctx, info: &InterceptorInfo{Operation: OpReadWords}, invoker: func(context.Context) (interface{}, error) {
		if attempts < 2 {
			attempts++
			return nil, errors.New("boom")
		}
		return "ok", nil
	}})
	if err != nil || result != "ok" || attempts != 2 {
		t.Fatalf("retry interceptor failed: result=%v err=%v attempts=%d", result, err, attempts)
	}

	// Respect canceled context (should not retry)
	cancelCtx, cancel := context.WithCancel(ctx)
	cancel()
	attempts = 0
	_, err = RetryInterceptor(3, 0)(&InterceptorCtx{ctx: cancelCtx, info: &InterceptorInfo{Operation: OpReadWords}, invoker: func(context.Context) (interface{}, error) {
		attempts++
		return nil, errors.New("boom")
	}})
	if err == nil || attempts != 1 {
		t.Fatalf("expected single attempt due to canceled context, attempts=%d err=%v", attempts, err)
	}

	// Conditional retry
	attempts = 0
	_, err = RetryInterceptorConditional(3, 0, func(err error) bool { return false })(&InterceptorCtx{ctx: ctx, info: &InterceptorInfo{Operation: OpReadWords}, invoker: func(context.Context) (interface{}, error) {
		attempts++
		return nil, errors.New("boom")
	}})
	if err == nil || attempts != 1 {
		t.Fatalf("conditional retry should stop when shouldRetry returns false, attempts=%d err=%v", attempts, err)
	}

	attempts = 0
	result, err = RetryInterceptorWithBackoff(2, 0, 1*time.Millisecond)(&InterceptorCtx{ctx: ctx, info: &InterceptorInfo{Operation: OpReadWords}, invoker: func(context.Context) (interface{}, error) {
		if attempts == 0 {
			attempts++
			return nil, errors.New("boom")
		}
		return "ok", nil
	}})
	if err != nil || result != "ok" || attempts != 1 {
		t.Fatalf("backoff retry failed: result=%v err=%v attempts=%d", result, err, attempts)
	}
}

func TestChainInterceptorsOrder(t *testing.T) {
	ctx := context.Background()
	info := &InterceptorInfo{Operation: OpReadWords}
	order := make([]string, 0, 5)

	i1 := func(ic *InterceptorCtx) (interface{}, error) {
		order = append(order, "i1-start")
		res, err := ic.Invoke(nil)
		order = append(order, "i1-end")
		return res, err
	}
	i2 := func(ic *InterceptorCtx) (interface{}, error) {
		order = append(order, "i2-start")
		res, err := ic.Invoke(nil)
		order = append(order, "i2-end")
		return res, err
	}

	result, err := ChainInterceptors(i1, i2)(&InterceptorCtx{
		ctx:  ctx,
		info: info,
		invoker: func(context.Context) (interface{}, error) {
			order = append(order, "invoker")
			return "ok", nil
		},
	})
	if err != nil || result != "ok" {
		t.Fatalf("chain interceptors error: result=%v err=%v", result, err)
	}

	expected := []string{"i1-start", "i2-start", "invoker", "i2-end", "i1-end"}
	if len(order) != len(expected) {
		t.Fatalf("unexpected order length: %v", order)
	}
	for i := range expected {
		if order[i] != expected[i] {
			t.Fatalf("unexpected order at %d: got %s want %s", i, order[i], expected[i])
		}
	}
}

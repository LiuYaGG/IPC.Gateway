//go:build ignore
// +build ignore

package gofins

import (
	"context"
	"fmt"
	"log"
	"time"

	"go.uber.org/zap"
)

// LoggingInterceptor creates an interceptor that logs all operations
// It logs operation start, end, duration, and any errors
func LoggingInterceptor(logger *zap.Logger) Interceptor {
	if logger == nil {
		logger = zap.NewExample()
	}

	return func(ctx context.Context, info *InterceptorInfo, invoker Invoker) (interface{}, error) {
		start := time.Now()

		// Log operation start
		logger.Info("[FINS] Starting operation",
			zap.String("operation", string(info.Operation)),
			zap.Uint8("memoryArea", info.MemoryArea),
			zap.Uint16("address", info.Address),
		)

		// Execute the operation
		result, err := invoker(ctx)

		// Log operation end with duration
		duration := time.Since(start)
		if err != nil {
			logger.Error("[FINS] Failed operation",
				zap.String("operation", string(info.Operation)),
				zap.Duration("duration", duration),
				zap.Error(err),
			)
		} else {
			logger.Info("[FINS] Completed operation",
				zap.String("operation", string(info.Operation)),
				zap.Duration("duration", duration),
			)
		}

		return result, err
	}
}

// MetricsInterceptor creates an interceptor that collects operation metrics
// It tracks operation counts, durations, and errors
type MetricsCollector struct {
	OperationCount map[OperationType]int64
	ErrorCount     map[OperationType]int64
	TotalDuration  map[OperationType]time.Duration
}

func NewMetricsCollector() *MetricsCollector {
	return &MetricsCollector{
		OperationCount: make(map[OperationType]int64),
		ErrorCount:     make(map[OperationType]int64),
		TotalDuration:  make(map[OperationType]time.Duration),
	}
}

func (m *MetricsCollector) Interceptor() Interceptor {
	return func(ctx context.Context, info *InterceptorInfo, invoker Invoker) (interface{}, error) {
		start := time.Now()

		result, err := invoker(ctx)

		duration := time.Since(start)
		m.OperationCount[info.Operation]++
		m.TotalDuration[info.Operation] += duration

		if err != nil {
			m.ErrorCount[info.Operation]++
		}

		return result, err
	}
}

// GetStats returns statistics for a specific operation
func (m *MetricsCollector) GetStats(op OperationType) (count int64, errors int64, avgDuration time.Duration) {
	count = m.OperationCount[op]
	errors = m.ErrorCount[op]
	if count > 0 {
		avgDuration = m.TotalDuration[op] / time.Duration(count)
	}
	return
}

// TracingInterceptor creates an interceptor that adds trace IDs to operations
func TracingInterceptor(traceIDKey interface{}) Interceptor {
	return func(ctx context.Context, info *InterceptorInfo, invoker Invoker) (interface{}, error) {
		// Get trace ID from context
		traceID := ctx.Value(traceIDKey)
		if traceID != nil {
			log.Printf("[TRACE:%v] %s - Address:%d", traceID, info.Operation, info.Address)
		}

		return invoker(ctx)
	}
}

// RetryInterceptor creates an interceptor that retries failed operations
func RetryInterceptor(maxRetries int, delay time.Duration) Interceptor {
	return func(ctx context.Context, info *InterceptorInfo, invoker Invoker) (interface{}, error) {
		var result interface{}
		var err error

		for attempt := 0; attempt <= maxRetries; attempt++ {
			result, err = invoker(ctx)
			if err == nil {
				return result, nil
			}

			// Don't retry on context errors
			if ctx.Err() != nil {
				return nil, err
			}

			// Don't retry on last attempt
			if attempt < maxRetries {
				log.Printf("[RETRY] Attempt %d/%d failed for %s: %v. Retrying in %v...",
					attempt+1, maxRetries+1, info.Operation, err, delay)
				time.Sleep(delay)
			}
		}

		return result, fmt.Errorf("operation failed after %d retries: %w", maxRetries+1, err)
	}
}

// ValidationInterceptor creates an interceptor that validates operation parameters
func ValidationInterceptor() Interceptor {
	return func(ctx context.Context, info *InterceptorInfo, invoker Invoker) (interface{}, error) {
		// Validate based on operation type
		switch info.Operation {
		case OpReadWords, OpReadBytes:
			if info.Count == 0 {
				return nil, fmt.Errorf("invalid read count: 0")
			}
			if info.Count > 1000 {
				return nil, fmt.Errorf("read count too large: %d (max 1000)", info.Count)
			}

		case OpWriteWords:
			data, ok := info.Data.([]uint16)
			if !ok || len(data) == 0 {
				return nil, fmt.Errorf("invalid write data")
			}

		case OpWriteBytes:
			data, ok := info.Data.([]byte)
			if !ok || len(data) == 0 {
				return nil, fmt.Errorf("invalid write data")
			}

		case OpWriteBits:
			data, ok := info.Data.([]bool)
			if !ok || len(data) == 0 {
				return nil, fmt.Errorf("invalid write data")
			}
		}

		return invoker(ctx)
	}
}

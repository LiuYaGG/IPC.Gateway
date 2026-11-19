package gofins

import (
	"sync"
	"time"
)

// MetricsCollector collects operation metrics including counts, errors, and durations
// It is safe for concurrent use.
//
// Example:
//
//	metrics := fins.NewMetricsCollector()
//	client.SetInterceptor(metrics.Interceptor())
//
//	// Perform operations...
//	client.ReadWords(ctx, fins.MemoryAreaDMWord, 100, 5)
//
//	// Get statistics
//	count, errors, avgDuration := metrics.GetStats(fins.OpReadWords)
//	log.Printf("ReadWords: %d calls, %d errors, avg: %v", count, errors, avgDuration)
type MetricsCollector struct {
	mu             sync.RWMutex
	OperationCount map[OperationType]int64
	ErrorCount     map[OperationType]int64
	TotalDuration  map[OperationType]time.Duration
}

// NewMetricsCollector creates a new metrics collector
func NewMetricsCollector() *MetricsCollector {
	return &MetricsCollector{
		OperationCount: make(map[OperationType]int64),
		ErrorCount:     make(map[OperationType]int64),
		TotalDuration:  make(map[OperationType]time.Duration),
	}
}

// Interceptor returns an interceptor that collects metrics
func (m *MetricsCollector) Interceptor() Interceptor {
	return func(c *InterceptorCtx) (interface{}, error) {
		start := time.Now()

		result, err := c.Invoke(nil)

		duration := time.Since(start)

		m.mu.Lock()
		op := c.Info().Operation
		m.OperationCount[op]++
		m.TotalDuration[op] += duration
		if err != nil {
			m.ErrorCount[op]++
		}
		m.mu.Unlock()

		return result, err
	}
}

// GetStats returns statistics for a specific operation
// Returns: count, errors, avgDuration
func (m *MetricsCollector) GetStats(op OperationType) (count int64, errors int64, avgDuration time.Duration) {
	m.mu.RLock()
	defer m.mu.RUnlock()

	count = m.OperationCount[op]
	errors = m.ErrorCount[op]
	if count > 0 {
		avgDuration = m.TotalDuration[op] / time.Duration(count)
	}
	return
}

// Reset clears all collected metrics
func (m *MetricsCollector) Reset() {
	m.mu.Lock()
	defer m.mu.Unlock()

	m.OperationCount = make(map[OperationType]int64)
	m.ErrorCount = make(map[OperationType]int64)
	m.TotalDuration = make(map[OperationType]time.Duration)
}

// GetAllStats returns statistics for all operations
func (m *MetricsCollector) GetAllStats() map[OperationType]struct {
	Count       int64
	Errors      int64
	AvgDuration time.Duration
} {
	m.mu.RLock()
	defer m.mu.RUnlock()

	stats := make(map[OperationType]struct {
		Count       int64
		Errors      int64
		AvgDuration time.Duration
	})

	for op := range m.OperationCount {
		count := m.OperationCount[op]
		errors := m.ErrorCount[op]
		var avgDuration time.Duration
		if count > 0 {
			avgDuration = m.TotalDuration[op] / time.Duration(count)
		}
		stats[op] = struct {
			Count       int64
			Errors      int64
			AvgDuration time.Duration
		}{
			Count:       count,
			Errors:      errors,
			AvgDuration: avgDuration,
		}
	}

	return stats
}

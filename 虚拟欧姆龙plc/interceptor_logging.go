package gofins

import (
	"fmt"
	"time"

	"go.uber.org/zap"
)

// LoggingInterceptor creates an interceptor that logs all operations
// It logs operation start, end, duration, and any errors
//
// Example:
//
//	logger, _ := zap.NewProduction()
//	client.SetInterceptor(fins.LoggingInterceptor(logger))
//
// Output:
//
//	INFO	FINS	starting operation=ReadWords area=0x82 address=100
//	INFO	FINS	completed operation=ReadWords duration_ms=5
func LoggingInterceptor(logger *zap.Logger) Interceptor {
	if logger == nil {
		logger = zap.NewNop()
	}
	// Named logger keeps consistent component label.
	logger = logger.Named("FINS")

	return func(c *InterceptorCtx) (interface{}, error) {
		info := c.Info()
		start := time.Now()

		// Log operation start
		logger.Info("starting",
			zap.String("operation", string(info.Operation)),
			zap.String("area", fmt.Sprintf("0x%02X", info.MemoryArea)),
			zap.Uint16("address", info.Address),
		)

		// Execute the operation
		result, err := c.Invoke(nil)

		// Log operation end with duration
		duration := time.Since(start)
		if err != nil {
			logger.Error("failed",
				zap.String("operation", string(info.Operation)),
				zap.Duration("duration", duration),
				zap.Error(err),
			)
		} else {
			logger.Info("completed",
				zap.String("operation", string(info.Operation)),
				zap.Duration("duration", duration),
			)
		}

		return result, err
	}
}

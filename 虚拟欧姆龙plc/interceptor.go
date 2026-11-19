package gofins

import "context"

// OperationType represents the type of FINS operation
type OperationType string

const (
	OpReadWords   OperationType = "ReadWords"
	OpReadBytes   OperationType = "ReadBytes"
	OpReadString  OperationType = "ReadString"
	OpReadBits    OperationType = "ReadBits"
	OpReadClock   OperationType = "ReadClock"
	OpWriteWords  OperationType = "WriteWords"
	OpWriteBytes  OperationType = "WriteBytes"
	OpWriteString OperationType = "WriteString"
	OpWriteBits   OperationType = "WriteBits"
	OpSetBit      OperationType = "SetBit"
	OpResetBit    OperationType = "ResetBit"
	OpToggleBit   OperationType = "ToggleBit"
)

// InterceptorInfo contains information about the operation being performed
type InterceptorInfo struct {
	Operation  OperationType
	MemoryArea byte
	Address    uint16
	BitOffset  byte        // Only for bit operations
	Count      uint16      // For read operations
	Data       interface{} // For write operations ([]uint16, []byte, []bool, string, etc.)
}

// Invoker is a function that executes the actual operation
type Invoker func(ctx context.Context) (interface{}, error)

// InterceptorCtx groups arguments for an interceptor.
// It mirrors Fiber's style where the context is accessed via methods.
type InterceptorCtx struct {
	ctx     context.Context
	info    *InterceptorInfo
	invoker Invoker
}

// Context returns the operation context.
func (c *InterceptorCtx) Context() context.Context { return c.ctx }

// Info returns the operation metadata.
func (c *InterceptorCtx) Info() *InterceptorInfo { return c.info }

// Invoke calls the underlying operation with the provided context.
// If ctx is nil, the stored context is used.
func (c *InterceptorCtx) Invoke(ctx context.Context) (interface{}, error) {
	if ctx == nil {
		ctx = c.ctx
	}
	return c.invoker(ctx)
}

// Interceptor can intercept and wrap FINS operations.
// It receives a single *InterceptorCtx for readability and future extensibility.
type Interceptor func(c *InterceptorCtx) (interface{}, error)

// ChainInterceptors chains multiple interceptors into a single interceptor
// Interceptors are executed in order: first interceptor wraps second, second wraps third, etc.
func ChainInterceptors(interceptors ...Interceptor) Interceptor {
	if len(interceptors) == 0 {
		return nil
	}

	if len(interceptors) == 1 {
		return interceptors[0]
	}

	return func(c *InterceptorCtx) (interface{}, error) {
		return interceptors[0](&InterceptorCtx{
			ctx:  c.ctx,
			info: c.info,
			invoker: func(ctx context.Context) (interface{}, error) {
				return ChainInterceptors(interceptors[1:]...)(&InterceptorCtx{
					ctx:     ctx,
					info:    c.info,
					invoker: c.invoker,
				})
			},
		})
	}
}

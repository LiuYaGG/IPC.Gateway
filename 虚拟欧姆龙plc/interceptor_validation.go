package gofins

import "fmt"

// ValidationInterceptor creates an interceptor that validates operation parameters
// before executing them. It checks for common mistakes like zero counts or invalid data.
//
// Example:
//
//	client.SetInterceptor(fins.ValidationInterceptor())
//
//	// This will fail validation
//	_, err := client.ReadWords(ctx, fins.MemoryAreaDMWord, 100, 0)
//	// Error: invalid read count: 0
func ValidationInterceptor() Interceptor {
	return ValidationInterceptorWithLimits(1000, 1000)
}

// ValidationInterceptorWithLimits creates a validation interceptor with custom limits
// maxReadCount: maximum number of items that can be read in a single operation
// maxWriteCount: maximum number of items that can be written in a single operation
//
// Example:
//
//	client.SetInterceptor(fins.ValidationInterceptorWithLimits(500, 500))
func ValidationInterceptorWithLimits(maxReadCount, maxWriteCount uint16) Interceptor {
	return func(c *InterceptorCtx) (interface{}, error) {
		info := c.Info()
		// Validate based on operation type
		switch info.Operation {
		case OpReadWords, OpReadBytes, OpReadString:
			if info.Count == 0 {
				return nil, fmt.Errorf("invalid read count: 0")
			}
			if info.Count > maxReadCount {
				return nil, fmt.Errorf("read count too large: %d (max %d)", info.Count, maxReadCount)
			}

		case OpReadBits:
			if info.Count == 0 {
				return nil, fmt.Errorf("invalid read count: 0")
			}
			if info.Count > maxReadCount*16 { // Bits are packed
				return nil, fmt.Errorf("read count too large: %d (max %d)", info.Count, maxReadCount*16)
			}

		case OpWriteWords:
			data, ok := info.Data.([]uint16)
			if !ok {
				return nil, fmt.Errorf("invalid write data type: expected []uint16")
			}
			if len(data) == 0 {
				return nil, fmt.Errorf("invalid write data: empty slice")
			}
			if uint16(len(data)) > maxWriteCount {
				return nil, fmt.Errorf("write count too large: %d (max %d)", len(data), maxWriteCount)
			}

		case OpWriteBytes, OpWriteString:
			var dataLen int
			switch d := info.Data.(type) {
			case []byte:
				dataLen = len(d)
			case string:
				dataLen = len(d)
			default:
				return nil, fmt.Errorf("invalid write data type")
			}
			if dataLen == 0 {
				return nil, fmt.Errorf("invalid write data: empty")
			}
			if uint16(dataLen) > maxWriteCount*2 { // Bytes are packed into words
				return nil, fmt.Errorf("write size too large: %d bytes (max %d)", dataLen, maxWriteCount*2)
			}

		case OpWriteBits:
			data, ok := info.Data.([]bool)
			if !ok {
				return nil, fmt.Errorf("invalid write data type: expected []bool")
			}
			if len(data) == 0 {
				return nil, fmt.Errorf("invalid write data: empty slice")
			}
			if uint16(len(data)) > maxWriteCount*16 {
				return nil, fmt.Errorf("write count too large: %d (max %d)", len(data), maxWriteCount*16)
			}
		}

		return c.Invoke(nil)
	}
}

// AddressRangeValidator creates an interceptor that validates address ranges
// It ensures operations only access allowed memory regions.
//
// Example:
//
//	// Only allow DM area addresses 0-999
//	validator := fins.AddressRangeValidator(map[byte]struct{Min, Max uint16}{
//		fins.MemoryAreaDMWord: {Min: 0, Max: 999},
//		fins.MemoryAreaDMBit:  {Min: 0, Max: 999},
//	})
//	client.SetInterceptor(validator)
func AddressRangeValidator(allowedRanges map[byte]struct{ Min, Max uint16 }) Interceptor {
	return func(c *InterceptorCtx) (interface{}, error) {
		info := c.Info()
		// Check if memory area is allowed
		addrRange, allowed := allowedRanges[info.MemoryArea]
		if !allowed {
			return nil, fmt.Errorf("memory area 0x%02X is not allowed", info.MemoryArea)
		}

		// Check address range
		if info.Address < addrRange.Min || info.Address > addrRange.Max {
			return nil, fmt.Errorf("address %d is outside allowed range [%d-%d] for area 0x%02X",
				info.Address, addrRange.Min, addrRange.Max, info.MemoryArea)
		}

		// For read operations, check that address+count doesn't exceed max
		if info.Count > 0 {
			endAddress := info.Address + info.Count - 1
			if endAddress > addrRange.Max {
				return nil, fmt.Errorf("operation would access address %d, which exceeds max %d",
					endAddress, addrRange.Max)
			}
		}

		return c.Invoke(nil)
	}
}

// ReadOnlyInterceptor creates an interceptor that blocks all write operations
//
// Example:
//
//	client.SetInterceptor(fins.ReadOnlyInterceptor())
func ReadOnlyInterceptor() Interceptor {
	return func(c *InterceptorCtx) (interface{}, error) {
		info := c.Info()
		switch info.Operation {
		case OpWriteWords, OpWriteBytes, OpWriteString, OpWriteBits, OpSetBit, OpResetBit, OpToggleBit:
			return nil, fmt.Errorf("write operation %s is not allowed in read-only mode", info.Operation)
		}

		return c.Invoke(nil)
	}
}

package gofins

import (
	"testing"

	"github.com/stretchr/testify/assert"
)

func TestEncodeBCD(t *testing.T) {
	tests := []struct {
		name     string
		input    uint64
		expected []byte
	}{
		{"zero", 0, []byte{0x0f}},
		{"single digit", 5, []byte{0x5f}},
		{"two digits", 12, []byte{0x12}},
		{"three digits", 123, []byte{0x12, 0x3f}},
		{"four digits", 1234, []byte{0x12, 0x34}},
		{"five digits", 12345, []byte{0x12, 0x34, 0x5f}},
		{"year 2023", 2023, []byte{0x20, 0x23}},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			result := encodeBCD(tt.input)
			assert.Equal(t, tt.expected, result, "encodeBCD(%d) should equal %v", tt.input, tt.expected)
		})
	}
}

func TestDecodeBCD(t *testing.T) {
	tests := []struct {
		name     string
		input    []byte
		expected uint64
		hasError bool
	}{
		{"zero", []byte{0x0f}, 0, false},
		{"single digit", []byte{0x5f}, 5, false},
		{"two digits", []byte{0x12}, 12, false},
		{"three digits", []byte{0x12, 0x3f}, 123, false},
		{"four digits", []byte{0x12, 0x34}, 1234, false},
		{"five digits", []byte{0x12, 0x34, 0x5f}, 12345, false},
		{"year 2023", []byte{0x20, 0x23}, 2023, false},
		{"bad digit hi", []byte{0xA0}, 0, true},       // A > 9
		{"bad digit lo", []byte{0x0A}, 0, true},       // A > 9 in low nibble
		{"invalid terminator", []byte{0x1A}, 0, true}, // A in low nibble but not last byte
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			result, err := decodeBCD(tt.input)
			if tt.hasError {
				assert.Error(t, err)
			} else {
				assert.NoError(t, err)
				assert.Equal(t, tt.expected, result, "decodeBCD(%v) should equal %d", tt.input, tt.expected)
			}
		})
	}
}

func TestTimesTenPlusCatchingOverflow(t *testing.T) {
	tests := []struct {
		name     string
		x        uint64
		digit    uint64
		expected uint64
		hasError bool
	}{
		{"simple", 1, 2, 12, false},
		{"zero", 0, 5, 5, false},
		{"larger", 123, 4, 1234, false},
		// Test overflow detection
		{"overflow", ^uint64(0) / 10, 9, 0, true},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			result, err := timesTenPlusCatchingOverflow(tt.x, tt.digit)
			if tt.hasError {
				assert.Error(t, err)
				assert.IsType(t, BCDOverflowError{}, err)
			} else {
				assert.NoError(t, err)
				assert.Equal(t, tt.expected, result)
			}
		})
	}
}

func TestBCDRoundTrip(t *testing.T) {
	// Test encoding and decoding round trip
	testValues := []uint64{0, 1, 5, 12, 99, 100, 255, 1234, 9999, 12345}

	for _, val := range testValues {
		t.Run("", func(t *testing.T) {
			encoded := encodeBCD(val)
			decoded, err := decodeBCD(encoded)
			assert.NoError(t, err)
			assert.Equal(t, val, decoded, "Round trip for %d failed", val)
		})
	}
}

func TestClockReadCommand(t *testing.T) {
	cmd := clockReadCommand()
	assert.NotNil(t, cmd)
	assert.Equal(t, FINS_COMMAND_CODE_SIZE, len(cmd))
	// Should contain CommandCodeClockRead
	assert.Equal(t, byte(0x07), cmd[0]) // 0x0701 >> 8
	assert.Equal(t, byte(0x01), cmd[1]) // 0x0701 & 0xFF
}

func TestMemoryAddressEncoding(t *testing.T) {
	addr := memoryAddress{
		memoryArea: MemoryAreaDMWord,
		address:    0x1234,
		bitOffset:  5,
	}

	encoded := encodeMemoryAddress(addr)
	assert.Equal(t, FINS_MEMORY_ADDR_SIZE, len(encoded))
	assert.Equal(t, MemoryAreaDMWord, encoded[0])
	assert.Equal(t, byte(0x12), encoded[1])
	assert.Equal(t, byte(0x34), encoded[2])
	assert.Equal(t, byte(5), encoded[3])

	// Test decode
	decoded := decodeMemoryAddress(encoded)
	assert.Equal(t, addr.memoryArea, decoded.memoryArea)
	assert.Equal(t, addr.address, decoded.address)
	assert.Equal(t, addr.bitOffset, decoded.bitOffset)
}

func TestMemAddr(t *testing.T) {
	addr := memAddr(MemoryAreaDMWord, 0x100)
	assert.Equal(t, MemoryAreaDMWord, addr.memoryArea)
	assert.Equal(t, uint16(0x100), addr.address)
	assert.Equal(t, byte(0), addr.bitOffset)
}

func TestMemAddrWithBitOffset(t *testing.T) {
	addr := memAddrWithBitOffset(MemoryAreaDMBit, 0x200, 3)
	assert.Equal(t, MemoryAreaDMBit, addr.memoryArea)
	assert.Equal(t, uint16(0x200), addr.address)
	assert.Equal(t, byte(3), addr.bitOffset)
}

func TestHeaderEncoding(t *testing.T) {
	src := FinsAddress{Network: 0, Node: 1, Unit: 0}
	dst := FinsAddress{Network: 0, Node: 10, Unit: 0}

	header := defaultCommandHeader(src, dst, 42)

	encoded := encodeHeader(header)
	assert.Equal(t, FINS_HEADER_SIZE, len(encoded))

	decoded := decodeHeader(encoded)
	assert.Equal(t, header.messageType, decoded.messageType)
	assert.Equal(t, header.responseRequired, decoded.responseRequired)
	assert.Equal(t, header.src, decoded.src)
	assert.Equal(t, header.dst, decoded.dst)
	assert.Equal(t, header.serviceID, decoded.serviceID)
}

func TestDefaultResponseHeader(t *testing.T) {
	src := FinsAddress{Network: 0, Node: 1, Unit: 0}
	dst := FinsAddress{Network: 0, Node: 10, Unit: 0}

	cmdHeader := defaultCommandHeader(src, dst, 42)
	respHeader := defaultResponseHeader(cmdHeader)

	// Response should swap src and dst
	assert.Equal(t, cmdHeader.src, respHeader.dst)
	assert.Equal(t, cmdHeader.dst, respHeader.src)
	assert.Equal(t, MessageTypeResponse, respHeader.messageType)
	assert.False(t, respHeader.responseRequired)
}

func TestReadCommand(t *testing.T) {
	addr := memAddr(MemoryAreaDMWord, 100)
	cmd := readCommand(addr, 5)

	assert.NotNil(t, cmd)
	assert.GreaterOrEqual(t, len(cmd), FINS_READ_CMD_MIN_SIZE)

	// Check command code
	assert.Equal(t, byte(0x01), cmd[0]) // CommandCodeMemoryAreaRead high byte
	assert.Equal(t, byte(0x01), cmd[1]) // CommandCodeMemoryAreaRead low byte
}

func TestWriteCommand(t *testing.T) {
	addr := memAddr(MemoryAreaDMWord, 100)
	data := []byte{0x12, 0x34}
	cmd := writeCommand(addr, 1, data)

	assert.NotNil(t, cmd)
	assert.GreaterOrEqual(t, len(cmd), FINS_WRITE_CMD_MIN_SIZE)

	// Check command code
	assert.Equal(t, byte(0x01), cmd[0]) // CommandCodeMemoryAreaWrite high byte
	assert.Equal(t, byte(0x02), cmd[1]) // CommandCodeMemoryAreaWrite low byte

	// Check data is appended
	assert.Contains(t, cmd, byte(0x12))
	assert.Contains(t, cmd, byte(0x34))
}

func TestCheckResponse(t *testing.T) {
	// Test normal completion
	resp := &response{endCode: EndCodeNormalCompletion}
	err := checkResponse(resp, nil)
	assert.NoError(t, err)

	// Test with error
	resp2 := &response{endCode: 0x0101}
	err2 := checkResponse(resp2, nil)
	assert.Error(t, err2)
	assert.Contains(t, err2.Error(), "0x101")

	// Test with nil response but error
	err3 := checkResponse(nil, assert.AnError)
	assert.Error(t, err3)
	assert.Equal(t, assert.AnError, err3)
}

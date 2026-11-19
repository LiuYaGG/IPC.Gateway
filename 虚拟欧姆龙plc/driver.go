package gofins

import (
	"encoding/binary"
)

const (
	// FINS protocol frame structure constants
	FINS_HEADER_SIZE        = 10 // FINS header is always 10 bytes
	FINS_COMMAND_CODE_SIZE  = 2  // Command code field size
	FINS_END_CODE_SIZE      = 2  // End code field size
	FINS_MEMORY_ADDR_SIZE   = 4  // Memory address field size
	FINS_ITEM_COUNT_SIZE    = 2  // Item count field size
	FINS_READ_CMD_MIN_SIZE  = 8  // Minimum read command size
	FINS_WRITE_CMD_MIN_SIZE = 8  // Minimum write command size

	// ICF (Information Control Field) byte offsets
	ICF_INDEX               = 0
	GATEWAY_COUNT_INDEX     = 2
	DST_NETWORK_INDEX       = 3
	DST_NODE_INDEX          = 4
	DST_UNIT_INDEX          = 5
	SRC_NETWORK_INDEX       = 6
	SRC_NODE_INDEX          = 7
	SRC_UNIT_INDEX          = 8
	SERVICE_ID_INDEX        = 9
	COMMAND_CODE_INDEX      = 10
	RESPONSE_END_CODE_INDEX = 12
	RESPONSE_DATA_INDEX     = 14

	// BCD encoding constants
	BCD_NIBBLE_MASK = 0x0f
	BCD_MAX_DIGIT   = 9
)

// request A FINS command request
type request struct {
	header      Header
	commandCode uint16
	data        []byte
}

// response A FINS command response
type response struct {
	header      Header
	commandCode uint16
	endCode     uint16
	data        []byte
}

// memoryAddress A plc memory address to do a work
type memoryAddress struct {
	memoryArea byte
	address    uint16
	bitOffset  byte
}

func memAddr(memoryArea byte, address uint16) memoryAddress {
	return memAddrWithBitOffset(memoryArea, address, 0)
}

func memAddrWithBitOffset(memoryArea byte, address uint16, bitOffset byte) memoryAddress {
	return memoryAddress{memoryArea, address, bitOffset}
}

func readCommand(memoryAddr memoryAddress, itemCount uint16) []byte {
	commandData := make([]byte, FINS_COMMAND_CODE_SIZE, FINS_READ_CMD_MIN_SIZE)
	binary.BigEndian.PutUint16(commandData[0:FINS_COMMAND_CODE_SIZE], CommandCodeMemoryAreaRead)
	commandData = append(commandData, encodeMemoryAddress(memoryAddr)...)
	commandData = append(commandData, []byte{0, 0}...)
	binary.BigEndian.PutUint16(commandData[6:8], itemCount)
	return commandData
}

func writeCommand(memoryAddr memoryAddress, itemCount uint16, bytes []byte) []byte {
	commandData := make([]byte, FINS_COMMAND_CODE_SIZE, FINS_WRITE_CMD_MIN_SIZE+len(bytes))
	binary.BigEndian.PutUint16(commandData[0:FINS_COMMAND_CODE_SIZE], CommandCodeMemoryAreaWrite)
	commandData = append(commandData, encodeMemoryAddress(memoryAddr)...)
	commandData = append(commandData, []byte{0, 0}...)
	binary.BigEndian.PutUint16(commandData[6:8], itemCount)
	commandData = append(commandData, bytes...)
	return commandData
}

func clockReadCommand() []byte {
	commandData := make([]byte, FINS_COMMAND_CODE_SIZE, FINS_COMMAND_CODE_SIZE)
	binary.BigEndian.PutUint16(commandData[0:FINS_COMMAND_CODE_SIZE], CommandCodeClockRead)
	return commandData
}

func encodeMemoryAddress(memoryAddr memoryAddress) []byte {
	bytes := make([]byte, FINS_MEMORY_ADDR_SIZE)
	bytes[0] = memoryAddr.memoryArea
	binary.BigEndian.PutUint16(bytes[1:3], memoryAddr.address)
	bytes[3] = memoryAddr.bitOffset
	return bytes
}

func decodeMemoryAddress(data []byte) memoryAddress {
	return memoryAddress{data[0], binary.BigEndian.Uint16(data[1:3]), data[3]}
}

func decodeRequest(bytes []byte) request {
	return request{
		decodeHeader(bytes[0:FINS_HEADER_SIZE]),
		binary.BigEndian.Uint16(bytes[COMMAND_CODE_INDEX : COMMAND_CODE_INDEX+FINS_COMMAND_CODE_SIZE]),
		bytes[COMMAND_CODE_INDEX+FINS_COMMAND_CODE_SIZE:],
	}
}

func decodeResponse(bytes []byte) response {
	return response{
		decodeHeader(bytes[0:FINS_HEADER_SIZE]),
		binary.BigEndian.Uint16(bytes[COMMAND_CODE_INDEX : COMMAND_CODE_INDEX+FINS_COMMAND_CODE_SIZE]),
		binary.BigEndian.Uint16(bytes[RESPONSE_END_CODE_INDEX : RESPONSE_END_CODE_INDEX+FINS_END_CODE_SIZE]),
		bytes[RESPONSE_DATA_INDEX:],
	}
}

func encodeResponse(resp response) []byte {
	responseSize := FINS_COMMAND_CODE_SIZE + FINS_END_CODE_SIZE
	bytes := make([]byte, responseSize, responseSize+len(resp.data))
	binary.BigEndian.PutUint16(bytes[0:FINS_COMMAND_CODE_SIZE], resp.commandCode)
	binary.BigEndian.PutUint16(bytes[FINS_COMMAND_CODE_SIZE:responseSize], resp.endCode)
	bytes = append(bytes, resp.data...)
	bh := encodeHeader(resp.header)
	bh = append(bh, bytes...)
	return bh
}

const (
	icfBridgesBit          byte = 7
	icfMessageTypeBit      byte = 6
	icfResponseRequiredBit byte = 0
)

func decodeHeader(bytes []byte) Header {
	header := Header{}
	icf := bytes[ICF_INDEX]
	if icf&1<<icfResponseRequiredBit == 0 {
		header.responseRequired = true
	}
	if icf&1<<icfMessageTypeBit == 0 {
		header.messageType = MessageTypeCommand
	} else {
		header.messageType = MessageTypeResponse
	}
	header.gatewayCount = bytes[GATEWAY_COUNT_INDEX]
	header.dst = FinsAddress{bytes[DST_NETWORK_INDEX], bytes[DST_NODE_INDEX], bytes[DST_UNIT_INDEX]}
	header.src = FinsAddress{bytes[SRC_NETWORK_INDEX], bytes[SRC_NODE_INDEX], bytes[SRC_UNIT_INDEX]}
	header.serviceID = bytes[SERVICE_ID_INDEX]

	return header
}

func encodeHeader(h Header) []byte {
	var icf byte
	icf = 1 << icfBridgesBit
	if h.responseRequired == false {
		icf |= 1 << icfResponseRequiredBit
	}
	if h.messageType == MessageTypeResponse {
		icf |= 1 << icfMessageTypeBit
	}
	bytes := []byte{
		icf, 0x00, h.gatewayCount,
		h.dst.Network, h.dst.Node, h.dst.Unit,
		h.src.Network, h.src.Node, h.src.Unit,
		h.serviceID}
	return bytes
}

func encodeBCD(x uint64) []byte {
	if x == 0 {
		return []byte{BCD_NIBBLE_MASK}
	}
	var n int
	for xx := x; xx > 0; n++ {
		xx = xx / 10
	}
	bcd := make([]byte, (n+1)/2)
	if n%2 == 1 {
		hi, lo := byte(x%10), byte(BCD_NIBBLE_MASK)
		bcd[(n-1)/2] = hi<<4 | lo
		x = x / 10
		n--
	}
	for i := n/2 - 1; i >= 0; i-- {
		hi, lo := byte((x/10)%10), byte(x%10)
		bcd[i] = hi<<4 | lo
		x = x / 100
	}
	return bcd
}

func timesTenPlusCatchingOverflow(x uint64, digit uint64) (uint64, error) {
	x5 := x<<2 + x
	if int64(x5) < 0 || x5<<1 > ^digit {
		return 0, BCDOverflowError{}
	}
	return x5<<1 + digit, nil
}

func decodeBCD(bcd []byte) (x uint64, err error) {
	for i, b := range bcd {
		hi, lo := uint64(b>>4), uint64(b&BCD_NIBBLE_MASK)
		if hi > BCD_MAX_DIGIT {
			return 0, BCDBadDigitError{"hi", hi}
		}
		x, err = timesTenPlusCatchingOverflow(x, hi)
		if err != nil {
			return 0, err
		}
		if lo == BCD_NIBBLE_MASK && i == len(bcd)-1 {
			return x, nil
		}
		if lo > BCD_MAX_DIGIT {
			return 0, BCDBadDigitError{"lo", lo}
		}
		x, err = timesTenPlusCatchingOverflow(x, lo)
		if err != nil {
			return 0, err
		}
	}
	return x, nil
}

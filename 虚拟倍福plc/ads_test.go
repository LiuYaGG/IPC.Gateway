package main

import (
	"bytes"
	"encoding/binary"
	"testing"
)

func newTestServer(t *testing.T) *AdsServer {
	t.Helper()
	cfg := defaultConfig()
	store, err := newStore(cfg)
	if err != nil {
		t.Fatal(err)
	}
	return NewAdsServer(cfg, store)
}

func commandRequest(command uint16, data []byte) amsRequest {
	return amsRequest{header: amsHeader{Command: command, InvokeID: 42}, data: data}
}

func resultOf(t *testing.T, response []byte) uint32 {
	t.Helper()
	if len(response) < 4 {
		t.Fatalf("响应长度不足: %d", len(response))
	}
	return binary.LittleEndian.Uint32(response[:4])
}

func createTestHandle(t *testing.T, server *AdsServer, name string) uint32 {
	t.Helper()
	request := make([]byte, 16+len(name)+1)
	putU32(request, 0, adsGroupSymbolHandleByName)
	putU32(request, 8, 4)
	putU32(request, 12, uint32(len(name)+1))
	copy(request[16:], name)
	response := server.process(commandRequest(adsCmdReadWrite, request))
	if result := resultOf(t, response); result != adsErrNone {
		t.Fatalf("创建句柄失败: 0x%X", result)
	}
	if len(response) != 12 || binary.LittleEndian.Uint32(response[4:8]) != 4 {
		t.Fatalf("句柄响应无效: %X", response)
	}
	return binary.LittleEndian.Uint32(response[8:12])
}

func TestReadStateReturnsRun(t *testing.T) {
	server := newTestServer(t)
	response := server.process(commandRequest(adsCmdReadState, nil))
	if resultOf(t, response) != 0 || len(response) != 8 {
		t.Fatalf("状态响应无效: %X", response)
	}
	if state := binary.LittleEndian.Uint16(response[4:6]); state != 5 {
		t.Fatalf("期望 Run(5)，实际 %d", state)
	}
}

func TestHandleReadWriteAndRelease(t *testing.T) {
	server := newTestServer(t)
	handle := createTestHandle(t, server, "GVL.iCounter")

	read := make([]byte, 12)
	putU32(read, 0, adsGroupSymbolValueByHandle)
	putU32(read, 4, handle)
	putU32(read, 8, 2)
	response := server.process(commandRequest(adsCmdRead, read))
	if resultOf(t, response) != 0 || binary.LittleEndian.Uint16(response[8:10]) != 123 {
		t.Fatalf("初始值读取错误: %X", response)
	}

	write := make([]byte, 14)
	putU32(write, 0, adsGroupSymbolValueByHandle)
	putU32(write, 4, handle)
	putU32(write, 8, 2)
	binary.LittleEndian.PutUint16(write[12:14], 456)
	if result := resultOf(t, server.process(commandRequest(adsCmdWrite, write))); result != 0 {
		t.Fatalf("写入失败: 0x%X", result)
	}
	response = server.process(commandRequest(adsCmdRead, read))
	if binary.LittleEndian.Uint16(response[8:10]) != 456 {
		t.Fatalf("写入后读取错误: %X", response)
	}

	release := make([]byte, 16)
	putU32(release, 0, adsGroupReleaseSymbolHandle)
	putU32(release, 8, 4)
	putU32(release, 12, handle)
	if result := resultOf(t, server.process(commandRequest(adsCmdWrite, release))); result != 0 {
		t.Fatalf("释放句柄失败: 0x%X", result)
	}
}

func TestSumRead(t *testing.T) {
	server := newTestServer(t)
	boolHandle := createTestHandle(t, server, "GVL.bStart")
	counterHandle := createTestHandle(t, server, "GVL.iCounter")

	write := make([]byte, 16+24)
	putU32(write, 0, adsGroupSumRead)
	putU32(write, 4, 2)
	putU32(write, 8, 11)
	putU32(write, 12, 24)
	putU32(write, 16, adsGroupSymbolValueByHandle)
	putU32(write, 20, boolHandle)
	putU32(write, 24, 1)
	putU32(write, 28, adsGroupSymbolValueByHandle)
	putU32(write, 32, counterHandle)
	putU32(write, 36, 2)

	response := server.process(commandRequest(adsCmdReadWrite, write))
	if resultOf(t, response) != 0 || len(response) != 19 {
		t.Fatalf("批量读取响应无效: %X", response)
	}
	if binary.LittleEndian.Uint32(response[8:12]) != 0 || binary.LittleEndian.Uint32(response[12:16]) != 0 {
		t.Fatalf("批量读取子命令失败: %X", response)
	}
	if response[16] != 0 || binary.LittleEndian.Uint16(response[17:19]) != 123 {
		t.Fatalf("批量读取数据错误: %X", response)
	}
}

func TestUnknownPointReturnsSymbolNotFound(t *testing.T) {
	server := newTestServer(t)
	name := "GVL.notExists"
	request := make([]byte, 16+len(name)+1)
	putU32(request, 0, adsGroupSymbolHandleByName)
	putU32(request, 8, 4)
	putU32(request, 12, uint32(len(name)+1))
	copy(request[16:], name)
	response := server.process(commandRequest(adsCmdReadWrite, request))
	if result := resultOf(t, response); result != adsErrSymbolNotFound {
		t.Fatalf("期望 0x710，实际 0x%X", result)
	}
}

func TestAmsTcpFrameRoundTrip(t *testing.T) {
	request := commandRequest(adsCmdReadState, nil)
	var encoded bytes.Buffer
	if err := writeAmsResponse(&encoded, request.header, []byte{1, 2, 3, 4}); err != nil {
		t.Fatal(err)
	}
	decoded, err := readAmsRequest(&encoded)
	if err != nil {
		t.Fatal(err)
	}
	if decoded.header.InvokeID != 42 || decoded.header.StateFlags&1 == 0 || !bytes.Equal(decoded.data, []byte{1, 2, 3, 4}) {
		t.Fatalf("AMS/TCP 编解码错误: %+v", decoded)
	}
}

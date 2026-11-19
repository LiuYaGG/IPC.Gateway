package main

import (
	"encoding/binary"
	"fmt"
	"io"
	"log"
	"net"
	"strconv"
	"strings"
	"sync"
	"sync/atomic"
)

const (
	amsTcpCommandAds           = 0
	amsTcpCommandPortClose     = 1
	amsTcpCommandPortConnect   = 0x1000
	amsTcpCommandGetLocalNetID = 0x1002

	adsCmdReadDeviceInfo     = 1
	adsCmdRead               = 2
	adsCmdWrite              = 3
	adsCmdReadState          = 4
	adsCmdWriteControl       = 5
	adsCmdAddNotification    = 6
	adsCmdDeleteNotification = 7
	adsCmdReadWrite          = 9

	adsGroupSymbolHandleByName  = 0xF003
	adsGroupSymbolValueByName   = 0xF004
	adsGroupSymbolValueByHandle = 0xF005
	adsGroupReleaseSymbolHandle = 0xF006
	adsGroupSymbolVersion       = 0xF008
	adsGroupSymbolUploadInfo2   = 0xF00F
	adsGroupSumRead             = 0xF080
	adsGroupSumWrite            = 0xF081
	adsGroupSumReadWrite        = 0xF082

	adsErrNone             = 0
	adsErrNotSupported     = 0x701
	adsErrInvalidGroup     = 0x702
	adsErrInvalidOffset    = 0x703
	adsErrInvalidSize      = 0x705
	adsErrInvalidData      = 0x706
	adsErrSymbolNotFound   = 0x710
	adsErrInvalidParameter = 0x70B

	maxAdsFrameSize = 16 * 1024 * 1024
)

type amsHeader struct {
	TargetNetID [6]byte
	TargetPort  uint16
	SourceNetID [6]byte
	SourcePort  uint16
	Command     uint16
	StateFlags  uint16
	Length      uint32
	ErrorCode   uint32
	InvokeID    uint32
}

type amsRequest struct {
	header amsHeader
	data   []byte
}

type AdsServer struct {
	cfg              Config
	store            *Store
	listener         net.Listener
	closed           chan struct{}
	closeOnce        sync.Once
	wg               sync.WaitGroup
	connMu           sync.Mutex
	conns            map[net.Conn]struct{}
	nextNotification atomic.Uint32
	nextClientPort   atomic.Uint32
}

func NewAdsServer(cfg Config, store *Store) *AdsServer {
	s := &AdsServer{cfg: cfg, store: store, closed: make(chan struct{}), conns: make(map[net.Conn]struct{})}
	s.nextNotification.Store(1000)
	s.nextClientPort.Store(30000)
	return s
}

func (s *AdsServer) Start() error {
	listener, err := net.Listen("tcp", fmt.Sprintf("%s:%d", s.cfg.Host, s.cfg.Port))
	if err != nil {
		return err
	}
	s.listener = listener
	s.wg.Add(1)
	go s.acceptLoop()
	return nil
}

func (s *AdsServer) acceptLoop() {
	defer s.wg.Done()
	for {
		conn, err := s.listener.Accept()
		if err != nil {
			select {
			case <-s.closed:
				return
			default:
				log.Printf("接受连接失败: %v", err)
				continue
			}
		}
		s.connMu.Lock()
		s.conns[conn] = struct{}{}
		s.connMu.Unlock()
		s.wg.Add(1)
		go s.handleConnection(conn)
	}
}

func (s *AdsServer) handleConnection(conn net.Conn) {
	defer s.wg.Done()
	defer func() {
		s.connMu.Lock()
		delete(s.conns, conn)
		s.connMu.Unlock()
		_ = conn.Close()
	}()
	log.Printf("客户端已连接: %s", conn.RemoteAddr())
	defer log.Printf("客户端已断开: %s", conn.RemoteAddr())

	for {
		var tcpHeader [6]byte
		if _, err := io.ReadFull(conn, tcpHeader[:]); err != nil {
			if err != io.EOF && !strings.Contains(err.Error(), "use of closed network connection") {
				log.Printf("读取请求结束: %v", err)
			}
			return
		}
		command := binary.LittleEndian.Uint16(tcpHeader[0:2])
		length := binary.LittleEndian.Uint32(tcpHeader[2:6])
		if length > maxAdsFrameSize {
			log.Printf("AMS/TCP 帧长度无效: %d", length)
			return
		}

		switch command {
		case amsTcpCommandAds:
			request, err := readAmsRequestBody(conn, length)
			if err != nil {
				log.Printf("ADS 请求无效: %v", err)
				return
			}
			response := s.process(request)
			if err := writeAmsResponse(conn, request.header, response); err != nil {
				return
			}

		case amsTcpCommandPortConnect:
			if length != 2 {
				return
			}
			var requestData [2]byte
			if _, err := io.ReadFull(conn, requestData[:]); err != nil {
				return
			}
			assignedPort := binary.LittleEndian.Uint16(requestData[:])
			if assignedPort == 0 {
				assignedPort = uint16(s.nextClientPort.Add(1))
			}
			netID, err := parseAmsNetID(s.cfg.AmsNetID)
			if err != nil {
				log.Printf("AMS NetId 配置无效: %v", err)
				return
			}
			response := make([]byte, 8)
			copy(response[:6], netID[:])
			binary.LittleEndian.PutUint16(response[6:8], assignedPort)
			if err := writeAmsTcpMessage(conn, command, response); err != nil {
				return
			}
			log.Printf("注册客户端 ADS 端口: %d", assignedPort)

		case amsTcpCommandPortClose:
			if length > 0 {
				if _, err := io.CopyN(io.Discard, conn, int64(length)); err != nil {
					return
				}
			}
			return

		case amsTcpCommandGetLocalNetID:
			if length > 0 {
				if _, err := io.CopyN(io.Discard, conn, int64(length)); err != nil {
					return
				}
			}
			netID, err := parseAmsNetID(s.cfg.AmsNetID)
			if err != nil {
				return
			}
			if err := writeAmsTcpMessage(conn, command, netID[:]); err != nil {
				return
			}

		default:
			log.Printf("不支持的 AMS/TCP 命令: 0x%X", command)
			return
		}
	}
}

func (s *AdsServer) Close() error {
	s.closeOnce.Do(func() { close(s.closed) })
	if s.listener != nil {
		_ = s.listener.Close()
	}
	s.connMu.Lock()
	for conn := range s.conns {
		_ = conn.Close()
	}
	s.connMu.Unlock()
	s.wg.Wait()
	return nil
}

func readAmsRequest(reader io.Reader) (amsRequest, error) {
	var tcpHeader [6]byte
	if _, err := io.ReadFull(reader, tcpHeader[:]); err != nil {
		return amsRequest{}, err
	}
	if command := binary.LittleEndian.Uint16(tcpHeader[0:2]); command != amsTcpCommandAds {
		return amsRequest{}, fmt.Errorf("不是 ADS 数据帧: 0x%X", command)
	}
	return readAmsRequestBody(reader, binary.LittleEndian.Uint32(tcpHeader[2:6]))
}

func readAmsRequestBody(reader io.Reader, length uint32) (amsRequest, error) {
	if length < 32 || length > maxAdsFrameSize {
		return amsRequest{}, fmt.Errorf("AMS 帧长度无效: %d", length)
	}
	frame := make([]byte, length)
	if _, err := io.ReadFull(reader, frame); err != nil {
		return amsRequest{}, err
	}
	var header amsHeader
	copy(header.TargetNetID[:], frame[0:6])
	header.TargetPort = binary.LittleEndian.Uint16(frame[6:8])
	copy(header.SourceNetID[:], frame[8:14])
	header.SourcePort = binary.LittleEndian.Uint16(frame[14:16])
	header.Command = binary.LittleEndian.Uint16(frame[16:18])
	header.StateFlags = binary.LittleEndian.Uint16(frame[18:20])
	header.Length = binary.LittleEndian.Uint32(frame[20:24])
	header.ErrorCode = binary.LittleEndian.Uint32(frame[24:28])
	header.InvokeID = binary.LittleEndian.Uint32(frame[28:32])
	if header.Length != length-32 {
		return amsRequest{}, fmt.Errorf("AMS 数据长度不一致")
	}
	return amsRequest{header: header, data: frame[32:]}, nil
}

func writeAmsTcpMessage(writer io.Writer, command uint16, data []byte) error {
	header := make([]byte, 6)
	binary.LittleEndian.PutUint16(header[0:2], command)
	binary.LittleEndian.PutUint32(header[2:6], uint32(len(data)))
	if _, err := writer.Write(header); err != nil {
		return err
	}
	_, err := writer.Write(data)
	return err
}

func parseAmsNetID(value string) ([6]byte, error) {
	var result [6]byte
	parts := strings.Split(strings.TrimSpace(value), ".")
	if len(parts) != 6 {
		return result, fmt.Errorf("应为六段地址，例如 127.0.0.1.1.1")
	}
	for i, part := range parts {
		number, err := strconv.ParseUint(part, 10, 8)
		if err != nil {
			return result, fmt.Errorf("第 %d 段无效: %s", i+1, part)
		}
		result[i] = byte(number)
	}
	return result, nil
}

func writeAmsResponse(writer io.Writer, request amsHeader, data []byte) error {
	frame := make([]byte, 38+len(data))
	binary.LittleEndian.PutUint32(frame[2:6], uint32(32+len(data)))
	copy(frame[6:12], request.SourceNetID[:])
	binary.LittleEndian.PutUint16(frame[12:14], request.SourcePort)
	copy(frame[14:20], request.TargetNetID[:])
	binary.LittleEndian.PutUint16(frame[20:22], request.TargetPort)
	binary.LittleEndian.PutUint16(frame[22:24], request.Command)
	binary.LittleEndian.PutUint16(frame[24:26], request.StateFlags|0x0001)
	binary.LittleEndian.PutUint32(frame[26:30], uint32(len(data)))
	binary.LittleEndian.PutUint32(frame[30:34], 0)
	binary.LittleEndian.PutUint32(frame[34:38], request.InvokeID)
	copy(frame[38:], data)
	_, err := writer.Write(frame)
	return err
}

func (s *AdsServer) process(request amsRequest) []byte {
	data := request.data
	if s.store.verbose {
		log.Printf("ADS Command=%d, TargetPort=%d, Data=%X", request.header.Command, request.header.TargetPort, data)
	}
	switch request.header.Command {
	case adsCmdReadDeviceInfo:
		response := make([]byte, 24)
		response[4] = 3 // TwinCAT major version
		response[5] = 1 // TwinCAT minor version
		binary.LittleEndian.PutUint16(response[6:8], 4024)
		copy(response[8:24], []byte("IPC Ads Simulator"))
		return response

	case adsCmdReadState:
		response := make([]byte, 8)
		binary.LittleEndian.PutUint16(response[4:6], s.cfg.AdsState)
		binary.LittleEndian.PutUint16(response[6:8], s.cfg.DeviceState)
		return response

	case adsCmdRead:
		if len(data) != 12 {
			return resultOnly(adsErrInvalidParameter)
		}
		group := getU32(data, 0)
		offset := getU32(data, 4)
		length := getU32(data, 8)
		value, result := s.readGroup(group, offset, length)
		response := make([]byte, 8+len(value))
		putU32(response, 0, result)
		putU32(response, 4, uint32(len(value)))
		copy(response[8:], value)
		if result == adsErrNone && s.store.verbose {
			log.Printf("读取 handle=%d，%d 字节", offset, len(value))
		}
		return response

	case adsCmdWrite:
		if len(data) < 12 {
			return resultOnly(adsErrInvalidParameter)
		}
		group := getU32(data, 0)
		offset := getU32(data, 4)
		length := getU32(data, 8)
		if uint64(length) != uint64(len(data)-12) {
			return resultOnly(adsErrInvalidSize)
		}
		result := s.writeGroup(group, offset, data[12:])
		return resultOnly(result)

	case adsCmdReadWrite:
		if len(data) < 16 {
			return resultOnly(adsErrInvalidParameter)
		}
		group := getU32(data, 0)
		offset := getU32(data, 4)
		readLength := getU32(data, 8)
		writeLength := getU32(data, 12)
		if uint64(writeLength) != uint64(len(data)-16) {
			return resultOnly(adsErrInvalidSize)
		}
		value, result := s.readWriteGroup(group, offset, readLength, data[16:])
		response := make([]byte, 8+len(value))
		putU32(response, 0, result)
		putU32(response, 4, uint32(len(value)))
		copy(response[8:], value)
		return response

	case adsCmdAddNotification:
		if len(data) < 24 {
			return resultOnly(adsErrInvalidParameter)
		}
		response := make([]byte, 8)
		putU32(response, 4, s.nextNotification.Add(1))
		return response

	case adsCmdDeleteNotification:
		if len(data) != 4 {
			return resultOnly(adsErrInvalidParameter)
		}
		return resultOnly(adsErrNone)

	case adsCmdWriteControl:
		// 状态固定由配置控制；接受请求，避免客户端切换状态时产生无关错误。
		return resultOnly(adsErrNone)
	default:
		return resultOnly(adsErrNotSupported)
	}
}

func (s *AdsServer) readGroup(group, offset, length uint32) ([]byte, uint32) {
	switch group {
	case adsGroupSymbolValueByHandle:
		return s.store.readHandle(offset, length)
	case adsGroupSymbolVersion:
		if length != 4 {
			return nil, adsErrInvalidSize
		}
		value := make([]byte, 4)
		putU32(value, 0, 1)
		return value, adsErrNone
	case adsGroupSymbolUploadInfo2:
		if length != 64 {
			return nil, adsErrInvalidSize
		}
		value := make([]byte, 64)
		putU32(value, 0, 2)      // Upload information format version
		putU32(value, 32, 20127) // ASCII symbol/default-value encoding
		return value, adsErrNone
	default:
		return nil, adsErrInvalidGroup
	}
}

func (s *AdsServer) writeGroup(group, offset uint32, data []byte) uint32 {
	switch group {
	case adsGroupSymbolValueByHandle:
		name, _, result := s.store.writeHandle(offset, data)
		if result == adsErrNone {
			log.Printf("写入 %s（%d 字节）", name, len(data))
		}
		return result
	case adsGroupReleaseSymbolHandle:
		if len(data) != 4 {
			return adsErrInvalidSize
		}
		return s.store.releaseHandle(getU32(data, 0))
	default:
		return adsErrInvalidGroup
	}
}

func (s *AdsServer) readWriteGroup(group, offset, readLength uint32, writeData []byte) ([]byte, uint32) {
	switch group {
	case adsGroupSymbolHandleByName:
		if readLength != 4 || len(writeData) == 0 {
			return nil, adsErrInvalidSize
		}
		name := strings.TrimRight(string(writeData), "\x00")
		handle, result := s.store.createHandle(name)
		if result != adsErrNone {
			log.Printf("找不到点位: %s", name)
			return nil, result
		}
		response := make([]byte, 4)
		putU32(response, 0, handle)
		if s.store.verbose {
			log.Printf("创建符号句柄: %s -> %d", name, handle)
		}
		return response, adsErrNone

	case adsGroupSymbolValueByName:
		// 兼容直接按符号名读写：写入数据为名称，读取值为变量内容。
		name := strings.TrimRight(string(writeData), "\x00")
		handle, result := s.store.createHandle(name)
		if result != adsErrNone {
			return nil, result
		}
		defer s.store.releaseHandle(handle)
		return s.store.readHandle(handle, readLength)

	case adsGroupSumRead:
		return s.sumRead(offset, writeData)
	case adsGroupSumWrite:
		return s.sumWrite(offset, writeData)
	case adsGroupSumReadWrite:
		return s.sumReadWrite(offset, writeData)
	default:
		log.Printf("不支持的 ReadWrite IndexGroup=0x%X, IndexOffset=0x%X", group, offset)
		return nil, adsErrInvalidGroup
	}
}

func (s *AdsServer) sumRead(count uint32, data []byte) ([]byte, uint32) {
	if count == 0 {
		count = uint32(len(data) / 12)
	}
	if count == 0 || count > 500 || uint64(len(data)) != uint64(count)*12 {
		return nil, adsErrInvalidSize
	}
	results := make([]uint32, count)
	values := make([][]byte, count)
	total := int(count) * 4
	for i := uint32(0); i < count; i++ {
		base := int(i) * 12
		values[i], results[i] = s.readGroup(getU32(data, base), getU32(data, base+4), getU32(data, base+8))
		total += len(values[i])
	}
	response := make([]byte, total)
	position := int(count) * 4
	for i := uint32(0); i < count; i++ {
		putU32(response, int(i)*4, results[i])
		copy(response[position:], values[i])
		position += len(values[i])
	}
	return response, adsErrNone
}

func (s *AdsServer) sumWrite(count uint32, data []byte) ([]byte, uint32) {
	if count == 0 || count > 500 || uint64(len(data)) < uint64(count)*12 {
		return nil, adsErrInvalidSize
	}
	dataPosition := int(count) * 12
	results := make([]byte, int(count)*4)
	for i := uint32(0); i < count; i++ {
		base := int(i) * 12
		length := int(getU32(data, base+8))
		if length < 0 || dataPosition+length > len(data) {
			return nil, adsErrInvalidSize
		}
		result := s.writeGroup(getU32(data, base), getU32(data, base+4), data[dataPosition:dataPosition+length])
		putU32(results, int(i)*4, result)
		dataPosition += length
	}
	if dataPosition != len(data) {
		return nil, adsErrInvalidSize
	}
	return results, adsErrNone
}

func (s *AdsServer) sumReadWrite(count uint32, data []byte) ([]byte, uint32) {
	if count == 0 || count > 500 || uint64(len(data)) < uint64(count)*16 {
		return nil, adsErrInvalidSize
	}
	dataPosition := int(count) * 16
	results := make([]uint32, count)
	lengths := make([]uint32, count)
	values := make([][]byte, count)
	total := int(count) * 8
	for i := uint32(0); i < count; i++ {
		base := int(i) * 16
		writeLength := int(getU32(data, base+12))
		if writeLength < 0 || dataPosition+writeLength > len(data) {
			return nil, adsErrInvalidSize
		}
		values[i], results[i] = s.readWriteGroup(
			getU32(data, base), getU32(data, base+4), getU32(data, base+8),
			data[dataPosition:dataPosition+writeLength])
		lengths[i] = uint32(len(values[i]))
		total += len(values[i])
		dataPosition += writeLength
	}
	if dataPosition != len(data) {
		return nil, adsErrInvalidSize
	}
	response := make([]byte, total)
	position := int(count) * 8
	for i := uint32(0); i < count; i++ {
		putU32(response, int(i)*8, results[i])
		putU32(response, int(i)*8+4, lengths[i])
		copy(response[position:], values[i])
		position += len(values[i])
	}
	return response, adsErrNone
}

func resultOnly(result uint32) []byte {
	response := make([]byte, 4)
	putU32(response, 0, result)
	return response
}

func getU32(data []byte, offset int) uint32 {
	return binary.LittleEndian.Uint32(data[offset : offset+4])
}

func putU32(data []byte, offset int, value uint32) {
	binary.LittleEndian.PutUint32(data[offset:offset+4], value)
}

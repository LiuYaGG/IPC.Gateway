package gofins

import (
	"bufio"
	"context"
	"encoding/binary"
	"fmt"
	"io"
	"net"
	"time"
)

// transport abstracts the wire-level operations so the client logic can work
// with both UDP and TCP implementations.
type transport interface {
	Send(ctx context.Context, payload []byte) error
	Recv(ctx context.Context) ([]byte, error)
	Close() error
	LocalAddr() net.Addr
	RemoteAddr() net.Addr
}

// udpTransport is a thin wrapper around net.UDPConn to satisfy the transport interface.
type udpTransport struct {
	conn   *net.UDPConn
	reader *bufio.Reader
}

func newUDPTransport(local, remote *net.UDPAddr) (*udpTransport, error) {
	conn, err := net.DialUDP("udp", local, remote)
	if err != nil {
		return nil, err
	}
	return &udpTransport{
		conn:   conn,
		reader: bufio.NewReader(conn),
	}, nil
}

func (t *udpTransport) Send(ctx context.Context, payload []byte) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	_, err := t.conn.Write(payload)
	return err
}

func (t *udpTransport) Recv(ctx context.Context) ([]byte, error) {
	if deadline, ok := ctx.Deadline(); ok {
		_ = t.conn.SetReadDeadline(deadline)
	} else {
		_ = t.conn.SetReadDeadline(time.Time{})
	}

	buf := make([]byte, READ_BUFFER_SIZE)
	n, err := t.reader.Read(buf)
	if err != nil {
		// If the context expired, surface that instead of a timeout error.
		if ctx.Err() != nil {
			return nil, ctx.Err()
		}
		return nil, err
	}
	return buf[:n], nil
}

func (t *udpTransport) Close() error {
	return t.conn.Close()
}

func (t *udpTransport) LocalAddr() net.Addr {
	return t.conn.LocalAddr()
}

func (t *udpTransport) RemoteAddr() net.Addr {
	return t.conn.RemoteAddr()
}

const (
	finsTCPSignature                = "FINS"
	finsTCPHandshakeCommand         = uint32(0x00000000)
	finsTCPHandshakeResponseCommand = uint32(0x00000001)
	finsTCPDataCommand              = uint32(0x00000002)
)

// tcpTransport implements FINS over TCP with framing and the initial handshake.
type tcpTransport struct {
	conn   net.Conn
	reader *bufio.Reader
}

func newTCPTransport(ctx context.Context, local, remote *net.TCPAddr) (*tcpTransport, error) {
	dialer := net.Dialer{
		LocalAddr: local,
		Timeout:   5 * time.Second,
	}
	conn, err := dialer.DialContext(ctx, "tcp", remote.String())
	if err != nil {
		return nil, err
	}

	if tc, ok := conn.(*net.TCPConn); ok {
		_ = tc.SetKeepAlive(true)
		_ = tc.SetNoDelay(true)
	}

	t := &tcpTransport{
		conn:   conn,
		reader: bufio.NewReader(conn),
	}

	if err := t.handshake(ctx); err != nil {
		_ = conn.Close()
		return nil, err
	}

	return t, nil
}

func (t *tcpTransport) handshake(ctx context.Context) error {
	// Minimal handshake frame per FINS/TCP: command 0x0 with an empty body.
	if err := t.writeFrame(ctx, finsTCPHandshakeCommand, nil); err != nil {
		return err
	}

	resp, err := t.readFrame(ctx)
	if err != nil {
		return err
	}

	if resp.command != finsTCPHandshakeCommand {
		return fmt.Errorf("unexpected handshake command: %d", resp.command)
	}
	if resp.errorCode != 0 {
		return fmt.Errorf("handshake failed with error code: %d", resp.errorCode)
	}

	return nil
}

func (t *tcpTransport) Send(ctx context.Context, payload []byte) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	return t.writeFrame(ctx, finsTCPDataCommand, payload)
}

func (t *tcpTransport) Recv(ctx context.Context) ([]byte, error) {
	frame, err := t.readFrame(ctx)
	if err != nil {
		return nil, err
	}
	if frame.errorCode != 0 {
		return nil, fmt.Errorf("FINS/TCP error: %d", frame.errorCode)
	}
	return frame.body, nil
}

func (t *tcpTransport) readFrame(ctx context.Context) (*finsTCPMessage, error) {
	if deadline, ok := ctx.Deadline(); ok {
		_ = t.conn.SetReadDeadline(deadline)
	} else {
		_ = t.conn.SetReadDeadline(time.Time{})
	}

	header := make([]byte, 8)
	if _, err := io.ReadFull(t.reader, header); err != nil {
		if ctx.Err() != nil {
			return nil, ctx.Err()
		}
		return nil, err
	}

	if string(header[:4]) != finsTCPSignature {
		return nil, fmt.Errorf("invalid FINS/TCP signature: %q", header[:4])
	}

	length := binary.BigEndian.Uint32(header[4:8])
	if length < 8 {
		return nil, fmt.Errorf("invalid FINS/TCP length: %d", length)
	}

	body := make([]byte, length)
	if _, err := io.ReadFull(t.reader, body); err != nil {
		if ctx.Err() != nil {
			return nil, ctx.Err()
		}
		return nil, err
	}

	return &finsTCPMessage{
		command:   binary.BigEndian.Uint32(body[0:4]),
		errorCode: binary.BigEndian.Uint32(body[4:8]),
		body:      body[8:],
	}, nil
}

func (t *tcpTransport) Close() error {
	return t.conn.Close()
}

func (t *tcpTransport) LocalAddr() net.Addr {
	return t.conn.LocalAddr()
}

func (t *tcpTransport) RemoteAddr() net.Addr {
	return t.conn.RemoteAddr()
}

type finsTCPMessage struct {
	command   uint32
	errorCode uint32
	body      []byte
}

// finsTCPFrame builds a framed FINS/TCP packet.
// Note: caller is responsible for writing the returned frame to the TCP connection.
func finsTCPFrame(command uint32, body []byte) []byte {
	frame := make([]byte, 16+len(body))
	copy(frame[0:4], []byte(finsTCPSignature))
	binary.BigEndian.PutUint32(frame[4:8], uint32(len(body)+8)) // cmd + err + body
	binary.BigEndian.PutUint32(frame[8:12], command)
	binary.BigEndian.PutUint32(frame[12:16], 0) // error code reserved for responses
	copy(frame[16:], body)
	return frame
}

func (t *tcpTransport) writeFrame(ctx context.Context, command uint32, body []byte) error {
	frame := finsTCPFrame(command, body)
	_, err := t.conn.Write(frame)
	if err != nil {
		if ctx.Err() != nil {
			return ctx.Err()
		}
		return err
	}
	return nil
}

package main

import (
	"context"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"log"
	"net"
	"os"
	"strconv"
	"strings"
	"time"

	fins "github.com/bronystylecrazy/gofins"
	"github.com/peterh/liner"
)

type outputMode string

const (
	outputText outputMode = "text"
	outputJSON outputMode = "json"
)

type output struct {
	mode      outputMode
	quiet     bool
	transport string
}

func newOutput(mode string, quiet bool, transport string) (output, error) {
	if transport == "" {
		transport = "udp"
	}
	switch strings.ToLower(mode) {
	case string(outputText):
		return output{mode: outputText, quiet: quiet, transport: strings.ToLower(transport)}, nil
	case string(outputJSON):
		return output{mode: outputJSON, quiet: quiet, transport: strings.ToLower(transport)}, nil
	default:
		return output{}, fmt.Errorf("unsupported format %q (use text or json)", mode)
	}
}

func (o output) print(label string, v interface{}) error {
	if o.quiet {
		label = ""
	}
	switch o.mode {
	case outputText:
		if label != "" {
			fmt.Printf("%s: %v\n", label, v)
		} else {
			fmt.Printf("%v\n", v)
		}
		return nil
	case outputJSON:
		type wrapped struct {
			Label  string      `json:"label,omitempty"`
			Result interface{} `json:"result"`
		}
		w := wrapped{Label: label, Result: v}
		enc := json.NewEncoder(os.Stdout)
		enc.SetIndent("", "  ")
		return enc.Encode(w)
	default:
		return fmt.Errorf("unknown output mode")
	}
}

func (o output) printWithRTT(label string, v interface{}, rtt time.Duration) error {
	if o.quiet {
		label = ""
	}
	suffix := fmt.Sprintf("(%s %dms)", o.transport, rtt.Milliseconds())
	switch o.mode {
	case outputText:
		if label != "" {
			fmt.Printf("%s: %v %s\n", label, v, suffix)
		} else {
			fmt.Printf("%v %s\n", v, suffix)
		}
		return nil
	case outputJSON:
		type wrapped struct {
			Label   string      `json:"label,omitempty"`
			Result  interface{} `json:"result"`
			RTTMsec int64       `json:"rtt_ms"`
			Mode    string      `json:"mode"`
		}
		w := wrapped{Label: label, Result: v, RTTMsec: rtt.Milliseconds(), Mode: o.transport}
		enc := json.NewEncoder(os.Stdout)
		enc.SetIndent("", "  ")
		return enc.Encode(w)
	default:
		return fmt.Errorf("unknown output mode")
	}
}

func (o output) printError(err error) {
	if err == nil {
		return
	}
	if o.mode == outputJSON {
		type errPayload struct {
			Error string `json:"error"`
		}
		enc := json.NewEncoder(os.Stderr)
		enc.SetIndent("", "  ")
		_ = enc.Encode(errPayload{Error: err.Error()})
		return
	}
	fmt.Fprintln(os.Stderr, err)
}

func main() {
	remoteHost := flag.String("remote-host", "127.0.0.1", "Target PLC host/IP")
	remotePort := flag.Int("remote-port", 9600, "Target PLC port")
	remoteNetwork := flag.Int("remote-network", 0, "Target FINS network number")
	remoteNode := flag.Int("remote-node", 10, "Target FINS node address")
	remoteUnit := flag.Int("remote-unit", 0, "Target FINS unit address")

	localHost := flag.String("local-host", "", "Local host/IP to bind (auto-detect if empty)")
	localPort := flag.Int("local-port", 0, "Local port (0 = auto)")
	localNetwork := flag.Int("local-network", 0, "Local FINS network number")
	localNode := flag.Int("local-node", 2, "Local FINS node address")
	localUnit := flag.Int("local-unit", 0, "Local FINS unit address")

	timeout := flag.Duration("timeout", 5*time.Second, "Per-command timeout")
	respTimeout := flag.Uint("resp-timeout-ms", 1000, "FINS response timeout in milliseconds (0 = wait indefinitely)")
	execOnce := flag.String("exec", "", "Execute a single command (quoted) and exit (e.g., \"readwords dm 100 3\")")
	execInterval := flag.Duration("exec-interval", 0, "If set with -exec, repeat the command with this interval (e.g., 500ms)")
	autoReconnect := flag.Bool("auto-reconnect", true, "Enable auto-reconnect")
	autoReconnectDelay := flag.Duration("auto-reconnect-delay", time.Second, "Initial delay for auto-reconnect backoff")
	noReconnectBackoff := flag.Bool("no-reconnect-backoff", false, "Disable exponential backoff; reuse the initial delay for every reconnect")
	format := flag.String("format", "text", "Output format: text|json")
	quiet := flag.Bool("quiet", false, "Quiet output (suppress info, show only command output)")
	tcp := flag.Bool("tcp", false, "Use FINS over TCP instead of UDP")
	flag.Parse()

	log.SetFlags(0)

	transportMode := "udp"
	if *tcp {
		transportMode = "tcp"
	}

	out, err := newOutput(*format, *quiet, transportMode)
	if err != nil {
		log.Fatalf("%v", err)
	}
	if *quiet {
		log.SetOutput(io.Discard)
	}

	// Resolve remote host if a hostname was provided.
	if host, port, err := resolveHost(*remoteHost, *remotePort, *tcp); err == nil {
		*remoteHost = host
		*remotePort = port
	} else {
		log.Fatalf("failed to resolve remote host: %v", err)
	}

	remoteEndpoint := fmt.Sprintf("%s:%d", *remoteHost, *remotePort)
	// Resolve local host if set to a hostname (otherwise leave empty for auto-detect).
	if *localHost != "" {
		if host, port, err := resolveHost(*localHost, *localPort, *tcp); err == nil {
			*localHost = host
			*localPort = port
		} else {
			log.Printf("warning: could not resolve local host %q: %v (will try anyway)", *localHost, err)
		}
	}

	if *localHost == "" || *localPort == 0 {
		if ip, port, err := detectLocalAddr(remoteEndpoint, *tcp); err == nil {
			if *localHost == "" && ip != "" {
				*localHost = ip
			}
			if !*tcp && *localPort == 0 && port > 0 {
				*localPort = port
			}
		}
	}

	var localAddr, remoteAddr fins.Address
	if *tcp {
		localAddr = fins.NewTCPAddress(*localHost, *localPort, byte(*localNetwork), byte(*localNode), byte(*localUnit))
		remoteAddr = fins.NewTCPAddress(*remoteHost, *remotePort, byte(*remoteNetwork), byte(*remoteNode), byte(*remoteUnit))
	} else {
		localAddr = fins.NewAddress(*localHost, *localPort, byte(*localNetwork), byte(*localNode), byte(*localUnit))
		remoteAddr = fins.NewAddress(*remoteHost, *remotePort, byte(*remoteNetwork), byte(*remoteNode), byte(*remoteUnit))
	}

	var client *fins.Client
	if *tcp {
		client, err = fins.NewTCPClient(localAddr, remoteAddr)
	} else {
		client, err = fins.NewUDPClient(localAddr, remoteAddr)
	}
	if err != nil {
		log.Fatalf("failed to create client: %v", err)
	}
	// When network interfaces change (e.g., VPN toggles), re-detect the best local bind address.
	client.EnableDynamicLocalAddress()
	client.SetTimeoutMs(*respTimeout)
	if *autoReconnect {
		client.EnableAutoReconnect(0, *autoReconnectDelay) // 0 = infinite retries
		if *noReconnectBackoff {
			client.DisableReconnectBackoff()
		}
	}
	defer client.Close()

	transport := strings.ToUpper(transportMode)
	if *tcp {
		transport = "TCP"
	}
	log.Printf("Connected to PLC %s (%s) (net=%d node=%d unit=%d) from local %s (net=%d node=%d unit=%d)",
		formatAddr(remoteAddr.UdpAddress, remoteAddr.TcpAddress, *tcp), transport, remoteAddr.FinAddress.Network, remoteAddr.FinAddress.Node, remoteAddr.FinAddress.Unit,
		formatAddr(localAddr.UdpAddress, localAddr.TcpAddress, *tcp), localAddr.FinAddress.Network, localAddr.FinAddress.Node, localAddr.FinAddress.Unit)

	if !out.quiet {
		printHelp()
	}

	// Non-interactive execution path.
	if strings.TrimSpace(*execOnce) != "" {
		fields := strings.Fields(*execOnce)
		if len(fields) == 0 {
			log.Fatalf("no command provided to -exec")
		}
		cmd := strings.ToLower(fields[0])
		for {
			ctx, cancel := context.WithTimeout(context.Background(), *timeout)
			err := handleCommand(ctx, out, client, cmd, fields[1:])
			cancel()
			if err != nil {
				if out.quiet {
					out.printError(err)
				} else {
					log.Printf("error: %v", err)
				}
			}
			if *execInterval <= 0 {
				if err != nil && out.quiet {
					os.Exit(1)
				}
				return
			}
			time.Sleep(*execInterval)
		}
	}

	line := liner.NewLiner()
	defer line.Close()
	line.SetCtrlCAborts(true)

	for {
		input, err := line.Prompt("> ")
		if errors.Is(err, liner.ErrPromptAborted) {
			// Ctrl+C pressed, keep session alive.
			continue
		}
		if err != nil {
			if errors.Is(err, io.EOF) {
				fmt.Println()
				return
			}
			log.Printf("prompt error: %v", err)
			continue
		}

		line.AppendHistory(input)
		text := strings.TrimSpace(input)
		if text == "" {
			continue
		}
		switch strings.ToLower(text) {
		case "exit", "quit":
			return
		case "help", "?":
			printHelp()
			continue
		}

		fields := strings.Fields(text)
		cmd := strings.ToLower(fields[0])

		ctx, cancel := context.WithTimeout(context.Background(), *timeout)
		err = handleCommand(ctx, out, client, cmd, fields[1:])
		cancel()

		if err != nil {
			out.printError(err)
		}
	}
}

func resolveHost(host string, port int, useTCP bool) (string, int, error) {
	if host == "" || net.ParseIP(host) != nil {
		return host, port, nil
	}
	if useTCP {
		addr, err := net.ResolveTCPAddr("tcp", fmt.Sprintf("%s:%d", host, port))
		if err != nil {
			return "", 0, err
		}
		return addr.IP.String(), addr.Port, nil
	}
	addr, err := net.ResolveUDPAddr("udp", fmt.Sprintf("%s:%d", host, port))
	if err != nil {
		return "", 0, err
	}
	return addr.IP.String(), addr.Port, nil
}

func detectLocalAddr(remote string, useTCP bool) (string, int, error) {
	network := "udp"
	if useTCP {
		network = "tcp"
	}
	conn, err := net.DialTimeout(network, remote, 2*time.Second)
	if err != nil {
		return "", 0, err
	}
	defer conn.Close()

	switch addr := conn.LocalAddr().(type) {
	case *net.UDPAddr:
		ip := ""
		if addr.IP != nil {
			ip = addr.IP.String()
		}
		return ip, addr.Port, nil
	case *net.TCPAddr:
		ip := ""
		if addr.IP != nil {
			ip = addr.IP.String()
		}
		return ip, addr.Port, nil
	default:
		return "", 0, fmt.Errorf("unexpected local address type %T", conn.LocalAddr())
	}
}

func formatAddr(udp *net.UDPAddr, tcp *net.TCPAddr, useTCP bool) string {
	if useTCP && tcp != nil {
		return tcp.String()
	}
	if !useTCP && udp != nil {
		return udp.String()
	}
	if tcp != nil {
		return tcp.String()
	}
	if udp != nil {
		return udp.String()
	}
	return ""
}

func handleCommand(ctx context.Context, out output, client *fins.Client, cmd string, args []string) error {
	start := time.Now()
	switch cmd {
	case "readw", "readwords":
		if len(args) != 3 {
			return fmt.Errorf("usage: readwords <area> <address> <count>")
		}
		area, err := parseWordArea(args[0])
		if err != nil {
			return err
		}
		address, err := parseUint16(args[1])
		if err != nil {
			return err
		}
		count, err := parseUint16(args[2])
		if err != nil {
			return err
		}
		data, err := client.ReadWords(ctx, area, address, count)
		if err != nil {
			return err
		}
		return out.printWithRTT("words", data, time.Since(start))

	case "readb", "readbytes":
		if len(args) != 3 {
			return fmt.Errorf("usage: readbytes <area> <address> <count>")
		}
		area, err := parseWordArea(args[0])
		if err != nil {
			return err
		}
		address, err := parseUint16(args[1])
		if err != nil {
			return err
		}
		count, err := parseUint16(args[2])
		if err != nil {
			return err
		}
		data, err := client.ReadBytes(ctx, area, address, count)
		if err != nil {
			return err
		}
		return out.printWithRTT("bytes", data, time.Since(start))

	case "readbits":
		if len(args) != 4 {
			return fmt.Errorf("usage: readbits <area> <address> <bitOffset> <count>")
		}
		area, err := parseBitArea(args[0])
		if err != nil {
			return err
		}
		address, err := parseUint16(args[1])
		if err != nil {
			return err
		}
		bitOffset, err := parseByte(args[2])
		if err != nil {
			return err
		}
		count, err := parseUint16(args[3])
		if err != nil {
			return err
		}
		data, err := client.ReadBits(ctx, area, address, bitOffset, count)
		if err != nil {
			return err
		}
		return out.printWithRTT("bits", data, time.Since(start))

	case "writew", "writewords":
		if len(args) < 3 {
			return fmt.Errorf("usage: writewords <area> <address> <values...>")
		}
		area, err := parseWordArea(args[0])
		if err != nil {
			return err
		}
		address, err := parseUint16(args[1])
		if err != nil {
			return err
		}
		values, err := parseUint16Slice(args[2:])
		if err != nil {
			return err
		}
		if err := client.WriteWords(ctx, area, address, values); err != nil {
			return err
		}
		return out.printWithRTT("writewords", "ok", time.Since(start))

	case "writeb", "writebytes":
		if len(args) < 3 {
			return fmt.Errorf("usage: writebytes <area> <address> <values...>")
		}
		area, err := parseWordArea(args[0])
		if err != nil {
			return err
		}
		address, err := parseUint16(args[1])
		if err != nil {
			return err
		}
		values, err := parseByteSlice(args[2:])
		if err != nil {
			return err
		}
		if err := client.WriteBytes(ctx, area, address, values); err != nil {
			return err
		}
		return out.printWithRTT("writebytes", "ok", time.Since(start))

	case "writebits":
		if len(args) < 4 {
			return fmt.Errorf("usage: writebits <area> <address> <bitOffset> <values...>")
		}
		area, err := parseBitArea(args[0])
		if err != nil {
			return err
		}
		address, err := parseUint16(args[1])
		if err != nil {
			return err
		}
		bitOffset, err := parseByte(args[2])
		if err != nil {
			return err
		}
		values, err := parseBoolSlice(args[3:])
		if err != nil {
			return err
		}
		if err := client.WriteBits(ctx, area, address, bitOffset, values); err != nil {
			return err
		}
		return out.printWithRTT("writebits", "ok", time.Since(start))

	case "clock", "readclock":
		t, err := client.ReadClock(ctx)
		if err != nil {
			return err
		}
		return out.printWithRTT("clock", t.Format(time.RFC3339), time.Since(start))

	default:
		if !out.quiet {
			printHelp()
		}
		return fmt.Errorf("unknown command %q", cmd)
	}
}

func parseWordArea(s string) (byte, error) {
	switch strings.ToLower(s) {
	case "dm", "dmw", "dmword":
		return fins.MemoryAreaDMWord, nil
	case "wr", "wrw", "wrword":
		return fins.MemoryAreaWRWord, nil
	case "hr", "hrw", "hrword":
		return fins.MemoryAreaHRWord, nil
	case "ar", "arw", "arword":
		return fins.MemoryAreaARWord, nil
	case "cio", "ciow", "cioword":
		return fins.MemoryAreaCIOWord, nil
	default:
		return 0, fmt.Errorf("unknown word area %q", s)
	}
}

func parseBitArea(s string) (byte, error) {
	switch strings.ToLower(s) {
	case "dm", "dmb", "dmbit":
		return fins.MemoryAreaDMBit, nil
	case "wr", "wrb", "wrbit":
		return fins.MemoryAreaWRBit, nil
	case "hr", "hrb", "hrbit":
		return fins.MemoryAreaHRBit, nil
	case "ar", "arb", "arbit":
		return fins.MemoryAreaARBit, nil
	case "cio", "ciob", "ciobit":
		return fins.MemoryAreaCIOBit, nil
	default:
		return 0, fmt.Errorf("unknown bit area %q", s)
	}
}

func parseUint16(s string) (uint16, error) {
	v, err := parseUintWithSize(s, 16)
	return uint16(v), err
}

func parseByte(s string) (byte, error) {
	v, err := parseUintWithSize(s, 8)
	return byte(v), err
}

func parseUintWithSize(s string, bits int) (uint64, error) {
	v, err := strconv.ParseUint(s, 10, bits)
	if err != nil {
		return 0, fmt.Errorf("invalid number %q: %w", s, err)
	}
	return v, nil
}

func parseUint16Slice(values []string) ([]uint16, error) {
	out := make([]uint16, len(values))
	for i, v := range values {
		val, err := parseUint16(v)
		if err != nil {
			return nil, err
		}
		out[i] = val
	}
	return out, nil
}

func parseByteSlice(values []string) ([]byte, error) {
	out := make([]byte, len(values))
	for i, v := range values {
		val, err := parseUintWithSize(v, 8)
		if err != nil {
			return nil, err
		}
		out[i] = byte(val)
	}
	return out, nil
}

func parseBoolSlice(values []string) ([]bool, error) {
	out := make([]bool, len(values))
	for i, v := range values {
		switch strings.ToLower(v) {
		case "1", "true", "t", "on":
			out[i] = true
		case "0", "false", "f", "off":
			out[i] = false
		default:
			return nil, fmt.Errorf("invalid bit value %q (use 0/1/true/false)", v)
		}
	}
	return out, nil
}

func printHelp() {
	fmt.Println("Commands:")
	fmt.Println("  readwords <area> <address> <count>      - read words (e.g., readwords dm 100 3)")
	fmt.Println("  readbytes <area> <address> <count>      - read bytes (e.g., readbytes dm 100 6)")
	fmt.Println("  readbits <area> <address> <bitOffset> <count>")
	fmt.Println("  writewords <area> <address> <values...> - write words (uint16)")
	fmt.Println("  writebytes <area> <address> <values...> - write bytes (0-255)")
	fmt.Println("  writebits <area> <address> <bitOffset> <values...> - write bits (0/1)")
	fmt.Println("  clock                                    - read PLC clock")
	fmt.Println("  help                                     - show this message")
	fmt.Println("  exit|quit                                - leave the session")
	fmt.Println("Areas: dm, wr, hr, ar, cio (append 'bit' or 'b' for bit areas; default word areas)")
}

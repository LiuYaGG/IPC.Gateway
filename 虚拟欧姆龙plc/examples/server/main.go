package main

import (
	"flag"
	"log"
	"os"
	"os/signal"
	"syscall"

	fins "github.com/bronystylecrazy/gofins"
	"github.com/kardianos/service"
)

type program struct {
	host    string
	port    int
	network int
	node    int
	unit    int
	tcp     bool
	server  *fins.Server
	logger  service.Logger
	done    chan struct{}
}

func main() {
	args := os.Args[1:]
	var svcCmd string
	if len(args) > 0 {
		switch args[0] {
		case "start", "stop", "restart", "install", "uninstall":
			svcCmd = args[0]
			args = args[1:]
		}
	}

	fs := flag.NewFlagSet(os.Args[0], flag.ExitOnError)
	host := fs.String("host", "127.0.0.1", "Host/IP to bind the PLC simulator")
	port := fs.Int("port", 9600, "Port for the PLC simulator")
	network := fs.Int("network", 0, "FINS network number for the PLC")
	node := fs.Int("node", 10, "FINS node address for the PLC")
	unit := fs.Int("unit", 0, "FINS unit address for the PLC")
	tcp := fs.Bool("tcp", false, "Serve FINS over TCP instead of UDP")

	svcName := fs.String("svc-name", "fins-mock", "Service name")
	svcDisplay := fs.String("svc-display", "FINS Mock Server", "Service display name")
	svcDesc := fs.String("svc-desc", "Omron FINS mock server", "Service description")
	_ = fs.Parse(args)

	prog := &program{
		host:    *host,
		port:    *port,
		network: *network,
		node:    *node,
		unit:    *unit,
		tcp:     *tcp,
		done:    make(chan struct{}),
	}

	config := &service.Config{
		Name:        *svcName,
		DisplayName: *svcDisplay,
		Description: *svcDesc,
		Arguments:   fs.Args(),
	}

	svc, err := service.New(prog, config)
	if err != nil {
		log.Fatalf("failed to create service: %v", err)
	}

	logger, err := svc.Logger(nil)
	if err != nil {
		log.Printf("service logger unavailable, using default logger: %v", err)
	} else {
		prog.logger = logger
	}

	if svcCmd != "" {
		if err := service.Control(svc, svcCmd); err != nil {
			log.Fatalf("service %s failed: %v", svcCmd, err)
		}
		log.Printf("service %s OK", svcCmd)
		return
	}

	if err := svc.Run(); err != nil {
		log.Fatalf("service run failed: %v", err)
	}
}

func (p *program) Start(s service.Service) error {
	go p.run()
	return nil
}

func (p *program) run() {
	plcAddr := fins.NewAddress(p.host, p.port, byte(p.network), byte(p.node), byte(p.unit))
	opts := []fins.ServerOption{}
	if p.tcp {
		opts = append(opts, fins.WithTCPTransport())
	}

	srv, err := fins.NewPLCSimulator(plcAddr, opts...)
	if err != nil {
		p.logf("failed to start PLC simulator: %v", err)
		return
	}
	p.server = srv

	go func() {
		if err := <-srv.Err(); err != nil {
			p.logf("server error: %v", err)
		}
	}()

	addr := plcAddr.UdpAddress.String()
	transport := "udp"
	if p.tcp {
		addr = plcAddr.TcpAddress.String()
		transport = "tcp"
	}

	p.logf("PLC simulator listening on %s (%s) (network=%d node=%d unit=%d)", addr, transport, plcAddr.FinAddress.Network, plcAddr.FinAddress.Node, plcAddr.FinAddress.Unit)

	sigCh := make(chan os.Signal, 1)
	signal.Notify(sigCh, os.Interrupt, syscall.SIGTERM)

	select {
	case <-sigCh:
	case <-p.done:
	}

	p.logf("shutting down simulator...")
	_ = srv.Close()
}

func (p *program) Stop(s service.Service) error {
	close(p.done)
	if p.server != nil {
		return p.server.Close()
	}
	return nil
}

func (p *program) logf(format string, args ...interface{}) {
	if p.logger != nil {
		_ = p.logger.Infof(format, args...)
		return
	}
	log.Printf(format, args...)
}

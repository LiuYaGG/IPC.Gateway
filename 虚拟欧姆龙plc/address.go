package gofins

import "net"

// FinsAddress A FINS device address
type FinsAddress struct {
	Network byte
	Node    byte
	Unit    byte
}

// Address A full device address
type Address struct {
	FinAddress FinsAddress
	UdpAddress *net.UDPAddr
	TcpAddress *net.TCPAddr
}

func NewAddress(ip string, port int, network, node, unit byte) Address {
	return Address{
		UdpAddress: &net.UDPAddr{
			IP:   net.ParseIP(ip),
			Port: port,
		},
		TcpAddress: &net.TCPAddr{
			IP:   net.ParseIP(ip),
			Port: port,
		},
		FinAddress: FinsAddress{
			Network: network,
			Node:    node,
			Unit:    unit,
		},
	}
}

func NewLocalAddress(network, node, unit byte) Address {
	return Address{
		FinAddress: FinsAddress{
			Network: network,
			Node:    node,
			Unit:    unit,
		},
		UdpAddress: nil,
		TcpAddress: nil,
	}
}

// NewTCPAddress creates an Address for TCP-only connections (no UDP address).
func NewTCPAddress(ip string, port int, network, node, unit byte) Address {
	return Address{
		TcpAddress: &net.TCPAddr{
			IP:   net.ParseIP(ip),
			Port: port,
		},
		FinAddress: FinsAddress{
			Network: network,
			Node:    node,
			Unit:    unit,
		},
	}
}

// NewUDPAddress creates an Address for UDP-only connections (no TCP address).
func NewUDPAddress(ip string, port int, network, node, unit byte) Address {
	return Address{
		UdpAddress: &net.UDPAddr{
			IP:   net.ParseIP(ip),
			Port: port,
		},
		FinAddress: FinsAddress{
			Network: network,
			Node:    node,
			Unit:    unit,
		},
	}
}

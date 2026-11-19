package gofins

import (
	"sync"
	"time"
)

// ConnectionEventType describes the type of connection event.
type ConnectionEventType string

const (
	ConnectionEventConnected    ConnectionEventType = "connected"
	ConnectionEventDisconnected ConnectionEventType = "disconnected"
)

// ConnectionEvent is emitted whenever the client connects or disconnects.
type ConnectionEvent struct {
	Time      time.Time
	Type      ConnectionEventType
	Err       error         // Set on disconnect
	Downtime  time.Duration // Time spent disconnected (on connect)
	Connected bool          // Current connection state after the event
}

// ConnectionStats contains snapshot metrics about connection health.
type ConnectionStats struct {
	Connected         bool
	LastConnected     time.Time
	LastDisconnected  time.Time
	CurrentDowntime   time.Duration
	TotalDowntime     time.Duration
	LastDisconnectErr error
}

// ConnectionWatchdog is a plugin that tracks connection uptime/downtime and emits events.
// Hooks are non-blocking; events are dropped if the channel buffer is full.
type ConnectionWatchdog struct {
	eventBuf int

	events chan ConnectionEvent

	mu sync.RWMutex

	// guarded by mu
	connected        bool
	lastConnected    time.Time
	lastDisconnected time.Time
	downtimeStart    time.Time
	totalDowntime    time.Duration
	lastErr          error
}

// NewConnectionWatchdog creates a new watchdog plugin.
// eventBuffer controls the channel buffer size for Events(); use 0 for the default of 16.
func NewConnectionWatchdog(eventBuffer int) *ConnectionWatchdog {
	if eventBuffer <= 0 {
		eventBuffer = 16
	}
	return &ConnectionWatchdog{
		eventBuf: eventBuffer,
		events:   make(chan ConnectionEvent, eventBuffer),
	}
}

// Name implements Plugin.
func (w *ConnectionWatchdog) Name() string { return "connection_watchdog" }

// Initialize implements Plugin. No-op.
func (w *ConnectionWatchdog) Initialize(*Client) error { return nil }

// OnConnected implements ConnectionPlugin.
func (w *ConnectionWatchdog) OnConnected(*Client) error {
	now := time.Now()
	var downtime time.Duration

	w.mu.Lock()
	if !w.downtimeStart.IsZero() {
		downtime = time.Since(w.downtimeStart)
		w.totalDowntime += downtime
		w.downtimeStart = time.Time{}
	}
	w.connected = true
	w.lastConnected = now
	w.mu.Unlock()

	w.emit(ConnectionEvent{
		Time:      now,
		Type:      ConnectionEventConnected,
		Downtime:  downtime,
		Connected: true,
	})
	return nil
}

// OnDisconnected implements ConnectionPlugin.
func (w *ConnectionWatchdog) OnDisconnected(_ *Client, err error) error {
	now := time.Now()

	w.mu.Lock()
	w.connected = false
	w.lastDisconnected = now
	w.downtimeStart = now
	w.lastErr = err
	w.mu.Unlock()

	w.emit(ConnectionEvent{
		Time:      now,
		Type:      ConnectionEventDisconnected,
		Err:       err,
		Connected: false,
	})
	return nil
}

// Events returns a read-only channel of connection events.
func (w *ConnectionWatchdog) Events() <-chan ConnectionEvent {
	return w.events
}

// Stats returns a snapshot of connection health metrics.
func (w *ConnectionWatchdog) Stats() ConnectionStats {
	stats := ConnectionStats{
		Connected: true,
	}

	w.mu.RLock()
	stats.Connected = w.connected
	stats.LastConnected = w.lastConnected
	stats.LastDisconnected = w.lastDisconnected
	stats.TotalDowntime = w.totalDowntime
	stats.LastDisconnectErr = w.lastErr

	if !w.connected && !w.downtimeStart.IsZero() {
		stats.CurrentDowntime = time.Since(w.downtimeStart)
	}
	w.mu.RUnlock()
	return stats
}

func (w *ConnectionWatchdog) emit(evt ConnectionEvent) {
	select {
	case w.events <- evt:
	default:
		// Drop if buffer is full to avoid blocking hooks.
	}
}

// Ensure ConnectionWatchdog satisfies the interfaces.
var _ ConnectionPlugin = (*ConnectionWatchdog)(nil)
var _ Plugin = (*ConnectionWatchdog)(nil)

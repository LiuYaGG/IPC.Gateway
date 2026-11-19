package gofins

import (
	"errors"
	"testing"
	"time"
)

func TestConnectionWatchdogTracksEventsAndStats(t *testing.T) {
	w := NewConnectionWatchdog(4)
	if err := w.Initialize(nil); err != nil {
		t.Fatalf("initialize: %v", err)
	}

	discErr := errors.New("network down")
	if err := w.OnDisconnected(nil, discErr); err != nil {
		t.Fatalf("OnDisconnected: %v", err)
	}

	evt1 := <-w.Events()
	if evt1.Type != ConnectionEventDisconnected {
		t.Fatalf("expected disconnected event, got %v", evt1.Type)
	}
	if evt1.Err == nil || evt1.Err.Error() != discErr.Error() {
		t.Fatalf("expected error %v, got %v", discErr, evt1.Err)
	}

	time.Sleep(10 * time.Millisecond)

	if err := w.OnConnected(nil); err != nil {
		t.Fatalf("OnConnected: %v", err)
	}

	evt2 := <-w.Events()
	if evt2.Type != ConnectionEventConnected {
		t.Fatalf("expected connected event, got %v", evt2.Type)
	}
	if evt2.Downtime <= 0 {
		t.Fatalf("expected downtime >0, got %v", evt2.Downtime)
	}

	stats := w.Stats()
	if !stats.Connected {
		t.Fatalf("expected connected")
	}
	if stats.LastDisconnectErr == nil || stats.LastDisconnectErr.Error() != discErr.Error() {
		t.Fatalf("expected last error %v, got %v", discErr, stats.LastDisconnectErr)
	}
	if stats.TotalDowntime <= 0 {
		t.Fatalf("expected total downtime >0, got %v", stats.TotalDowntime)
	}
	if stats.CurrentDowntime != 0 {
		t.Fatalf("expected zero current downtime after reconnect, got %v", stats.CurrentDowntime)
	}
}

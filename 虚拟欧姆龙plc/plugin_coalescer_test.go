package gofins

import (
	"context"
	"errors"
	"sync"
	"testing"
	"time"
)

func TestBulkReadCoalescerBatchesAdjacentRequests(t *testing.T) {
	bc := NewBulkReadCoalescer(20*time.Millisecond, 10)
	bc.client = &Client{}
	bc.WithInterceptors(false)

	var mu sync.Mutex
	calls := 0
	bc.readWords = func(ctx context.Context, area byte, address uint16, count uint16) ([]uint16, error) {
		mu.Lock()
		calls++
		mu.Unlock()
		data := make([]uint16, count)
		for i := range data {
			data[i] = address + uint16(i)
		}
		return data, nil
	}
	bc.start()
	defer bc.Stop()

	interceptor := bc.Interceptor()
	ctx, cancel := context.WithTimeout(context.Background(), time.Second)
	defer cancel()

	req1 := &InterceptorCtx{
		ctx: ctx,
		info: &InterceptorInfo{
			Operation:  OpReadWords,
			MemoryArea: MemoryAreaDMWord,
			Address:    100,
			Count:      3,
		},
		invoker: func(ctx context.Context) (interface{}, error) {
			return nil, errors.New("should not be called")
		},
	}
	req2 := &InterceptorCtx{
		ctx: ctx,
		info: &InterceptorInfo{
			Operation:  OpReadWords,
			MemoryArea: MemoryAreaDMWord,
			Address:    103,
			Count:      2,
		},
		invoker: func(ctx context.Context) (interface{}, error) {
			return nil, errors.New("should not be called")
		},
	}

	var wg sync.WaitGroup
	wg.Add(2)

	var res1, res2 []uint16
	var err1, err2 error

	go func() {
		defer wg.Done()
		r, e := interceptor(req1)
		if e == nil {
			res1 = r.([]uint16)
		}
		err1 = e
	}()

	go func() {
		defer wg.Done()
		r, e := interceptor(req2)
		if e == nil {
			res2 = r.([]uint16)
		}
		err2 = e
	}()

	wg.Wait()

	if err1 != nil || err2 != nil {
		t.Fatalf("unexpected errors: %v %v", err1, err2)
	}

	if len(res1) != 3 || res1[0] != 100 || res1[2] != 102 {
		t.Fatalf("unexpected res1: %v", res1)
	}
	if len(res2) != 2 || res2[0] != 103 || res2[1] != 104 {
		t.Fatalf("unexpected res2: %v", res2)
	}

	mu.Lock()
	defer mu.Unlock()
	if calls != 1 {
		t.Fatalf("expected 1 backend call, got %d", calls)
	}
}

func TestBulkReadCoalescerBypassesLargeRequests(t *testing.T) {
	bc := NewBulkReadCoalescer(5*time.Millisecond, 4)
	bc.client = &Client{}
	bc.WithInterceptors(false)

	interceptor := bc.Interceptor()
	ctx, cancel := context.WithTimeout(context.Background(), time.Second)
	defer cancel()

	called := false
	req := &InterceptorCtx{
		ctx: ctx,
		info: &InterceptorInfo{
			Operation:  OpReadWords,
			MemoryArea: MemoryAreaDMWord,
			Address:    10,
			Count:      10, // larger than maxSpan
		},
		invoker: func(ctx context.Context) (interface{}, error) {
			called = true
			return []uint16{1}, nil
		},
	}

	_, err := interceptor(req)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if !called {
		t.Fatalf("expected underlying invoker to be called for large request")
	}
}

package gofins

import (
	"context"
	"fmt"
	"sync"
	"time"
)

// BulkReadCoalescer batches adjacent ReadWords requests into a single PLC read.
// It supports Plugin for auto-registration and exposes an Interceptor for manual chaining.
type BulkReadCoalescer struct {
	window          time.Duration
	maxSpan         uint16
	maxBatch        int
	useInterceptors bool

	client    *Client
	requests  chan *bulkReadRequest
	stop      chan struct{}
	startOnce sync.Once
	stopOnce  sync.Once

	readWords func(ctx context.Context, area byte, address uint16, count uint16) ([]uint16, error)
}

type bulkReadRequest struct {
	ctx     context.Context
	area    byte
	address uint16
	count   uint16
	resp    chan bulkReadResult
	invoker Invoker
}

type bulkReadResult struct {
	data []uint16
	err  error
}

type ctxSkipCoalescerKey struct{}

// NewBulkReadCoalescer creates a coalescer plugin.
// window controls how long to wait to group adjacent reads.
// maxSpan is the maximum combined word span per batch.
func NewBulkReadCoalescer(window time.Duration, maxSpan uint16) *BulkReadCoalescer {
	if window <= 0 {
		window = 2 * time.Millisecond
	}
	if maxSpan == 0 {
		maxSpan = 32
	}
	return &BulkReadCoalescer{
		window:          window,
		maxSpan:         maxSpan,
		maxBatch:        0, // 0 = unbounded
		useInterceptors: true,
		requests:        make(chan *bulkReadRequest, 128),
		stop:            make(chan struct{}),
	}
}

// WithInterceptors toggles whether the coalesced backend read passes through the client's interceptors.
// When false, it calls the raw reader for maximum throughput (default: true).
func (b *BulkReadCoalescer) WithInterceptors(enabled bool) *BulkReadCoalescer {
	b.useInterceptors = enabled
	return b
}

// WithMaxBatchSize limits how many requests can be merged in a single batch (0 = unlimited).
func (b *BulkReadCoalescer) WithMaxBatchSize(n int) *BulkReadCoalescer {
	if n < 0 {
		n = 0
	}
	b.maxBatch = n
	return b
}

// Name implements Plugin.
func (b *BulkReadCoalescer) Name() string { return "bulk_read_coalescer" }

// Initialize implements Plugin.
func (b *BulkReadCoalescer) Initialize(c *Client) error {
	b.client = c
	if b.readWords == nil {
		b.readWords = c.readWordsRaw
	}
	b.start()

	// Chain with existing interceptor, if any.
	c.interceptorMutex.Lock()
	existing := c.interceptor
	c.interceptorMutex.Unlock()

	if existing != nil {
		c.SetInterceptor(ChainInterceptors(b.Interceptor(), existing))
	} else {
		c.SetInterceptor(b.Interceptor())
	}
	return nil
}

// Interceptor returns the coalescing interceptor.
func (b *BulkReadCoalescer) Interceptor() Interceptor {
	return func(ic *InterceptorCtx) (interface{}, error) {
		info := ic.Info()
		if info.Operation != OpReadWords ||
			info.Count == 0 ||
			info.Count > b.maxSpan ||
			ic.Context().Value(ctxSkipCoalescerKey{}) != nil ||
			b.client == nil {
			// Bypass coalescing
			ctx := context.WithValue(ic.Context(), ctxSkipCoalescerKey{}, true)
			return ic.Invoke(ctx)
		}

		req := &bulkReadRequest{
			ctx:     ic.Context(),
			area:    info.MemoryArea,
			address: info.Address,
			count:   info.Count,
			resp:    make(chan bulkReadResult, 1),
			invoker: ic.invoker,
		}

		select {
		case b.requests <- req:
		case <-ic.Context().Done():
			return nil, ic.Context().Err()
		case <-b.stop:
			return nil, context.Canceled
		}

		select {
		case res := <-req.resp:
			return res.data, res.err
		case <-ic.Context().Done():
			return nil, ic.Context().Err()
		case <-b.stop:
			return nil, context.Canceled
		}
	}
}

// Stop stops the coalescer goroutine.
func (b *BulkReadCoalescer) Stop() {
	b.stopOnce.Do(func() {
		close(b.stop)
	})
}

func (b *BulkReadCoalescer) start() {
	b.startOnce.Do(func() {
		go b.run()
	})
}

func (b *BulkReadCoalescer) run() {
	for {
		select {
		case <-b.stop:
			return
		case req := <-b.requests:
			if req == nil {
				continue
			}
			b.processBatch(req)
		}
	}
}

func (b *BulkReadCoalescer) processBatch(first *bulkReadRequest) {
	batch := []*bulkReadRequest{first}
	timer := time.NewTimer(b.window)
	defer timer.Stop()

	minAddr := first.address
	maxEnd := first.address + first.count

	for {
		select {
		case <-b.stop:
			return
		case <-timer.C:
			b.flushBatch(batch, minAddr, maxEnd)
			return
		case req := <-b.requests:
			if req == nil {
				continue
			}

			// Skip cancelled requests immediately.
			select {
			case <-req.ctx.Done():
				req.resp <- bulkReadResult{err: req.ctx.Err()}
				continue
			default:
			}

			if b.maxBatch > 0 && len(batch) >= b.maxBatch {
				// Flush current batch and start new.
				b.flushBatch(batch, minAddr, maxEnd)
				batch = []*bulkReadRequest{req}
				minAddr = req.address
				maxEnd = req.address + req.count
				timer.Reset(b.window)
				continue
			}

			newMin := min(minAddr, req.address)
			newMax := max(maxEnd, req.address+req.count)
			combinedSpan := uint32(newMax) - uint32(newMin)
			if req.area == batch[0].area && combinedSpan <= uint32(b.maxSpan) {
				minAddr = newMin
				maxEnd = newMax
				batch = append(batch, req)

				// If we've hit the span limit, flush immediately.
				if uint16(combinedSpan) >= b.maxSpan {
					b.flushBatch(batch, minAddr, maxEnd)
					return
				}
			} else {
				// Flush current batch and start a new one with this request.
				b.flushBatch(batch, minAddr, maxEnd)
				batch = []*bulkReadRequest{req}
				minAddr = req.address
				maxEnd = req.address + req.count
				timer.Reset(b.window)
			}
		}
	}
}

func (b *BulkReadCoalescer) flushBatch(batch []*bulkReadRequest, minAddr uint16, maxEnd uint16) {
	active := batch[:0]
	for _, req := range batch {
		select {
		case <-req.ctx.Done():
			req.resp <- bulkReadResult{err: req.ctx.Err()}
		default:
			active = append(active, req)
		}
	}
	if len(active) == 0 {
		return
	}

	span := maxEnd - minAddr

	var data []uint16
	var err error

	if b.useInterceptors {
		// Run a synthesized interceptor call so logging/metrics still see the combined read.
		ctx := context.Background()
		if deadline, ok := earliestDeadline(active); ok {
			var cancel context.CancelFunc
			ctx, cancel = context.WithDeadline(ctx, deadline)
			defer cancel()
		}

		ic := &InterceptorCtx{
			ctx: ctx,
			info: &InterceptorInfo{
				Operation:  OpReadWords,
				MemoryArea: active[0].area,
				Address:    minAddr,
				Count:      span,
			},
			invoker: func(ctx context.Context) (interface{}, error) {
				return b.readWords(ctx, active[0].area, minAddr, span)
			},
		}
		res, callErr := b.client.invoke(context.WithValue(ctx, ctxSkipCoalescerKey{}, true), ic.info, ic.invoker)
		if callErr != nil {
			err = callErr
		} else {
			data = res.([]uint16)
		}
	} else {
		// Use the earliest deadline among active requests.
		ctx := context.Background()
		if deadline, ok := earliestDeadline(active); ok {
			var cancel context.CancelFunc
			ctx, cancel = context.WithDeadline(ctx, deadline)
			defer cancel()
		}
		data, err = b.readWords(ctx, active[0].area, minAddr, span)
	}

	for _, req := range active {
		if err != nil {
			req.resp <- bulkReadResult{err: err}
			continue
		}

		startOffset := int(req.address - minAddr)
		endOffset := startOffset + int(req.count)
		if endOffset > len(data) {
			req.resp <- bulkReadResult{err: fmt.Errorf("coalesced read response too short")}
			continue
		}
		// Create a copy to avoid sharing backing array.
		slice := make([]uint16, req.count)
		copy(slice, data[startOffset:endOffset])
		req.resp <- bulkReadResult{data: slice}
	}
}

func earliestDeadline(reqs []*bulkReadRequest) (time.Time, bool) {
	var earliest time.Time
	found := false
	for _, r := range reqs {
		if d, ok := r.ctx.Deadline(); ok {
			if !found || d.Before(earliest) {
				earliest = d
				found = true
			}
		}
	}
	return earliest, found
}

func min(a, b uint16) uint16 {
	if a < b {
		return a
	}
	return b
}

func max(a, b uint16) uint16 {
	if a > b {
		return a
	}
	return b
}

// Ensure BulkReadCoalescer satisfies Plugin.
var _ Plugin = (*BulkReadCoalescer)(nil)

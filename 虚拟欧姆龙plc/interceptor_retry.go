package gofins

import (
	"fmt"
	"log"
	"time"
)

// RetryInterceptor creates an interceptor that retries failed operations
// It will retry up to maxRetries times with the specified delay between attempts.
// Context errors (Canceled, DeadlineExceeded) are not retried.
//
// Example:
//
//	// Retry up to 3 times with 100ms delay
//	client.SetInterceptor(fins.RetryInterceptor(3, 100*time.Millisecond))
func RetryInterceptor(maxRetries int, delay time.Duration) Interceptor {
	return func(c *InterceptorCtx) (interface{}, error) {
		var result interface{}
		var err error
		ctx := c.Context()
		info := c.Info()

		for attempt := 0; attempt <= maxRetries; attempt++ {
			result, err = c.Invoke(ctx)
			if err == nil {
				return result, nil
			}

			// Don't retry on context errors
			if ctx.Err() != nil {
				return nil, err
			}

			// Don't retry on last attempt
			if attempt < maxRetries {
				log.Printf("[RETRY] Attempt %d/%d failed for %s: %v. Retrying in %v...",
					attempt+1, maxRetries+1, info.Operation, err, delay)
				time.Sleep(delay)
			}
		}

		return result, fmt.Errorf("operation failed after %d retries: %w", maxRetries+1, err)
	}
}

// RetryInterceptorWithBackoff creates a retry interceptor with exponential backoff
// The delay is doubled after each retry, up to a maximum delay.
//
// Example:
//
//	// Retry with exponential backoff: 100ms, 200ms, 400ms, max 1s
//	client.SetInterceptor(fins.RetryInterceptorWithBackoff(3, 100*time.Millisecond, 1*time.Second))
func RetryInterceptorWithBackoff(maxRetries int, initialDelay, maxDelay time.Duration) Interceptor {
	return func(c *InterceptorCtx) (interface{}, error) {
		var result interface{}
		var err error
		delay := initialDelay
		ctx := c.Context()
		info := c.Info()

		for attempt := 0; attempt <= maxRetries; attempt++ {
			result, err = c.Invoke(ctx)
			if err == nil {
				return result, nil
			}

			// Don't retry on context errors
			if ctx.Err() != nil {
				return nil, err
			}

			// Don't retry on last attempt
			if attempt < maxRetries {
				log.Printf("[RETRY] Attempt %d/%d failed for %s: %v. Retrying in %v...",
					attempt+1, maxRetries+1, info.Operation, err, delay)
				time.Sleep(delay)

				// Exponential backoff
				delay *= 2
				if delay > maxDelay {
					delay = maxDelay
				}
			}
		}

		return result, fmt.Errorf("operation failed after %d retries: %w", maxRetries+1, err)
	}
}

// RetryInterceptorConditional creates a retry interceptor that only retries certain errors
// The shouldRetry function determines whether an error should be retried.
//
// Example:
//
//	// Only retry timeout errors
//	shouldRetry := func(err error) bool {
//		return errors.Is(err, fins.ResponseTimeoutError{})
//	}
//	client.SetInterceptor(fins.RetryInterceptorConditional(3, 100*time.Millisecond, shouldRetry))
func RetryInterceptorConditional(maxRetries int, delay time.Duration, shouldRetry func(error) bool) Interceptor {
	return func(c *InterceptorCtx) (interface{}, error) {
		var result interface{}
		var err error
		ctx := c.Context()
		info := c.Info()

		for attempt := 0; attempt <= maxRetries; attempt++ {
			result, err = c.Invoke(ctx)
			if err == nil {
				return result, nil
			}

			// Don't retry on context errors
			if ctx.Err() != nil {
				return nil, err
			}

			// Check if we should retry this error
			if !shouldRetry(err) {
				return result, err
			}

			// Don't retry on last attempt
			if attempt < maxRetries {
				log.Printf("[RETRY] Attempt %d/%d failed for %s: %v. Retrying in %v...",
					attempt+1, maxRetries+1, info.Operation, err, delay)
				time.Sleep(delay)
			}
		}

		return result, fmt.Errorf("operation failed after %d retries: %w", maxRetries+1, err)
	}
}

package gofins

import (
	"context"
	"errors"
	"testing"
)

type mockPlugin struct {
	name    string
	initErr error
	onInit  func(*Client) error
}

func (m *mockPlugin) Name() string { return m.name }
func (m *mockPlugin) Initialize(c *Client) error {
	if m.onInit != nil {
		if err := m.onInit(c); err != nil {
			return err
		}
	}
	return m.initErr
}

func TestUseRegistersAndAppliesPlugin(t *testing.T) {
	c := &Client{}
	called := false

	plugin := &mockPlugin{
		name: "interceptor",
		onInit: func(c *Client) error {
			c.SetInterceptor(func(ic *InterceptorCtx) (interface{}, error) {
				called = true
				return ic.Invoke(nil)
			})
			return nil
		},
	}

	if err := c.Use(plugin); err != nil {
		t.Fatalf("unexpected error registering plugin: %v", err)
	}

	_, err := c.invoke(context.Background(), &InterceptorInfo{Operation: OpReadWords}, func(context.Context) (interface{}, error) {
		return "ok", nil
	})
	if err != nil {
		t.Fatalf("unexpected invoke error: %v", err)
	}
	if !called {
		t.Fatalf("interceptor from plugin was not called")
	}
}

func TestUseRejectsDuplicateNames(t *testing.T) {
	c := &Client{}
	first := &mockPlugin{name: "dup"}
	second := &mockPlugin{name: "dup"}

	if err := c.Use(first); err != nil {
		t.Fatalf("unexpected error registering first plugin: %v", err)
	}
	if err := c.Use(second); err == nil {
		t.Fatalf("expected duplicate plugin error")
	}
}

func TestUseRejectsInvalidPlugins(t *testing.T) {
	c := &Client{}
	if err := c.Use(&mockPlugin{name: ""}); err == nil {
		t.Fatalf("expected error for empty plugin name")
	}
	if err := c.Use(nil); err == nil {
		t.Fatalf("expected error for nil plugin")
	}
}

func TestUseCleansUpAfterInitFailure(t *testing.T) {
	c := &Client{}
	fail := &mockPlugin{name: "unstable", initErr: errors.New("boom")}
	if err := c.Use(fail); err == nil {
		t.Fatalf("expected initialization error")
	}

	retry := &mockPlugin{name: "unstable"}
	if err := c.Use(retry); err != nil {
		t.Fatalf("expected successful registration after failure, got %v", err)
	}
}

package gofins

import (
	"fmt"
	"sync"
)

// Plugin allows extending client behavior (logging, metrics, tracing, etc.).
// Inspired by gorm's plugin model.
type Plugin interface {
	// Name must return a unique plugin name.
	Name() string
	// Initialize is called once when the plugin is registered via Use.
	Initialize(*Client) error
}

// ConnectionPlugin can react to connection lifecycle events.
// Hooks are invoked synchronously in registration order.
// Returning an error aborts registration (or reconnection when fired there).
type ConnectionPlugin interface {
	Plugin
	OnConnected(*Client) error
	OnDisconnected(*Client, error) error
}

// pluginManager wraps plugin registration to keep the Client struct focused.
type pluginManager struct {
	mu      sync.Mutex
	plugins map[string]Plugin
	order   []Plugin
}

func (pm *pluginManager) use(c *Client, plugins ...Plugin) error {
	for _, p := range plugins {
		if p == nil {
			return fmt.Errorf("plugin is nil")
		}
		name := p.Name()
		if name == "" {
			return fmt.Errorf("plugin name cannot be empty")
		}

		// Reserve the name to avoid duplicate registration races.
		pm.mu.Lock()
		if pm.plugins == nil {
			pm.plugins = make(map[string]Plugin)
		}
		if _, exists := pm.plugins[name]; exists {
			pm.mu.Unlock()
			return fmt.Errorf("plugin %s already registered", name)
		}
		pm.plugins[name] = nil
		pm.mu.Unlock()

		if err := p.Initialize(c); err != nil {
			pm.mu.Lock()
			delete(pm.plugins, name)
			pm.mu.Unlock()
			return fmt.Errorf("initialize plugin %s: %w", name, err)
		}

		pm.mu.Lock()
		pm.plugins[name] = p
		pm.order = append(pm.order, p)
		pm.mu.Unlock()

		// Fire OnConnected immediately if the client is already connected.
		if cp, ok := p.(ConnectionPlugin); ok && c.isConnected() {
			if err := cp.OnConnected(c); err != nil {
				pm.mu.Lock()
				delete(pm.plugins, name)
				pm.order = pm.order[:len(pm.order)-1]
				pm.mu.Unlock()
				return fmt.Errorf("plugin %s OnConnected: %w", name, err)
			}
		}
	}

	return nil
}

func (pm *pluginManager) notifyConnected(c *Client) error {
	pm.mu.Lock()
	plugins := append([]Plugin(nil), pm.order...)
	pm.mu.Unlock()

	for _, p := range plugins {
		if cp, ok := p.(ConnectionPlugin); ok {
			if err := cp.OnConnected(c); err != nil {
				return fmt.Errorf("plugin %s OnConnected: %w", p.Name(), err)
			}
		}
	}
	return nil
}

func (pm *pluginManager) notifyDisconnected(c *Client, err error) error {
	pm.mu.Lock()
	plugins := append([]Plugin(nil), pm.order...)
	pm.mu.Unlock()

	for _, p := range plugins {
		if cp, ok := p.(ConnectionPlugin); ok {
			if hookErr := cp.OnDisconnected(c, err); hookErr != nil {
				return fmt.Errorf("plugin %s OnDisconnected: %w", p.Name(), hookErr)
			}
		}
	}
	return nil
}

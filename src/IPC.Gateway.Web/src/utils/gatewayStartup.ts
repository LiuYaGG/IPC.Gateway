import { onBeforeUnmount, onMounted, ref } from 'vue'
import { loadStartupState, type GatewayStartupState } from '../api'

export function useGatewayStartup() {
  const state = ref<GatewayStartupState | 'checking' | 'unavailable'>('checking')
  let timer: number | undefined
  let pending: Promise<void> | undefined
  let controller: AbortController | undefined
  let disposed = false

  function schedule() {
    window.clearTimeout(timer)
    if (!disposed && !document.hidden && state.value !== 'ready')
      timer = window.setTimeout(check, state.value === 'starting' ? 2000 : 5000)
  }

  function check(): Promise<void> {
    if (pending) return pending
    if (disposed || document.hidden) return Promise.resolve()
    window.clearTimeout(timer)
    const request = new AbortController()
    controller = request
    const timeout = window.setTimeout(() => request.abort(), 5000)
    pending = loadStartupState(request.signal)
      .then(value => { if (!disposed) state.value = value })
      .catch(() => { if (!disposed) state.value = 'unavailable' })
      .finally(() => {
        window.clearTimeout(timeout)
        pending = undefined
        controller = undefined
        schedule()
      })
    return pending
  }

  function observe(value: GatewayStartupState) {
    state.value = value
    schedule()
  }

  function visibilityChanged() {
    if (document.hidden) window.clearTimeout(timer)
    else if (state.value !== 'ready') void check()
  }

  onMounted(() => {
    document.addEventListener('visibilitychange', visibilityChanged)
    void check()
  })
  onBeforeUnmount(() => {
    disposed = true
    window.clearTimeout(timer)
    controller?.abort()
    document.removeEventListener('visibilitychange', visibilityChanged)
  })
  return { state, check, observe }
}

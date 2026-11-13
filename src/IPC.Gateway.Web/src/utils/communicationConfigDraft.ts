import { shallowReactive, shallowRef, watch } from 'vue'

// MQTT and OPC UA configuration DTOs contain scalar fields. Keep local edits
// while adopting fresh server values for fields the user hasn't changed.
export function useCommunicationConfigDraft<T extends object>(
  source: () => T | null,
  resetRevision: () => number | undefined,
  persist: (config: T, acceptSaved: (saved: T) => void) => void
) {
  const draft = shallowRef<T | null>(null)
  let baseline: T | null = null

  function sync(server: T | null, compareWith: T | null) {
    const next = server ? { ...server } : null
    const current = draft.value
    if (next && current && compareWith) {
      for (const key of Object.keys(current) as (keyof T)[]) {
        if (!Object.is(current[key], compareWith[key])) next[key] = current[key]
      }
    }
    baseline = server ? { ...server } : null
    draft.value = next ? shallowReactive(next) : null
  }

  watch([source, resetRevision], ([server, revision], [, previousRevision]) => {
    sync(server, revision === previousRevision ? baseline : null)
  }, { immediate: true })

  function save() {
    if (!draft.value) return
    const submitted = { ...draft.value }
    persist(submitted, saved => sync(saved, submitted))
  }

  return { draft, save }
}

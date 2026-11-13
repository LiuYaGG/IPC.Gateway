import { onActivated, onMounted } from 'vue'

// These three views share the same users, roles and permission assignments.
// A cached view only needs to query again after a successful related edit.
let directoryRevision = 0

export function useAccountDirectory(reload: () => Promise<void>) {
  let seenRevision = -1
  function refreshIfChanged() {
    if (seenRevision === directoryRevision) return
    seenRevision = directoryRevision
    void reload()
  }
  onMounted(refreshIfChanged)
  onActivated(refreshIfChanged)
  return () => { seenRevision = ++directoryRevision }
}

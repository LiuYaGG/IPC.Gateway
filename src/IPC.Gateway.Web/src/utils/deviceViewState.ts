export interface DeviceViewState {
  selected: string
  ancestors: string[]
  expanded: string[]
  filter: string
  page: number
  pageSize: number
  treeTop: number
  treeLeft: number
  tagTop: number
  tagLeft: number
  allColumns?: boolean
  tagPage?: number
  tagPageSize?: number
}

const prefix = 'ipc.gateway.devices.view.v1:'
const memory = new Map<string, DeviceViewState>()
export const deviceViewScope = (user: string, project: string) =>
  user && project ? prefix + JSON.stringify([user, project]) : ''

export function readDeviceViewState(scope: string): DeviceViewState | undefined {
  if (!scope) return
  try {
    const value = memory.get(scope) ?? JSON.parse(sessionStorage.getItem(scope) ?? 'null')
    if (!value || typeof value.selected !== 'string') return
    const position = (n: unknown) => (typeof n === 'number' && Number.isFinite(n) ? Math.max(0, n) : 0)
    const keys = (items: unknown) =>
      Array.isArray(items) ? items.filter((key): key is string => typeof key === 'string').slice(0, 10000) : []
    return {
      selected: value.selected,
      ancestors: keys(value.ancestors),
      expanded: keys(value.expanded),
      filter: typeof value.filter === 'string' ? value.filter : '',
      page: Math.max(1, Math.floor(position(value.page))),
      pageSize: [20, 50, 100, 200, 500].includes(value.pageSize) ? value.pageSize : 100,
      treeTop: position(value.treeTop),
      treeLeft: position(value.treeLeft),
      tagTop: position(value.tagTop),
      tagLeft: position(value.tagLeft),
      ...(typeof value.allColumns === 'boolean' ? { allColumns: value.allColumns } : {}),
      ...(typeof value.tagPage === 'number' ? { tagPage: Math.max(1, Math.floor(position(value.tagPage))) } : {}),
      ...(typeof value.tagPageSize === 'number'
        ? { tagPageSize: [20, 50, 100, 200, 500].includes(value.tagPageSize) ? value.tagPageSize : 20 }
        : {})
    }
  } catch {
    return memory.get(scope)
  }
}

export function saveDeviceViewState(scope: string, value: DeviceViewState) {
  if (!scope) return
  memory.set(scope, value)
  try {
    sessionStorage.setItem(scope, JSON.stringify(value))
  } catch {
    /* In-memory navigation still works. */
  }
}

export function clearDeviceViewStates() {
  memory.clear()
  try {
    for (const key of Object.keys(sessionStorage)) if (key.startsWith(prefix)) sessionStorage.removeItem(key)
  } catch {
    /* Storage may be disabled by the browser. */
  }
}

export function resolveDeviceSelection(selected: string, ancestors: string[], exists: (key: string) => boolean) {
  return [selected, ...ancestors.slice().reverse(), 'root'].find(exists) ?? 'root'
}

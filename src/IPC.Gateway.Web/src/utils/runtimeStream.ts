import type {
  DeviceRuntimeStatus, RuntimeDevicesChangedEvent, RuntimeEventEnvelope, RuntimeStatusPatchEvent,
  RuntimeTagsChangedEvent, TagValueSnapshot
} from '../api'
import { createRuntimeEventGuard, shouldApplyRuntimeSnapshot } from './runtimePush'
import { decodeTagSnapshot } from '../api'

export function tagKey(tag: TagValueSnapshot) {
  return [tag.channelId, tag.deviceId, tag.groupId, tag.tagId].map(id => (id ?? '').trim().toLowerCase()).join('/')
}

export function deviceKey(device: DeviceRuntimeStatus) {
  return [device.channelId, device.deviceId].map(id => (id ?? '').trim().toLowerCase()).join('/')
}

export function setLatestTag(tags: Map<string, TagValueSnapshot>, key: string, tag: TagValueSnapshot) {
  const previous = tags.get(key)
  if (previous) {
    const previousVersion = previous.runtimeRevision ?? 0
    const nextVersion = tag.runtimeRevision ?? 0
    // Source timestamps (e.g. OPC UA after recovery) may precede the last poll.
    if (previousVersion > 0 || nextVersion > 0) {
      if (nextVersion < previousVersion) return false
    } else if (!shouldApplyRuntimeSnapshot(previous.timestamp, tag.timestamp)) return false
  }
  tags.set(key, tag)
  return true
}

type StatusPatch = RuntimeStatusPatchEvent['status']

function newChanges() {
  return {
    tags: new Map<string, TagValueSnapshot>(),
    devices: new Map<string, DeviceRuntimeStatus>(),
    removedDevices: new Set<string>(),
    status: undefined as StatusPatch | undefined
  }
}

export type RuntimeChanges = ReturnType<typeof newChanges>

export function connectRuntimeEvents(handlers: {
  projectRevision(): string | undefined
  opened(): void
  snapshot(changes: RuntimeChanges): void
  update(changes: RuntimeChanges): void
  reconnect(): void
  resync(): void
}) {
  const source = new EventSource('/api/config/status/events', { withCredentials: true })
  const guard = createRuntimeEventGuard()
  let staging: RuntimeChanges | undefined
  let snapshotSequence = -1
  let ready = false
  let pending = newChanges()
  let frame: number | undefined
  let closed = false
  let lastReceived = performance.now()
  // A half-open connection can outlive EventSource's error detection. The server
  // sends heartbeats every 15s even when no tags or devices are visible.
  const watchdog = window.setInterval(() => {
    if (performance.now() - lastReceived > 45000) reconnect()
  }, 15000)

  function close() {
    if (closed) return
    closed = true
    source.close()
    window.clearInterval(watchdog)
    if (frame !== undefined) cancelAnimationFrame(frame)
    staging = undefined
    pending = newChanges()
  }
  function reconnect() {
    if (closed) return
    close()
    handlers.reconnect()
  }
  function listen<T>(type: string, callback: (envelope: RuntimeEventEnvelope<T>) => void) {
    source.addEventListener(type, event => {
      if (closed || document.hidden) return
      let envelope: RuntimeEventEnvelope<T>
      try {
        envelope = JSON.parse((event as MessageEvent<string>).data)
      } catch {
        reconnect()
        return
      }
      if (!envelope || envelope.type !== type || !Number.isSafeInteger(envelope.sequence)) {
        reconnect()
        return
      }
      if (type !== 'hello') {
        const result = guard.check(envelope)
        if (result === 'resync') reconnect()
        if (result !== 'accept') return
      }
      lastReceived = performance.now()
      callback(envelope)
    })
  }
  function collect(sequence: number, apply: (changes: RuntimeChanges) => void) {
    if (staging) {
      if (sequence !== snapshotSequence) return reconnect()
      apply(staging)
    } else if (ready) {
      apply(pending)
      if (frame !== undefined) return
      frame = requestAnimationFrame(() => {
        frame = undefined
        if (closed || document.hidden) return
        const changes = pending
        pending = newChanges()
        handlers.update(changes)
      })
    } else reconnect()
  }

  source.addEventListener('open', () => { if (!closed) handlers.opened() })
  source.addEventListener('error', reconnect)
  listen('hello', e => guard.reset(e.timestamp, e.sequence))
  listen<{ projectRevision?: string }>('snapshot-start', e => {
    if (e.data?.projectRevision && e.data.projectRevision !== handlers.projectRevision()) {
      close()
      handlers.resync()
      return
    }
    staging = newChanges()
    snapshotSequence = e.sequence
    ready = false
  })
  listen('snapshot-end', e => {
    if (!staging || e.sequence !== snapshotSequence) return reconnect()
    const snapshot = staging
    staging = undefined
    ready = true
    handlers.snapshot(snapshot)
  })
  listen<RuntimeTagsChangedEvent>('tags', e => collect(e.sequence, changes => {
    for (const tag of e.data.tags) setLatestTag(changes.tags, tagKey(tag), decodeTagSnapshot(tag))
  }))
  listen<RuntimeDevicesChangedEvent>('devices', e => collect(e.sequence, changes => {
    for (const device of e.data.devices) {
      const key = deviceKey(device)
      changes.removedDevices.delete(key)
      changes.devices.set(key, device)
    }
    for (const wireKey of e.data.removedDeviceKeys) {
      const key = wireKey.trim().toLowerCase().replace(/^id:/, '')
      changes.devices.delete(key)
      if (!staging) changes.removedDevices.add(key)
    }
  }))
  listen<RuntimeStatusPatchEvent>('status', e => collect(e.sequence, changes => {
    changes.status = e.data.status
  }))
  listen<{ projectRevision?: string }>('resync', e => {
    if (e.data?.projectRevision && e.data.projectRevision === handlers.projectRevision()) return
    close()
    handlers.resync()
  })
  listen('heartbeat', () => {})
  return close
}

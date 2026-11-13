import type {
  DeviceConfig, DeviceRuntimeStatus, GatewayStatus, ProjectConfig, RuntimeErrorDetail, TagConfig
} from '../../api'
import { deviceRuntimeKey, normalizeKey, tagSnapshotKey } from './topologyStatus'

export type TopologyDeviceState = 'online' | 'offline' | 'warning' | 'disabled' | 'unknown'

export interface TopologyDevice {
  id: string
  channelId: string
  name: string
  protocol: string
  state: TopologyDeviceState
  tagCount: number
  directTagCount: number
  groupCount: number
  badTagCount: number
  config: DeviceConfig
  runtime: DeviceRuntimeStatus | undefined
  errors: RuntimeErrorDetail[]
}

export interface TopologyChannel {
  id: string
  name: string
  protocol: string
  enabled: boolean
  devices: TopologyDevice[]
  counts: Record<TopologyDeviceState, number>
  pointIssueDevices: number
  tagCount: number
  errors: RuntimeErrorDetail[]
}

const failedQualities = new Set(['bad', 'readerror', 'notfound', 'accessdenied', 'outofrange', 'spike', 'uncertain'])

// Unknown, NotConnected and Disabled describe acquisition availability. Filtered
// can be a successful deadband/duplicate filter and is not a point failure.
export function isFailedTopologyQuality(quality: string | null | undefined) {
  return failedQualities.has(normalizeKey(quality))
}

function effectiveState(channelEnabled: boolean, device: DeviceConfig, runtime: DeviceRuntimeStatus | undefined,
  running: boolean, badTags: number): TopologyDeviceState {
  if (!channelEnabled || device.enabled === false) return 'disabled'
  if (!running || !runtime) return 'unknown'
  const status = normalizeKey(runtime.status)
  if (runtime.enabled === false || status === 'disabled') return 'disabled'
  if (runtime.isIsolated) return 'offline'
  if (status === 'degraded') return 'warning'
  if (runtime.isConnected || status === 'online') return badTags ? 'warning' : 'online'
  // The engine initially emits Offline with no attempts, even before it has tried to connect.
  if (status === 'offline' && runtime.totalReads === 0 && !runtime.consecutiveFailures &&
      !runtime.lastError && (!runtime.lastFailureTime || runtime.lastFailureTime.startsWith('0001-'))) return 'unknown'
  if (status === 'offline' || status === 'error' || runtime.consecutiveFailures > 0) return 'offline'
  return 'unknown'
}

export function buildChannelTopology(project: ProjectConfig | null, status: GatewayStatus | null,
  failedTagsByDevice?: ReadonlyMap<string, number>) {
  const channels: TopologyChannel[] = (project?.channels ?? []).map(config => ({
    id: config.id, name: config.name || config.id, protocol: config.protocol, enabled: config.enabled !== false,
    devices: [], counts: { online: 0, warning: 0, offline: 0, disabled: 0, unknown: 0 },
    pointIssueDevices: 0, tagCount: 0, errors: []
  }))
  const byChannel = new Map(channels.map(channel => [normalizeKey(channel.id), channel]))
  const byDevice = new Map<string, TopologyDevice>()
  const runtimes = new Map((status?.devices ?? []).map(runtime => [deviceRuntimeKey(runtime), runtime]))
  // Full snapshots are a fallback for callers without the incremental quality counts.
  // The live topology can update without materializing status.tags or walking each tag.
  const failedSnapshots = new Set<string>()
  if (!failedTagsByDevice) {
    for (const tag of status?.tags ?? []) {
      if (isFailedTopologyQuality(tag.quality)) failedSnapshots.add(tagSnapshotKey(tag))
    }
  }
  function countFailedRows(tags: TagConfig[] | undefined, prefix: string) {
    let count = 0
    for (const tag of tags ?? []) {
      if (tag.enabled !== false && normalizeKey(tag.accessMode) !== 'writeonly' &&
          failedSnapshots.has(prefix + normalizeKey(tag.id))) count++
    }
    return count
  }

  let tagCount = 0
  let deviceCount = 0
  for (const config of project?.devices ?? []) {
    const channel = byChannel.get(normalizeKey(config.channelId))
    if (!channel) continue
    const key = deviceRuntimeKey({ channelId: config.channelId, deviceId: config.id })
    const runtime = runtimes.get(key)
    const directTagCount = config.tags?.length ?? 0
    const groups = config.groups ?? []
    let total = directTagCount
    const enabled = config.enabled !== false && channel.enabled
    const checkSnapshots = enabled && !failedTagsByDevice && failedSnapshots.size > 0
    let badTags = enabled ? failedTagsByDevice?.get(key) ?? 0 : 0
    if (checkSnapshots) badTags += countFailedRows(config.tags, key + '//')
    for (const group of groups) {
      total += group.tags?.length ?? 0
      if (checkSnapshots && group.enabled !== false) {
        badTags += countFailedRows(group.tags, key + '/' + normalizeKey(group.id) + '/')
      }
    }
    const state = effectiveState(channel.enabled, config, runtime, !!status?.isRunning, badTags)
    const device: TopologyDevice = {
      id: config.id, name: config.name || config.id, protocol: config.protocol || channel.protocol,
      channelId: channel.id, state, tagCount: total, directTagCount, groupCount: groups.length,
      badTagCount: badTags, config, runtime, errors: []
    }
    byDevice.set(key, device)
    channel.devices.push(device)
    channel.counts[state]++
    if (state === 'warning' && badTags > 0) channel.pointIssueDevices++
    channel.tagCount += total
    tagCount += total
    deviceCount++
  }

  for (const error of status?.recentErrors ?? []) {
    byChannel.get(normalizeKey(error.channelId))?.errors.push(error)
    if (error.deviceId) byDevice.get(deviceRuntimeKey(error))?.errors.push(error)
  }
  return { channels, deviceCount, tagCount }
}

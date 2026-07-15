import type { DeviceConfig, DeviceRuntimeStatus, RuntimeErrorDetail, TagConfig, TagValueSnapshot } from '../../api'
import type { TopologyTone } from './topologyTypes'

export function normalizeKey(value: string | null | undefined) {
  return String(value || '').trim().toLowerCase()
}

export function deviceRuntimeKey(device: Pick<DeviceRuntimeStatus, 'channelId' | 'deviceId'>) {
  return `${normalizeKey(device.channelId)}/${normalizeKey(device.deviceId)}`
}

export function tagSnapshotKey(tag: Pick<TagValueSnapshot, 'channelId' | 'deviceId' | 'groupId' | 'tagId'>) {
  return [
    normalizeKey(tag.channelId),
    normalizeKey(tag.deviceId),
    normalizeKey(tag.groupId),
    normalizeKey(tag.tagId)
  ].join('/')
}

export function tagConfigKey(device: DeviceConfig, groupId: string, tag: TagConfig) {
  return [
    normalizeKey(device.channelId),
    normalizeKey(tag.deviceId || device.id),
    normalizeKey(tag.groupId || groupId),
    normalizeKey(tag.id)
  ].join('/')
}

export function resolveDeviceTone(device: DeviceConfig, runtime?: DeviceRuntimeStatus): TopologyTone {
  if (!device.enabled) return 'disabled'
  if (!runtime) return 'warn'
  const status = normalizeKey(runtime.status)
  if (status === 'degraded') return 'warn'
  if (runtime.isConnected || status === 'online') return 'good'
  if (runtime.lastError || status === 'error') return 'bad'
  return 'bad'
}

export function resolveTagTone(tag: TagConfig, snapshot?: TagValueSnapshot): TopologyTone {
  if (!tag.enabled) return 'disabled'
  if (!snapshot) return 'normal'
  if (snapshot.errorMessage) return 'bad'
  const quality = normalizeKey(snapshot.quality)
  if (!quality || quality === 'good' || quality === 'ok') return 'good'
  if (quality.includes('bad') || quality.includes('error')) return 'bad'
  return 'warn'
}

export function mergeTone(items: TopologyTone[]): TopologyTone {
  if (items.length === 0) return 'normal'
  if (items.every(item => item === 'disabled')) return 'disabled'
  if (items.includes('bad')) return 'bad'
  if (items.includes('warn')) return 'warn'
  if (items.includes('good')) return 'good'
  return 'normal'
}

export function filterErrors(errors: RuntimeErrorDetail[], channelId?: string, deviceId?: string, groupId?: string, tagId?: string) {
  const channel = normalizeKey(channelId)
  const device = normalizeKey(deviceId)
  const group = normalizeKey(groupId)
  const tag = normalizeKey(tagId)
  return errors.filter(error => {
    if (channel && normalizeKey(error.channelId) !== channel) return false
    if (device && normalizeKey(error.deviceId) !== device) return false
    if (group && normalizeKey(error.groupId) !== group) return false
    if (tag && normalizeKey(error.tagId) !== tag) return false
    return true
  })
}

export function toneLabel(tone: TopologyTone) {
  if (tone === 'good') return '正常'
  if (tone === 'warn') return '预警'
  if (tone === 'bad') return '异常'
  if (tone === 'disabled') return '停用'
  return '未知'
}

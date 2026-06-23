import type { DeviceRuntimeStatus, GatewayHealthResponse, GatewayStatus } from '../../api'

export function percent(part: number, total: number, digits = 1) {
  if (!total || total <= 0) return 0
  return Number(((part / total) * 100).toFixed(digits))
}

export function averageSuccessRate(devices: DeviceRuntimeStatus[]) {
  if (devices.length === 0) return 0
  const total = devices.reduce((sum, item) => sum + Number(item.successRate || 0), 0)
  return Number((total / devices.length).toFixed(1))
}

export function countDevices(devices: DeviceRuntimeStatus[]) {
  return {
    online: devices.filter(item => item.enabled && item.isConnected).length,
    offline: devices.filter(item => item.enabled && !item.isConnected).length,
    disabled: devices.filter(item => !item.enabled).length
  }
}

export function protocolDistribution(devices: DeviceRuntimeStatus[]) {
  const map = new Map<string, number>()
  devices.forEach(device => {
    const key = device.protocol || 'Unknown'
    map.set(key, (map.get(key) ?? 0) + 1)
  })
  return Array.from(map.entries())
    .map(([name, count]) => ({ name, count }))
    .sort((left, right) => right.count - left.count)
    .slice(0, 8)
}

export function healthTone(status?: string) {
  const normalized = normalizeStatus(status)
  if (normalized === 'healthy') return 'good'
  if (normalized === 'degraded') return 'warn'
  if (normalized === 'unhealthy') return 'bad'
  return 'normal'
}

export function healthText(status?: string) {
  const normalized = normalizeStatus(status)
  if (normalized === 'healthy') return '健康'
  if (normalized === 'degraded') return '降级'
  if (normalized === 'unhealthy') return '异常'
  return '未知'
}

export function healthScore(status: GatewayStatus | null, health: GatewayHealthResponse | null) {
  if (!status) return 0
  const online = percent(status.onlineDeviceCount ?? 0, status.deviceCount ?? 0)
  const tags = percent(status.goodTagCount ?? 0, status.tagCount ?? 0)
  const devices = status.devices ?? []
  const success = averageSuccessRate(devices)
  const errorPenalty = Math.min((status.recentErrors?.length ?? 0) * 3, 25)
  const healthPenalty = normalizeStatus(health?.status) === 'unhealthy' ? 20 : normalizeStatus(health?.status) === 'degraded' ? 8 : 0
  return Math.max(0, Number(((online * 0.3) + (tags * 0.25) + (success * 0.35) + 10 - errorPenalty - healthPenalty).toFixed(1)))
}

export function normalizeStatus(status?: string) {
  return String(status || '').trim().toLowerCase()
}

export function topSlowDevices(devices: DeviceRuntimeStatus[], count = 6) {
  return [...devices]
    .sort((left, right) => Number(right.lastTaskDurationMs || 0) - Number(left.lastTaskDurationMs || 0))
    .slice(0, count)
}

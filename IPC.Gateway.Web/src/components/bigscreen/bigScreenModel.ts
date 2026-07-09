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
  const runtime = health?.runtime
  if (!status && !runtime) return 0

  const deviceCount = pickPositive(status?.enabledDeviceCount, status?.deviceCount, runtime?.deviceCount)
  const onlineDeviceCount = pickBounded(status?.onlineDeviceCount, runtime?.onlineDeviceCount, deviceCount)
  const tagCount = pickPositive(status?.tagCount, runtime?.tagCount)
  const goodTagCount = pickBounded(status?.goodTagCount, runtime?.goodTagCount, tagCount)
  const online = percent(onlineDeviceCount, deviceCount)
  const tags = percent(goodTagCount, tagCount)
  const devices = status?.devices ?? []
  const success = devices.length > 0 ? averageSuccessRate(devices) : averageFallbackRate(online, tags)
  const recentErrors = Math.max(status?.recentErrors?.length ?? 0, runtime?.recentErrorCount ?? 0)
  const errorPenalty = calculateErrorPenalty(recentErrors)
  const normalizedHealth = normalizeStatus(health?.status)
  const healthPenalty = normalizedHealth === 'unhealthy' ? 16 : normalizedHealth === 'degraded' ? 4 : 0
  return Math.max(0, Number(((online * 0.3) + (tags * 0.25) + (success * 0.35) + 10 - errorPenalty - healthPenalty).toFixed(1)))
}

function pickPositive(...values: Array<number | undefined>) {
  for (const value of values) {
    const normalized = Number(value ?? 0)
    if (normalized > 0) return normalized
  }

  return 0
}

function pickBounded(primary: number | undefined, fallback: number | undefined, total: number) {
  const value = pickPositive(primary, fallback)
  return total > 0 ? Math.min(value, total) : value
}

function averageFallbackRate(...rates: number[]) {
  const valid = rates.filter(rate => Number.isFinite(rate) && rate > 0)
  if (valid.length === 0) return 0
  return Number((valid.reduce((sum, rate) => sum + rate, 0) / valid.length).toFixed(1))
}

function calculateErrorPenalty(errorCount: number) {
  if (!Number.isFinite(errorCount) || errorCount <= 0) return 0
  return Math.min(Math.log2(errorCount + 1) * 1.5, 8)
}

export function normalizeStatus(status?: string) {
  return String(status || '').trim().toLowerCase()
}

export function topSlowDevices(devices: DeviceRuntimeStatus[], count = 6) {
  return [...devices]
    .sort((left, right) => Number(right.lastTaskDurationMs || 0) - Number(left.lastTaskDurationMs || 0))
    .slice(0, count)
}

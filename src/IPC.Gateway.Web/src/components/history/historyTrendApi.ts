import { request, type ApiResult, type HistoryRecordOption } from '../../api'

export interface TrendPoint {
  channelId: string
  deviceId: string
  groupId: string
  tagId: string
}

export interface TrendSample {
  timestamp: string
  value: number
  valueText: string
  quality: string
  unit: string
  segment: number
  path: string
  processing?: string | null
}

export interface TrendSeries {
  point: TrendPoint
  samples: TrendSample[]
}

const endpoint = '/api/config/history/trend'
export const maxTrendPoints = 6

// Paths and names can change in archived rows. The scoped identity remains stable.
export function trendPointKey(point: TrendPoint) {
  return JSON.stringify([point.channelId, point.deviceId, point.groupId, point.tagId].map(id => id.toLowerCase()))
}

export async function getTrendOptions(channelId: string, deviceId: string, groupId: string,
  search: string, language: string, signal: AbortSignal) {
  const params = new URLSearchParams({ channelId, deviceId, groupId, search })
  const response = await request<ApiResult<HistoryRecordOption[]>>(endpoint + '/options?' + params,
    { signal, headers: { 'Accept-Language': language }, cache: 'no-store' })
  if (!response.success) throw new Error(response.errorMessage || '读取点位候选失败')
  return response.data
}

export async function getTrend(points: TrendPoint[], from: string, to: string, language: string, signal: AbortSignal) {
  const params = new URLSearchParams({ from, to, points: JSON.stringify(points) })
  const response = await request<ApiResult<TrendSeries[]>>(endpoint + '?' + params,
    { signal, headers: { 'Accept-Language': language }, cache: 'no-store' })
  if (!response.success) throw new Error(response.errorMessage || '历史查询失败')
  return response.data
}

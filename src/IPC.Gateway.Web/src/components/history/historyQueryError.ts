import { HttpRequestError } from '../../api'

const messages: Record<string, string> = {
  HISTORY_QUERY_TIMEOUT: '历史查询超时，请缩短时间范围后重试。',
  HISTORY_INDEX_UNAVAILABLE: '历史索引尚未就绪，请稍后重试。',
  HISTORY_QUERY_BUSY: '历史查询繁忙，请稍后重试。',
  HISTORY_QUERY_UNAVAILABLE: '历史查询暂时不可用，请稍后重试。',
  HISTORY_SNAPSHOT_EXPIRED: '历史数据已变化，请重新查询。',
  HISTORY_PAGE_EXPIRED: '导出已过期，请重新查询后导出。',
  HISTORY_TEMP_CAPACITY: '临时空间不足，请缩短查询时间范围。',
  HISTORY_EXPORT_FORBIDDEN: '缺少点位历史导出权限。',
  HISTORY_FORBIDDEN: '缺少点位历史查看权限。',
  HISTORY_PATH_CONFLICT: '此时段的路径对应多个点位。',
  HISTORY_INVALID_ARGUMENT: '查询条件无效，请检查点位和时间范围。'
}
export function historyQueryError(cause: unknown, fallback: string) {
  if (cause instanceof HttpRequestError && cause.code && messages[cause.code]) return messages[cause.code]
  return cause instanceof Error ? cause.message : fallback
}

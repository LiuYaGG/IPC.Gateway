import type { HistoryConfig, HistoryDataProcessingConfig, HistoryStorageConfig } from '../../api'

export const aggregationMethodOptions = [
  { label: '平均值', value: 'Average' },
  { label: '最小值', value: 'Min' },
  { label: '最大值', value: 'Max' },
  { label: '求和', value: 'Sum' },
  { label: '计数', value: 'Count' },
  { label: '首值', value: 'First' },
  { label: '末值', value: 'Last' }
]

export const fillModeOptions = [
  { label: '保持上一值', value: 'Previous' },
  { label: '线性插值', value: 'Linear' }
]

export const retentionPolicyOptions = [
  { label: '仅删除过期热数据', value: 'DeleteOnly' },
  { label: '先转冷再删除', value: 'MoveToColdThenDelete' }
]

export function createDefaultHistoryStorage(): HistoryStorageConfig {
  return {
    tieringEnabled: false,
    coldDirectory: 'Data\\HistoryCold',
    retentionPolicy: 'DeleteOnly',
    hotRetentionDays: 7,
    coldRetentionDays: 90,
    compressionEnabled: false,
    compressHotFiles: false,
    compressColdFiles: true,
    compressAfterDays: 3,
    autoCleanupEnabled: true,
    cleanupIntervalHours: 24,
    maxStorageMegabytes: 0
  }
}

export function createDefaultDataProcessing(): HistoryDataProcessingConfig {
  return {
    enabled: false,
    compressionEnabled: false,
    compressionTolerance: 0,
    compressDuplicateText: true,
    downsamplingEnabled: false,
    downsamplingIntervalMs: 1000,
    alignmentEnabled: false,
    alignmentIntervalMs: 1000,
    fillEnabled: false,
    fillIntervalMs: 1000,
    fillMaxGapSeconds: 60,
    fillMode: 'Previous',
    aggregationEnabled: false,
    aggregationIntervalSeconds: 60,
    aggregationMethods: 'Average,Min,Max,Count',
    maxSyntheticPointsPerInput: 1000
  }
}

export function createDefaultHistoryConfig(): HistoryConfig {
  return {
    enabled: true,
    directory: 'Data\\History',
    retentionDays: 7,
    maxViewRecords: 500,
    dataProcessing: createDefaultDataProcessing(),
    storage: createDefaultHistoryStorage()
  }
}

export function normalizeHistoryConfig(input?: Partial<HistoryConfig> | null): HistoryConfig {
  const fallback = createDefaultHistoryConfig()
  const dataProcessing = normalizeDataProcessing(input?.dataProcessing)
  return {
    enabled: Boolean(input?.enabled ?? fallback.enabled),
    directory: String(input?.directory || fallback.directory),
    retentionDays: normalizeNumber(input?.retentionDays, fallback.retentionDays),
    maxViewRecords: normalizeNumber(input?.maxViewRecords, fallback.maxViewRecords),
    dataProcessing,
    storage: normalizeHistoryStorage(input?.storage, normalizeNumber(input?.retentionDays, fallback.retentionDays))
  }
}

export function cloneHistoryConfig(input: HistoryConfig): HistoryConfig {
  return normalizeHistoryConfig(input)
}

export function parseAggregationMethods(value: string) {
  return String(value || '')
    .split(/[,\s;|]+/)
    .map(item => item.trim())
    .filter(Boolean)
}

export function formatNumber(value: number | undefined) {
  return new Intl.NumberFormat('zh-CN').format(Number(value ?? 0))
}

export function formatBytes(value: number | undefined) {
  const bytes = Number(value ?? 0)
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1)} MB`
  return `${(bytes / 1024 / 1024 / 1024).toFixed(2)} GB`
}

function normalizeDataProcessing(input?: Partial<HistoryDataProcessingConfig> | null): HistoryDataProcessingConfig {
  const fallback = createDefaultDataProcessing()
  return {
    enabled: Boolean(input?.enabled ?? fallback.enabled),
    compressionEnabled: Boolean(input?.compressionEnabled ?? fallback.compressionEnabled),
    compressionTolerance: normalizeNumber(input?.compressionTolerance, fallback.compressionTolerance),
    compressDuplicateText: Boolean(input?.compressDuplicateText ?? fallback.compressDuplicateText),
    downsamplingEnabled: Boolean(input?.downsamplingEnabled ?? fallback.downsamplingEnabled),
    downsamplingIntervalMs: normalizeNumber(input?.downsamplingIntervalMs, fallback.downsamplingIntervalMs),
    alignmentEnabled: Boolean(input?.alignmentEnabled ?? fallback.alignmentEnabled),
    alignmentIntervalMs: normalizeNumber(input?.alignmentIntervalMs, fallback.alignmentIntervalMs),
    fillEnabled: Boolean(input?.fillEnabled ?? fallback.fillEnabled),
    fillIntervalMs: normalizeNumber(input?.fillIntervalMs, fallback.fillIntervalMs),
    fillMaxGapSeconds: normalizeNumber(input?.fillMaxGapSeconds, fallback.fillMaxGapSeconds),
    fillMode: input?.fillMode === 'Linear' ? 'Linear' : 'Previous',
    aggregationEnabled: Boolean(input?.aggregationEnabled ?? fallback.aggregationEnabled),
    aggregationIntervalSeconds: normalizeNumber(input?.aggregationIntervalSeconds, fallback.aggregationIntervalSeconds),
    aggregationMethods: String(input?.aggregationMethods || fallback.aggregationMethods),
    maxSyntheticPointsPerInput: normalizeNumber(input?.maxSyntheticPointsPerInput, fallback.maxSyntheticPointsPerInput)
  }
}

function normalizeHistoryStorage(input: Partial<HistoryStorageConfig> | null | undefined, retentionDays: number): HistoryStorageConfig {
  const fallback = createDefaultHistoryStorage()
  return {
    tieringEnabled: Boolean(input?.tieringEnabled ?? fallback.tieringEnabled),
    coldDirectory: String(input?.coldDirectory || fallback.coldDirectory),
    retentionPolicy: input?.retentionPolicy === 'MoveToColdThenDelete' ? 'MoveToColdThenDelete' : 'DeleteOnly',
    hotRetentionDays: normalizeNumber(input?.hotRetentionDays, retentionDays || fallback.hotRetentionDays),
    coldRetentionDays: normalizeNumber(input?.coldRetentionDays, fallback.coldRetentionDays),
    compressionEnabled: Boolean(input?.compressionEnabled ?? fallback.compressionEnabled),
    compressHotFiles: Boolean(input?.compressHotFiles ?? fallback.compressHotFiles),
    compressColdFiles: Boolean(input?.compressColdFiles ?? fallback.compressColdFiles),
    compressAfterDays: normalizeNumber(input?.compressAfterDays, fallback.compressAfterDays),
    autoCleanupEnabled: Boolean(input?.autoCleanupEnabled ?? fallback.autoCleanupEnabled),
    cleanupIntervalHours: normalizeNumber(input?.cleanupIntervalHours, fallback.cleanupIntervalHours),
    maxStorageMegabytes: normalizeNumber(input?.maxStorageMegabytes, fallback.maxStorageMegabytes)
  }
}

function normalizeNumber(value: unknown, fallback: number) {
  const numberValue = Number(value)
  return Number.isFinite(numberValue) ? numberValue : fallback
}

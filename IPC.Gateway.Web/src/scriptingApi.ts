import { request, type ApiResult } from './api'

export type ScriptTriggerType = 'Manual' | 'Interval' | 'TagChanged'
export type ScriptTagChangeMode = 'Any' | 'RisingEdge' | 'FallingEdge'
export type GatewayScriptType = 'DatabaseWrite' | 'TagLinkage' | 'ValueTransform'
export type ValueTransformScriptScope = 'RuleEngine' | 'TagCleaning' | 'Both'
export type ScriptDatabaseProvider =
  | 'SqlServer'
  | 'PostgreSql'
  | 'MySql'
  | 'Sqlite'
  | 'Oracle'
  | 'Dameng'
  | 'KingbaseEs'
  | 'ClickHouse'
export type ScriptExecutionState = 'Idle' | 'Running' | 'Succeeded' | 'Failed' | 'TimedOut' | 'Skipped'

export interface ScriptDatabaseConnection {
  id: string
  name: string
  provider: ScriptDatabaseProvider
  connectionString: string
  enabled: boolean
  connectionTimeoutSeconds: number
  updatedUtc?: string
}

export interface ScriptDatabaseTarget {
  id: string
  name: string
  connectionId: string
  schema: string
  table: string
  enabled: boolean
  allowInsert: boolean
  allowUpdate: boolean
  allowedColumns: string[]
  keyColumns: string[]
  maxAffectedRows: number
  updatedUtc?: string
}

export interface GatewayScriptDefinition {
  id: string
  name: string
  description: string
  enabled: boolean
  scriptType: GatewayScriptType
  triggerType: ScriptTriggerType
  intervalSeconds: number
  triggerTagPath: string
  tagChangeMode: ScriptTagChangeMode
  debounceMilliseconds: number
  timeoutSeconds: number
  allowedWriteTagPaths: string[]
  maxWritesPerExecution: number
  valueTransformScope: ValueTransformScriptScope
  nodeCategory: string
  inputDataType: string
  outputDataType: string
  transformTimeoutMilliseconds: number
  sourceCode: string
  version?: number
  publishedVersion?: number
  publishedUtc?: string
  createdUtc?: string
  updatedUtc?: string
}

export interface ScriptLogEntry {
  timestampUtc: string
  level: string
  message: string
}

export interface ScriptRuntimeStatus {
  scriptId: string
  state: ScriptExecutionState
  executionCount: number
  failureCount: number
  lastStartedUtc?: string
  lastFinishedUtc?: string
  lastDurationMilliseconds: number
  lastError: string
  recentLogs: ScriptLogEntry[]
}

export interface ScriptQueueStatus {
  pendingCount: number
  failedCount: number
  succeededCount: number
  retriedCount: number
  lastError: string
  lastSuccessUtc?: string
}

export interface ScriptCenterOverview {
  connections: ScriptDatabaseConnection[]
  targets: ScriptDatabaseTarget[]
  scripts: GatewayScriptDefinition[]
  runtimeStatuses: ScriptRuntimeStatus[]
  queueStatus: ScriptQueueStatus
}

export interface ScriptValidationResult {
  success: boolean
  errors: string[]
  warnings: string[]
}

export interface ScriptExecutionResult {
  scriptId: string
  state: ScriptExecutionState
  returnValue?: unknown
  errorMessage: string
  startedUtc: string
  finishedUtc: string
  durationMilliseconds: number
  logs: ScriptLogEntry[]
}

export interface ValueTransformCatalogItem {
  id: string
  name: string
  description: string
  scope: ValueTransformScriptScope
  nodeCategory: string
  inputDataType: string
  outputDataType: string
  version: number
  publishedUtc?: string
}

export interface ValueTransformTestResult {
  success: boolean
  value?: unknown
  valueText: string
  outputDataType: string
  errorMessage: string
  durationMilliseconds: number
}

export async function loadScriptOverview() {
  return request<ApiResult<ScriptCenterOverview>>('/api/scripts/overview')
}

export async function validateGatewayScript(sourceCode: string, scriptType: GatewayScriptType) {
  return request<ApiResult<ScriptValidationResult>>('/api/scripts/validate', {
    method: 'POST',
    body: JSON.stringify({ sourceCode, scriptType })
  })
}

export async function saveGatewayScript(script: GatewayScriptDefinition) {
  return request<ApiResult<GatewayScriptDefinition>>('/api/scripts/definitions', {
    method: 'PUT',
    body: JSON.stringify(script)
  })
}

export async function deleteGatewayScript(id: string) {
  return request<ApiResult<null>>(`/api/scripts/definitions/${encodeURIComponent(id)}`, { method: 'DELETE' })
}

export async function executeGatewayScript(id: string) {
  return request<ApiResult<ScriptExecutionResult>>(`/api/scripts/definitions/${encodeURIComponent(id)}/execute`, { method: 'POST' })
}

export async function publishValueTransformScript(id: string) {
  return request<ApiResult<GatewayScriptDefinition>>(`/api/scripts/definitions/${encodeURIComponent(id)}/publish`, { method: 'POST' })
}

export async function loadValueTransformCatalog() {
  return request<ApiResult<ValueTransformCatalogItem[]>>('/api/scripts/value-transforms/catalog')
}

export async function testValueTransformScript(payload: {
  sourceCode: string
  inputDataType: string
  outputDataType: string
  valueText: string
  timeoutMilliseconds: number
}) {
  return request<ApiResult<ValueTransformTestResult>>('/api/scripts/value-transforms/test', {
    method: 'POST',
    body: JSON.stringify(payload)
  })
}

export async function saveScriptConnection(connection: ScriptDatabaseConnection) {
  return request<ApiResult<ScriptDatabaseConnection>>('/api/scripts/connections', {
    method: 'PUT',
    body: JSON.stringify(connection)
  })
}

export async function deleteScriptConnection(id: string) {
  return request<ApiResult<null>>(`/api/scripts/connections/${encodeURIComponent(id)}`, { method: 'DELETE' })
}

export async function testScriptConnection(id: string) {
  return request<ApiResult<{ message: string }>>(`/api/scripts/connections/${encodeURIComponent(id)}/test`, { method: 'POST' })
}

export async function saveScriptTarget(target: ScriptDatabaseTarget) {
  return request<ApiResult<ScriptDatabaseTarget>>('/api/scripts/targets', {
    method: 'PUT',
    body: JSON.stringify(target)
  })
}

export async function deleteScriptTarget(id: string) {
  return request<ApiResult<null>>(`/api/scripts/targets/${encodeURIComponent(id)}`, { method: 'DELETE' })
}

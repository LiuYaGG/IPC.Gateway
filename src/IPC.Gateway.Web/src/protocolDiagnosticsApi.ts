import { request, type ApiResult, type GatewayProtocolCatalogItem, type PlcConnection } from './api'

export type DiagnosticOperation = 'connect' | 'read' | 'batch' | 'write'
export interface DiagnosticPoint {
  address: string
  dataType: string
  elementCount: number
  elementOffset: number
  valueText: string
}
export interface DiagnosticRequest {
  connection: PlcConnection
  operation: DiagnosticOperation
  items: DiagnosticPoint[]
  readBack: boolean
  confirmWrite: boolean
}
export interface DiagnosticPointResult {
  index: number
  address: string
  dataType: string
  status: string
  value: string
  error: string
  scope: string
  readBackValue: string
  readBackStatus: string
  batch: number
}
export interface DiagnosticFrame {
  direction: string
  stage: string
  hex: string
  byteCount: number
  truncated: boolean
  batch: number
}
export interface DiagnosticResult {
  writeExecution?: import('./api').PlcWriteExecution
  operation: DiagnosticOperation
  elapsedMs: number
  mode: string
  outcome: string
  stage: string
  error: string
  items: DiagnosticPointResult[]
  frames: DiagnosticFrame[]
  framesTruncated: boolean
}

export async function diagnosticProtocols(signal?: AbortSignal) {
  const result = await request<ApiResult<GatewayProtocolCatalogItem[]>>('/api/diagnostics/protocols', { signal })
  if (!result.success) throw new Error(result.errorMessage || '无法加载协议目录')
  return result.data
}

export async function runDiagnostic(input: DiagnosticRequest, signal?: AbortSignal, headers?: Record<string,string>) {
  const result = await request<ApiResult<DiagnosticResult>>('/api/diagnostics/protocol/run', {
    method: 'POST', body: JSON.stringify(input), signal, headers: {'Content-Type':'application/json', ...headers}
  })
  if (!result.success) throw new Error(result.errorMessage || '协议诊断失败')
  return result.data
}

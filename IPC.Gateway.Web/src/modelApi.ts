import { request, type ApiResult } from './api'

export interface OnnxTensorDescriptor {
  name: string
  elementType: string
  dimensions: number[]
}

export interface OnnxModelVersion {
  version: number
  status: 'Draft' | 'Published' | 'Archived'
  fileName: string
  relativePath: string
  sha256: string
  fileSize: number
  notes: string
  createdUtc: string
  publishedUtc?: string
  inputs: OnnxTensorDescriptor[]
  outputs: OnnxTensorDescriptor[]
}

export interface OnnxModelDefinition {
  id: string
  name: string
  purpose: string
  description: string
  publishedVersion: number
  createdUtc: string
  updatedUtc: string
  versions: OnnxModelVersion[]
}

export interface OnnxModelRuntimeStats {
  totalRuns: number
  successfulRuns: number
  failedRuns: number
  totalDurationMilliseconds: number
  lastRunUtc?: string
  lastError: string
}

export interface OnnxModelTestResult {
  success: boolean
  score: number
  outputs: number[]
  errorMessage: string
  timestamp: string
  durationMilliseconds: number
}

export async function loadModels() {
  return request<ApiResult<OnnxModelDefinition[]>>('/api/models/')
}

export async function loadModelRuntime() {
  return request<ApiResult<OnnxModelRuntimeStats>>('/api/models/runtime')
}

export async function saveModel(model: { id?: string; name: string; purpose: string; description: string }) {
  return request<ApiResult<OnnxModelDefinition>>('/api/models/', {
    method: 'PUT', body: JSON.stringify(model)
  })
}

export async function uploadModelVersion(modelId: string, file: File, notes: string) {
  const form = new FormData()
  form.append('file', file)
  form.append('notes', notes)
  const response = await fetch(`/api/models/${encodeURIComponent(modelId)}/versions`, {
    method: 'POST', credentials: 'include', body: form
  })
  const payload = await response.json().catch(() => undefined) as ApiResult<OnnxModelVersion> | undefined
  if (!response.ok) throw new Error(payload?.errorMessage || `模型上传失败（HTTP ${response.status}）`)
  return payload as ApiResult<OnnxModelVersion>
}

export async function publishModelVersion(modelId: string, version: number) {
  return request<ApiResult<OnnxModelDefinition>>(`/api/models/${encodeURIComponent(modelId)}/versions/${version}/publish`, { method: 'POST' })
}

export async function testModel(modelId: string, payload: {
  version: number
  inputName?: string
  inputNames?: string
  outputName?: string
  outputIndex: number
  features: number[]
  timeoutMilliseconds: number
}) {
  return request<ApiResult<OnnxModelTestResult>>(`/api/models/${encodeURIComponent(modelId)}/test`, {
    method: 'POST', body: JSON.stringify(payload)
  })
}

export async function deleteModelVersion(modelId: string, version: number) {
  return request<ApiResult<null>>(`/api/models/${encodeURIComponent(modelId)}/versions/${version}`, { method: 'DELETE' })
}

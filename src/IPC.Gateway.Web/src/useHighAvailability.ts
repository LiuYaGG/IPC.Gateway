import { computed, shallowRef } from 'vue'
import { HttpRequestError, NetworkRequestError } from './api'
import { haGet, haLabel, newHaId, type HaCapabilities } from './haApi'
import { tx } from './i18n'
import type { PlcWriteExecution } from './api'

export const haCapabilities = shallowRef<HaCapabilities | null>(null)
export const haCanDispatch = computed(() => haCapabilities.value?.mode === 'Standalone' && haCapabilities.value.canWrite !== false ||
  haCapabilities.value?.mode === 'Cluster' && haCapabilities.value.canWrite === true)
export const haIdentity = computed(() => {
  const state = haCapabilities.value
  if (!state) return ''
  return state.mode === 'Standalone' ? 'Standalone' :
    [state.clusterId, state.connectedNode, state.sourceInstanceId, state.localRole, state.owner, state.epoch].join('|')
})
let latestRequest = 0
export async function refreshHaCapabilities(signal?: AbortSignal) {
  const revision = ++latestRequest
  try {
    const result = await haGet<HaCapabilities>('capabilities', signal)
    if (!signal?.aborted && revision === latestRequest) haCapabilities.value = result
  } catch (error) {
    if (!signal?.aborted && revision === latestRequest) {
      if (error instanceof HttpRequestError && error.status === 404 && haCapabilities.value?.mode !== 'Cluster')
        haCapabilities.value = { mode: 'Standalone', connectedNode: '', localRole: 'Standalone' }
      else if (haCapabilities.value?.mode === 'Cluster') haCapabilities.value = { ...haCapabilities.value, canWrite: false }
    }
  }
}
export function haWriteHeaders(): Record<string, string> {
  const state = haCapabilities.value
  if (state?.mode !== 'Cluster' || !state.epoch) return {}
  return { 'X-IPC-Expected-Epoch': String(state.epoch), 'X-IPC-Command-Id': newHaId() }
}
export function executionSummary(execution:PlcWriteExecution) {
  return [haLabel(execution.outcome),tx('已确认分片')+' '+execution.confirmedSegments,
    tx('命令 ID')+' '+execution.commandId,execution.lastAttemptId?tx('尝试 ID')+' '+execution.lastAttemptId:''].filter(Boolean).join(' · ')
}
export async function explainWriteFailure(error:unknown, headers:Record<string,string>, canInspect:boolean) {
  const original=error instanceof Error?error.message:String(error)
  const id=headers['X-IPC-Command-Id']
  if(!id || !(error instanceof NetworkRequestError || error instanceof HttpRequestError && error.status>=500))
    return {uncertain:false,message:original}
  const parts=[tx('写入响应未确认，请先核对原命令的执行记录。'),tx('命令 ID')+' '+id]
  if(canInspect){
    try{
      const data=await haGet<{total:number;items:{attemptId:string;deviceResult?:string}[]}>(
        'write-attempts?evidence=local&page=1&pageSize=5&commandId='+encodeURIComponent(id),AbortSignal.timeout(4000))
      for(const attempt of data.items)parts.push(attempt.attemptId+' · '+haLabel(attempt.deviceResult || 'Unknown'))
    }catch{ /* The command ID remains available even when the node cannot be reached. */ }
  }
  return {uncertain:true,message:parts.join(' · ')}
}

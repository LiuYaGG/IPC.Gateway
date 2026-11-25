export interface SensitiveApproval { contextId:string; resolve:(ticket:string)=>void; reject:(error:Error)=>void; onCancelled?:()=>void }
let listener:((approval:SensitiveApproval)=>void)|undefined
let pending:SensitiveApproval|undefined
export function registerSensitiveApproval(handler:(approval:SensitiveApproval)=>void) {
  listener=handler
  return ()=>{if(listener===handler){listener=undefined;cancelSensitiveApproval()}}
}
export function cancelSensitiveApproval() {
  const current=pending
  current?.reject(new Error('已取消高危操作。'))
  current?.onCancelled?.()
}
export function requestSensitiveApproval(contextId:string,signal?:AbortSignal|null):Promise<string>{
  if(!listener)return Promise.reject(new Error('请重新打开网页后完成安全验证。'))
  if(pending)return Promise.reject(new Error('请先完成当前安全验证。'))
  if(signal?.aborted)return Promise.reject(new DOMException('Aborted','AbortError'))
  return new Promise((resolve,reject)=>{
    const onAbort=()=>{if(pending===approval){approval.reject(new DOMException('Aborted','AbortError'));approval.onCancelled?.()}}
    const settle=(result:string|Error)=>{
      signal?.removeEventListener('abort',onAbort)
      if(pending!==approval)return
      pending=undefined
      if(result instanceof Error)reject(result);else resolve(result)
    }
    const approval:SensitiveApproval={contextId,resolve:ticket=>settle(ticket),reject:error=>settle(error)}
    pending=approval
    signal?.addEventListener('abort',onAbort,{once:true})
    listener!(approval)
  })
}

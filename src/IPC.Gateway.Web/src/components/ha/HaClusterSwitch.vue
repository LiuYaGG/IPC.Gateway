<template>
  <div class="ha-cluster-switch">
    <div class="ha-toolbar">
      <strong>{{ tx('双机热备') }}</strong>
      <a-switch v-if="known" :checked="phase==='Enabled' || phase==='Disabling'" :loading="sending || transitioning"
        :disabled="!!disabledReason" :aria-label="tx('双机热备')" @change="openDialog" />
      <span role="status">{{ phaseLabel }}</span>
      <span v-if="(phase==='Disabled' || phase==='Disabling') && state?.standaloneNode">
        · {{ tx('单机业务节点') }} {{ state.standaloneNode }}
      </span>
    </div>
    <p v-if="disabledReason && !transitioning" class="ha-note">{{ disabledReason }}</p>
    <p v-if="phase==='Disabled'" class="ha-note">{{ tx('关闭期间，另一台网关不会自动接管业务。') }}</p>
    <p v-if="transitioning || state?.reasonCode || state?.message" class="ha-note" aria-live="polite">
      {{ haLabel(state?.reasonCode || state?.message || (phase==='Disabling' ? '正在确认旧租约和单机业务节点。' : '正在等待网关退出单机业务并恢复集群授主。')) }}
    </p>
    <HaOperationStatus v-if="operationId" kind="cluster-switch" :id="operationId" @finished="finish" />
    <p v-else-if="result" role="status">{{ result }}</p>
    <a-alert v-if="error" type="warning" show-icon :message="error" />
    <a-modal :open="dialog!==null" centered :title="tx(dialog?.enabled ? '启用双机热备' : '关闭双机热备')"
      :ok-text="tx(dialog?.enabled ? '确认启用' : '确认关闭')" :cancel-text="tx('取消')" :confirm-loading="sending"
      :ok-button-props="{danger:!dialog?.enabled,disabled:stale}" :closable="!sending" :mask-closable="!sending"
      :cancel-button-props="{disabled:sending}" @cancel="cancel" @ok="submit">
      <p v-if="dialog?.enabled">{{ tx('单机业务将完成交接，之后由集群选出的主节点执行采集和写入。') }}</p>
      <template v-else><p><strong>{{ tx('单机业务节点') }} {{ dialog?.node }}</strong></p>
        <p>{{ tx(embedded ? '请先停止另一台网关，保持本机网关和 C 见证成员在线。确认关闭后由本机独立采集和写入。' : '请先停止另一台的网关服务，并保持仲裁成员运行。关闭后仅本机采集和写入，本机离线期间不会自动切换。') }}</p>
      </template>
      <a-alert v-if="stale" type="warning" show-icon :message="tx('集群状态已变化，请重新确认。')" />
    </a-modal>
  </div>
</template>
<script setup lang="ts">
import { computed, ref, onBeforeUnmount, watch } from 'vue'
import { tx } from '../../i18n'
import { haLabel, haSend, newHaId, type HaClusterSwitch, type HaOperation } from '../../haApi'
import HaOperationStatus from './HaOperationStatus.vue'
import { NetworkRequestError } from '../../api'
const props=defineProps<{
  embedded:boolean
  state?: HaClusterSwitch
  node?: string
  scope:string
  canManage:boolean
  eligibleForEnrollment:boolean
  locked:boolean
}>()
const emit=defineEmits<{ enroll:[]; changed:[HaClusterSwitch]; refresh:[] }>()
const error=ref(''),sending=ref(false)
const controller=new AbortController()
onBeforeUnmount(()=>controller.abort())
const phase=computed(()=>props.state?.phase ?? 'Unknown')
const labels:Record<HaClusterSwitch['phase'],string>={Unconfigured:'尚未启用',Enabled:'已启用',Disabling:'正在关闭',Disabled:'已关闭',Enabling:'正在启用',Unknown:'状态未确认'}
const known=computed(()=>phase.value==='Unconfigured' || (!!props.state?.supported && phase.value!=='Unknown'))
const phaseLabel=computed(()=>tx(known.value ? labels[phase.value] : '状态未确认'))
const transitioning=computed(()=>phase.value==='Disabling'||phase.value==='Enabling')
const storageKey='ipc.ha.cluster-switch.'+props.scope
const operationId=ref(sessionStorage.getItem(storageKey)||''), result=ref('')
function track(id:string){operationId.value=id;sessionStorage.setItem(storageKey,id)}
watch(()=>props.state, state=>{
  if(transitioning.value && state?.operationId && !operationId.value)track(state.operationId)
},{immediate:true})
function finish(op:HaOperation){
  if(operationId.value!==op.operationId)return
  sessionStorage.removeItem(storageKey);operationId.value=''
  result.value=haLabel(op.reasonCode || op.message || op.status)
  emit('refresh')
}
const disabledReason=computed(()=>{
  if(!props.canManage)return tx('当前账号仅可查看配置。')
  if(!known.value)return tx('无法确认集群开关，请核对成员连通性和开关支持情况。')
  if(!props.state?.supported)return tx('请先在本机组网配置中完成成员登记，并确认三个仲裁成员支持集群开关。')
  if(transitioning.value)return tx('操作正在执行。')
  if(phase.value==='Unconfigured' && !props.eligibleForEnrollment)return tx('首次启用前，请先保存配置并通过连接预检。')
  if(props.locked)return tx('请等待当前请求完成。')
  if(phase.value==='Enabled' && props.node!=='A' && props.node!=='B')return tx('请直接访问要保留单机业务的 A 或 B 节点。')
  return ''
})
type Change={enabled:boolean;generation:string;node?:string;phase:HaClusterSwitch['phase'];id:string}
const dialog=ref<Change|null>(null)
const stale=computed(()=>!!dialog.value && (!!disabledReason.value || dialog.value.generation!==props.state?.generation ||
  dialog.value.phase!==phase.value || dialog.value.node!==props.node))
function openDialog(){
  if(disabledReason.value || !props.state || sending.value)return
  error.value='';result.value=''
  if(phase.value==='Unconfigured'){emit('enroll');return}
  dialog.value={enabled:phase.value==='Disabled',generation:props.state.generation,node:props.node,phase:phase.value,id:newHaId()}
}
function cancel(){if(!sending.value)dialog.value=null}
async function submit(){
  const request=dialog.value
  if(!request || stale.value || sending.value)return
  sending.value=true
  try{
    const next=await haSend<HaClusterSwitch>('cluster-switch',{
      operationId:request.id,enabled:request.enabled,expectedGeneration:request.generation,
      standaloneNode:request.enabled ? undefined : request.node
    },'POST',controller.signal)
    if(!controller.signal.aborted){track(next.operationId || request.id);emit('changed',next)}
  }catch(e){
    if(!controller.signal.aborted){
      if(e instanceof NetworkRequestError)track(request.id)
      error.value=e instanceof NetworkRequestError ? tx('开关请求的结果待确认，请核对当前状态。') : e instanceof Error?e.message:String(e)
      emit('refresh')
    }
  }finally{dialog.value=null;sending.value=false}
}
</script>
<style scoped>
.ha-cluster-switch{margin-bottom:14px;padding-bottom:12px;border-bottom:1px solid #dde6ee}
.ha-cluster-switch .ha-note{margin:5px 0 0;overflow-wrap:anywhere}
</style>

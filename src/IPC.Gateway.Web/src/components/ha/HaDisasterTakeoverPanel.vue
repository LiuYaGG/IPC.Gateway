<template>
  <section class="ha-panel ha-disaster" :class="{'ha-disaster--active': takeoverActive}">
    <div class="ha-disaster__heading">
      <div>
        <h3>{{ tx('人工灾难接管') }}</h3>
        <p class="ha-disaster__identity">
          {{ tx('当前访问节点') }} <strong>{{ state?.localNode || '—' }}</strong>
          <span> · {{ tx('当前管理范围') }} {{ scope || '—' }}</span>
        </p>
      </div>
      <div class="ha-disaster__actions">
        <a-tag :color="takeoverActive ? 'red' : state?.eligible ? 'orange' : 'default'">{{ stateLabel }}</a-tag>
        <a-button :loading="loading" :disabled="sending" @click="refresh">{{ tx('刷新') }}</a-button>
        <a-button v-if="canManage && !state?.active" danger :disabled="!canTakeover" @click="openDialog('takeover')">{{ tx('人工灾难接管') }}</a-button>
      </div>
    </div>
    <a-alert v-if="fetchError || actionError" type="warning" show-icon :message="actionError || fetchError" />
    <a-alert v-if="takeoverActive" type="error" show-icon :message="tx('人工灾难接管已启用')"
      :description="tx('人工灾难接管期间，本节点绕过正常多数派授主运行。')" />
    <a-alert v-else-if="expired" type="warning" show-icon :message="tx('授权已到期，正在核实当前状态。')" />
    <template v-if="state?.active && state.grant">
      <dl class="ha-disaster__fields">
        <dt>{{ tx('授权到期时间') }}</dt><dd class="ha-disaster__expiry">{{ formatDateTime(state.grant.expiresAtUtc) }}</dd>
        <dt>{{ tx('接管人') }}</dt><dd>{{ state.grant.actor }}</dd>
        <dt>{{ tx('接管原因') }}</dt><dd>{{ state.grant.reason }}</dd>
        <dt>{{ tx('授权开始时间') }}</dt><dd>{{ formatDateTime(state.grant.startedAtUtc) }}</dd>
      </dl>
      <div v-if="canManage" class="ha-disaster__actions">
        <a-button danger :disabled="!canRenew" @click="openDialog('renew')">{{ tx('续期人工接管') }}</a-button>
        <a-button :disabled="!canExit" @click="openDialog('exit')">{{ tx('退出人工接管') }}</a-button>
      </div>
    </template>
    <p v-if="loaded && !state?.active && !state?.eligible" class="ha-disaster__note">{{ haLabel(state?.blockedReason || '本机当前不满足人工灾难接管条件。') }}</p>
    <p v-if="loaded && !canManage" class="ha-disaster__note">{{ tx('仅拥有灾难接管管理权限的管理员可操作。') }}</p>
    <a-collapse v-model:active-key="expanded" ghost class="ha-disaster__details">
      <a-collapse-panel key="details" :header="tx('接管详情与记录')">
        <p class="ha-disaster__note">{{ tx('仅在其他网关已物理停机或隔离后使用。') }}</p>
        <p class="ha-disaster__note">{{ tx('无法连通不代表其他网关已停止。网络分区可能导致两台网关同时采集或写入 PLC。') }}</p>
        <p class="ha-disaster__note">{{ tx('任一其他节点恢复连通或授权到期后，人工授权立即撤销，再由正常仲裁决定业务归属。重启不会恢复人工授权。') }}</p>
        <h4>{{ tx('其他成员连通性') }}</h4>
        <ul class="ha-disaster__peers">
          <li v-for="peer in state?.peers || []" :key="peer.nodeId">
            <strong>{{ peer.nodeId }}</strong>
            <a-tag :color="peer.memberReachable ? 'green' : 'default'">{{ tx(peer.memberReachable ? '仲裁成员可达' : '仲裁成员不可达') }}</a-tag>
            <a-tag v-if="peer.nodeId!=='C'" :color="peer.gatewayReachable ? 'green' : 'default'">{{ tx(peer.gatewayReachable ? '网关可达' : '网关不可达') }}</a-tag>
          </li>
        </ul>
        <h4>{{ tx('最近人工接管事件') }}</h4>
        <ol v-if="state?.recentEvents?.length" class="ha-disaster__events">
          <li v-for="(event,index) in state.recentEvents" :key="event.operationId+'-'+event.eventType+'-'+index">
            <div><time>{{ formatDateTime(event.timestampUtc) }}</time> · {{ event.nodeId }} · <strong>{{ haLabel(event.eventType) }}</strong></div>
            <div>{{ event.actor || '—' }} · {{ event.reason?.startsWith('HA_') ? haLabel(event.reason) : event.reason || '—' }}</div>
            <div v-if="event.expiresAtUtc">{{ tx('授权到期时间') }} {{ formatDateTime(event.expiresAtUtc) }}</div>
          </li>
        </ol>
        <p v-else class="ha-disaster__note">{{ tx('暂无人工接管事件') }}</p>
      </a-collapse-panel>
    </a-collapse>
    <a-modal centered :open="dialog!==null" :title="dialogTitle" :ok-text="confirmText" :cancel-text="tx('取消')"
      :width="600" :confirm-loading="sending" :ok-button-props="{danger:dialog?.action!=='exit',disabled:!canSubmit}"
      :closable="!sending" :mask-closable="!sending" :cancel-button-props="{disabled:sending}" @cancel="cancelDialog" @ok="submit">
      <template v-if="dialog">
        <p class="ha-disaster__identity">{{ tx('当前访问节点') }} <strong>{{ dialog.node }}</strong> · {{ tx('当前管理范围') }} {{ dialog.scope || '—' }}</p>
        <a-alert v-if="dialog.action!=='exit'" type="error" show-icon :message="tx('仅在其他网关已物理停机或隔离后使用。')"
          :description="tx('无法连通不代表其他网关已停止。网络分区可能导致两台网关同时采集或写入 PLC。')" />
        <a-alert v-else type="info" show-icon :message="tx('退出后，本节点停止使用人工授权，由正常仲裁决定是否恢复业务。')" />
        <a-alert v-if="stale" class="ha-disaster__modal-note" type="warning" show-icon :message="tx('状态已变化，请关闭窗口并重新确认。')" />
        <a-alert v-if="actionError" class="ha-disaster__modal-note" type="warning" show-icon :message="actionError" />
        <a-form layout="vertical" class="ha-disaster__form" :disabled="sending">
          <a-form-item v-if="dialog.action!=='exit'" :label="tx('授权时长')" :extra="tx('有效期从本次确认成功时开始计算。')">
            <a-select v-model:value="durationMinutes" :options="durationOptions" />
          </a-form-item>
          <a-form-item :label="tx('原因')" required :extra="tx('请填写本次操作原因，续期也需要重新说明。')">
            <a-textarea v-model:value="reason" :maxlength="512" :rows="3" show-count />
          </a-form-item>
          <a-form-item :label="tx('请输入本机节点 ID 确认')" required :extra="formatMessage('必须与当前节点 {0} 完全一致。',[dialog.node])">
            <a-input v-model:value="confirmationNode" :maxlength="8" autocomplete="off" />
          </a-form-item>
          <a-form-item v-if="dialog.action!=='exit'" required>
            <a-checkbox v-model:checked="peerIsolated">{{ tx('我已确认其他业务网关已物理停机或与 PLC、业务网络隔离，不会同时采集或写入。') }}</a-checkbox>
          </a-form-item>
        </a-form>
        <p v-if="dialog.action!=='exit'" class="ha-disaster__note">{{ tx('任一其他节点恢复连通或授权到期后，人工授权立即撤销，再由正常仲裁决定业务归属。重启不会恢复人工授权。') }}</p>
      </template>
    </a-modal>
  </section>
</template>

<script setup lang="ts">
import { computed, onActivated, onBeforeUnmount, onDeactivated, onMounted, ref, watch } from 'vue'
import { formatMessage, tx } from '../../i18n'
import { HttpRequestError, NetworkRequestError } from '../../api'
import { haGet, haLabel, haSend, newHaId, type HaDisasterStatus, type HaDisasterTakeoverRequest, type HaDisasterExitRequest } from '../../haApi'
import { formatDateTime } from '../../utils/format'
import { PERMISSIONS, usePermissions } from '../../utils/permissions'

const props=defineProps<{active:boolean;scope:string}>()
const emit=defineEmits<{changed:[]}>()
const {hasPermission}=usePermissions()
const canManage=computed(()=>hasPermission(PERMISSIONS.haDisasterManage))
const state=ref<HaDisasterStatus|null>(null),loaded=ref(false),loading=ref(false),sending=ref(false)
const fetchError=ref(''),actionError=ref(''),expanded=ref<string[]>([]),now=ref(Date.now())
const durationMinutes=ref(60),reason=ref(''),confirmationNode=ref(''),peerIsolated=ref(false)
const durations=[15,30,60,120,240,480,1440]
const durationOptions=computed(()=>durations.map(value=>({value,label:formatMessage('{0} 分钟',[value])})))
type Action='takeover'|'renew'|'exit'
type Confirmation={action:Action;node:'A'|'B';scope:string;operationId:string;grantId?:string}
const dialog=ref<Confirmation|null>(null)
let componentActive=true,running=false,timer:number|undefined,clock:number|undefined,readRequest:AbortController|undefined,writeRequest:AbortController|undefined

const expired=computed(()=>!!state.value?.active && !!state.value.grant && new Date(state.value.grant.expiresAtUtc).getTime()<=now.value)
const takeoverActive=computed(()=>!!state.value?.active && state.value.grant?.active!==false && !expired.value)
const available=computed(()=>props.active && loaded.value && !fetchError.value && !sending.value && canManage.value)
const canTakeover=computed(()=>available.value && !!state.value?.eligible && !state.value.active)
const canRenew=computed(()=>available.value && takeoverActive.value && !!state.value?.eligible)
const canExit=computed(()=>available.value && !!state.value?.active)
const stateLabel=computed(()=>tx(!loaded.value || fetchError.value ? '人工灾难接管状态待核实' : takeoverActive.value ? '人工灾难接管已启用' : expired.value ? '人工灾难接管已到期' : state.value?.eligible ? '满足人工接管的连通性条件' : '未启用人工灾难接管'))
const dialogTitle=computed(()=>tx(dialog.value?.action==='exit' ? '确认退出人工接管' : dialog.value?.action==='renew' ? '确认续期人工接管' : '确认人工灾难接管'))
const confirmText=computed(()=>tx(dialog.value?.action==='exit'?'确认退出':dialog.value?.action==='renew'?'确认续期':'确认接管'))
const stale=computed(()=>{
  const pending=dialog.value,current=state.value
  if(!pending)return false
  if(!props.active || !loaded.value || !!fetchError.value || !canManage.value || pending.scope!==props.scope || pending.node!==current?.localNode)return true
  if(pending.action==='takeover')return !current.eligible || current.active
  return !current.active || expired.value || current.grant?.operationId!==pending.grantId || (pending.action==='renew' && !current.eligible)
})
const canSubmit=computed(()=>!!dialog.value && !stale.value && !sending.value && !!reason.value.trim() &&
  confirmationNode.value===dialog.value.node && (dialog.value.action==='exit' || (peerIsolated.value && durations.includes(durationMinutes.value))))

function resetDialog(){dialog.value=null;reason.value='';confirmationNode.value='';peerIsolated.value=false;durationMinutes.value=60}
function cancelDialog(){if(!sending.value)resetDialog()}
function openDialog(action:Action){
  if(action==='takeover'?!canTakeover.value:action==='renew'?!canRenew.value:!canExit.value)return
  const current=state.value
  if(!current || !['A','B'].includes(current.localNode))return
  resetDialog();actionError.value=''
  dialog.value={action,node:current.localNode,scope:props.scope,operationId:newHaId(),grantId:current.grant?.operationId}
}
function update(next:HaDisasterStatus){
  const previous=state.value
  state.value=next;loaded.value=true;fetchError.value='';now.value=Date.now()
  if(next.active && !previous?.active)expanded.value=['details']
  if(dialog.value && stale.value)resetDialog()
  if(previous && (previous.active!==next.active || previous.grant?.operationId!==next.grant?.operationId))emit('changed')
}
function errorText(error:unknown){return error instanceof HttpRequestError && error.code ? haLabel(error.code) : error instanceof Error ? error.message : String(error)}
async function poll(){
  if(!running || !props.active || document.hidden || readRequest || sending.value)return
  const controller=new AbortController();readRequest=controller;loading.value=true
  try{
    const next=await haGet<HaDisasterStatus>('disaster',controller.signal)
    if(!controller.signal.aborted)update(next)
  }catch(error){if(!controller.signal.aborted)fetchError.value=errorText(error) || tx('灾难接管状态查询失败')}
  finally{if(readRequest===controller){readRequest=undefined;loading.value=false}}
}
function refresh(){actionError.value='';void poll()}
async function submit(){
  const pending=dialog.value
  if(!pending || !canSubmit.value)return
  const controller=new AbortController();writeRequest=controller
  readRequest?.abort();readRequest=undefined;loading.value=false;sending.value=true;actionError.value=''
  const explanation=reason.value.trim()
  try{
    const body:HaDisasterTakeoverRequest|HaDisasterExitRequest=pending.action==='exit' ?
      {operationId:pending.operationId,reason:explanation} :
      {operationId:pending.operationId,reason:explanation,confirmationNode:pending.node,peerIsolated:true,durationMinutes:durationMinutes.value}
    const next=await haSend<HaDisasterStatus>(pending.action==='exit'?'disaster/exit':'disaster',body,'POST',controller.signal)
    if(controller.signal.aborted)return
    update(next);resetDialog()
  }catch(error){
    if(controller.signal.aborted)return
    actionError.value=error instanceof NetworkRequestError ? tx('请求结果尚未确认，请先刷新人工灾难接管状态。') : errorText(error)
    resetDialog()
  }finally{
    if(writeRequest===controller){writeRequest=undefined;sending.value=false;void poll()}
  }
}
function pause(){
  running=false;window.clearInterval(timer);window.clearInterval(clock);timer=undefined;clock=undefined
  readRequest?.abort();readRequest=undefined;loading.value=false
  if(writeRequest){writeRequest.abort();writeRequest=undefined;loaded.value=false}
  sending.value=false;resetDialog()
}
function resume(){
  if(running || !componentActive || !props.active || document.hidden)return
  running=true;now.value=Date.now();void poll()
  timer=window.setInterval(poll,4000)
  clock=window.setInterval(()=>{now.value=Date.now()},1000)
}
function visibility(){if(document.hidden)pause();else resume()}
watch(()=>props.active,active=>{if(active)resume();else pause()})
watch(()=>props.scope,()=>{pause();state.value=null;loaded.value=false;fetchError.value='';actionError.value='';resume()})
watch(expired,value=>{if(value){resetDialog();void poll()}})
watch(canManage,value=>{if(!value)resetDialog()})
onMounted(()=>{document.addEventListener('visibilitychange',visibility);resume()})
onActivated(()=>{componentActive=true;resume()})
onDeactivated(()=>{componentActive=false;pause()})
onBeforeUnmount(()=>{componentActive=false;pause();document.removeEventListener('visibilitychange',visibility)})
defineExpose({reload:refresh})
</script>

<style scoped>
.ha-disaster{flex:0 0 auto;min-width:0;display:flex;flex-direction:column;gap:10px}
.ha-disaster--active{border-color:#ff7875;background:#fff9f8}
.ha-disaster__heading{display:flex;align-items:center;justify-content:space-between;gap:10px;flex-wrap:wrap}
.ha-disaster__heading h3{margin:0}
.ha-disaster__identity{margin:4px 0 0;overflow-wrap:anywhere;color:#53687a}
.ha-disaster__identity strong{color:#172f43}
.ha-disaster__actions{display:flex;align-items:center;gap:8px;flex-wrap:wrap}
.ha-disaster__actions .ant-tag{margin-inline-end:0;white-space:normal}
.ha-disaster__fields{display:grid;grid-template-columns:minmax(110px,auto) 1fr;gap:7px 12px;margin:0}
.ha-disaster__fields dt{color:#53687a}
.ha-disaster__fields dd{margin:0;overflow-wrap:anywhere;white-space:pre-wrap}
.ha-disaster__expiry{font-weight:600;color:#b42318}
.ha-disaster__note{margin:0;color:#53687a;overflow-wrap:anywhere}
.ha-disaster__details{min-width:0}
.ha-disaster__details h4{margin:14px 0 8px}
.ha-disaster__details .ha-disaster__note+.ha-disaster__note{margin-top:8px}
.ha-disaster__peers,.ha-disaster__events{list-style:none;margin:0;padding:0}
.ha-disaster__peers li{display:flex;align-items:center;gap:8px;flex-wrap:wrap;margin:6px 0}
.ha-disaster__peers .ant-tag{margin-inline-end:0;white-space:normal}
.ha-disaster__events{max-height:260px;overflow:auto}
.ha-disaster__events li{border-top:1px solid #e3e9ee;padding:8px 0;overflow-wrap:anywhere}
.ha-disaster__events li:first-child{border-top:0}
.ha-disaster__modal-note,.ha-disaster__form{margin-top:12px}
.ha-disaster__form .ant-checkbox-wrapper{align-items:flex-start;line-height:1.6}
@media(max-width:600px){.ha-disaster__fields{grid-template-columns:1fr;gap:4px}.ha-disaster__fields dd{margin-bottom:5px}.ha-disaster__actions{width:100%}}
</style>

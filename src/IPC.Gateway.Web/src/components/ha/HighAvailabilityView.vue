<template>
  <section class="ha-view">
    <div class="ha-toolbar ha-heading">
      <span>{{ tx('当前访问节点') }} <strong>{{ status?.connectedNode || '—' }}</strong></span>
      <div class="ha-actions">
        <a-button v-if="businessLink" :href="businessLink" target="_blank" rel="noopener noreferrer">{{ tx('打开业务入口') }}</a-button>
        <a-button :loading="loading" @click="reload">{{ tx('刷新') }}</a-button>
        <a-button v-if="canSwitch && cluster && !modeChanging && !single" type="primary" :disabled="!status?.canStartSwitchover || !!operationId" @click="openSwitch">
          {{ tx('计划切换') }}
        </a-button>
      </div>
    </div>
    <a-alert v-if="error" show-icon type="warning" :message="error" />
    <a-alert v-if="single" type="info" show-icon :message="tx('双机热备已关闭')+' · '+tx('单机业务节点')+' '+single" />
    <a-alert v-else-if="modeChanging" type="info" show-icon :message="tx(status?.clusterSwitch?.phase==='Disabling'?'正在关闭双机热备':'正在启用双机热备')" />
    <a-alert v-else-if="status && !cluster && !disaster" type="info" show-icon :message="tx('当前为单机模式')">
      <template v-if="canManage" #action><a-button type="link" @click="tab='config'">{{ tx('开始配置') }}</a-button></template>
    </a-alert>
    <a-alert v-if="cluster && unavailable && !disaster" show-icon type="warning" :message="tx('无法核实当前主机，新的 PLC 写入已禁止。')" />
    <a-alert v-if="disaster" show-icon type="warning" :message="tx('当前使用人工灾难接管授权，请保持其他业务网关停机或隔离。')" />
    <div class="ha-metrics">
      <div><small>{{ tx(disaster ? '人工灾难接管' : single ? '单机业务节点' : '业务主机') }}</small><strong>{{ disaster ? status?.connectedNode : single || (cluster ? (!unavailable && status?.owner || tx('未知')) : '—') }}</strong></div>
      <div><small>{{ tx('可达仲裁成员') }}</small><strong>{{ cluster ? (status?.reachableMembers ?? '—') + ' / ' + (status?.memberCount || 3) : '—' }}</strong></div>
      <div><small>{{ tx('业务任期') }}</small><strong>{{ status?.epoch || status?.lastKnownEpoch || '—' }} {{ cluster && unavailable ? tx('（上次）') : '' }}</strong></div>
    </div>
    <HaOperationStatus v-if="operationId" kind="switchovers" :id="operationId" @finished="onFinished" @reauthenticate="onOperationSessionExpired" />
    <a-alert v-else-if="lastOperation" :type="['Completed','Succeeded'].includes(lastOperation.status)?'success':'warning'" show-icon :message="haLabel(lastOperation.reasonCode || lastOperation.message || lastOperation.status)" />
    <a-tabs v-model:active-key="tab" class="ha-tabs">
      <a-tab-pane key="overview" :tab="tx('集群总览')">
        <section class="ha-panel ha-overview">
          <h3>{{ tx('节点状态') }}</h3>
          <div v-if="status?.observedAtUtc">{{ tx('观测时间') }} {{ formatDateTime(status.observedAtUtc) }}</div>
          <div v-if="status?.reasonCodes?.length" class="ha-note">{{ status.reasonCodes.map(haLabel).join(' · ') }}</div>
          <GatewayTable resize-key="ha-members" :columns="memberColumns" :data-source="status?.members || []" row-key="nodeId" :paging="false" :scroll="{ x: 'max-content' }">
            <template #bodyCell="{column,record}">
              <template v-if="column.key==='node'">{{ record.nodeId }}</template>
              <template v-else-if="column.key==='role'">{{ haLabel(record.arbiterRole) }}</template>
              <template v-else-if="column.key==='business'">{{ businessRole(record.nodeId) }}</template>
              <a-tag v-else-if="column.key==='reachable'" :color="record.reachable?'green':'default'">{{ tx(record.reachable?'可达':'未确认') }}</a-tag>
              <template v-else-if="column.key==='ready'">{{ record.nodeId==='C'?tx('仅仲裁'):unavailable?tx('未知'):tx(record.ready?'已就绪':'未就绪') }}</template>
              <template v-else-if="column.key==='version'">{{ record.configVersion || '—' }}</template>
              <template v-else-if="column.key==='observed'">{{ record.observedAtUtc ? formatDateTime(record.observedAtUtc) : '—' }}</template>
              <template v-else-if="column.key==='actions'">
                <a-button type="link" @click="selectedNode=record.nodeId">{{ tx('详情') }}</a-button>
              </template>
            </template>
          </GatewayTable>
          <template v-if="cluster && status?.historyReplica">
            <h3 class="ha-replica-title">{{ tx('历史库复制') }}</h3>
            <dl class="ha-fields">
              <dt>{{ tx('状态') }}</dt><dd>{{ haLabel(status.historyReplica.state) }}</dd>
              <dt>{{ tx('待同步数据量') }}</dt><dd>{{ status.historyReplica.pendingBytes == null ? '—' : status.historyReplica.pendingBytes.toLocaleString()+' B' }}</dd>
              <dt>{{ tx('分卷') }}</dt><dd>{{ status.historyReplica.volume || '—' }}</dd>
              <dt>{{ tx('观测时间') }}</dt><dd>{{ status.historyReplica.checkedAt ? formatDateTime(status.historyReplica.checkedAt) : '—' }}</dd>
            </dl>
          </template>
        </section>
      </a-tab-pane>
      <a-tab-pane key="records" :tab="tx('切换记录')"><HaRecords v-if="visited.has('records')" :active="activeView && tab==='records'" /></a-tab-pane>
      <a-tab-pane key="disaster" :tab="tx('人工灾难接管')"><HaDisasterTakeoverPanel v-if="visited.has('disaster')" :active="activeView && tab==='disaster'" :scope="scope" @changed="reload" /></a-tab-pane>
      <a-tab-pane key="config" :tab="tx('高可用配置')">
        <template v-if="visited.has('config')">
          <HaSetup v-if="canManage && (setupMode===null || setupMode==='review')" :gateway-request="setupRequest" :active="activeView && tab==='config'" :review="setupMode==='review'" @ready="setupReady" @draft-saved="setupDraftSaved" />
          <HaConfigPanel ref="configPanel" v-if="!canManage || setupMode!==null" v-show="!canManage || setupMode!=='review'" :status="status" :scope="scope"
            :embedded="setupMode==='embedded' || setupMode==='review'" @open-setup="setupMode='review'" @changed="reload" />
        </template>
      </a-tab-pane>
    </a-tabs>
    <a-modal centered :open="!!selectedNode" :title="tx('节点详情')+' · '+selectedNode" :footer="null" @cancel="selectedNode=''">
      <dl v-if="selected" class="ha-fields">
        <dt>{{ tx('仲裁客户端地址') }}</dt><dd>{{ selected.clientUrl || '—' }}</dd>
        <dt>{{ tx('成员通信地址') }}</dt><dd>{{ selected.peerUrl || '—' }}</dd>
        <dt>{{ tx('网关管理地址') }}</dt><dd>{{ selected.gatewayUrl || '—' }}</dd>
        <template v-if="selected.nodeId!=='C'">
          <dt>{{ tx('业务任期') }}</dt><dd>{{ selected.businessEpoch || '—' }}</dd>
          <dt>{{ tx('本任期授主时间') }}</dt><dd>{{ selected.grantedAtUtc ? formatDateTime(selected.grantedAtUtc) : '—' }}</dd>
          <dt>{{ tx('业务就绪时间') }}</dt><dd>{{ selected.businessReadyAtUtc ? formatDateTime(selected.businessReadyAtUtc) : '—' }}</dd>
          <dt>{{ tx('首样本时间') }}</dt><dd>{{ selected.firstSampleAtUtc ? formatDateTime(selected.firstSampleAtUtc) : '—' }}</dd>
        </template>
        <dt>{{ tx('阻塞原因') }}</dt><dd>{{ selected.reasonCodes?.map(haLabel).join(' · ') || '—' }}</dd>
      </dl>
      <div v-if="canMaintain && cluster && selected && !modeChanging && !single">
        <a-alert v-if="maintenanceMessage" show-icon type="info" :message="maintenanceMessage" />
        <p v-if="maintenanceBlocked" class="ha-note">{{ maintenanceBlocked }}</p>
        <a-form layout="vertical">
          <a-form-item :label="tx('维护原因')" required><a-textarea v-model:value="maintenanceReason" :maxlength="512" :disabled="!!maintenanceBlocked || maintenanceBusy" /></a-form-item>
          <a-button type="primary" :disabled="!!maintenanceBlocked || !maintenanceReason.trim()" :loading="maintenanceBusy" @click="setMaintenance">{{ tx(selected.maintenance?'退出维护':'进入维护') }}</a-button>
        </a-form>
      </div>
    </a-modal>
    <a-modal centered v-model:open="switchOpen" :title="tx('计划切换')" :footer="null">
      <a-form layout="vertical">
        <a-form-item :label="tx('目标节点')"><a-input :value="source+' → '+target" readonly /></a-form-item>
        <a-form-item :label="tx('业务任期')"><a-input :value="expectedEpoch" readonly /></a-form-item>
        <a-form-item :label="tx('切换原因')" required><a-textarea v-model:value="reason" :maxlength="250" :disabled="switchBusy" /></a-form-item>
      </a-form>
      <a-alert v-if="switchMessage" :message="switchMessage" :type="checked?'info':'warning'" show-icon />
      <div class="ha-actions ha-dialog-actions"><a-button @click="switchOpen=false">{{ tx('取消') }}</a-button>
        <a-button v-if="!checked" type="primary" :loading="switchBusy" :disabled="!reason.trim()" @click="inspect">{{ tx('切换预检') }}</a-button>
        <a-button v-else type="primary" :loading="switchBusy" @click="commitSwitch">{{ tx('确认切换') }}</a-button>
      </div>
    </a-modal>
  </section>
</template>

<script setup lang="ts">
import { computed, onActivated, onBeforeUnmount, onDeactivated, onMounted, reactive, ref, watch } from 'vue'
import { tx } from '../../i18n'
import { haGet, haSend, haLabel, newHaId, type HaStatus, type HaCheck, type HaOperation } from '../../haApi'
import { HttpRequestError, NetworkRequestError, SessionExpiredError } from '../../api'
import { formatDateTime } from '../../utils/format'
import { usePermissions, PERMISSIONS } from '../../utils/permissions'
import GatewayTable from '../controls/GatewayTable.vue'
import HaRecords from './HaRecords.vue'
import HaConfigPanel from './HaConfigPanel.vue'
import HaSetup from './HaSetup.vue'
import HaOperationStatus from './HaOperationStatus.vue'
import HaDisasterTakeoverPanel from './HaDisasterTakeoverPanel.vue'
const props=defineProps<{scope:string}>()
const emit=defineEmits<{reauthenticate:[message:string]}>()
const { hasPermission }=usePermissions()
const canSwitch=computed(()=>hasPermission(PERMISSIONS.haSwitchover)), canManage=computed(()=>hasPermission(PERMISSIONS.haConfigManage))
const canMaintain = computed(() => hasPermission(PERMISSIONS.haMaintenance))
const status=ref<HaStatus|null>(null), tab=ref('overview'),visited=reactive(new Set(['overview']))
const activeView=ref(true), setupMode=ref<'embedded'|'legacy'|'review'|null>(null)
const configPanel=ref<InstanceType<typeof HaConfigPanel>|null>(null)
async function setupDraftSaved(){
  await configPanel.value?.reload()
  setupMode.value='embedded'
  void reload()
}
async function setupRequest<T>(route:string,body:unknown,signal:AbortSignal):Promise<T>{
  if(body!==undefined)return haSend<T>('setup/'+route,body,'POST',signal)
  try{return await haGet<T>('setup/'+route,signal)}
  catch(e){
    if(e instanceof HttpRequestError && e.status===404 && route==='status')setupReady(false)
    throw e
  }
}
function setupReady(embedded:boolean){setupMode.value=embedded?'embedded':'legacy'}
watch(tab,t=>visited.add(t))
const cluster=computed(()=>status.value?.mode==='Cluster')
const disaster=computed(()=>status.value?.disaster?.active===true)
const single=computed(()=>!disaster.value && status.value?.clusterSwitch?.phase==='Disabled' ? status.value.clusterSwitch.standaloneNode : null)
const modeChanging=computed(()=>['Disabling','Enabling'].includes(status.value?.clusterSwitch?.phase || ''))
function businessRole(node:string){
  if(node==='C')return tx('仅仲裁')
  if(disaster.value)return tx(node===status.value?.connectedNode?'人工灾难接管':'未知')
  if(single.value)return tx(node===single.value?'单机业务节点':'业务已暂停')
  if(unavailable.value || !status.value?.owner)return tx('未知')
  return tx(node===status.value.owner?'主机':'备机')
}
const unavailable=computed(()=>!!error.value || status.value?.authority==='LocalLastKnown' || (status.value?.reachableMembers ?? 0)<2)
const businessLink=computed(()=>{
  const s=status.value
  if(!s || !cluster.value || unavailable.value || !s.owner || s.localRole==='Active' || !s.businessUrl)return undefined
  try {
    const url=new URL(s.businessUrl)
    if(['http:','https:'].includes(url.protocol) && !url.username && !url.password && !url.search && !url.hash)return url.href
  } catch { /* Wait for a validated endpoint. */ }
  return undefined
})
const loading=ref(false),error=ref(''),selectedNode=ref('')
const selected=computed(()=>status.value?.members.find(member=>member.nodeId===selectedNode.value))
const memberColumns=computed(()=>[['node','节点',80],['business','业务角色',120],['role','仲裁角色',135],['reachable','连通性',105],['ready','就绪状态',105],['version','配置版本',100],['observed','更新时间',165],['actions','操作',70]].map(([key,title,width])=>({key,title:tx(title),width,fixed:key==='actions'?'right':undefined})))
const maintenanceReason=ref(''), maintenanceBusy=ref(false), maintenanceMessage=ref(''), maintenanceEpoch=ref('')
watch(selectedNode,()=>{maintenanceReason.value=''; maintenanceMessage.value=''; maintenanceEpoch.value=status.value?.epoch || ''})
const maintenanceBlocked=computed(()=>{
  if(unavailable.value || !status.value?.epoch) return tx('无法取得多数派')
  if(status.value.epoch!==maintenanceEpoch.value) return tx('业务任期已变化，请重新打开节点详情。')
  if(selected.value?.maintenance) return ''
  if(selectedNode.value===status.value.owner) return tx('请先将主机业务切换到备机。')
  if(status.value.members.some(m=>m.maintenance) || (status.value.reachableMembers ?? 0)<3) return tx('维护后将无法保证多数派。')
  return ''
})
async function setMaintenance(){
  if(!canMaintain.value || maintenanceBlocked.value || maintenanceBusy.value || !maintenanceReason.value.trim() || !selected.value) return
  maintenanceBusy.value=true
  const nodeId=selected.value.nodeId
  try {
    await haSend('maintenance',{nodeId,enabled:!selected.value.maintenance,expectedEpoch:maintenanceEpoch.value,operationId:newHaId(),reason:maintenanceReason.value.trim()})
    if(selectedNode.value===nodeId){ maintenanceMessage.value=tx('状态已更新'); maintenanceReason.value='' }
    await reload()
  } catch(e){ if(selectedNode.value===nodeId) maintenanceMessage.value=e instanceof Error?e.message:String(e) }
  finally {maintenanceBusy.value=false}
}
let running=false,timer:number|undefined,req:AbortController|undefined
async function reload(){
  if(!running || req || document.hidden)return
  const c=new AbortController();req=c;loading.value=true
  try{
    const next=await haGet<HaStatus>('status',c.signal)
    if(c.signal.aborted)return
    status.value=next;error.value=''
  }catch(e){
    if(!c.signal.aborted){
      if(e instanceof SessionExpiredError){onOperationSessionExpired();return}
      error.value=e instanceof Error?e.message:String(e)
      const previous=status.value
      status.value=previous?.mode==='Cluster'?{
        ...previous, authority:'LocalLastKnown', owner:undefined, businessUrl:undefined, canStartSwitchover:false,clusterSwitch:undefined,
        lastKnownEpoch:previous.epoch ?? previous.lastKnownEpoch, epoch:undefined, reachableMembers:null,
        members:previous.members.map(m=>({...m,reachable:null,arbiterRole:undefined}))
      }:null
    }
  }
  finally{if(req===c){req=undefined;loading.value=false}}
}
function visibility(){if(!document.hidden)void reload()}
function start(){if(running)return;running=true;activeView.value=true;void reload();timer=window.setInterval(reload,4000);document.addEventListener('visibilitychange',visibility)}
function stop(){running=false;activeView.value=false;window.clearInterval(timer);req?.abort();req=undefined;document.removeEventListener('visibilitychange',visibility)}
onMounted(start);onActivated(start);onDeactivated(stop);onBeforeUnmount(stop)
const storeKey='ipc.ha.switchover.'+props.scope
const authSessionKey='ipc.gateway.auth-session'
const operationSessionKey=storeKey+'.auth-session'
const operationId=ref(sessionStorage.getItem(storeKey)||'')
const lastOperation=ref<HaOperation>()
const switchOpen=ref(false),target=ref(''),source=ref(''),expectedEpoch=ref(''),reason=ref(''),switchMessage=ref(''),checked=ref(false),switchBusy=ref(false)
let pendingId='',precheckId:string|undefined
function openSwitch(){if(!status.value?.canStartSwitchover || unavailable.value)return
  source.value=status.value.owner || '';target.value=source.value==='A'?'B':'A';expectedEpoch.value=status.value.epoch||''
  reason.value='';checked.value=false;switchMessage.value='';pendingId=newHaId();switchOpen.value=true
}
watch(reason,()=>{checked.value=false})
watch(()=>[status.value?.epoch,status.value?.canStartSwitchover],()=>{
  if(switchOpen.value && (!status.value?.canStartSwitchover || expectedEpoch.value!==status.value?.epoch)){checked.value=false;switchMessage.value=tx('业务任期已变化，请重新打开切换窗口。')}
})
function payload(){return {operationId:pendingId,target:target.value,expectedEpoch:expectedEpoch.value,reason:reason.value.trim(),precheckId}}
async function inspect(){if(switchBusy.value || !status.value?.canStartSwitchover || status.value.epoch!==expectedEpoch.value)return
  switchBusy.value=true
  try{const result=await haSend<HaCheck>('switchovers/precheck',payload());checked.value=result.passed;precheckId=result.precheckId
    switchMessage.value=[tx(result.passed?'切换期间暂停新的 PLC 写入。':'预检未通过'),...(result.checks||[]).map(x=>haLabel(x.message)),...(result.reasonCodes||[]).map(haLabel)].join(' · ')
  }catch(e){checked.value=false;switchMessage.value=e instanceof Error?e.message:String(e)}finally{switchBusy.value=false}
}
async function commitSwitch(){if(!checked.value || switchBusy.value || !status.value?.canStartSwitchover || status.value.epoch!==expectedEpoch.value)return
  switchBusy.value=true
  const submittingSession=sessionStorage.getItem(authSessionKey)||''
  let sessionExpired=false
  try{const op=await haSend<HaOperation>('switchovers',payload());operationId.value=op.operationId||pendingId;switchOpen.value=false}
  catch(e){if(e instanceof NetworkRequestError || e instanceof SessionExpiredError){operationId.value=pendingId;switchOpen.value=false}
    sessionExpired=e instanceof SessionExpiredError
    checked.value=false;switchMessage.value=e instanceof Error?e.message:String(e)}
  finally{
    if(operationId.value){sessionStorage.setItem(storeKey,operationId.value);sessionStorage.setItem(operationSessionKey,submittingSession)}
    switchBusy.value=false
    if(sessionExpired)onOperationSessionExpired()
  }
}
function onOperationSessionExpired(){stop();emit('reauthenticate','节点连接已变化，请重新登录后核对切换结果。')}
function onFinished(op:HaOperation){
  const submittedSession=sessionStorage.getItem(operationSessionKey)
  const requiresLogin=['Completed','Succeeded'].includes(op.status) && submittedSession!==null && submittedSession===(sessionStorage.getItem(authSessionKey)||'')
  operationId.value='';sessionStorage.removeItem(storeKey);sessionStorage.removeItem(operationSessionKey);lastOperation.value=op
  if(requiresLogin){stop();emit('reauthenticate','计划切换已完成，请重新登录。');return}
  void reload()
}
</script>
<style>
.ha-view {display:flex;flex-direction:column;gap:10px;flex:1;min-height:0;min-width:0}
.ha-toolbar,.ha-actions {display:flex;align-items:center;gap:8px;flex-wrap:wrap}
.ha-toolbar label {display:flex;align-items:center;gap:5px}
.ha-toolbar label .ant-input,.ha-toolbar label .ant-select {width:auto;min-width:85px}
.ha-heading{justify-content:space-between}
.ha-metrics {display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:10px}
.ha-metrics>div,.ha-panel{background:white;border:1px solid #d5e2ed;border-radius:6px;padding:12px}
.ha-metrics small,.ha-metrics strong{display:block;overflow-wrap:anywhere}.ha-metrics small,.ha-note{color:#53687a}
.ha-metrics strong{font-size:1.35em}.ha-panel h3{margin:0 0 10px}
.ha-tabs,.ha-tabs .ant-tabs-content-holder,.ha-tabs .ant-tabs-content,.ha-tabs .ant-tabs-tabpane-active{flex:1;min-height:0;min-width:0}
.ha-tabs .ant-tabs-content,.ha-tabs .ant-tabs-tabpane-active{height:100%}
.ha-tabs .ant-tabs-tabpane-active {
  display: flex;
  flex-direction: column;
  overflow: auto;
}
.ha-overview,
.ha-config {
  display: flex;
  flex: 1 0 auto;
  flex-direction: column;
  min-width: 0;
}
.ha-overview > .gateway-table-shell {
  flex: 1 0 150px;
}
.ha-overview .ha-replica-title {
  margin-top: 12px;
}
.ha-config > .ha-toolbar {
  margin-top: auto;
}
.ha-tabs>.ant-tabs-nav{margin-bottom:8px}
.ha-records{display:flex;flex-direction:column;gap:8px;height:100%;min-height:260px}
.ha-table{flex:1;min-height:0}
.ha-form-grid,.ha-node-row{display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:10px;align-items:start}
.ha-node-row{border-top:1px solid #dde6ee;padding:8px 0;grid-template-columns:32px repeat(4,minmax(105px,1fr))}
.ha-node-row>b{align-self:center}.ha-panel .ant-form-item{margin-bottom:12px}
.ha-config .ant-input-number{width:100%}.ha-config .ant-alert,.ha-note,.ha-dialog-actions{margin-top:10px}
.ha-config .ha-steps{margin-bottom:16px}.ha-config section{margin-top:14px}
.ha-fields{display:grid;grid-template-columns:minmax(95px,auto) 1fr;gap:8px}.ha-fields dd{margin:0;overflow-wrap:anywhere}.ha-fields dt{color:#56697a}
.ha-dialog-actions{justify-content:flex-end}
@media(max-width:900px){.ha-node-row{grid-template-columns:repeat(2,minmax(0,1fr))}.ha-node-row>b{grid-column:1/-1}}
@media(max-width:600px){.ha-metrics{gap:5px}.ha-metrics>div,.ha-panel{padding:8px}.ha-node-row,.ha-form-grid{grid-template-columns:1fr}.ha-toolbar label{flex:1 1 100%}.ha-toolbar label .ant-input{flex:1}.ha-fields{grid-template-columns:1fr}}
</style>

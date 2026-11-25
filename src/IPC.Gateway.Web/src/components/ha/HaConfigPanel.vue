<template>
  <section class="ha-panel ha-config">
    <HaClusterSwitch :state="switchState" :node="status?.connectedNode" :can-manage="canManage" :embedded="embedded"
      :scope="scope" :locked="!loaded || busy || loading || transportPlanLocked" :eligible-for-enrollment="canEnroll"
      @enroll="confirmOpen=true" @changed="switchChanged" @refresh="emit('changed')" />
    <div v-if="canManage" class="ha-setup-link">
      <a-button v-if="embedded" :disabled="transportPlanLocked || busy || loading" @click="emit('openSetup')">{{ tx('本机组网配置') }}</a-button>
      <template v-else>
        <a href="http://127.0.0.1:7380/" target="_blank" rel="noopener noreferrer">{{ tx('本机组网配置') }}</a>
        <span>{{ tx('请在安装节点的机器上打开。在 A 创建集群，使用 B、C 本机生成的一次性配对码登记成员。') }}</span>
      </template>
    </div>
    <a-alert v-if="error" type="error" show-icon :message="error">
      <template #action><a-button type="link" :disabled="busy || loading" @click="reloadOpen=true">{{ tx('重新读取配置') }}</a-button></template>
    </a-alert>
    <a-steps v-if="!online" size="small" :current="!saved || dirty ? 0 : check?.passed ? 2 : 1" :items="wizardSteps" class="ha-steps" />
    <a-spin :spinning="loading">
      <a-form layout="vertical" :disabled="editingLocked">
        <div class="ha-form-grid">
          <a-form-item :label="tx('集群名称')" required><a-input v-model:value="form.name" :maxlength="120" /></a-form-item>
          <a-form-item :label="tx('稳定业务地址')" required><a-input v-model:value="form.businessUrl" placeholder="https://gateway.example/" /></a-form-item>
          <a-form-item :label="tx('集群 ID')"><a-input :value="form.clusterId" readonly /></a-form-item>
        </div>
        <h3>{{ tx('节点配置') }}</h3>
        <HaMemberFields v-for="(member,index) in form.members" :key="member.nodeId" :member="member" :locked="editingLocked" :transport-locked="online && switchState?.phase!=='Disabled'" @change="form.members[index]=$event" />
        <p v-if="online && switchState?.phase!=='Disabled'" class="ha-note">{{ l('关闭双机热备后，可以修改仲裁通信地址和证书。网关管理地址及运行策略可保存草稿并在线应用。','Disable failover to edit arbiter endpoints and certificates. Gateway management URLs and operating policy can be saved and applied online.') }}</p>
        <div class="ha-form-grid">
          <a-form-item :label="tx('首选业务节点')" :extra="tx('更改首选节点不会触发切换。')"><a-select v-model:value="form.preferredNode" :options="nodeChoices" /></a-form-item>
          <a-form-item :label="tx('续约间隔（秒）')"><a-input-number v-model:value="form.renewSeconds" :min="1" :max="30" :precision="0" /></a-form-item>
          <a-form-item :label="tx('租约时长（秒）')"><a-input-number v-model:value="form.leaseSeconds" :min="3*form.renewSeconds" :max="120" :precision="0" /></a-form-item>
        </div>
      </a-form>
    </a-spin>
    <div class="ha-toolbar">
      <span>{{ tx('生效版本') }} {{ revision }} · {{ tx('草稿版本') }} {{ draftVersion }}</span>
      <template v-if="canManage">
        <a-button :disabled="editingLocked" @click="save">{{ tx('保存草稿') }}</a-button>
        <a-button :disabled="editingLocked || dirty || !saved" @click="precheck">{{ tx('连接预检') }}</a-button>
        <a-button v-if="online || switchState?.phase==='Enabled'" type="primary" :disabled="editingLocked || !canEnroll" @click="confirmOpen=true">
          {{ tx(online ? '应用变更' : '完成组网') }}
        </a-button>
      </template>
    </div>
    <p v-if="readonlyReason" class="ha-note">{{ readonlyReason }}</p>
    <p v-else-if="dirty" class="ha-note">{{ tx('修改后请保存草稿并重新预检。') }}</p>
    <a-alert v-if="transportChanged" type="warning" show-icon
      :message="l('仲裁地址或证书变更需通过组网迁移后重启生效。','Arbiter endpoint or certificate changes require a network migration and restart.')"
      :description="l('保存草稿后，在下方准备并确认组网变更。普通配置应用不会修改成员监听地址或已安装证书。网关管理地址及运行策略通过预检后可在线应用。','Save the draft, then prepare and confirm the network changes below. Applying operating policy does not change arbiter listeners or installed certificates. Gateway management URLs and operating policy can be applied online after precheck.')" />
    <section v-if="diff.length">
      <h3>{{ tx('待应用的变更') }}</h3>
      <dl class="ha-fields"><template v-for="d in diff" :key="d.key"><dt>{{ tx(d.label) }}</dt><dd>{{ d.from || '—' }} → {{ d.to || '—' }}</dd></template></dl>
    </section>
    <section v-if="online">
      <div class="ha-toolbar ha-heading"><h3>{{ tx('节点应用进度') }}</h3><a-button :loading="progressBusy" @click="refreshProgress">{{ tx('查询进度') }}</a-button></div>
      <dl class="ha-fields"><template v-for="member in progress" :key="member.nodeId">
        <dt>{{ member.nodeId }}</dt><dd>{{ haLabel(member.state) }} · {{ tx('配置版本') }} {{ member.configVersion ?? '—' }} · {{ tx('提交索引') }} {{ member.commitIndex ?? '—' }}</dd>
      </template></dl>
    </section>
    <a-alert v-if="check" :type="check.passed ? 'success' : 'warning'" show-icon :message="tx(check.passed ? '预检通过' : '预检未通过')">
      <template #description>
        <div v-for="(item,index) in check.checks" :key="index">
          {{ item.nodeId }} · {{ haLabel(item.message) }}
          <div v-if="!item.passed && item.details">{{ haCheckDetails(item.details) }}</div>
        </div>
      </template>
    </a-alert>
    <HaOperationStatus v-if="operationId" kind="config/operations" :id="operationId" @finished="complete" />
    <a-alert v-if="applied" type="success" show-icon :message="tx('配置已应用')" />
    <a-alert v-else-if="cancelled" type="info" show-icon :message="haLabel(cancelled)" />
    <HaTransportPlan v-if="canManage" :draft-version="draftVersion" :dirty="dirty" :saved="saved" :locked="readonly" :scope="scope"
      :switch-state="switchState" :status="status" :transport-changed="transportChanged" @lock="transportPlanLocked=$event" @changed="transportUpdated" />
    <a-modal v-model:open="confirmOpen" centered :title="tx(online ? '应用高可用配置' : '启用高可用')" :ok-text="tx('确认应用')" :cancel-text="tx('取消')" :confirm-loading="busy" @ok="apply">
      <p>{{ form.name }} · {{ tx('首选业务节点') }} {{ form.preferredNode }} · {{ tx('草稿版本') }} {{ draftVersion }}</p>
      <p>{{ tx(online ? '已授予的旧租约会按原安全期限结束。' : '先等待对端退出单机业务，再交接本机业务。交接期间会暂停新的 PLC 写入。') }}</p>
    </a-modal>
    <a-modal v-model:open="reloadOpen" centered :title="tx('重新读取配置')" :ok-text="tx('确认')" :cancel-text="tx('取消')" @ok="reloadFromServer">
      {{ tx('重新读取将使用服务器草稿替换当前表单。') }}
    </a-modal>
  </section>
</template>
<script setup lang="ts">
import { computed, onMounted, onBeforeUnmount, ref, watch } from 'vue'
import { locale, tx } from '../../i18n'
import { NetworkRequestError } from '../../api'
import { haGet, haSend, haLabel, haCheckDetails, newHaId, type ConfigState, type HaCheck, type HaConfig, type HaOperation, type HaStatus, type HaClusterSwitch as ClusterSwitchState } from '../../haApi'
import { usePermissions, PERMISSIONS } from '../../utils/permissions'
import HaOperationStatus from './HaOperationStatus.vue'
import HaClusterSwitch from './HaClusterSwitch.vue'
import HaMemberFields from './HaMemberFields.vue'
import HaTransportPlan from './HaTransportPlan.vue'
const props = defineProps<{ status: HaStatus | null; scope: string; embedded:boolean }>()
function l(zh:string,en:string){return locale.value==='zh-CN'?zh:en}
const emit = defineEmits<{ changed: []; openSetup: [] }>()
const { hasPermission } = usePermissions()
const canManage = computed(() => hasPermission(PERMISSIONS.haConfigManage))
const nodeChoices = ['A','B'].map(value => ({ label:value, value }))
const loading = ref(true), busy = ref(false), error = ref(''), online = ref(false), saved = ref(false)
const loaded = ref(false), applied = ref(false)
const transportPlanLocked=ref(false)
const cancelled=ref('')
const progress = ref<NonNullable<ConfigState['nodeProgress']>>([]), progressBusy=ref(false)
const draftVersion = ref(0), revision = ref(0), baseline = ref('')
const check = ref<HaCheck | null>(null), confirmOpen = ref(false)
const reloadOpen = ref(false), activeConfig = ref<HaConfig | null>(null)
const installedConfig = ref<HaConfig | null>(null)
const wizardSteps = computed(() => ['填写配置','连接预检','确认应用'].map(key=>({title:tx(key)})))
const storeKey = 'ipc.ha.config-operation.' + props.scope
const operationId = ref(sessionStorage.getItem(storeKey) || '')
const switchState = ref<ClusterSwitchState>()
const controller = new AbortController()
const form = ref<HaConfig>({
  name:'', clusterId:newHaId(), businessUrl:'', preferredNode:'A', renewSeconds:1, leaseSeconds:5,
  members:['A','B','C'].map(nodeId => ({ nodeId, clientUrl:'', peerUrl:'', gatewayUrl:'', certificateRef:'' }))
})
const dirty = computed(() => baseline.value !== JSON.stringify(form.value))
const diff = computed(() => {
  const active=activeConfig.value
  if(!active)return []
  const fields={name:'集群名称',businessUrl:'稳定业务地址',preferredNode:'首选业务节点',leaseSeconds:'租约时长（秒）',renewSeconds:'续约间隔（秒）'} as const
  const changes: {key:string;label:string;from:string|number;to:string|number}[] = (Object.keys(fields) as (keyof typeof fields)[]).filter(key=>active[key]!==form.value[key])
    .map(key=>({key,label:fields[key],from:active[key],to:form.value[key]}))
  const memberFields={clientUrl:'仲裁客户端地址',peerUrl:'成员通信地址',gatewayUrl:'网关管理地址',certificateRef:'证书引用'} as const
  for(const member of form.value.members){
    const previous=active.members.find(m=>m.nodeId===member.nodeId)
    if(!previous)continue
    for(const key of Object.keys(memberFields) as (keyof typeof memberFields)[]){
      if((previous[key] || '')!==(member[key] || ''))changes.push({key:member.nodeId+'.'+key,
        label:member.nodeId+' · '+tx(memberFields[key]),from:previous[key] || '',to:member[key] || ''})
    }
  }
  return changes
})
const transportChanged=computed(()=>!!installedConfig.value && form.value.members.some(member=>{
  const installed=installedConfig.value!.members.find(m=>m.nodeId===member.nodeId)
  const sameAddress=(left:string,right:string)=>{try{return new URL(left).href===new URL(right).href}catch{return left===right}}
  return !installed || !sameAddress(installed.clientUrl,member.clientUrl) || !sameAddress(installed.peerUrl,member.peerUrl) ||
    installed.certificateRef.toLowerCase()!==member.certificateRef.toLowerCase()
}))
const readonlyReason = computed(() => !canManage.value ? tx('当前账号仅可查看配置。') :
  !props.status ? tx('无法核实集群状态。') : props.status.mode === 'Cluster' && (props.status.authority !== 'Quorum' || (props.status.reachableMembers ?? 0) < 2) ? tx('无法取得多数派') : '')
const readonly = computed(() => !loaded.value || loading.value || busy.value || !!readonlyReason.value || !!operationId.value)
const editingLocked=computed(()=>readonly.value || transportPlanLocked.value)
const canEnroll=computed(()=>!editingLocked.value && !dirty.value && !!check.value?.passed && !!check.value.precheckId &&
  switchState.value?.phase!=='Enabling' && switchState.value?.phase!=='Disabling' && switchState.value?.phase!=='Disabled')
watch(()=>props.status, s=>{switchState.value=s?.clusterSwitch}, {immediate:true})
function switchChanged(state:ClusterSwitchState){
  switchState.value=state
  emit('changed')
}
watch(form, () => { check.value=null; confirmOpen.value=false; applied.value=false }, {deep:true,flush:'sync'})
async function load() {
  loading.value=true; loaded.value=false
  try {
    const state=await haGet<ConfigState>('config',controller.signal)
    if (controller.signal.aborted) return
    if (state.draft || state.active || state.template) form.value=JSON.parse(JSON.stringify(state.draft || state.active || state.template))
    activeConfig.value=state.active ?? null
    installedConfig.value=state.template ?? state.active ?? null
    switchState.value=state.clusterSwitch ?? props.status?.clusterSwitch
    progress.value=state.nodeProgress ?? []
    online.value=!!state.active; saved.value=!!state.draft; revision.value=state.revision ?? 0; draftVersion.value=state.draftVersion ?? 0
    baseline.value=JSON.stringify(form.value)
    if(state.lastChange?.status==='Pending'){
      operationId.value=state.lastChange.operationId
      sessionStorage.setItem(storeKey,operationId.value)
    }
    loaded.value=true
  } catch(e) { if(!controller.signal.aborted) error.value=String(e instanceof Error ? e.message : e) }
  finally { loading.value=false }
}
function reloadFromServer(){reloadOpen.value=false;error.value='';void load()}
async function refreshProgress(){
  if(progressBusy.value)return
  progressBusy.value=true
  try{progress.value=(await haGet<ConfigState>('config',controller.signal)).nodeProgress ?? []}
  catch(e){if(!controller.signal.aborted)error.value=e instanceof Error?e.message:String(e)}
  finally{progressBusy.value=false}
}
async function run(task:()=>Promise<void>) {
  if (editingLocked.value) return
  busy.value=true; error.value=''
  try {await task()} catch(e){if(!controller.signal.aborted)error.value=e instanceof Error ? e.message : String(e)}
  finally {busy.value=false}
}
async function transportUpdated(){await load();emit('changed')}
function save(){void run(async()=>{
  const state=await haSend<ConfigState>('config/draft',{config:form.value,expectedDraftVersion:draftVersion.value},'PUT',controller.signal)
  draftVersion.value=state.draftVersion; saved.value=true; baseline.value=JSON.stringify(form.value); check.value=null
})}
function precheck(){void run(async()=>{
  check.value=null
  check.value=await haSend<HaCheck>('config/precheck',{draftVersion:draftVersion.value},'POST',controller.signal)
})}
function apply(){if(!canEnroll.value) return; void run(async()=>{
  const id=newHaId(); confirmOpen.value=false
  try {
    const op=await haSend<HaOperation>('config/apply',{operationId:id,expectedRevision:revision.value,draftVersion:draftVersion.value,precheckId:check.value?.precheckId},'POST',controller.signal)
    operationId.value=op.operationId || id
  } catch(e) {
    if (e instanceof NetworkRequestError) operationId.value=id
    throw e
  } finally {if(operationId.value)sessionStorage.setItem(storeKey,operationId.value)}
})}
function complete(op:HaOperation) {
  sessionStorage.removeItem(storeKey); operationId.value=''; check.value=null
  cancelled.value=['Cancelled','Canceled'].includes(op.status) ? op.message || op.status : ''
  if (['Failed','Rejected','NotFound'].includes(op.status)) error.value=haLabel(op.message || op.status)
  else error.value=''
  void load().then(()=>{applied.value=op.status==='Applied'}); emit('changed')
}
onMounted(load)
defineExpose({reload:load})
onBeforeUnmount(()=>controller.abort())
</script>
<style scoped>
.ha-setup-link {display:flex;align-items:baseline;gap:4px 12px;flex-wrap:wrap;margin-bottom:12px}
.ha-setup-link span {color:#53687a}
</style>

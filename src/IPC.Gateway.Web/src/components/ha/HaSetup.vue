<template>
  <a-config-provider :locale="locale==='zh-CN'?zhCN:enUS" :theme="{token:{colorPrimary:'#36678a',borderRadius:6}}">
    <header v-if="!gatewayRequest" class="setup-bar">
      <div><img :src="logo" width="32" height="32" alt="" /><strong>IPC Gateway</strong></div>
      <a-button @click="setLocale(locale==='zh-CN'?'en-US':'zh-CN')">{{ locale==='zh-CN'?'English':'中文' }}</a-button>
    </header>
    <section class="setup-main" :class="{'setup-embedded':gatewayRequest}">
      <h1 v-if="!gatewayRequest">{{ tx('本机组网配置') }}</h1>
      <a-button v-if="review" class="setup-back" @click="emit('ready',true)">{{ l('返回高可用配置','Back to HA configuration') }}</a-button>
      <a-alert v-if="error" type="error" show-icon :message="tx(error)" role="alert">
        <template v-if="gatewayRequest" #action><a-button type="link" :disabled="busy" @click="loadStatus">{{ tx('刷新') }}</a-button></template>
      </a-alert>
      <a-spin v-if="gatewayRequest && !status" :spinning="busy" />
      <a-card v-else-if="!status" :title="l('打开本机向导','Open local setup')" class="setup-token">
        <p>{{ l('请使用本机安装程序提供的完整链接打开向导，或输入链接中的管理令牌。','Use the full URL provided by the local installer, or enter the management token from that URL.') }}</p>
        <a-form layout="vertical" @submit.prevent="loadStatus">
          <a-form-item :label="l('本机管理令牌','Local management token')">
            <a-input-password v-model:value="token" autocomplete="off" :disabled="busy" />
          </a-form-item>
          <a-button type="primary" html-type="submit" :loading="busy" :disabled="!token">{{ l('连接本机','Connect') }}</a-button>
        </a-form>
      </a-card>
      <template v-else>
        <a-alert v-if="status.restartRequired" type="warning" show-icon
          :message="l('组网配置已保存，请重启本机网关服务。','Cluster configuration saved. Restart the gateway service on this host.')"
          :description="l('A、B 各重启一次网关服务后，在 A 的高可用配置中保存草稿、执行连接预检，再启用集群。','Restart the gateway service once on both A and B. On A, save the draft under HA configuration, check connectivity, then enable the cluster.')" />
        <a-alert v-else-if="status.configured && !gatewayRequest" type="info" show-icon :message="l('本机已完成组网，请登录本机网关编辑高可用草稿。','This host is enrolled. Sign in to its gateway to edit the HA draft.')">
          <template #description><a v-if="gatewayManagementUrl" :href="gatewayManagementUrl" target="_blank" rel="noopener noreferrer">{{ l('打开本机网关','Open local gateway') }}</a></template>
        </a-alert>
        <a-alert v-if="gatewayRequest && status.role==='B' && !status.configured" type="info" show-icon :message="l('B 等待加入集群。生成本机配对码后，请在 A 的高可用配置中登记 B 和 C。','B is waiting to join the cluster. Generate its pairing code, then register B and C in HA configuration on A.')" />
        <div class="setup-grid">
          <a-card :title="l('本机节点身份','Local node identities')">
            <div v-if="status?.roles.length" class="setup-identities">
              <div v-for="r in status?.roles" :key="r.nodeId" class="setup-identity">
                <strong>{{ r.nodeId }}</strong><div><b>{{ roleName(r.nodeId) }}</b><div>{{ r.clientUrl }}</div></div>
              </div>
            </div>
            <a-form v-if="editableRoles.length && (!status?.configured || gatewayRequest)" layout="vertical" @submit.prevent="saveIdentity">
              <a-form-item :label="gatewayRequest ? tx('节点') : l('添加本机角色','Add a local role')">
                <a-input v-if="gatewayRequest" :value="identity.nodeId+' · '+roleName(identity.nodeId)" readonly />
                <a-select v-else :value="identity.nodeId" :options="roleOptions" :disabled="busy" @change="chooseRole" />
              </a-form-item>
              <a-form-item :label="tx('仲裁客户端地址')"><a-input v-model:value="identity.clientUrl" :disabled="busy || transportLocked" :placeholder="clientExample" /></a-form-item>
              <a-form-item :label="tx('成员通信地址')"><a-input v-model:value="identity.peerUrl" :disabled="busy || transportLocked" :placeholder="peerExample" /></a-form-item>
              <a-form-item v-if="identity.nodeId!=='C'" :label="tx('网关管理地址')"><a-input v-model:value="identity.gatewayUrl" :disabled="busy" placeholder="http://192.168.1.10:5184" /></a-form-item>
              <p v-if="status.configured" class="setup-help">{{ l('修改将保存到高可用草稿。网关管理地址返回高可用配置预检后应用；仲裁地址和证书请在关闭热备后，使用「组网地址与证书变更」准备并确认，重启三个成员后生效。集群 ID 和节点身份保持不变。','Changes are saved to the HA draft. Return to HA configuration to precheck and apply gateway management URLs. For arbiter endpoints and certificates, disable failover, prepare and confirm Network and certificate changes, then restart all three members. The cluster ID and member identities are retained.') }}</p>
              <p v-if="transportLocked" class="setup-help">{{ l('请先关闭双机热备，再编辑仲裁通信地址和证书。','Disable failover before editing arbiter endpoints and certificates.') }}</p>
              <p v-else class="setup-help">{{ l('请填写其他节点可以访问的地址。保存后在本机生成节点身份和证书。','Enter addresses reachable by the other nodes. Saving creates the identity and certificates locally.') }}</p>
              <a-button html-type="submit" :loading="busy" :disabled="!identityValid">{{ status.configured ? l('保存节点草稿','Save node draft') : l('保存节点身份','Save node identity') }}</a-button>
              <a-alert v-if="identityDraftSaved" type="success" show-icon :message="l('节点草稿已保存，尚未生效。请返回高可用配置执行连接预检并应用。','Node draft saved; it is not active yet. Return to HA configuration to run precheck and apply it.')" />
            </a-form>
            <template v-if="pairable.length">
              <a-divider />
              <h2>{{ l('一次性配对码','One-time pairing code') }}</h2>
              <p>{{ l('将远端配对地址和配对码输入 A 的创建集群界面。','Enter this pairing address and code in Create cluster on node A.') }}</p>
              <a-form layout="vertical" @submit.prevent="generateCode">
                <a-form-item v-if="pairable.length>1" :label="tx('节点')"><a-select v-model:value="codeNode" :options="pairable.map(r=>({label:r.nodeId,value:r.nodeId}))" :disabled="busy" /></a-form-item>
                <div class="setup-endpoint"><span>{{ l('配对地址','Pairing URL') }}</span><code>{{ pairingUrl }}</code></div>
                <a-button html-type="submit" :loading="busy">{{ l('生成配对码','Generate pairing code') }}</a-button>
              </a-form>
              <div v-if="issuedCode && issuedCode.nodeId===codeNode" class="setup-code" role="status">
                <code>{{ issuedCode.code }}</code><small>{{ l('有效期至','Valid until') }} {{ displayTime(issuedCode.expiresAt) }}</small>
              </div>
            </template>
          </a-card>
          <a-card v-if="!gatewayRequest || (status?.role==='A' && !status.configured)" :title="l('创建集群','Create cluster')">
            <p v-if="!isA">{{ l('在 A 上登记本机身份后，可在此登记 B、C 并生成组网配置。','Register the local identity on A to enroll B and C and generate the cluster configuration.') }}</p>
            <a-form v-else-if="!status?.configured && !committed" layout="vertical" @submit.prevent="requestCommit">
              <div class="setup-fields">
                <a-form-item :label="tx('集群名称')"><a-input v-model:value="cluster.name" :disabled="busy" :maxlength="120" /></a-form-item>
                <a-form-item :label="tx('集群 ID')"><a-input v-model:value="cluster.clusterId" :disabled="busy" :maxlength="120" /></a-form-item>
              </div>
              <a-form-item :label="tx('稳定业务地址')"><a-input v-model:value="cluster.businessUrl" :disabled="busy" placeholder="https://gateway.example/" /></a-form-item>
              <a-form-item :label="tx('首选业务节点')"><a-radio-group v-model:value="cluster.preferredNode" :disabled="busy"><a-radio-button value="A">A</a-radio-button><a-radio-button value="B">B</a-radio-button></a-radio-group></a-form-item>
              <div v-for="peer in requiredPeers" :key="peer.nodeId" class="setup-peer">
                <h2>{{ tx('节点') }} {{ peer.nodeId }} · {{ roleName(peer.nodeId) }}</h2>
                <HaSetupPeerFields :address="peer.address" :code="peer.code" :disabled="busy"
                  @update:address="peer.address=$event" @update:code="peer.code=$event" />
              </div>
              <p v-if="status?.roles.some(r=>r.nodeId==='C')" class="setup-help">{{ l('C 与 A 在同一台机器，使用已登记的本机身份。','C is on the same machine as A. Its local identity is included automatically.') }}</p>
              <a-button type="primary" html-type="submit" :loading="busy" :disabled="!clusterValid">{{ l('生成组网配置','Generate cluster configuration') }}</a-button>
            </a-form>
            <div v-if="committed || status?.configured" class="setup-result">
              <a-alert type="success" show-icon :message="l('组网配置已登记','Cluster configuration registered')" />
              <div v-if="committed?.configDirectory"><b>{{ l('配置目录','Configuration directory') }}</b><p>{{ committed.configDirectory }}</p></div>
              <p>{{ l('按部署说明加载各节点配置，然后在网关「系统 → 高可用」检查连接并启用双机热备。','Load the configuration on each node, then check connectivity and enable failover under System → High availability.') }}</p>
            </div>
          </a-card>
        </div>
      </template>
    </section>
    <a-modal v-model:open="confirmOpen" centered :title="l('生成组网配置','Generate cluster configuration')" :ok-text="tx('确认')" :cancel-text="tx('取消')" @ok="commitCluster">
      <p>{{ cluster.name }} · {{ tx('集群 ID') }} {{ cluster.clusterId }}</p>
      <p>{{ l('将在已登记的成员上写入此配置。已存在的现场配置将保留。','This writes the configuration on the enrolled members and preserves existing site configuration.') }}</p>
    </a-modal>
  </a-config-provider>
</template>
<script setup lang="ts">
import { computed, onMounted, onBeforeUnmount, reactive, ref, watch, watchEffect } from 'vue'
import zhCN from 'ant-design-vue/es/locale/zh_CN'
import enUS from 'ant-design-vue/es/locale/en_US'
import { locale, setLocale, tx } from '../../i18n'
import logo from '../../assets/logo.png'
import HaSetupPeerFields from './HaSetupPeerFields.vue'
import { haGet, haSend, type ConfigState } from '../../haApi'

type NodeId='A'|'B'|'C'
type Member={nodeId:NodeId;clientUrl:string;peerUrl:string;gatewayUrl:string}
type Status={roles:Member[];clusterId?:string;configured:boolean;pairPort:number;enabled?:boolean;role?:'A'|'B';restartRequired?:boolean}
type CommitResult={members:Member[];configDirectory:string}
const props=withDefaults(defineProps<{
  active?:boolean
  review?:boolean
  gatewayRequest?:<T>(route:string,body:unknown,signal:AbortSignal)=>Promise<T>
}>(),{active:true})
const emit=defineEmits<{ready:[embedded:boolean];draftSaved:[]}>()
function l(zh:string,en:string){return locale.value==='zh-CN'?zh:en}
function roleName(node:NodeId){return node==='C'?tx('仅仲裁'):l('业务网关和仲裁成员','Gateway and arbiter')}
watchEffect(()=>{if(!props.gatewayRequest)document.title=tx('本机组网配置')+' · IPC Gateway'})
const params=new URLSearchParams(props.gatewayRequest?'':location.hash.slice(1)), token=ref(params.get('token')||'')
if(params.has('token'))history.replaceState(null,'',location.pathname+location.search)
const status=ref<Status>(), error=ref(''),busy=ref(false),committed=ref<CommitResult>(),confirmOpen=ref(false)
const controller=new AbortController()
const identity=reactive<Member>({nodeId:'A',clientUrl:'',peerUrl:'',gatewayUrl:''})
const configState=ref<ConfigState>(),identityDraftSaved=ref(false)
const transportLocked=computed(()=>!!status.value?.configured && configState.value?.clusterSwitch?.phase!=='Disabled')
const allRoles:NodeId[]=['A','B','C']
const availableRoles=computed(()=>allRoles.filter(id=>(!props.gatewayRequest || id===status.value?.role) &&
  !status.value?.roles.some(r=>r.nodeId===id || (id!=='C' && r.nodeId!=='C'))))
const editableRoles=computed(()=>status.value?.configured ? (status.value.roles.map(r=>r.nodeId)) : availableRoles.value)
const roleOptions=computed(()=>editableRoles.value.map(id=>({value:id,label:id+' · '+roleName(id)})))
const gatewayManagementUrl=computed(()=>{
  const address=status.value?.roles.find(r=>r.nodeId==='A'||r.nodeId==='B')?.gatewayUrl
  return address && urlOk(address) ? address : ''
})
const isA=computed(()=>status.value?.roles.some(r=>r.nodeId==='A'))
const pairable=computed(()=>isA.value?[]:status.value?.roles.filter(r=>r.nodeId!=='A')??[])
const codeNode=ref<NodeId>('B'),issuedCode=ref<{nodeId:NodeId;code:string;expiresAt:string}>()
const cluster=ref({clusterId:'',name:'',businessUrl:'',preferredNode:'A'})
const peers=reactive<{nodeId:NodeId;address:string;code:string}[]>([{nodeId:'B',address:'',code:''},{nodeId:'C',address:'',code:''}])
const requiredPeers=computed(()=>peers.filter(p=>!status.value?.roles.some(r=>r.nodeId===p.nodeId)))
const clientExample=computed(()=>'https://192.168.1.10:'+(identity.nodeId==='C'&&isA.value?'7291':'7281'))
const peerExample=computed(()=>'https://192.168.1.10:'+(identity.nodeId==='C'&&isA.value?'7292':'7282'))
function urlOk(value:string,httpsOnly=false){try{const url=new URL(value);return (url.protocol==='https:'||(!httpsOnly&&url.protocol==='http:'))&&!url.username&&!url.password&&!url.hash&&!url.search&&url.pathname==='/'}catch{return false}}
const identityValid=computed(()=>editableRoles.value.includes(identity.nodeId) && (!status.value?.configured || !!configState.value) && urlOk(identity.clientUrl,true)&&urlOk(identity.peerUrl,true)&&
  identity.clientUrl!==identity.peerUrl&&(identity.nodeId==='C'||urlOk(identity.gatewayUrl)))
const clusterValid=computed(()=>cluster.value.name.trim() && cluster.value.clusterId.trim() && urlOk(cluster.value.businessUrl) &&
  requiredPeers.value.every(p=>urlOk(p.address,true)&&!!p.code))
const pairingUrl=computed(()=>{
  const endpoint=pairable.value.find(r=>r.nodeId===codeNode.value)?.clientUrl
  try{return 'https://'+new URL(endpoint!).hostname+':'+(status.value?.pairPort ?? 7381)}catch{return '—'}
})
function displayTime(value:string){return new Date(value).toLocaleString(locale.value)}
function chooseRole(id:NodeId){
  const draft=configState.value?.draft || configState.value?.active || configState.value?.template
  const member=status.value?.configured ? draft?.members.find(m=>m.nodeId===id) || status.value.roles.find(m=>m.nodeId===id) : undefined
  Object.assign(identity,{nodeId:id,clientUrl:member?.clientUrl || '',peerUrl:member?.peerUrl || '',gatewayUrl:member?.gatewayUrl || ''})
  identityDraftSaved.value=false
}
watch(identity,()=>{identityDraftSaved.value=false},{deep:true})
async function api<T>(route:string,body?:unknown):Promise<T>{
  if(props.gatewayRequest)return props.gatewayRequest<T>(route,body,controller.signal)
  const response=await fetch('/setup/'+route,{method:body===undefined?'GET':'POST',cache:'no-store',redirect:'error',
    headers:{'X-Setup-Token':token.value,...(body===undefined?{}:{'Content-Type':'application/json'})},
    body:body===undefined?undefined:JSON.stringify(body),signal:controller.signal})
  if(!response.ok){
    const payload=await response.json().catch(()=>({}))
    if(response.status===401 || response.status===403)status.value=undefined
    throw new Error(payload.error || 'HTTP '+response.status)
  }
  return response.json()
}
async function run(task:()=>Promise<void>){
  if(busy.value)return
  error.value='';busy.value=true
  try{await task()}catch(e){if(!controller.signal.aborted)error.value=e instanceof Error?e.message:String(e)}finally{busy.value=false}
}
async function readStatus(){
  const value=await api<Status>('status')
  if(controller.signal.aborted)return
  if(props.gatewayRequest && (!value.enabled || (value.configured && !value.restartRequired && !props.review))){
    emit('ready',value.enabled===true)
    return
  }
  status.value=value
  if(props.gatewayRequest && value.configured){
    configState.value=await haGet<ConfigState>('config',controller.signal)
    chooseRole(value.role || value.roles[0]?.nodeId || 'A')
  }
  if(!cluster.value.clusterId)cluster.value.clusterId=value.clusterId||''
  if(!editableRoles.value.includes(identity.nodeId) && editableRoles.value.length)chooseRole(editableRoles.value[0])
  if(pairable.value.length&&!pairable.value.some(r=>r.nodeId===codeNode.value))codeNode.value=pairable.value[0].nodeId
}
function loadStatus(){if(props.gatewayRequest || token.value)void run(readStatus)}
function saveIdentity(){
  if(!identityValid.value)return
  void run(async()=>{
    if(status.value?.configured){
      const state=configState.value
      const current=state?.draft || state?.active || state?.template
      if(!state || !current)throw new Error(l('请重新读取配置。','Reload the configuration.'))
      const config={...current,members:current.members.map(member=>member.nodeId===identity.nodeId ?
        {...member,clientUrl:identity.clientUrl,peerUrl:identity.peerUrl,gatewayUrl:identity.nodeId==='C'?'':identity.gatewayUrl} : member)}
      const saved=await haSend<ConfigState>('config/draft',{config,expectedDraftVersion:state.draftVersion},'PUT',controller.signal)
      configState.value={...state,draft:saved.draft,draftVersion:saved.draftVersion}
      identityDraftSaved.value=true
      emit('draftSaved')
    }else{
      await api('identity',{...identity,gatewayUrl:identity.nodeId==='C'?'':identity.gatewayUrl})
      await readStatus()
    }
  })
}
let expiryTimer:number|undefined
function generateCode(){
  if(!pairable.value.some(r=>r.nodeId===codeNode.value))return
  void run(async()=>{
    const nodeId=codeNode.value,response=await api<{code:string;expiresAt:string}>('code',{nodeId})
    issuedCode.value={...response,nodeId}
    window.clearTimeout(expiryTimer)
    expiryTimer=window.setTimeout(()=>{issuedCode.value=undefined},Math.max(0,Date.parse(response.expiresAt)-Date.now()))
  })
}
function requestCommit(){if(clusterValid.value && isA.value && !busy.value)confirmOpen.value=true}
function commitCluster(){
  confirmOpen.value=false
  if(!clusterValid.value||!isA.value)return
  void run(async()=>{
    try{committed.value=await api<CommitResult>('commit',{...cluster.value,peers:requiredPeers.value.map(p=>({...p}))})}
    catch(e){throw new Error(tx(e instanceof Error?e.message:String(e))+' '+l('如已有成员写入，请恢复连接后重试原计划。','If any members have already applied the plan, restore connectivity and retry the same plan.'))}
    for(const peer of peers)peer.code=''
    await readStatus()
  })
}
let pollTimer:number|undefined
onMounted(()=>{
  loadStatus()
  pollTimer=window.setInterval(()=>{
    if(props.active && !document.hidden && !busy.value && !error.value &&
        ((pairable.value.length && !status.value?.configured) || status.value?.restartRequired))loadStatus()
  },5000)
})
watch(()=>props.active,active=>{if(active)loadStatus()})
onBeforeUnmount(()=>{controller.abort();window.clearTimeout(expiryTimer);window.clearInterval(pollTimer)})
</script>
<style scoped>
:global(.ha-setup-page){margin:0;background:#eff5fa;color:#151a20;font:15px/1.5 "Cascadia Code","Microsoft YaHei",sans-serif}
.setup-bar{display:flex;align-items:center;justify-content:space-between;gap:16px;padding:12px clamp(16px,5vw,72px);background:#172b3b;color:white}
.setup-bar>div{display:flex;gap:12px;align-items:center}.setup-bar strong{font-size:18px}
.setup-main{max-width:1250px;margin:auto;padding:clamp(16px,3vw,38px)}
.setup-main.setup-embedded{flex:1;min-width:0;max-width:none;margin:0;padding:0}
.setup-main h1{font-size:clamp(22px,3vw,30px);margin:0 0 16px}
.setup-main :deep(.ant-alert){margin:0 0 14px}
.setup-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(360px,100%),1fr));gap:16px;align-items:start}
.setup-token{max-width:560px}
.setup-back{margin-bottom:12px}
.setup-main h2{font-size:15px;margin:0 0 8px}.setup-help{color:#475968}
.setup-main :deep(.setup-fields){display:grid;grid-template-columns:repeat(auto-fit,minmax(min(210px,100%),1fr));gap:0 12px}
.setup-main :deep(.ant-form-item){margin-bottom:14px}
.setup-identities,.setup-result{display:grid;gap:10px;margin-bottom:12px}
.setup-identity{display:flex;align-items:center;gap:12px;overflow-wrap:anywhere}
.setup-identity>strong{background:#e1edf5;color:#254d69;border-radius:5px;min-width:36px;text-align:center;padding:6px}
.setup-peer{border-top:1px solid #d9e5ee;padding-top:10px}
.setup-endpoint,.setup-code{display:grid;gap:3px;margin:10px 0;overflow-wrap:anywhere}
.setup-code code{font-size:18px;user-select:all}.setup-code small{color:#475968}
.setup-result p{overflow-wrap:anywhere}
@media(max-width:780px){.setup-grid{grid-template-columns:minmax(0,1fr)}}
</style>

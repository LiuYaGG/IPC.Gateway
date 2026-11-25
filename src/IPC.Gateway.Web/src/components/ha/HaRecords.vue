<template>
  <section class="ha-panel ha-records">
    <div class="ha-toolbar">
      <label>{{ tx('节点') }} <a-select v-model:value="node" allow-clear :options="nodeOptions" /></label>
      <label>{{ tx('操作类型') }} <a-select v-model:value="operation" allow-clear :options="operationOptions" /></label>
      <label>{{ tx('提交结果') }} <a-select v-model:value="outcome" allow-clear show-search :options="resultOptions" option-filter-prop="label" /></label>
      <label>{{ tx('开始时间') }} <a-input type="datetime-local" :step="1" v-model:value="from" /></label>
      <label>{{ tx('结束时间') }} <a-input type="datetime-local" :step="1" v-model:value="to" /></label>
      <a-button type="primary" :loading="busy" @click="query">{{ tx('查询') }}</a-button>
    </div>
    <a-alert v-if="error" type="error" show-icon :message="error" />
    <div class="auto-height-table ha-table">
      <GatewayTable :columns="columns" :data-source="items" resize-key="ha-records-switchovers" :row-key="recordKey" :paging="false" :scroll="{ x: 'max-content' }">
        <template #bodyCell="{column,record}">
          <a-button v-if="column.key==='actions'" type="link" @click="detail=record">{{ tx('详情') }}</a-button>
          <template v-else>{{ cell(record,column.key) }}</template>
        </template>
      </GatewayTable>
    </div>
    <TablePager :total="total" :current="page" :page-size="size" :disabled="busy" @change="paginate" />
    <a-modal centered :open="!!detail" :title="tx('记录详情')" :footer="null" @cancel="detail=null">
      <dl v-if="detail" class="ha-fields"><template v-for="key in Object.keys(detail)" :key="key"><dt>{{ tx(labels[key] || key) }}</dt><dd>{{ cell(detail,key) }}</dd></template></dl>
    </a-modal>
  </section>
</template>
<script setup lang="ts">
import { computed, onActivated, onBeforeUnmount, onDeactivated, onMounted, ref, watch } from 'vue'
import GatewayTable from '../controls/GatewayTable.vue'
import TablePager from '../controls/TablePager.vue'
import { tx } from '../../i18n'
import { haGet, haLabel } from '../../haApi'
import { formatDateTime } from '../../utils/format'
type RecordRow=Record<string,unknown>
const props=defineProps<{active:boolean}>()
const labels:Record<string,string>={timestampUtc:'时间',timestamp:'时间',operationId:'操作号',requestId:'请求 ID',source:'源节点',target:'目标节点',nodeId:'节点',epoch:'业务任期',previousEpoch:'上一任期',commitIndex:'提交索引',status:'状态',reason:'原因',reasonCode:'原因代码',result:'提交结果',operator:'操作者',actor:'操作者',message:'说明',operation:'操作类型',eventType:'事件类型',instanceId:'进程实例',relatedOperationId:'关联操作号',planId:'迁移计划 ID',transportPlanId:'迁移计划 ID',phase:'阶段',state:'状态',restartRequired:'需要重启'}
const eventLabels:Record<string,string>={InitialGrant:'首次授主',PlannedTransferRequest:'计划切换请求',PlannedTransfer:'计划切换授主',AutomaticFailover:'自动故障转移',ClusterActivation:'重新启用热备后授主',OwnershipRecovery:'同节点重新授主',OwnershipRelease:'交还主身份',ClusterModeChange:'集群启停',OwnershipGrant:'授主（旧版记录）'}
const columns=computed(()=> {
  const keys=['timestampUtc','operation','source','target','epoch','commitIndex','result','actor','reason']
  return [...keys.map(key=>({key,title:tx(labels[key]),width:key==='timestampUtc'?180:125})),{key:'actions',title:tx('操作'),width:75,fixed:'right'}]
})
function cell(row:RecordRow,key:string) {
  let value=row[key]
  if(key==='timestampUtc')value ??= row.createdAtUtc ?? row.timestamp ?? row.startedAtUtc
  if(key==='source')value ??=row.fromNode ?? row.sourceNode ?? (row.operation==='acquire'?undefined:row.nodeId)
  if(key==='target' && !value)value=row.toNode ?? row.targetNode ?? (row.operation==='acquire'?row.nodeId:undefined)
  if(key==='actor')value ??=row.operator
  if(key==='eventType' && !value && row.operation==='transport')value='TransportMigration'
  if(value==null || value==='')return '—'
  if (['timestamp','timestampUtc'].includes(key) && typeof value==='string')return formatDateTime(value)
  if(key==='eventType' && typeof value==='string')return eventLabels[value] ? tx(eventLabels[value]) : haLabel(value)
  if(['status','result','operation','reason','reasonCode','message','phase','state'].includes(key)&&typeof value==='string')return haLabel(value)
  if(key==='restartRequired' && typeof value==='boolean')return tx(value?'是':'否')
  return typeof value==='object'?JSON.stringify(value):String(value)
}
function recordKey(row:RecordRow){return String(row.commitIndex || row.operationId || row.requestId || JSON.stringify(row))}
const nodeOptions=['A','B','C'].map(value=>({value,label:value}))
const operation=ref<string>()
const operationOptions=computed(()=>['transfer','acquire','release','maintenance','config','mode','transport'].map(value=>({value,label:haLabel(value)})))
const resultOptions=computed(()=>['Committed','HA_NOT_ACTIVE','HA_STALE_EPOCH','HA_TARGET_NOT_READY','HA_NO_QUORUM'].map(value=>({value,label:haLabel(value)})))
function localDateTime(date:Date){
  const pad=(value:number)=>String(value).padStart(2,'0')
  return `${date.getFullYear()}-${pad(date.getMonth()+1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`
}
const today=new Date(),yesterday=new Date(today)
yesterday.setDate(yesterday.getDate()-1)
const from=ref(localDateTime(yesterday)),to=ref(localDateTime(today)),node=ref<string>(),outcome=ref<string>(),page=ref(1),size=ref(20),total=ref(0),busy=ref(false),error=ref(''),items=ref<RecordRow[]>([]),detail=ref<RecordRow|null>(null)
let filters=new URLSearchParams(), controller:AbortController|undefined, queried=false, needsLoad=true
function query(){
  const start=from.value?new Date(from.value):undefined,end=to.value?new Date(to.value):undefined
  if((start && Number.isNaN(start.getTime())) || (end && Number.isNaN(end.getTime()))){error.value=tx('请输入有效的开始和结束时间。');return}
  if(start && end && start>end){error.value=tx('结束时间必须晚于开始时间');return}
  filters=new URLSearchParams()
  if(node.value)filters.set('nodeId',node.value)
  if(operation.value)filters.set('operation',operation.value)
  if(outcome.value)filters.set('result',outcome.value)
  if(start)filters.set('from',start.toISOString())
  if(end)filters.set('to',end.toISOString())
  queried=true
  page.value=1;void load()
}
async function load(){
  needsLoad=true
  if(!props.active)return
  controller?.abort();const c=new AbortController();controller=c;busy.value=true;error.value=''
  const params=new URLSearchParams(filters);params.set('page',String(page.value));params.set('pageSize',String(size.value))
  try {
    const data=await haGet<{items:RecordRow[];total:number}>('switchovers?'+params,c.signal)
    if(c.signal.aborted)return
    items.value=data.items;total.value=data.total;needsLoad=false
  } catch(e){if(!c.signal.aborted)error.value=e instanceof Error?e.message:String(e)}
  finally{if(controller===c){controller=undefined;busy.value=false}}
}
function paginate(p:number,s:number){page.value=p;size.value=s;void load()}
function ensure(){if(props.active && needsLoad && !busy.value){if(queried)void load();else query()}}
function stop(){controller?.abort();controller=undefined;busy.value=false}
watch(()=>props.active,active=>{if(active)ensure();else stop()})
onMounted(ensure);onActivated(ensure);onDeactivated(stop);onBeforeUnmount(stop)
</script>

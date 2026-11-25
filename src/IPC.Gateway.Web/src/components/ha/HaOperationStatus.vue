<template>
  <a-alert :type="error ? 'warning' : 'info'" show-icon>
    <template #message>{{ tx('操作进度') }} · {{ haLabel(op?.status) }}</template>
    <template #description>
      <div>{{ tx('操作号') }} {{ id }}</div><div v-if="error || op?.message">{{ error || haLabel(op?.message) }}</div>
      <a-button v-if="error" @click="poll">{{ tx('查询进度') }}</a-button>
    </template>
  </a-alert>
</template>
<script setup lang="ts">
import { onActivated, onDeactivated, onMounted, onBeforeUnmount, ref, watch } from 'vue'
import { haGet, haLabel, type HaOperation } from '../../haApi'
import { tx } from '../../i18n'
import { HttpRequestError, SessionExpiredError } from '../../api'
const props=defineProps<{kind:'config/operations'|'switchovers'|'cluster-switch';id:string}>()
const emit=defineEmits<{finished:[HaOperation];reauthenticate:[]}>()
const op=ref<HaOperation>(), error=ref('')
let active=false,timer:number|undefined, request:AbortController|undefined
function stop(){ active=false;window.clearTimeout(timer);request?.abort();request=undefined }
async function poll(){
  if(!active || request || !props.id) return
  window.clearTimeout(timer);const c=new AbortController();request=c
  try {
    const result=await haGet<HaOperation>(props.kind+'/'+encodeURIComponent(props.id),c.signal)
    if(c.signal.aborted)return
    error.value='';op.value=result
    if(['Succeeded','Completed','Applied','Failed','Rejected','Cancelled','Canceled'].includes(result.status)){emit('finished',result);return}
  } catch(e){
    if(c.signal.aborted)return
    if(props.kind==='switchovers' && e instanceof SessionExpiredError){
      error.value=tx('节点连接已变化，请重新登录后核对切换结果。');stop();emit('reauthenticate');return
    }
    if(e instanceof HttpRequestError && e.status===404){emit('finished',{operationId:props.id,status:'NotFound',message:'服务端未找到此操作，请核对当前状态。'});return}
    error.value=e instanceof Error ? e.message : String(e)
  } finally {if(request===c)request=undefined}
  if(active)timer=window.setTimeout(poll,2500)
}
function start(){active=true;void poll()}
onMounted(start);onActivated(start);onDeactivated(stop);onBeforeUnmount(stop)
watch(()=>props.id,()=>{const wasActive=active;stop();if(wasActive)start()})
</script>

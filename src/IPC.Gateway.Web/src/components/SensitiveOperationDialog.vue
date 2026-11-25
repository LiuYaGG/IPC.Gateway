<template>
  <a-modal :open="!!approval" centered :width="440" :title="tx('高危操作二次验证')" :mask-closable="false"
    :z-index="securityModalLayers.password" wrap-class-name="sensitive-operation-modal" transition-name=""
    :confirm-loading="busy" :ok-text="tx('验证并继续')" :cancel-text="tx('取消')" @ok="verify" @cancel="cancel">
    <a-alert type="warning" show-icon :message="tx('此操作需要验证当前登录账号。')" class="sensitive-auth-note" />
    <a-form layout="vertical" @submit.prevent>
      <a-form-item :label="tx('当前密码')" required><a-input-password ref="passwordInput" v-model:value="password" autocomplete="current-password" :disabled="busy" @press-enter="verify" /></a-form-item>
      <LoginCaptcha v-if="approval" ref="captcha" />
    </a-form>
    <a-alert v-if="error" type="error" show-icon :message="tx(error)" />
  </a-modal>
</template>
<script setup lang="ts">
import { nextTick, onBeforeUnmount, ref, watch } from 'vue'
import LoginCaptcha from './LoginCaptcha.vue'
import { request, SessionExpiredError } from '../api'
import { tx } from '../i18n'
import { registerSensitiveApproval, type SensitiveApproval } from '../utils/sensitiveOperation'
import { securityModalLayers } from '../utils/securityModalLayers'
const emit=defineEmits<{expired:[]}>()
const approval=ref<SensitiveApproval>(),password=ref(''),error=ref(''),busy=ref(false),captcha=ref<InstanceType<typeof LoginCaptcha>>()
const passwordInput=ref<{focus:()=>void}>()
watch(approval,async current=>{
  if(!current)return
  await nextTick()
  if(approval.value===current)passwordInput.value?.focus()
},{flush:'post'})
let controller:AbortController|undefined
const unregister=registerSensitiveApproval(value=>{value.onCancelled=()=>cancel();approval.value=value;password.value='';error.value=''})
function cancel(){
  controller?.abort();controller=undefined
  const current=approval.value;approval.value=undefined;password.value='';error.value='';busy.value=false
  current?.reject(new Error(tx('已取消高危操作。')))
}
async function verify(){
  if(busy.value||!approval.value)return
  if(!password.value){error.value='请输入当前密码。';return}
  if(!captcha.value?.validate())return
  const token=captcha.value.getToken()
  if(!token)return
  const current=approval.value, c=new AbortController();controller=c;busy.value=true;error.value=''
  try{
    const result=await request<{success:boolean;ticket:string}>('/api/auth/reauth',{method:'POST',body:JSON.stringify({password:password.value,captchaToken:token,contextId:current.contextId}),signal:c.signal})
    if(c.signal.aborted||approval.value!==current)return
    approval.value=undefined;password.value=''
    current.resolve(result.ticket)
  }catch(e){
    if(!c.signal.aborted && approval.value===current){
      if(e instanceof SessionExpiredError){cancel();emit('expired');return}
      error.value=e instanceof Error?e.message:String(e);password.value='';captcha.value?.refresh()
    }
  }finally{if(controller===c){controller=undefined;busy.value=false}}
}
onBeforeUnmount(()=>{cancel();unregister()})
</script>
<style scoped>
.sensitive-auth-note{margin-bottom:16px}
</style>

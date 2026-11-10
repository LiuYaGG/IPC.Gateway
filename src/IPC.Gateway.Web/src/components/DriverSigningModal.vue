<template>
  <a-modal :open="open" :title="tx('协议驱动签名')" centered :width="660" :confirm-loading="busy" :closable="!busy" :mask-closable="false" :cancel-button-props="{ disabled: busy }" :ok-button-props="{ disabled: !privateKey || !exported || !info }" :ok-text="tx('签名并验证')" @cancel="close" @ok="submit">
    <a-alert type="info" show-icon :message="tx('私钥仅在当前浏览器使用，不会上传。请下载并妥善保存，后续签名需使用同一私钥。')" style="margin-bottom: 18px" />
    <a-alert v-if="!secure" type="warning" show-icon :message="tx('驱动签名需要 HTTPS 或 localhost、127.0.0.1 地址。')" style="margin-bottom: 18px" />
    <a-spin :spinning="loading">
      <a-form layout="vertical">
        <a-form-item :label="tx('驱动')"><a-input :value="driver?.displayName || driver?.driverId" readonly /></a-form-item>
        <a-form-item label="SHA256"><a-textarea :value="info?.assemblySha256" readonly :auto-size="{ minRows: 2, maxRows: 2 }" /></a-form-item>
        <a-form-item :label="tx('签名方')"><a-input v-model:value="signer" :maxlength="128" :disabled="busy" /></a-form-item>
        <a-form-item :label="tx('签名密钥')">
          <a-space wrap>
            <a-button :disabled="busy || !secure || !!info?.trustedPublicKeyPem" @click="generate"><KeyOutlined />{{ tx('生成新密钥') }}</a-button>
            <a-button :disabled="busy || !secure" @click="fileInput?.click()"><UploadOutlined />{{ tx('导入私钥') }}</a-button>
            <a-button :disabled="busy || !privateKey" @click="download"><DownloadOutlined />{{ tx('下载私钥') }}</a-button>
          </a-space>
          <input ref="fileInput" type="file" accept=".pem,.key" hidden @change="importKey" />
          <p style="margin: 10px 0 0">{{ tx(privateKey ? '私钥已就绪' : '尚未选择私钥') }}</p>
          <p v-if="info?.trustedPublicKeyPem" style="margin: 8px 0 0">{{ tx('网关已配置可信公钥，请导入对应私钥。') }}</p>
        </a-form-item>
        <a-alert v-if="error" type="error" show-icon :message="tx(error)" />
      </a-form>
    </a-spin>
  </a-modal>
</template>

<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import { KeyOutlined, UploadOutlined, DownloadOutlined } from '@ant-design/icons-vue'
import { tx } from '../i18n'
import { notify } from '../utils/ui'
import { prepareDriverSigning, applyDriverSignature, type GatewayDriverSigningInfo, type GatewayProtocolDriverInfo } from '../api'
import { createSigningKey, signDriverPayload } from '../utils/driverSigning'

const props = defineProps<{ open: boolean; driver: GatewayProtocolDriverInfo | null }>()
const emit = defineEmits<{ close: []; signed: [] }>()
const secure = !!globalThis.crypto?.subtle
const fileInput = ref<HTMLInputElement>()
const info = ref<GatewayDriverSigningInfo | null>(null)
const signer = ref('')
const privateKey = ref('')
const exported = ref(false)
const busy = ref(false)
const loading = ref(false)
const error = ref('')
let generation = 0
const message = (e: unknown) => e instanceof Error ? e.message : '驱动签名失败'
function clear() { generation++; privateKey.value = ''; exported.value = false; info.value = null; error.value = ''; if (fileInput.value) fileInput.value.value = '' }
function close() { if (!busy.value) { clear(); emit('close') } }
onBeforeUnmount(clear)
watch(() => props.open, async open => {
  clear()
  if (!open || !props.driver) return
  const id = generation
  signer.value = props.driver.signer || ''
  loading.value = true
  try { const result = await prepareDriverSigning(props.driver.driverId); if (id === generation) info.value = result }
  catch (e) { if (id === generation) error.value = message(e) }
  finally { if (id === generation) loading.value = false }
})
async function generate() {
  busy.value = true; error.value = ''; const id = generation
  try { const key = await createSigningKey(); if (id === generation) { privateKey.value = key; exported.value = false } }
  catch (e) { error.value = message(e) }
  finally { busy.value = false }
}
async function importKey(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]; if (!file) return
  if (file.size > 32768) { error.value = '私钥文件过大'; input.value = ''; return }
  const id = generation
  try { const key = await file.text(); if (id === generation) { privateKey.value = key; exported.value = true; error.value = '' } }
  catch (e) { error.value = message(e) }
  finally { input.value = '' }
}
function download() {
  const url = URL.createObjectURL(new Blob([privateKey.value], { type: 'application/x-pem-file' }))
  const link = document.createElement('a'); link.href = url; link.download = 'ipc-driver-private-key.pem'; link.click()
  setTimeout(() => URL.revokeObjectURL(url), 1000); exported.value = true
}
async function submit() {
  if (!props.driver || !info.value || !privateKey.value || !exported.value || busy.value) return
  busy.value = true; error.value = ''
  try {
    const signed = await signDriverPayload(privateKey.value, info.value.payloadBase64)
    await applyDriverSignature(props.driver.driverId, { ...signed, assemblySha256: info.value.assemblySha256, signer: signer.value })
    clear(); notify.success(tx('驱动签名验证通过')); emit('signed'); emit('close')
  } catch (e) { error.value = message(e) }
  finally { busy.value = false }
}
</script>

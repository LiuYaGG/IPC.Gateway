<template>
  <a-modal wrap-class-name="upgrade-upload-modal" :open="upload.active" :title="tx(upload.phase === 'verifying' ? '正在校验升级包' : '正在上传升级包')" centered :closable="false" :mask-closable="false" :keyboard="false" :footer="null" :z-index="100000" :width="460">
    <div class="upgrade-upload-overlay">
      <p class="upgrade-upload-overlay__file">{{ upload.fileName }}</p>
      <a-progress :percent="upload.percent" :show-info="upload.known" status="active" :stroke-color="'#567b9f'" role="progressbar" :aria-label="tx('升级包上传进度')" :aria-valuemin="0" :aria-valuemax="100" :aria-valuenow="upload.known ? upload.percent : undefined" />
      <p v-if="upload.phase === 'verifying'" aria-live="polite">{{ tx('文件已发送，正在校验并保存升级包，请稍候。') }}</p>
      <p class="upgrade-upload-overlay__tip">{{ tx('上传及校验期间暂不能操作网站，请勿刷新或关闭页面。') }}</p>
    </div>
  </a-modal>
</template>
<script setup lang="ts">
import { onBeforeUnmount, onMounted } from 'vue'
import { tx } from '../i18n'
import { upgradeUploadState as upload } from '../utils/upgradeUploadState'
function beforeUnload(event: BeforeUnloadEvent) {
  if (!upload.active) return
  event.preventDefault()
  event.returnValue = ''
}
onMounted(() => window.addEventListener('beforeunload', beforeUnload))
onBeforeUnmount(() => window.removeEventListener('beforeunload', beforeUnload))
</script>
<style scoped>
:global(.upgrade-upload-modal .ant-modal-body) { overflow-x: hidden; }
.upgrade-upload-overlay { display: grid; grid-template-columns: minmax(0, 1fr); gap: 16px; min-width: 0; }
.upgrade-upload-overlay > * { min-width: 0; max-width: 100%; }
.upgrade-upload-overlay p { margin: 0; white-space: normal; overflow-wrap: anywhere; }
.upgrade-upload-overlay__file { overflow-wrap: anywhere; color: #000; }
.upgrade-upload-overlay__tip { color: #7a8a9e; font-size: calc(11px + 1pt); }
</style>

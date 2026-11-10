<template>
  <a-modal :open="configurationSaveDialogVisible" :title="tx('正在保存配置')" centered :closable="false" :mask-closable="false" :keyboard="false" :footer="null" :z-index="100001" :width="420">
    <a-space align="center"><a-spin /><span>{{ tx('正在保存并应用配置，请稍候。完成前请勿关闭或刷新页面。') }}</span></a-space>
  </a-modal>
</template>
<script setup lang="ts">
import { onBeforeUnmount, onMounted } from 'vue'
import { tx } from '../i18n'
import { configurationSaving, configurationSaveDialogVisible } from '../utils/configurationSaveState'
function preventLeave(event: BeforeUnloadEvent) {
  if (configurationSaving.value) { event.preventDefault(); event.returnValue = '' }
}
onMounted(() => window.addEventListener('beforeunload', preventLeave))
onBeforeUnmount(() => window.removeEventListener('beforeunload', preventLeave))
</script>

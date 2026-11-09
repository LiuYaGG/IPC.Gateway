<template>
  <a-form :model="model" :disabled="disabled" class="history-pane-form" layout="vertical">
    <div class="history-pane-grid">
      <a-form-item :label="tx('启用数据压缩')"><a-switch v-model:checked="model.compressionEnabled" /></a-form-item>
      <a-form-item :label="tx('数值压缩容差')">
        <gateway-number-input v-model="model.compressionTolerance" :min="0" :step="0.1" />
      </a-form-item>
      <a-form-item :label="tx('重复文本值压缩')">
        <a-switch v-model:checked="model.compressDuplicateText" :disabled="disabled || !model.compressionEnabled" />
      </a-form-item>
    </div>
  </a-form>
</template>

<script setup lang="ts">
import { tx } from '../../i18n'
import type { HistoryDataProcessingConfig } from '../../api'

defineProps<{
  model: HistoryDataProcessingConfig
  disabled?: boolean
}>()
</script>

<style scoped>
.history-pane-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 12px 18px;
}

:deep(.ant-input-number) {
  width: 100%;
}
</style>

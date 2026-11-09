<template>
  <a-form :model="model" :disabled="disabled" class="history-pane-form" layout="vertical">
    <div class="history-pane-grid">
      <a-form-item :label="tx('启用降采样')"><a-switch v-model:checked="model.downsamplingEnabled" /></a-form-item>
      <a-form-item :label="tx('降采样间隔 ms')">
        <gateway-number-input
          v-model="model.downsamplingIntervalMs"
          :disabled="disabled || !model.downsamplingEnabled"
          :min="1"
          :max="86400000"
          :step="100"
        />
      </a-form-item>
      <a-form-item :label="tx('启用时间对齐')"><a-switch v-model:checked="model.alignmentEnabled" /></a-form-item>
      <a-form-item :label="tx('对齐间隔 ms')">
        <gateway-number-input
          v-model="model.alignmentIntervalMs"
          :disabled="disabled || !model.alignmentEnabled"
          :min="1"
          :max="86400000"
          :step="100"
        />
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

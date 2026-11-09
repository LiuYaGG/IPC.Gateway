<template>
  <a-form :model="model" :disabled="disabled" class="history-pane-form" layout="vertical">
    <div class="history-pane-grid">
      <a-form-item :label="tx('启用补点')"><a-switch v-model:checked="model.fillEnabled" /></a-form-item>
      <a-form-item :label="tx('补点模式')">
        <gateway-select v-model="model.fillMode" :disabled="disabled || !model.fillEnabled">
          <a-select-option
            v-for="option in fillModeOptions"
            :key="option.value"
            :label="tx(option.label)"
            :value="option.value"
          >
            {{ option.label }}
          </a-select-option>
        </gateway-select>
      </a-form-item>
      <a-form-item :label="tx('补点间隔 ms')">
        <gateway-number-input
          v-model="model.fillIntervalMs"
          :disabled="disabled || !model.fillEnabled"
          :min="0"
          :max="86400000"
          :step="100"
        />
      </a-form-item>
      <a-form-item :label="tx('最大补点间隔 s')">
        <gateway-number-input
          v-model="model.fillMaxGapSeconds"
          :disabled="disabled || !model.fillEnabled"
          :min="0"
          :max="86400"
        />
      </a-form-item>
      <a-form-item :label="tx('单次最大生成点数')">
        <gateway-number-input
          v-model="model.maxSyntheticPointsPerInput"
          :disabled="disabled || !model.fillEnabled"
          :min="1"
          :max="10000"
        />
      </a-form-item>
    </div>
  </a-form>
</template>

<script setup lang="ts">
import { tx } from '../../i18n'
import type { HistoryDataProcessingConfig } from '../../api'
import { fillModeOptions } from './historyModel'

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

:deep(.ant-input-number),
:deep(.ant-select) {
  width: 100%;
}
</style>

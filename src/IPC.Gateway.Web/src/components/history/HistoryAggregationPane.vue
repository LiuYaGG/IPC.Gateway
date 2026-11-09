<template>
  <a-form :model="model" :disabled="disabled" class="history-pane-form" layout="vertical">
    <div class="history-pane-grid">
      <a-form-item :label="tx('启用窗口聚合')"><a-switch v-model:checked="model.aggregationEnabled" /></a-form-item>
      <a-form-item :label="tx('聚合窗口 s')">
        <gateway-number-input
          v-model="model.aggregationIntervalSeconds"
          :disabled="disabled || !model.aggregationEnabled"
          :min="1"
          :max="86400"
        />
      </a-form-item>
      <a-form-item :label="tx('聚合方法')" class="history-pane-grid__wide">
        <a-checkbox-group v-model:value="selectedMethods" :disabled="disabled || !model.aggregationEnabled">
          <a-checkbox v-for="option in aggregationMethodOptions" :key="option.value" :value="option.value">
            {{ option.label }}
          </a-checkbox>
        </a-checkbox-group>
      </a-form-item>
    </div>
  </a-form>
</template>

<script setup lang="ts">
import { tx } from '../../i18n'
import { computed } from 'vue'
import type { HistoryDataProcessingConfig } from '../../api'
import { aggregationMethodOptions, parseAggregationMethods } from './historyModel'

const props = defineProps<{
  model: HistoryDataProcessingConfig
  disabled?: boolean
}>()

const selectedMethods = computed({
  get: () => parseAggregationMethods(props.model.aggregationMethods),
  set: (value) => {
    props.model.aggregationMethods = value.join(',')
  }
})
</script>

<style scoped>
.history-pane-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 12px 18px;
}

.history-pane-grid__wide {
  grid-column: 1 / -1;
}

:deep(.ant-input-number) {
  width: 100%;
}

:deep(.ant-checkbox-group) {
  display: flex;
  flex-wrap: wrap;
  gap: 8px 18px;
}
</style>

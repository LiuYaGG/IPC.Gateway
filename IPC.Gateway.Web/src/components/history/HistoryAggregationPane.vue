<template>
  <el-form :model="model" :disabled="disabled" label-position="top" class="history-pane-form">
    <div class="history-pane-grid">
      <el-form-item label="启用窗口聚合">
        <el-switch v-model="model.aggregationEnabled" />
      </el-form-item>
      <el-form-item label="聚合窗口 s">
        <el-input-number
          v-model="model.aggregationIntervalSeconds"
          :disabled="disabled || !model.aggregationEnabled"
          :min="1"
          :max="86400"
          controls-position="right"
        />
      </el-form-item>
      <el-form-item label="聚合方法" class="history-pane-grid__wide">
        <el-checkbox-group v-model="selectedMethods" :disabled="disabled || !model.aggregationEnabled">
          <el-checkbox
            v-for="option in aggregationMethodOptions"
            :key="option.value"
            :label="option.value"
          >
            {{ option.label }}
          </el-checkbox>
        </el-checkbox-group>
      </el-form-item>
    </div>
  </el-form>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import type { HistoryDataProcessingConfig } from '../../api'
import { aggregationMethodOptions, parseAggregationMethods } from './historyModel'

const props = defineProps<{
  model: HistoryDataProcessingConfig
  disabled?: boolean
}>()

const selectedMethods = computed({
  get: () => parseAggregationMethods(props.model.aggregationMethods),
  set: value => {
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

:deep(.el-input-number) {
  width: 100%;
}

:deep(.el-checkbox-group) {
  display: flex;
  flex-wrap: wrap;
  gap: 8px 18px;
}
</style>

<template>
  <el-form :model="model" :disabled="disabled" label-position="top" class="history-pane-form">
    <div class="history-pane-grid">
      <el-form-item label="启用降采样">
        <el-switch v-model="model.downsamplingEnabled" />
      </el-form-item>
      <el-form-item label="降采样间隔 ms">
        <el-input-number
          v-model="model.downsamplingIntervalMs"
          :disabled="disabled || !model.downsamplingEnabled"
          :min="1"
          :max="86400000"
          :step="100"
          controls-position="right"
        />
      </el-form-item>
      <el-form-item label="启用时间对齐">
        <el-switch v-model="model.alignmentEnabled" />
      </el-form-item>
      <el-form-item label="对齐间隔 ms">
        <el-input-number
          v-model="model.alignmentIntervalMs"
          :disabled="disabled || !model.alignmentEnabled"
          :min="1"
          :max="86400000"
          :step="100"
          controls-position="right"
        />
      </el-form-item>
    </div>
  </el-form>
</template>

<script setup lang="ts">
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

:deep(.el-input-number) {
  width: 100%;
}
</style>

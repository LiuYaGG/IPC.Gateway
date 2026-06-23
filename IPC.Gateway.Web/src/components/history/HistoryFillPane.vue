<template>
  <el-form :model="model" :disabled="disabled" label-position="top" class="history-pane-form">
    <div class="history-pane-grid">
      <el-form-item label="启用补点">
        <el-switch v-model="model.fillEnabled" />
      </el-form-item>
      <el-form-item label="补点模式">
        <el-select v-model="model.fillMode" :disabled="disabled || !model.fillEnabled">
          <el-option
            v-for="option in fillModeOptions"
            :key="option.value"
            :label="option.label"
            :value="option.value"
          />
        </el-select>
      </el-form-item>
      <el-form-item label="补点间隔 ms">
        <el-input-number
          v-model="model.fillIntervalMs"
          :disabled="disabled || !model.fillEnabled"
          :min="0"
          :max="86400000"
          :step="100"
          controls-position="right"
        />
      </el-form-item>
      <el-form-item label="最大补点间隔 s">
        <el-input-number
          v-model="model.fillMaxGapSeconds"
          :disabled="disabled || !model.fillEnabled"
          :min="0"
          :max="86400"
          controls-position="right"
        />
      </el-form-item>
      <el-form-item label="单次最大生成点数">
        <el-input-number
          v-model="model.maxSyntheticPointsPerInput"
          :disabled="disabled || !model.fillEnabled"
          :min="1"
          :max="10000"
          controls-position="right"
        />
      </el-form-item>
    </div>
  </el-form>
</template>

<script setup lang="ts">
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

:deep(.el-input-number),
:deep(.el-select) {
  width: 100%;
}
</style>

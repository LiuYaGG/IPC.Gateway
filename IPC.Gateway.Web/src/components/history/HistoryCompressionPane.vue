<template>
  <el-form :model="model" :disabled="disabled" label-position="top" class="history-pane-form">
    <div class="history-pane-grid">
      <el-form-item label="启用数据压缩">
        <el-switch v-model="model.compressionEnabled" />
      </el-form-item>
      <el-form-item label="数值压缩容差">
        <el-input-number
          v-model="model.compressionTolerance"
          :min="0"
          :step="0.1"
          controls-position="right"
        />
      </el-form-item>
      <el-form-item label="重复文本值压缩">
        <el-switch v-model="model.compressDuplicateText" :disabled="disabled || !model.compressionEnabled" />
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

<template>
  <section class="history-section">
    <div class="history-section__title">
      <h3>冷热分层与保留策略</h3>
      <span>配置热目录、冷目录、压缩和自动清理，控制历史文件的生命周期。</span>
    </div>

    <el-form :model="config.storage" :disabled="disabled" label-position="top" class="history-form">
      <div class="history-form-grid">
        <el-form-item label="启用冷热分层">
          <el-switch v-model="config.storage.tieringEnabled" />
        </el-form-item>
        <el-form-item label="保留策略">
          <el-select v-model="config.storage.retentionPolicy">
            <el-option
              v-for="option in retentionPolicyOptions"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="冷数据目录" class="history-form-grid__wide">
          <el-input v-model="config.storage.coldDirectory" :disabled="disabled || !config.storage.tieringEnabled" placeholder="Data\\HistoryCold" />
        </el-form-item>
        <el-form-item label="热数据保留天数">
          <el-input-number v-model="config.storage.hotRetentionDays" :min="1" :max="3650" controls-position="right" />
        </el-form-item>
        <el-form-item label="冷数据保留天数">
          <el-input-number v-model="config.storage.coldRetentionDays" :min="config.storage.hotRetentionDays" :max="3650" controls-position="right" />
        </el-form-item>
        <el-form-item label="自动清理">
          <el-switch v-model="config.storage.autoCleanupEnabled" />
        </el-form-item>
        <el-form-item label="清理间隔 h">
          <el-input-number
            v-model="config.storage.cleanupIntervalHours"
            :disabled="disabled || !config.storage.autoCleanupEnabled"
            :min="1"
            :max="720"
            controls-position="right"
          />
        </el-form-item>
        <el-form-item label="启用文件压缩">
          <el-switch v-model="config.storage.compressionEnabled" />
        </el-form-item>
        <el-form-item label="压缩等待天数">
          <el-input-number
            v-model="config.storage.compressAfterDays"
            :disabled="disabled || !config.storage.compressionEnabled"
            :min="0"
            :max="3650"
            controls-position="right"
          />
        </el-form-item>
        <el-form-item label="压缩热数据">
          <el-switch v-model="config.storage.compressHotFiles" :disabled="disabled || !config.storage.compressionEnabled" />
        </el-form-item>
        <el-form-item label="压缩冷数据">
          <el-switch v-model="config.storage.compressColdFiles" :disabled="disabled || !config.storage.compressionEnabled" />
        </el-form-item>
        <el-form-item label="容量上限 MB">
          <el-input-number v-model="config.storage.maxStorageMegabytes" :min="0" :max="1048576" :step="1024" controls-position="right" />
        </el-form-item>
      </div>
    </el-form>
  </section>
</template>

<script setup lang="ts">
import type { HistoryConfig } from '../../api'
import { retentionPolicyOptions } from './historyModel'

defineProps<{
  config: HistoryConfig
  disabled?: boolean
}>()
</script>

<style scoped>
.history-section {
  display: grid;
  gap: 16px;
  padding: 18px;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  background: #ffffff;
}

.history-section__title {
  display: grid;
  gap: 4px;
}

.history-section__title h3 {
  margin: 0;
  color: #172033;
  font-size: 16px;
}

.history-section__title span {
  color: #64748b;
  font-size: 13px;
}

.history-form-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 12px 18px;
}

.history-form-grid__wide {
  grid-column: 1 / -1;
}

:deep(.el-input-number),
:deep(.el-select) {
  width: 100%;
}
</style>

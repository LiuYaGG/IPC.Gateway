<template>
  <section class="history-section">
    <div class="history-section__title">
      <h3>历史库</h3>
      <span>控制本地历史数据保存、查询上限和边缘处理总开关。</span>
    </div>

    <el-form :model="config" :disabled="disabled" label-position="top" class="history-form">
      <div class="history-form-grid">
        <el-form-item label="启用历史库">
          <el-switch v-model="config.enabled" />
        </el-form-item>
        <el-form-item label="数据处理总开关">
          <el-switch v-model="config.dataProcessing.enabled" />
        </el-form-item>
        <el-form-item label="保存目录" class="history-form-grid__wide" required>
          <el-input v-model="config.directory" placeholder="Data\\History" />
        </el-form-item>
        <el-form-item label="保留天数" required>
          <el-input-number v-model="config.retentionDays" :min="1" :max="3650" controls-position="right" />
        </el-form-item>
        <el-form-item label="最大查询条数" required>
          <el-input-number v-model="config.maxViewRecords" :min="50" :max="10000" :step="50" controls-position="right" />
        </el-form-item>
      </div>
    </el-form>
  </section>
</template>

<script setup lang="ts">
import type { HistoryConfig } from '../../api'

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

:deep(.el-input-number) {
  width: 100%;
}
</style>

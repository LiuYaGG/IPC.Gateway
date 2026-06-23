<template>
  <section class="history-section">
    <div class="history-section__title">
      <h3>边缘侧数据处理</h3>
      <span>配置压缩、降采样、补点、对齐和聚合，处理后的值会进入历史库。</span>
    </div>

    <el-tabs v-model="activeTab" class="history-tabs">
      <el-tab-pane label="数据压缩" name="compression">
        <HistoryCompressionPane :model="config.dataProcessing" :disabled="disabled || !config.dataProcessing.enabled" />
      </el-tab-pane>
      <el-tab-pane label="降采样 / 对齐" name="sampling">
        <HistorySamplingPane :model="config.dataProcessing" :disabled="disabled || !config.dataProcessing.enabled" />
      </el-tab-pane>
      <el-tab-pane label="补点" name="fill">
        <HistoryFillPane :model="config.dataProcessing" :disabled="disabled || !config.dataProcessing.enabled" />
      </el-tab-pane>
      <el-tab-pane label="窗口聚合" name="aggregation">
        <HistoryAggregationPane :model="config.dataProcessing" :disabled="disabled || !config.dataProcessing.enabled" />
      </el-tab-pane>
    </el-tabs>
  </section>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import type { HistoryConfig } from '../../api'
import HistoryAggregationPane from './HistoryAggregationPane.vue'
import HistoryCompressionPane from './HistoryCompressionPane.vue'
import HistoryFillPane from './HistoryFillPane.vue'
import HistorySamplingPane from './HistorySamplingPane.vue'

defineProps<{
  config: HistoryConfig
  disabled?: boolean
}>()

const activeTab = ref('compression')
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

.history-tabs {
  min-width: 0;
}
</style>

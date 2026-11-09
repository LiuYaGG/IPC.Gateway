<template>
  <section class="history-section">
    <div class="history-section__title">
      <h3>{{ tx('边缘侧数据处理') }}</h3>
      <span>{{ tx('配置压缩、降采样、补点、对齐和聚合，处理后的值会进入历史库。') }}</span>
    </div>
    <a-tabs v-model:active-key="activeTab" class="history-tabs">
      <a-tab-pane :tab="tx('数据压缩')" key="compression">
        <HistoryCompressionPane :model="config.dataProcessing" :disabled="disabled || !config.dataProcessing.enabled" />
      </a-tab-pane>
      <a-tab-pane :tab="tx('降采样 / 对齐')" key="sampling">
        <HistorySamplingPane :model="config.dataProcessing" :disabled="disabled || !config.dataProcessing.enabled" />
      </a-tab-pane>
      <a-tab-pane :tab="tx('补点')" key="fill">
        <HistoryFillPane :model="config.dataProcessing" :disabled="disabled || !config.dataProcessing.enabled" />
      </a-tab-pane>
      <a-tab-pane :tab="tx('窗口聚合')" key="aggregation">
        <HistoryAggregationPane :model="config.dataProcessing" :disabled="disabled || !config.dataProcessing.enabled" />
      </a-tab-pane>
    </a-tabs>
  </section>
</template>

<script setup lang="ts">
import { tx } from '../../i18n'
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
  font-size: calc(16px + 1pt);
}

.history-section__title span {
  color: #64748b;
  font-size: calc(13px + 1pt);
}

.history-tabs {
  min-width: 0;
}
</style>

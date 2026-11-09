<template>
  <section class="history-section">
    <div class="history-section__title">
      <h3>{{ tx('历史库') }}</h3>
      <span>{{ tx('控制本地历史数据保存、查询上限和边缘处理总开关。') }}</span>
    </div>
    <a-form :model="config" :disabled="disabled" class="history-form" layout="vertical">
      <div class="history-form-grid">
        <a-form-item :label="tx('启用历史库')"><a-switch v-model:checked="config.enabled" /></a-form-item>
        <a-form-item :label="tx('数据处理总开关')">
          <a-switch v-model:checked="config.dataProcessing.enabled" />
        </a-form-item>
        <a-form-item :label="tx('保存目录')" class="history-form-grid__wide" required>
          <gateway-input v-model="config.directory" placeholder="Data\\History" />
        </a-form-item>
        <a-form-item :label="tx('保留天数')" required>
          <gateway-number-input v-model="config.retentionDays" :min="1" :max="3650" />
        </a-form-item>
        <a-form-item :label="tx('最大查询条数')" required>
          <gateway-number-input v-model="config.maxViewRecords" :min="50" :max="10000" :step="50" />
        </a-form-item>
      </div>
    </a-form>
  </section>
</template>

<script setup lang="ts">
import { tx } from '../../i18n'
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
  font-size: calc(16px + 1pt);
}

.history-section__title span {
  color: #64748b;
  font-size: calc(13px + 1pt);
}

.history-form-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 12px 18px;
}

.history-form-grid__wide {
  grid-column: 1 / -1;
}

:deep(.ant-input-number) {
  width: 100%;
}
</style>

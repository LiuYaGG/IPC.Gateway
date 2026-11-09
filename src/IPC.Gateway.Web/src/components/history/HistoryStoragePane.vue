<template>
  <section class="history-section">
    <div class="history-section__title">
      <h3>{{ tx('冷热分层与保留策略') }}</h3>
      <span>{{ tx('配置热目录、冷目录、压缩和自动清理，控制历史文件的生命周期。') }}</span>
    </div>
    <a-form :model="config.storage" :disabled="disabled" class="history-form" layout="vertical">
      <div class="history-form-grid">
        <a-form-item :label="tx('启用冷热分层')">
          <a-switch v-model:checked="config.storage.tieringEnabled" />
        </a-form-item>
        <a-form-item :label="tx('保留策略')">
          <gateway-select v-model="config.storage.retentionPolicy">
            <a-select-option
              v-for="option in retentionPolicyOptions"
              :key="option.value"
              :label="tx(option.label)"
              :value="option.value"
            >
              {{ option.label }}
            </a-select-option>
          </gateway-select>
        </a-form-item>
        <a-form-item :label="tx('冷数据目录')" class="history-form-grid__wide">
          <gateway-input
            v-model="config.storage.coldDirectory"
            :disabled="disabled || !config.storage.tieringEnabled"
            placeholder="Data\\HistoryCold"
          />
        </a-form-item>
        <a-form-item :label="tx('热数据保留天数')">
          <gateway-number-input v-model="config.storage.hotRetentionDays" :min="1" :max="3650" />
        </a-form-item>
        <a-form-item :label="tx('冷数据保留天数')">
          <gateway-number-input
            v-model="config.storage.coldRetentionDays"
            :min="config.storage.hotRetentionDays"
            :max="3650"
          />
        </a-form-item>
        <a-form-item :label="tx('自动清理')">
          <a-switch v-model:checked="config.storage.autoCleanupEnabled" />
        </a-form-item>
        <a-form-item :label="tx('清理间隔 h')">
          <gateway-number-input
            v-model="config.storage.cleanupIntervalHours"
            :disabled="disabled || !config.storage.autoCleanupEnabled"
            :min="1"
            :max="720"
          />
        </a-form-item>
        <a-form-item :label="tx('启用文件压缩')">
          <a-switch v-model:checked="config.storage.compressionEnabled" />
        </a-form-item>
        <a-form-item :label="tx('压缩等待天数')">
          <gateway-number-input
            v-model="config.storage.compressAfterDays"
            :disabled="disabled || !config.storage.compressionEnabled"
            :min="0"
            :max="3650"
          />
        </a-form-item>
        <a-form-item :label="tx('压缩热数据')">
          <a-switch
            v-model:checked="config.storage.compressHotFiles"
            :disabled="disabled || !config.storage.compressionEnabled"
          />
        </a-form-item>
        <a-form-item :label="tx('压缩冷数据')">
          <a-switch
            v-model:checked="config.storage.compressColdFiles"
            :disabled="disabled || !config.storage.compressionEnabled"
          />
        </a-form-item>
        <a-form-item :label="tx('容量上限 MB')">
          <gateway-number-input v-model="config.storage.maxStorageMegabytes" :min="0" :max="1048576" :step="1024" />
        </a-form-item>
      </div>
    </a-form>
  </section>
</template>

<script setup lang="ts">
import { tx } from '../../i18n'
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

:deep(.ant-input-number),
:deep(.ant-select) {
  width: 100%;
}
</style>

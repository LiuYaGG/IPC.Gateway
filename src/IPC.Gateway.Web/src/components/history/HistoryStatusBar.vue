<template>
  <section class="history-status-bar">
    <div class="history-status-main">
      <span>{{ tx('历史库状态') }}</span>
      <strong>{{ tx(statusText) }}</strong>
      <a-tag :color="tagColor(statusTagType)">{{ tx(statusTagText) }}</a-tag>
    </div>
    <div class="history-status-grid">
      <div>
        <span>{{ tx('处理链路') }}</span>
        <strong>{{ status?.dataProcessingEnabled ? tx('已启用') : tx('未启用') }}</strong>
      </div>
      <div>
        <span>{{ tx('接收/写入') }}</span>
        <strong>{{ formatNumber(status?.receivedValueCount) }} / {{ formatNumber(status?.writtenValueCount) }}</strong>
      </div>
      <div>
        <span>{{ tx('跳过') }}</span>
        <strong>{{ formatNumber(status?.skippedValueCount) }}</strong>
      </div>
      <div>
        <span>{{ tx('压缩') }}</span>
        <strong>{{ formatNumber(status?.compressedValueCount) }}</strong>
      </div>
      <div>
        <span>{{ tx('降采样') }}</span>
        <strong>{{ formatNumber(status?.downsampledValueCount) }}</strong>
      </div>
      <div>
        <span>{{ tx('补点/聚合') }}</span>
        <strong>{{ formatNumber(status?.filledValueCount) }} / {{ formatNumber(status?.aggregatedValueCount) }}</strong>
      </div>
      <div>
        <span>{{ tx('文件数') }}</span>
        <strong>{{ fileCount }}</strong>
      </div>
      <div>
        <span>{{ tx('占用空间') }}</span>
        <strong>{{ formatBytes(status?.totalBytes) }}</strong>
      </div>
      <div>
        <span>{{ tx('热 / 冷文件') }}</span>
        <strong>{{ formatNumber(status?.hotFileCount) }} / {{ formatNumber(status?.coldFileCount) }}</strong>
      </div>
      <div>
        <span>{{ tx('热 / 冷空间') }}</span>
        <strong>{{ formatBytes(status?.hotBytes) }} / {{ formatBytes(status?.coldBytes) }}</strong>
      </div>
      <div>
        <span>{{ tx('压缩文件') }}</span>
        <strong>{{ formatNumber(status?.compressedFileCount) }} · {{ formatBytes(status?.compressedBytes) }}</strong>
      </div>
      <div>
        <span>{{ tx('自动清理') }}</span>
        <strong>{{ status?.autoCleanupEnabled ? `${status.cleanupIntervalHours}h` : tx('关闭') }}</strong>
      </div>
      <div>
        <span>{{ tx('上次清理') }}</span>
        <strong>{{ formatDateTime(status?.lastCleanupTime || '') || '-' }}</strong>
      </div>
      <div>
        <span>{{ tx('下次清理') }}</span>
        <strong>{{ formatDateTime(status?.nextCleanupTime || '') || '-' }}</strong>
      </div>
    </div>
    <div v-if="status?.lastError" class="history-status-error">
      <span>{{ tx('最近错误') }}</span>
      <strong>{{ status.lastError }}</strong>
      <small>{{ formatDateTime(status.lastErrorTime) }}</small>
    </div>
  </section>
</template>

<script setup lang="ts">
import { tx } from '../../i18n'
import { tagColor } from '../../utils/ui'
import { computed } from 'vue'
import type { HistoryConfig, HistoryRuntimeStatus } from '../../api'
import { formatDateTime } from '../../utils/format'
import { formatBytes, formatNumber } from './historyModel'

const props = defineProps<{
  config: HistoryConfig | null
  status?: HistoryRuntimeStatus
}>()

const statusText = computed(() => {
  if (props.status?.isRunning) return '运行中'
  if (props.config?.enabled || props.status?.enabled) return '已启用，未运行'
  return '未启用'
})

const statusTagType = computed(() => {
  if (props.status?.isRunning) return 'success'
  if (props.config?.enabled || props.status?.enabled) return 'warning'
  return 'info'
})

const statusTagText = computed(() => {
  if (props.status?.isDegraded) return '降级'
  if (props.status?.isRunning) return '正常'
  return '停止'
})

const fileCount = computed(() => {
  const status = props.status
  return formatNumber((status?.valueFiles ?? 0) + (status?.alarmFiles ?? 0) + (status?.publishFiles ?? 0))
})
</script>

<style scoped>
.history-status-bar {
  display: grid;
  gap: 14px;
  padding: 18px;
  border: 1px solid #d9e2ef;
  border-radius: 8px;
  background: linear-gradient(135deg, #f7fbff, #f3f8f6);
}

.history-status-main {
  display: flex;
  align-items: center;
  gap: 10px;
}

.history-status-main span,
.history-status-grid span,
.history-status-error span {
  color: #64748b;
  font-size: calc(13px + 1pt);
}

.history-status-main strong {
  font-size: calc(20px + 1pt);
  color: #172033;
}

.history-status-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));
  gap: 10px;
}

.history-status-grid div {
  display: grid;
  gap: 4px;
  min-width: 0;
  padding: 10px 12px;
  border: 1px solid rgba(148, 163, 184, 0.22);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.74);
}

.history-status-grid strong {
  overflow: hidden;
  color: #0f172a;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.history-status-error {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  padding: 10px 12px;
  border-radius: 8px;
  color: #b42318;
  background: #fff1f0;
}

.history-status-error strong {
  flex: 1;
  min-width: 180px;
}
</style>

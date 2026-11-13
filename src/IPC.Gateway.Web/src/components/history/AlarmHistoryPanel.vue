<template>
  <div class="alarm-history">
    <div class="alarm-history-tools">
      <a-button :loading="loading" @click="refresh++">{{ tx('刷新') }}</a-button>
    </div>
    <a-alert v-if="error" type="error" show-icon :message="tx(error)" />
    <AutoHeightTable resize-key="alarm-history" :columns="columns" :data-source="rows" :loading="loading"
      row-key="eventId" :paging="false" :scroll-x="1150">
      <template #bodyCell="{ column, record }">
        <template v-if="column.key === 'timestamp'">{{ new Date(record.timestamp).toLocaleString(locale) }}</template>
        <template v-else-if="column.key === 'path'">{{ path(record) }}</template>
        <template v-else-if="column.key === 'state'">{{ transition(record) }}</template>
        <template v-else-if="column.key === 'deliveryStatus'">{{ label(record.deliveryStatus) }}</template>
        <template v-else-if="column.key === 'message'">{{ tx(record.message) || '—' }}</template>
        <template v-else-if="column.key === 'value' || column.key === 'threshold'">{{ record[column.key] ?? '—' }}</template>
      </template>
    </AutoHeightTable>
    <TablePager :current="page" :page-size="pageSize" :total="total" @change="changePage" />
  </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { loadAlarmHistoryPage, type AlarmHistoryRow } from '../../api'
import { locale, tx } from '../../i18n'
import { qualityText, statusText } from '../../utils/display'
import AutoHeightTable from '../AutoHeightTable.vue'
import TablePager from '../controls/TablePager.vue'

const page = ref(1)
const pageSize = ref(20)
const total = ref(0)
const rows = ref<AlarmHistoryRow[]>([])
const refresh = ref(0)
const loading = ref(false)
const error = ref('')
const columns = computed(() => [
  { title: tx('时间'), key: 'timestamp', width: 180 },
  { title: tx('报警来源'), key: 'path', width: 250 },
  { title: tx('状态变化'), key: 'state', width: 180 },
  { title: tx('触发值'), key: 'value', width: 100 },
  { title: tx('阈值'), key: 'threshold', width: 90 },
  { title: tx('消息'), key: 'message', width: 230 },
  { title: tx('发送状态'), key: 'deliveryStatus', width: 120 },
])
const labels: Record<string, string> = {
  Normal: '正常', HighAlarm: '高限报警', LowAlarm: '低限报警', HighWarning: '高限预警', LowWarning: '低限预警',
  Pending: '待发送', Sent: '已发送', Local: '仅本地记录',
  alarm: '报警', warning: '预警', recovery: '恢复', active: '触发', clear: '恢复', High: '高限', Low: '低限',
  Deadband: '死区', RateOfChange: '变化率', QualityGate: '质量门控', Expression: '表达式', Sequence: '顺序/时序',
}
function label(code: string) {
  if (code?.startsWith('Quality:')) return tx(qualityText(code.slice(8)))
  return tx(labels[code] || labels[code?.toLowerCase()] || statusText(code, '—'))
}
function path(record: AlarmHistoryRow) {
  const tagPath = [record.channelName, record.deviceName, record.groupName, record.tagName || record.tagId].filter(Boolean).join('.')
  return [record.ruleName, tagPath].filter(Boolean).join(' · ') || '—'
}
function transition(record: AlarmHistoryRow) {
  return record.previousState && record.state
    ? `${label(record.previousState)} → ${label(record.state)}`
    : label(record.state || record.eventType)
}
function changePage(current: number, size: number) {
  page.value = current
  pageSize.value = size
}
watch([page, pageSize, refresh], async ([current, size], _, onCleanup) => {
  const controller = new AbortController()
  onCleanup(() => controller.abort())
  loading.value = true
  error.value = ''
  try {
    const result = await loadAlarmHistoryPage(current, size, controller.signal)
    if (controller.signal.aborted) return
    total.value = result.total
    const lastPage = Math.max(1, Math.ceil(result.total / size))
    if (current > lastPage) page.value = lastPage
    else rows.value = result.items
  } catch (cause) {
    if (!controller.signal.aborted) {
      rows.value = []
      total.value = 0
      error.value = cause instanceof Error ? cause.message : '读取报警记录失败'
    }
  } finally {
    if (!controller.signal.aborted) loading.value = false
  }
}, { immediate: true })
</script>

<style scoped>
.alarm-history {
  display: flex;
  flex-direction: column;
  gap: 8px;
  height: 100%;
  min-height: 0;
  padding: 12px;
  background: #fff;
  border: 1px solid var(--gateway-border);
  border-radius: 8px;
}
.alarm-history-tools {
  display: flex;
  justify-content: flex-end;
  margin-bottom: 8px;
}
</style>

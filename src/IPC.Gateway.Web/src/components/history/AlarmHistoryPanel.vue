<template>
  <section class="alarm-history">
    <form class="alarm-history-tools" @submit.prevent="submit">
      <label class="alarm-history-time">
        <span>{{ tx('开始时间') }}</span>
        <a-date-picker v-model:value="from" show-time allow-clear value-format="YYYY-MM-DDTHH:mm:ss"
          format="YYYY-MM-DD HH:mm:ss" :placeholder="tx('开始时间')" :aria-label="tx('开始时间')" />
      </label>
      <label class="alarm-history-time">
        <span>{{ tx('结束时间') }}</span>
        <a-date-picker v-model:value="to" show-time allow-clear value-format="YYYY-MM-DDTHH:mm:ss"
          format="YYYY-MM-DD HH:mm:ss" :placeholder="tx('结束时间')" :aria-label="tx('结束时间')" />
      </label>
      <label v-for="field in fields" :key="field.level">
        <span>{{ tx(field.label) }}</span>
        <HistoryPointSelect source="alarms" :level="field.level" :label="tx(field.label)" :filters="ancestors(field.level)"
          :selected="selected[field.level]" :active="active && !deactivated"
          :disabled="!!field.parent && !selected[field.parent]" @change="select(field.level, $event)" />
      </label>
      <div class="alarm-history-actions">
        <a-button type="primary" html-type="submit" :loading="loading" :disabled="exporting">
          <template #icon><SearchOutlined /></template>{{ tx('查询') }}
        </a-button>
        <a-button v-if="canExport" :disabled="!result?.items.length || loading" :loading="exporting"
          :title="tx('将查询结果的当前页导出为 Excel。')" @click="download">
          <template #icon><DownloadOutlined /></template>{{ tx('导出本页') }}
        </a-button>
      </div>
    </form>
    <a-alert v-if="error" type="error" show-icon :message="tx(error)" />
    <div class="alarm-history-results">
      <AutoHeightTable resize-key="alarm-history" :columns="columns" :data-source="rows" :loading="loading"
        row-key="eventId" :paging="false" :scroll-x="1150">
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'timestamp'">{{ new Date(record.timestamp).toLocaleString(locale) }}</template>
          <template v-else-if="column.key === 'path'">{{ path(record) }}</template>
          <template v-else-if="column.key === 'state'">{{ transition(record) }}</template>
          <template v-else-if="column.key === 'deliveryStatus'">{{ label(record.deliveryStatus) }}</template>
          <template v-else-if="column.key === 'message'">{{ record.message || '—' }}</template>
          <template v-else-if="column.key === 'value' || column.key === 'threshold'">{{ record[column.key] ?? '—' }}</template>
        </template>
        <template #emptyText>
          <a-empty :description="tx(result ? '没有符合查询条件的报警记录' : '设置查询条件后点击查询')" />
        </template>
      </AutoHeightTable>
      <TablePager :current="page" :page-size="pageSize" :total="result?.total ?? 0"
        :disabled="loading || exporting || !result" @change="changePage" />
    </div>
  </section>
</template>

<script setup lang="ts">
import { computed, onActivated, onBeforeUnmount, onDeactivated, reactive, ref, shallowRef, watch } from 'vue'
import { DownloadOutlined, SearchOutlined } from '@ant-design/icons-vue'
import { exportAlarmHistory, loadAlarmHistoryPage, type AlarmHistoryPage, type AlarmHistoryQuery, type AlarmHistoryRow,
  type HistoryOptionLevel, type HistoryRecordFilters, type HistoryRecordOption } from '../../api'
import { locale, tx } from '../../i18n'
import { ruleStateText } from '../../i18n/ruleText'
import { PERMISSIONS, usePermissions } from '../../utils/permissions'
import { localExportFileName, saveExcelAs } from '../../utils/excelSave'
import AutoHeightTable from '../AutoHeightTable.vue'
import TablePager from '../controls/TablePager.vue'
import HistoryPointSelect from './HistoryPointSelect.vue'

const props = withDefaults(defineProps<{ active?: boolean }>(), { active: true })
const { hasPermission } = usePermissions()
const canExport = computed(() => hasPermission(PERMISSIONS.historyView) && hasPermission(PERMISSIONS.historyExport))
const fields: { level: HistoryOptionLevel; label: string; parent?: HistoryOptionLevel }[] = [
  { level: 'channel', label: '通道' }, { level: 'device', label: '设备', parent: 'channel' },
  { level: 'group', label: '分组', parent: 'device' }, { level: 'tag', label: '标签', parent: 'device' }
]
const selected = reactive<Partial<Record<HistoryOptionLevel, HistoryRecordOption>>>({})
function ancestors(level: HistoryOptionLevel): HistoryRecordFilters {
  if (level === 'channel') return {}
  if (level === 'device') return { channelId: selected.channel?.id }
  if (level === 'group') return { channelId: selected.channel?.id, deviceId: selected.device?.id }
  return { channelId: selected.channel?.id, deviceId: selected.device?.id, groupId: selected.group?.id }
}
function select(level: HistoryOptionLevel, option?: HistoryRecordOption) {
  if (selected[level]?.id === option?.id) return
  selected[level] = option
  for (const field of fields.slice(fields.findIndex(field => field.level === level) + 1)) delete selected[field.level]
}
function localInputTime(date: Date) {
  return new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 19)
}
const now = new Date()
const from = ref<string | null>(localInputTime(new Date(+now - 86400000)))
const to = ref<string | null>(localInputTime(now))
const page = ref(1)
const pageSize = ref(20)
const result = shallowRef<AlarmHistoryPage>()
const applied = shallowRef<AlarmHistoryQuery>()
const rows = computed(() => result.value?.items ?? [])
const loading = ref(false)
const exporting = ref(false)
const deactivated = ref(false)
const error = ref('')
let queryController: AbortController | undefined
let exportController: AbortController | undefined
const columns = computed(() => [
  { title: tx('时间'), key: 'timestamp', width: 180 },
  { title: tx('报警来源'), key: 'path', width: 250 },
  { title: tx('状态变化'), key: 'state', width: 180 },
  { title: tx('触发值'), key: 'value', width: 100 },
  { title: tx('阈值'), key: 'threshold', width: 90 },
  { title: tx('消息'), key: 'message', width: 230 },
  { title: tx('发送状态'), key: 'deliveryStatus', width: 120 },
])
function label(code: string) {
  return ruleStateText(code, locale.value)
}
function path(record: AlarmHistoryRow) {
  const tagPath = [record.channelName, record.deviceName, record.groupName, record.tagName || record.tagId].filter(Boolean).join('.')
  return [record.ruleName, tagPath].filter(Boolean).join(' · ') || '—'
}
function transition(record: AlarmHistoryRow) {
  return record.previousState && record.state
    ? `${record.previousState} → ${record.state}`
    : record.state || record.eventType || '—'
}
function cancelRequests() {
  queryController?.abort()
  exportController?.abort()
  loading.value = false
  exporting.value = false
}
function resetResult() {
  cancelRequests()
  result.value = undefined
  applied.value = undefined
  page.value = 1
  error.value = ''
}
watch([from, to, () => selected.channel?.id, () => selected.device?.id, () => selected.group?.id, () => selected.tag?.id], resetResult, { flush: 'sync' })
watch(() => props.active, active => { if (!active) cancelRequests() })
onActivated(() => { deactivated.value = false })
onDeactivated(() => { deactivated.value = true; cancelRequests() })
onBeforeUnmount(cancelRequests)

function submit() {
  if (loading.value || exporting.value) return
  const begin = from.value ? Date.parse(from.value) : undefined
  const end = to.value ? Date.parse(to.value) : undefined
  if ((begin !== undefined && !Number.isFinite(begin)) || (end !== undefined && !Number.isFinite(end)) ||
    (begin !== undefined && end !== undefined && begin > end)) {
    error.value = '请填写有效的时间范围，开始时间不能晚于结束时间。'
    return
  }
  const filters: AlarmHistoryQuery = {
    ...ancestors('tag'), tagId: selected.tag?.id,
    from: begin !== undefined ? new Date(begin).toISOString() : undefined,
    to: end !== undefined ? new Date(end).toISOString() : undefined
  }
  result.value = undefined
  applied.value = undefined
  void load(1, pageSize.value, filters)
}
async function load(current: number, size: number, filters: AlarmHistoryQuery) {
  cancelRequests()
  const controller = queryController = new AbortController()
  loading.value = true
  error.value = ''
  try {
    const next = await loadAlarmHistoryPage(current, size, filters, locale.value, controller.signal)
    if (controller.signal.aborted) return
    const lastPage = Math.max(1, Math.ceil(next.total / size))
    if (current > lastPage) { await load(lastPage, size, { ...filters, snapshot: next.snapshot }); return }
    result.value = next
    applied.value = { ...filters, snapshot: next.snapshot }
    page.value = current
    pageSize.value = size
  } catch (cause) {
    if (!controller.signal.aborted) error.value = cause instanceof Error ? cause.message : '读取报警记录失败'
  } finally {
    if (!controller.signal.aborted) loading.value = false
  }
}
function changePage(current: number, size: number) {
  if (applied.value && !loading.value && !exporting.value) void load(current, size, applied.value)
}
async function download() {
  if (!applied.value || !result.value?.items.length || !canExport.value || loading.value || exporting.value) return
  const controller = exportController = new AbortController()
  const filters = { ...applied.value }
  const currentPage = page.value
  const size = pageSize.value
  const language = locale.value
  exporting.value = true
  error.value = ''
  try {
    await saveExcelAs(localExportFileName('alarm-history', currentPage),
      () => exportAlarmHistory(filters, currentPage, size, language, controller.signal), controller.signal)
  } catch (cause) {
    if (!controller.signal.aborted) error.value = cause instanceof Error ? cause.message : '导出报警记录失败'
  } finally {
    if (!controller.signal.aborted) exporting.value = false
  }
}
</script>

<style scoped>
.alarm-history { display: flex; flex-direction: column; gap: 10px; height: 100%; min-height: 0; min-width: 0; container-type: inline-size; }
.alarm-history-tools, .alarm-history-results { padding: 12px; background: #fff; border: 1px solid var(--gateway-border); border-radius: 8px; }
.alarm-history-tools { display: grid; grid-template-columns: repeat(2, minmax(198px, 1.3fr)) repeat(4, minmax(110px, 1fr)) auto; gap: 10px; align-items: start; flex-shrink: 0; }
.alarm-history-tools > label { display: flex; flex-direction: column; gap: 5px; min-width: 0; }
.alarm-history-tools .ant-picker { width: 100%; min-width: 0; }
.alarm-history-actions { display: flex; align-self: end; align-items: center; gap: 8px; white-space: nowrap; }
.alarm-history-results { display: flex; flex-direction: column; flex: 1; min-height: 175px; min-width: 0; overflow: hidden; }
@container (max-width: 1190px) {
  .alarm-history-tools { grid-template-columns: repeat(4, minmax(0, 1fr)); }
  .alarm-history-time { grid-column: span 2; }
  .alarm-history-actions { grid-column: 1 / -1; justify-self: end; }
}
@container (max-width: 660px) { .alarm-history-tools { grid-template-columns: repeat(2, minmax(0, 1fr)); } .alarm-history-time { grid-column: auto; } }
@container (max-width: 430px) { .alarm-history-time { grid-column: span 2; } }
</style>

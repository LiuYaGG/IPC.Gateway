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
      <label>
        <span>{{ at('处理状态') }}</span>
        <a-select v-model:value="processingStatus" allow-clear :placeholder="at('全部处理状态')"
          :aria-label="at('处理状态')" :options="statusOptions" />
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
        row-key="eventId" :paging="false" :scroll-x="1950">
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'timestamp'">
            <a-button type="link" size="small" :title="at('报警详情')" @click="showDetail(record)">{{ displayTime(record.timestamp) }}</a-button>
          </template>
          <template v-else-if="column.key === 'path'">{{ path(record) }}</template>
          <template v-else-if="column.key === 'state'">{{ transition(record) }}</template>
          <template v-else-if="column.key === 'alarmStatus'">
            <a-tag v-if="record.alarmStatus === 'Active'" color="red">{{ at('报警中') }}</a-tag>
            <a-tag v-else-if="record.alarmStatus === 'Recovered'" color="green">{{ at('已恢复') }}</a-tag>
            <a-tag v-else-if="record.alarmStatus === 'Occurred'">{{ at('已发生') }}</a-tag>
            <span v-else :title="record.canProcess ? at('恢复状态未知') : undefined">—</span>
          </template>
          <template v-else-if="column.key === 'processingStatus'">
            <a-tag v-if="record.processingStatus" :color="statusColors[record.processingStatus as AlarmProcessingStatus]">
              {{ processingLabel(record.processingStatus) }}
            </a-tag>
            <template v-else>—</template>
          </template>
          <template v-else-if="column.key === 'recoveredAtUtc'">
            <span :title="record.recoveryObservedAfterRestart ? at('恢复时间为重启后首次观测到恢复的时间，实际恢复时刻未知。') : undefined">
              {{ record.recoveredAtUtc ? displayTime(record.recoveredAtUtc) : '—' }}
              <small v-if="record.recoveryObservedAfterRestart">{{ at('重启后首次观测') }}</small>
            </span>
          </template>
          <template v-else-if="column.key === 'duration'">{{ duration(record) }}</template>
          <template v-else-if="column.key === 'deliveryStatus'">{{ label(record.deliveryStatus) }}</template>
          <template v-else-if="column.key === 'message'">{{ record.message || '—' }}</template>
          <template v-else-if="['value','recoveryValue','threshold'].includes(column.key)">{{ record[column.key] ?? '—' }}</template>
          <template v-else-if="column.key === 'actions'">
            <a-button v-if="record.canProcess" type="link" size="small" @click="showDetail(record)">{{ at(rowActionLabel(record)) }}</a-button>
            <span v-else>—</span>
          </template>
        </template>
        <template #emptyText>
          <a-empty :description="tx(result ? '没有符合查询条件的报警记录' : '设置查询条件后点击查询')" />
        </template>
      </AutoHeightTable>
      <TablePager :current="page" :page-size="pageSize" :total="result?.total ?? 0"
        :disabled="loading || exporting || !result" @change="changePage" />
    </div>
    <a-modal v-model:open="detailOpen" centered width="560px" destroy-on-close
      :closable="!savingAction" :keyboard="!savingAction" :mask-closable="false"
      :title="at(actionMode === 'acknowledge' ? '确认报警' : actionMode === 'handle' ? '填写处理记录' : '报警详情')">
      <div v-if="detail" class="alarm-work-details">
        <dl class="alarm-work-context">
          <dt>{{ tx('报警来源') }}</dt><dd>{{ path(detail) }}</dd>
          <dt>{{ at('发生时间') }}</dt><dd>{{ displayTime(detail.timestamp) }}</dd>
          <dt>{{ tx('状态变化') }}</dt><dd>{{ transition(detail) }}</dd>
          <dt v-if="detail.recoveredAtUtc">{{ at('恢复时间') }}</dt><dd v-if="detail.recoveredAtUtc">{{ displayTime(detail.recoveredAtUtc) }}</dd>
          <dt v-if="detail.value != null">{{ tx('触发值') }}</dt><dd v-if="detail.value != null">{{ detail.value }}</dd>
          <dt v-if="detail.recoveryValue != null">{{ at('恢复值') }}</dt><dd v-if="detail.recoveryValue != null">{{ detail.recoveryValue }}</dd>
          <dt v-if="detail.recoveredAtUtc || detail.alarmStatus === 'Active'">{{ at('持续时长') }}</dt>
          <dd v-if="detail.recoveredAtUtc || detail.alarmStatus === 'Active'">{{ duration(detail) }}</dd>
          <dt v-if="detail.message">{{ tx('消息') }}</dt><dd v-if="detail.message" class="alarm-operator-note">{{ detail.message }}</dd>
        </dl>
        <a-alert v-if="detail.recoveryObservedAfterRestart" type="info" show-icon :message="at('恢复时间为重启后首次观测到恢复的时间，实际恢复时刻未知。')" />
        <a-timeline v-if="detail.acknowledged || detail.handled">
          <a-timeline-item v-if="detail.acknowledged" color="blue">
            <strong>{{ at('已确认') }}</strong> · {{ detail.acknowledged.operator }} · {{ displayTime(detail.acknowledged.timestampUtc) }}
            <div v-if="detail.acknowledged.note" class="alarm-operator-note">{{ detail.acknowledged.note }}</div>
          </a-timeline-item>
          <a-timeline-item v-if="detail.handled" color="green">
            <strong>{{ at('已处理') }}</strong> · {{ detail.handled.operator }} · {{ displayTime(detail.handled.timestampUtc) }}
            <div class="alarm-operator-note">{{ detail.handled.note }}</div>
          </a-timeline-item>
        </a-timeline>
        <label v-if="actionMode" class="alarm-work-note">
          <span>{{ at(actionMode === 'acknowledge' ? '确认备注（可选）' : '处理记录（必填）') }}</span>
          <a-textarea v-model:value="operatorNote" :rows="4" :maxlength="1000" :disabled="savingAction"
            :aria-required="actionMode === 'handle'" show-count />
        </label>
        <a-alert v-if="workflowError" type="error" show-icon :message="workflowError" />
      </div>
      <template #footer>
        <a-button :disabled="savingAction" @click="detailOpen = false">{{ tx(actionMode ? '取消' : '关闭') }}</a-button>
        <a-button v-if="actionMode" type="primary" :loading="savingAction"
          :disabled="actionMode === 'handle' && !operatorNote.trim()" @click="saveAction">
          {{ at(actionMode === 'acknowledge' ? '确认报警' : '保存处理记录') }}
        </a-button>
      </template>
    </a-modal>
  </section>
</template>

<script setup lang="ts">
import { computed, onActivated, onBeforeUnmount, onDeactivated, reactive, ref, shallowRef, watch } from 'vue'
import { DownloadOutlined, SearchOutlined } from '@ant-design/icons-vue'
import { exportAlarmHistory, loadAlarmHistoryPage, recordAlarmOperatorAction, type AlarmHistoryPage, type AlarmHistoryQuery, type AlarmHistoryRow,
  type AlarmProcessingStatus,
  type HistoryOptionLevel, type HistoryRecordFilters, type HistoryRecordOption } from '../../api'
import { locale, tx } from '../../i18n'
import { ruleStateText } from '../../i18n/ruleText'
import { alarmText as at } from '../../i18n/alarmProcessing'
import { PERMISSIONS, usePermissions } from '../../utils/permissions'
import { localExportFileName, saveExcelAs } from '../../utils/excelSave'
import AutoHeightTable from '../AutoHeightTable.vue'
import TablePager from '../controls/TablePager.vue'
import HistoryPointSelect from './HistoryPointSelect.vue'

const props = withDefaults(defineProps<{ active?: boolean }>(), { active: true })
const { hasPermission } = usePermissions()
const canExport = computed(() => hasPermission(PERMISSIONS.historyView) && hasPermission(PERMISSIONS.historyExport))
const canAcknowledge = computed(() => hasPermission(PERMISSIONS.alarmAcknowledge))
const canHandle = computed(() => hasPermission(PERMISSIONS.alarmHandle))
const processingStatus = ref<AlarmProcessingStatus>()
const statusLabels: Record<AlarmProcessingStatus, string> = { AutoMonitoring: '待自动恢复', AutoClosed: '已自动恢复', Recorded: '已记录', Pending: '待确认', Acknowledged: '已确认', Handled: '已处理' }
const statusColors: Record<AlarmProcessingStatus, string> = { AutoMonitoring: 'orange', AutoClosed: 'green', Recorded: 'default', Pending: 'orange', Acknowledged: 'blue', Handled: 'green' }
const statusOptions = computed(() => Object.entries(statusLabels).map(([value, label]) => ({ value, label: at(label) })))
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
const detail = shallowRef<AlarmHistoryRow>()
const detailOpen = ref(false)
const savingAction = ref(false)
const operatorNote = ref('')
const workflowError = ref('')
const actionMode = computed(() => detail.value ? rowAction(detail.value) : undefined)
let queryController: AbortController | undefined
let exportController: AbortController | undefined
const columns = computed(() => [
  { title: at('发生时间'), key: 'timestamp', width: 185 },
  { title: tx('报警来源'), key: 'path', width: 250 },
  { title: tx('状态变化'), key: 'state', width: 180 },
  { title: at('报警状态'), key: 'alarmStatus', width: 120 },
  { title: at('处理状态'), key: 'processingStatus', width: 160 },
  { title: at('恢复时间'), key: 'recoveredAtUtc', width: 185 },
  { title: at('持续时长'), key: 'duration', width: 200 },
  { title: tx('触发值'), key: 'value', width: 100 },
  { title: at('恢复值'), key: 'recoveryValue', width: 100 },
  { title: tx('阈值'), key: 'threshold', width: 90 },
  { title: tx('消息'), key: 'message', width: 230 },
  { title: tx('发送状态'), key: 'deliveryStatus', width: 120 },
  { title: tx('操作'), key: 'actions', width: 135, fixed: 'right' as const },
])
function label(code: string) {
  if (code === 'PerAction') return at('按动作配置')
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
function processingLabel(status: AlarmProcessingStatus) {
  return at(statusLabels[status] ?? status)
}
function displayTime(timestamp: string) {
  return new Date(timestamp).toLocaleString(locale.value)
}
function duration(record: AlarmHistoryRow) {
  const end = record.recoveredAtUtc || (record.alarmStatus === 'Active' ? result.value?.queriedAtUtc : null)
  if (!end) return '—'
  const ms = Date.parse(end) - Date.parse(record.timestamp)
  if (!Number.isFinite(ms) || ms < 0) return '—'
  const seconds = record.recoveryObservedAfterRestart ? Math.ceil(ms / 1000) : Math.floor(ms / 1000)
  const pad = (n: number) => String(n).padStart(2, '0')
  const elapsed = ms < 1000 ? ms + ' ms' : [Math.floor(seconds / 3600), Math.floor(seconds / 60) % 60, seconds % 60].map(pad).join(':')
  if (record.recoveryObservedAfterRestart) return '≤ ' + elapsed
  return record.recoveredAtUtc ? elapsed : elapsed + ' · ' + at('截至本次查询')
}
function rowAction(record: AlarmHistoryRow) {
  if (record.canProcess && record.processingStatus === 'Pending' && canAcknowledge.value) return 'acknowledge'
  if (record.canProcess && record.processingStatus === 'Acknowledged' && canHandle.value) return 'handle'
}
function rowActionLabel(record: AlarmHistoryRow) {
  const action = rowAction(record)
  return action === 'acknowledge' ? '确认' : action === 'handle' ? '填写处理' : '记录'
}
function showDetail(record: AlarmHistoryRow) {
  detail.value = record
  operatorNote.value = ''
  workflowError.value = ''
  detailOpen.value = true
}
async function saveAction() {
  const record = detail.value
  const action = actionMode.value
  const note = operatorNote.value.trim()
  if (!record || !action || savingAction.value || (action === 'handle' && !note)) return
  savingAction.value = true
  workflowError.value = ''
  try {
    await recordAlarmOperatorAction(record.eventId, action, note, locale.value)
    detailOpen.value = false
    if (applied.value && props.active && !deactivated.value) await load(page.value, pageSize.value, applied.value)
    else resetResult()
  } catch (cause) {
    workflowError.value = cause instanceof Error ? at(cause.message) : at('保存报警记录失败')
  } finally {
    savingAction.value = false
  }
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
watch([from, to, processingStatus, () => selected.channel?.id, () => selected.device?.id, () => selected.group?.id, () => selected.tag?.id], resetResult, { flush: 'sync' })
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
    ...ancestors('tag'), tagId: selected.tag?.id, processingStatus: processingStatus.value,
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
    // A later page still represents the first page's snapshot, including alarms that
    // were active at that time and may already have recovered in the live journal.
    if (filters.snapshot != null && result.value?.snapshot === next.snapshot && result.value.queriedAtUtc)
      next.queriedAtUtc = result.value.queriedAtUtc
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
.alarm-history-tools { display: grid; grid-template-columns: repeat(2, minmax(198px, 1.3fr)) repeat(4, minmax(110px, 1fr)) minmax(145px, 1fr) auto; gap: 10px; align-items: start; flex-shrink: 0; }
.alarm-history-tools > label { display: flex; flex-direction: column; gap: 5px; min-width: 0; }
.alarm-history-tools .ant-picker { width: 100%; min-width: 0; }
.alarm-history-actions { display: flex; align-self: end; align-items: center; gap: 8px; white-space: nowrap; }
.alarm-history-results { display: flex; flex-direction: column; flex: 1; min-height: 175px; min-width: 0; overflow: hidden; }
.alarm-work-details { display: grid; gap: 12px; min-width: 0; max-height: min(65dvh, 650px); overflow: auto; padding: 2px; }
.alarm-work-context { display: grid; grid-template-columns: auto minmax(0, 1fr); align-items: start; gap: 7px 12px; margin: 0; }
.alarm-work-context dt { color: var(--gateway-muted); }
.alarm-work-context dd { margin: 0; overflow-wrap: anywhere; }
.alarm-work-note { display: grid; gap: 5px; }
.alarm-operator-note { white-space: pre-wrap; overflow-wrap: anywhere; }
.alarm-history-results small { display:block; }
@container (max-width: 1320px) {
  .alarm-history-tools { grid-template-columns: repeat(4, minmax(0, 1fr)); }
  .alarm-history-time { grid-column: span 2; }
  .alarm-history-actions { grid-column: span 3; justify-self: end; }
}
@container (max-width: 660px) { .alarm-history-tools { grid-template-columns: repeat(2, minmax(0, 1fr)); } .alarm-history-time { grid-column: auto; } .alarm-history-actions { grid-column: 1 / -1; } }
@container (max-width: 430px) { .alarm-history-time { grid-column: span 2; } }
</style>

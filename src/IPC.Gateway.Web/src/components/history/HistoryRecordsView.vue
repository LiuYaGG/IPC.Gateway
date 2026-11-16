<template>
  <section class="history-records">
    <form class="history-filters" @submit.prevent="submit">
      <label v-for="field in fields" :key="field.level">
        <span>{{ tx(field.label) }}</span>
        <HistoryPointSelect :level="field.level" :label="tx(field.label)" :filters="ancestors(field.level)"
          :selected="selected[field.level]" :disabled="field.parent && !selected[field.parent]"
          :active="active" @change="select(field.level, $event)" />
      </label>
      <div class="history-range">
        <span>{{ tx('时间范围') }}</span>
        <a-range-picker v-model:value="range" show-time value-format="YYYY-MM-DDTHH:mm:ss"
          format="YYYY-MM-DD HH:mm:ss" :placeholder="[tx('开始时间'), tx('结束时间')]" />
      </div>
      <label>
        <span>{{ tx('质量') }}</span>
        <a-select v-model:value="quality" :aria-label="tx('质量')" allow-clear
          :placeholder="tx('全部')" :options="qualities" />
      </label>
      <div class="history-actions">
        <a-button v-if="loading" @click="cancelQuery">{{ tx('取消查询') }}</a-button>
        <a-button v-else type="primary" html-type="submit" :disabled="!valid">{{ tx('查询') }}</a-button>
        <a-button v-if="canExport" :disabled="!result?.exportToken || loading" :loading="exporting"
          @click="downloadPage">{{ tx('导出本页') }}</a-button>
      </div>
    </form>
    <a-alert v-if="enabled === false" type="info" show-icon :message="tx('历史记录已关闭，仍可查询已保存的数据。')" />
    <a-alert v-if="error" type="error" show-icon :message="tx(error)" />
    <div class="history-results">
      <AutoHeightTable resize-key="history-records" :scroll-x="1100" :paging="false" :columns="columns"
        :data-source="rows" :loading="loading" row-key="rowKey">
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'timestamp'">{{ displayTime(record.timestamp) }}</template>
          <template v-else-if="column.key === 'quality'">{{ tx(qualityText(record.quality)) }}</template>
          <template v-else-if="column.key === 'valueText' || column.key === 'rawValueText'">
            <a-button type="link" class="history-value" :title="tx('查看完整值')" @click="detail = record">
              {{ preview(column.key === 'valueText' ? record.valueText : rawValue(record)) }}
            </a-button>
          </template>
          <template v-else-if="column.key === 'unit'">{{ record.unit || '—' }}</template>
          <template v-else-if="column.key === 'errorMessage'">{{ tx(record.errorMessage || '') || '—' }}</template>
        </template>
        <template #emptyText>
          <a-empty :description="tx(result ? '所选时段没有匹配的历史记录' : '请选择点位和时间范围后查询')" />
        </template>
      </AutoHeightTable>
      <TablePager :total="result?.total ?? 0" :current="result?.page ?? 1" :page-size="pageSize" :disabled="loading || !result"
        @change="changePage" />
    </div>
    <a-modal :open="!!detail" centered :title="tx('历史记录详情')" :footer="null" width="min(820px, 94vw)" @cancel="detail = undefined">
      <div v-if="detail" class="history-detail">
        <strong>{{ detail.path }}</strong>
        <div>{{ displayTime(detail.timestamp) }} · {{ tx(qualityText(detail.quality)) }} · {{ detail.dataType || '—' }}</div>
        <h4>{{ tx('工程值') }}</h4><pre>{{ fullValue(detail.valueText) }}</pre>
        <h4>{{ tx('原始值') }}</h4><pre>{{ fullValue(rawValue(detail)) }}</pre>
        <template v-if="detail.edgeProcessingType"><h4>{{ tx('处理方式') }}</h4>{{ tx(processingLabels[detail.edgeProcessingType] ?? detail.edgeProcessingType) }}</template>
        <template v-for="(value, key) in detail.details" :key="key">
          <h4>{{ tx(detailLabels[key] || key) }}</h4>{{ detailText(key, value) }}
        </template>
        <template v-if="detail.errorMessage"><h4>{{ tx('最近错误') }}</h4>{{ tx(detail.errorMessage) }}</template>
      </div>
    </a-modal>
  </section>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onDeactivated, reactive, ref, shallowRef, watch } from 'vue'
import { exportHistoryPage, HttpRequestError, queryHistoryRecords, type HistoryOptionLevel, type HistoryRecord,
  type HistoryRecordFilters, type HistoryRecordOption, type HistoryRecordsPage, type HistoryRecordsQuery } from '../../api'
import { locale, tx } from '../../i18n'
import { PERMISSIONS, usePermissions } from '../../utils/permissions'
import { qualityText, tagQualityValues } from '../../utils/display'
import AutoHeightTable from '../AutoHeightTable.vue'
import TablePager from '../controls/TablePager.vue'
import HistoryPointSelect from './HistoryPointSelect.vue'
import { historyQueryError } from './historyQueryError'
import { localExportFileName, saveExcelAs } from '../../utils/excelSave'

const props = defineProps<{ enabled?: boolean; active: boolean }>()
const { hasPermission } = usePermissions()
const canExport = computed(() => hasPermission(PERMISSIONS.historyView) && hasPermission(PERMISSIONS.historyExport))
const fields: { level: HistoryOptionLevel; label: string; parent?: HistoryOptionLevel }[] = [
  { level: 'channel', label: '通道' }, { level: 'device', label: '设备', parent: 'channel' },
  { level: 'group', label: '分组', parent: 'device' }, { level: 'tag', label: '标签', parent: 'group' }
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
const range = ref<[string, string] | null>([localInputTime(new Date(+now - 3600000)), localInputTime(now)])
const quality = ref<string>()
const qualities = computed(() => tagQualityValues.map(value => ({ value, label: tx(qualityText(value)) })))
const valid = computed(() => !!selected.tag && !!range.value?.[1] && Date.parse(range.value[0]) < Date.parse(range.value[1]))
const columns = computed(() => [
  { key: 'timestamp', title: tx('采样时间'), width: 206 },
  { key: 'path', dataIndex: 'path', title: tx('点位路径'), width: 290, ellipsis: true },
  { key: 'valueText', title: tx('工程值'), width: 176, ellipsis: true },
  { key: 'rawValueText', title: tx('原始值'), width: 176, ellipsis: true },
  { key: 'quality', title: tx('质量'), width: 104 },
  { key: 'dataType', dataIndex: 'dataType', title: tx('数据类型'), width: 116 },
  { key: 'unit', title: tx('单位'), width: 92 },
  { key: 'errorMessage', title: tx('最近错误'), width: 220, ellipsis: true }
])
const clock = computed(() => new Intl.DateTimeFormat(locale.value, {
  year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit',
  second: '2-digit', fractionalSecondDigits: 3, hour12: false
}))
function displayTime(text: string) { return clock.value.format(new Date(text)) }
function rawValue(row: HistoryRecord) { return row.rawValueText ?? row.valueText }
function fullValue(value: string | null | undefined) { return value == null ? '—' : value === '' ? '""' : value }
function preview(value: string | null | undefined) {
  const text = fullValue(value)
  return text.length > 120 ? text.slice(0, 120) + '…' : text
}

const result = shallowRef<HistoryRecordsPage>()
const rows = computed(() => result.value?.items.map((row, rowKey) => ({ ...row, rowKey })) ?? [])
const detail = shallowRef<HistoryRecord>()
const processingLabels: Record<string, string> = { raw: '原始数据', aligned: '时间对齐', fill: '补点', aggregate: '聚合' }
const detailLabels: Record<string, string> = {
  cleaningApplied: '已执行清洗', cleaningAction: '清洗动作', cleaningMessage: '清洗说明',
  edgeProcessingReason: '处理原因', edgeAggregateMethod: '聚合方法', edgeSampleCount: '样本数',
  edgeOriginalTimestamp: '原始采样时间', edgeWindowStart: '窗口开始时间', edgeWindowEnd: '窗口结束时间'
}
function detailText(key: string, value: unknown) {
  if (typeof value === 'boolean') return tx(value ? '是' : '否')
  if (typeof value === 'string' && ['edgeOriginalTimestamp', 'edgeWindowStart', 'edgeWindowEnd'].includes(key)
    && Number.isFinite(Date.parse(value))) return displayTime(value)
  return tx(String(value ?? '—'))
}
const loading = ref(false)
const exporting = ref(false)
const error = ref('')
const pageSize = ref(20)
let controller: AbortController | undefined
let downloadController: AbortController | undefined

function cancelQuery() {
  controller?.abort()
  loading.value = false
}
function cancelExport() {
  downloadController?.abort()
  exporting.value = false
}
function resetResult() {
  cancelQuery()
  cancelExport()
  result.value = undefined
  detail.value = undefined
  error.value = ''
}
watch([() => selected.tag?.id, range, quality, () => selected.channel?.id, () => selected.device?.id, () => selected.group?.id], resetResult)
watch(() => props.active, active => {
  if (!active) { cancelQuery(); cancelExport(); detail.value = undefined }
})
onBeforeUnmount(() => { cancelQuery(); cancelExport() })
onDeactivated(() => { cancelQuery(); cancelExport() })

async function load(page: number, size: number, snapshot?: string) {
  if (!valid.value || !range.value || !selected.tag) return
  cancelQuery()
  cancelExport()
  const current = controller = new AbortController()
  error.value = ''
  loading.value = true
  const query: HistoryRecordsQuery = {
    ...ancestors('tag'), tagId: selected.tag.id, quality: quality.value,
    from: new Date(range.value[0]).toISOString(), to: new Date(range.value[1]).toISOString(),
    page, pageSize: size, snapshot
  }
  try {
    const next = await queryHistoryRecords(query, locale.value, current.signal)
    if (current.signal.aborted) return
    result.value = next
    pageSize.value = size
  } catch (cause) {
    if (current.signal.aborted) return
    if (cause instanceof HttpRequestError && cause.code === 'HISTORY_SNAPSHOT_EXPIRED') result.value = undefined
    error.value = historyQueryError(cause, '历史查询失败')
  } finally {
    if (!current.signal.aborted) loading.value = false
  }
}
function submit() { void load(1, pageSize.value) }
function changePage(page: number, size: number) {
  if (result.value) void load(page, size, result.value.snapshot)
}
async function downloadPage() {
  const token = result.value?.exportToken
  if (!token || !canExport.value || exporting.value) return
  const current = downloadController = new AbortController()
  exporting.value = true
  error.value = ''
  const language = locale.value
  try {
    await saveExcelAs(localExportFileName('point-history', result.value!.page),
      () => exportHistoryPage(token, language, current.signal), current.signal)
  } catch (cause) {
    if (!current.signal.aborted) error.value = historyQueryError(cause, '导出本页失败')
  } finally {
    if (!current.signal.aborted) exporting.value = false
  }
}
</script>

<style scoped>
.history-records, .history-results { display: flex; flex-direction: column; min-height: 0; }
.history-records { height: 100%; gap: 10px; container-type: inline-size; }
.history-filters, .history-results { border: 1px solid var(--gateway-border); border-radius: 8px; background: white; padding: 12px; }
.history-filters {
  display: grid;
  grid-template-columns: repeat(4, minmax(100px, 1fr)) minmax(306px, 2.4fr) minmax(94px, .7fr) auto;
  gap: 10px;
  align-items: start;
}
.history-filters > label, .history-range { display: flex; flex-direction: column; min-width: 0; gap: 5px; }
.history-actions { display: flex; gap: 6px; align-self: end; white-space: nowrap; }
.history-results { flex: 1; min-height: 175px; overflow: hidden; }
.history-value { max-width: 100%; padding: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; height: auto; }
.history-detail { max-height: 65vh; overflow: auto; overflow-wrap: anywhere; }
.history-detail h4 { margin: 14px 0 6px; }
.history-detail pre { white-space: pre-wrap; overflow-wrap: anywhere; padding: 10px; margin: 0; background: #f4f7fa; }
@container (max-width: 1190px) {
  .history-filters { grid-template-columns: repeat(4, minmax(0, 1fr)); }
  .history-range { grid-column: span 2; }
}
@container (max-width: 660px) {
  .history-filters { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}
</style>

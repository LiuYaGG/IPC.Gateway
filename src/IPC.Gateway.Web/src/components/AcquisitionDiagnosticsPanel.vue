<template>
  <section class="acquisition-diagnostics" :aria-label="tx('采集诊断')">
    <div class="diag-heading">
      <div>
        <h3>{{ tx('采集周期达标情况') }}</h3>
        <small>{{ tx('最近 5 分钟 · 容差 100 ms') }}
          <template v-if="report?.observedSinceUtc"> · {{ tx('观测起点') }} {{ time(report.observedSinceUtc) }}</template>
        </small>
      </div>
      <label class="period-picker">{{ tx('有效采集周期') }}
        <a-select v-model:value="period" size="small" :options="periodOptions" />
      </label>
    </div>
    <a-alert v-if="loadError" type="error" show-icon :message="tx(loadError)" />
    <div class="diag-metrics">
      <button type="button" :aria-pressed="sort === 'misses'" :title="tx('按未按时次数查看排行')" @click="showRanking('misses')">
        <span>{{ tx('按时成功率') }} <ArrowDownOutlined /></span><strong>{{ report?.onTimePercent == null ? '—' : report.onTimePercent.toFixed(1) + '%' }}</strong>
        <small>{{ tx('按时 / 应采') }}　{{ count(report?.onTime) }} / {{ count(report?.expected) }}</small></button>
      <button type="button" :aria-pressed="sort === 'gap'" :title="tx('按最长断档查看排行')" @click="showRanking('gap')">
        <span>{{ tx('最长无有效样本') }} <ArrowDownOutlined /></span><strong>{{ ms(report?.maxGapMs) }}</strong>
        <small class="metric-tag" :title="longestTag">{{ longestTag || tx('包含窗口内断档') }}</small></button>
      <div><span>{{ tx('刷新间隔 P95') }}</span><strong>{{ ms(report?.p95GapMs) }}</strong><small>{{ tx('成功样本的间隔') }}</small></div>
      <div><span>{{ tx('当前隔离点') }}</span><strong>{{ count(report?.isolatedCount) }}</strong><small>{{ tx('等待独立恢复探测') }}</small></div>
    </div>
    <p class="diag-scope">{{ tx('首次读取成功后开始统计，独立恢复探测期间暂停计入周期。') }}
      <template v-if="report">{{ tx('迟到') }} {{ count(report.late) }} · {{ tx('读取失败') }} {{ count(report.failed) }} · {{ tx('漏采') }} {{ count(report.missed) }}</template>
    </p>
    <section ref="rankingPanel" class="diag-box slow-ranking" tabindex="-1" :aria-label="tx('慢点排行')">
      <header>
        <div><h3>{{ tx('慢点排行') }}</h3>
          <small>{{ tx(sort === 'gap' ? '按最长断档从长到短' : '按未按时次数从多到少') }}</small></div>
        <a-radio-group v-model:value="sort" size="small" button-style="solid" :aria-label="tx('排行口径')">
          <a-radio-button value="gap">{{ tx('最长断档') }}</a-radio-button>
          <a-radio-button value="misses">{{ tx('未按时次数') }}</a-radio-button>
        </a-radio-group>
      </header>
      <a-table size="small" :columns="rankingColumns" :data-source="slowRows" :row-key="slowRowKey" :pagination="false"
        :loading="!loadError && !!report && loadedSort !== sort" :scroll="{ x: rankingWidth, y: 290 }">
        <template #bodyCell="{ column, record, index }">
          <span v-if="column.key === 'rank'" class="rank-number">{{ index + 1 }}</span>
          <div v-else-if="column.key === 'tag'" class="rank-tag">
            <b :title="tagName(record.tagId)">{{ tagName(record.tagId) }}</b>
            <small :title="groupName(record.tagId)">{{ groupName(record.tagId) }}</small>
            <small v-if="tableSpace < 680" :title="tagAddress(record.tagId)">{{ tagAddress(record.tagId) }} · {{ ms(record.periodMs) }}</small>
          </div>
          <span v-else-if="column.key === 'address'" :title="tagAddress(record.tagId)">{{ tagAddress(record.tagId) }}</span>
          <template v-else-if="column.key === 'period'">{{ ms(record.periodMs) }}</template>
          <span v-else-if="column.key === 'gap'" :class="{ 'rank-measure': sort === 'gap' }">{{ ms(record.maxGapMs) }}</span>
          <span v-else-if="column.key === 'misses'" :class="{ 'rank-measure': sort === 'misses' }">{{ count(offSchedule(record)) }} / {{ count(record.expected) }}</span>
          <a-button v-else-if="column.key === 'detail'" size="small" type="link" @click="openDetail(record)">{{ tx('详情') }}</a-button>
        </template>
        <template #emptyText><a-empty :description="tx('暂无点位采集统计')" /></template>
      </a-table>
      <div class="rank-footer"><small>{{ tx('最多显示前 20 项') }}</small><small>{{ tx('未按时 = 迟到 + 读取失败 + 漏采') }}</small></div>
    </section>
    <div class="diag-columns">
      <section class="diag-box">
        <header><h3>{{ tx('批读影响范围') }}</h3><small>{{ tx('整台设备 · 最近读取记录') }}</small></header>
        <a-table size="small" :columns="columns" :data-source="report?.batches ?? []" :pagination="false"
          :row-key="batchKey" :scroll="{ x: 610 }">
          <template #bodyCell="{ column, record }">
            <template v-if="column.key === 'time'">{{ time(record.timestampUtc) }}</template>
            <template v-else-if="column.key === 'range'">{{ record.firstAddress }}<template v-if="record.lastAddress && record.lastAddress !== record.firstAddress"> … {{ record.lastAddress }}</template></template>
            <template v-else-if="column.key === 'mode'">{{ tx(batchModes[record.mode] ?? record.mode) }}</template>
            <template v-else-if="column.key === 'results'">{{ record.successCount }} / {{ record.failedCount }} / {{ record.deferredCount }}</template>
            <template v-else-if="column.key === 'elapsed'">{{ ms(record.elapsedMs) }}</template>
          </template>
        </a-table>
      </section>
      <section class="diag-box">
        <header><h3>{{ tx('采集事件') }}</h3><small>{{ tx('整台设备') }}</small></header>
        <ul v-if="report?.events.length" class="diag-events">
          <li v-for="(event, index) in report.events" :key="index">
            <time>{{ time(event.timestampUtc) }}</time>
            <div><b>{{ tx(eventKinds[event.kind] ?? event.kind) }}</b> {{ event.address }}
              <p v-if="event.message">{{ tx(event.message) }}</p></div>
          </li>
        </ul>
        <a-empty v-else :description="tx('暂无采集事件')" />
      </section>
    </div>
    <section ref="tracePanel" tabindex="-1" class="diag-box">
      <header><h3>{{ tx('单点跟踪') }}</h3>
        <div class="trace-controls">
          <a-select v-model:value="tagId" show-search option-filter-prop="label" size="small" :disabled="starting || !!ownedTag"
            :aria-label="tx('选择跟踪点位')" :placeholder="tx('选择跟踪点位')" :options="tags" />
          <a-button size="small" :disabled="!tagId || !!ownedTag" :loading="starting" @click="startTrace">{{ tx(ownedTag ? '跟踪中' : '跟踪 60 秒') }}</a-button>
        </div>
      </header>
      <a-alert v-if="traceError" type="error" show-icon :message="tx(traceError)" />
      <div class="diag-stages">
        <div><span>{{ tx('采集排队') }}</span><b>{{ ms(trace?.queueMs) }}</b></div>
        <div><span>{{ tx('驱动读取') }}</span><b>{{ ms(trace?.readMs) }}</b></div>
        <div><span>{{ tx('后端推送排队') }}</span><b>{{ ms(pushed?.pushMs) }}</b></div>
        <div><span>{{ tx('浏览器更新') }}</span><b>{{ ms(pushed?.domMs) }}</b></div>
      </div>
      <p class="diag-scope"><template v-if="finished">{{ tx('跟踪已结束') }} · </template>{{ tx('最近采集样本') }} {{ sampleTime(trace?.sampleUtc) }} · {{ tx('最近推送样本') }} {{ sampleTime(pushed?.timestamp) }}
        <template v-if="pushed"> · {{ tx('样本值') }} {{ pushed.value }}</template></p>
      <p class="diag-scope">{{ tx('未观测到的阶段显示为 —。浏览器耗时从收到实时推送计至视图更新，网络传输耗时未计入。') }}</p>
    </section>
    <a-modal v-model:open="detailOpen" :title="tx('点位采集明细')" width="590px" centered destroy-on-close :after-close="afterDetailClose">
      <div v-if="detail" class="rank-detail">
        <div><h3>{{ detail.name }}</h3><small class="detail-path">{{ device.name }} / {{ detail.group }}</small></div>
        <div class="detail-meta"><span>{{ tx('地址') }}　{{ detail.address }}</span>
          <span>{{ tx('有效采集周期') }}　{{ ms(detail.row.periodMs) }}</span>
          <span>{{ tx('未按时 / 应采') }}　{{ count(offSchedule(detail.row)) }} / {{ count(detail.row.expected) }}</span></div>
        <dl class="detail-counts">
          <div><dt>{{ tx('按时成功') }}</dt><dd>{{ count(detail.row.onTime) }}</dd></div>
          <div><dt>{{ tx('迟到') }}</dt><dd>{{ count(detail.row.late) }}</dd></div>
          <div><dt>{{ tx('读取失败') }}</dt><dd>{{ count(detail.row.failed) }}</dd></div>
          <div><dt>{{ tx('漏采') }}</dt><dd>{{ count(detail.row.missed) }}</dd></div>
        </dl>
        <section class="detail-gap">
          <div class="gap-heading"><h3>{{ tx('最长断档') }}</h3><strong>{{ ms(detail.row.maxGapMs) }}</strong>
            <a-tag v-if="detail.row.gapOngoing" color="orange">{{ tx('仍在断档') }}</a-tag>
            <a-tag v-else-if="detail.row.gapEndReason === 'Sample'" color="green">{{ tx('已恢复') }}</a-tag>
            <a-tag v-else-if="detail.row.gapEndReason === 'ReadFailure'" color="red">{{ tx('截至读取失败') }}</a-tag>
          </div>
          <template v-if="detail.row.gapStartUtc">
            <div>{{ tx('开始时间') }}　{{ localStamp(detail.row.gapStartUtc) }}</div>
            <div>{{ tx(detail.row.gapOngoing ? '统计时刻' : '结束时间') }}　{{ localStamp(detail.endUtc) }}</div>
          </template>
          <div v-if="detail.row.gapError" class="detail-read-error">
            <strong>{{ tx('断档结束时的读取错误') }}</strong><p>{{ detail.row.gapError }}</p>
          </div>
        </section>
        <section v-if="detail.row.gapStartUtc">
          <h3>{{ tx('与断档重叠的设备连接异常') }}</h3>
          <ul v-if="detail.incidents.length" class="detail-incidents">
            <li v-for="incident in detail.incidents" :key="incident.startedUtc">
              <div>{{ tx('设备连接失败') }}　{{ localStamp(incident.startedUtc) }}</div>
              <div>{{ incident.recoveredUtc ? tx('设备恢复连接') + '　' + localStamp(incident.recoveredUtc) : tx('截至统计时刻尚未恢复连接') }}</div>
              <small v-if="incident.message">{{ tx(incident.message) }}</small></li>
          </ul>
          <small>{{ tx(detail.incidents.length ? '时间重叠仅供排查。' : '最近的设备连接记录中没有与该断档重叠的异常。') }}</small>
        </section>
        <small>{{ tx('打开明细时的统计') }} · {{ localStamp(detail.observedAtUtc) }}</small>
      </div>
      <template #footer>
        <a-button @click="detailOpen = false">{{ tx('关闭') }}</a-button>
        <a-button type="primary" :disabled="!detail || !findTag(detail.row.tagId)?.traceable || starting || !!ownedTag"
          @click="traceDetail">{{ tx('跟踪 60 秒') }}</a-button>
      </template>
    </a-modal>
  </section>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'
import { ArrowDownOutlined } from '@ant-design/icons-vue'
import { request, type ApiResult, type DeviceConfig } from '../api'
import { locale, tx } from '../i18n'
import { browserTrace, observeBrowserTrace } from '../utils/acquisitionBrowserTrace'

interface Batch {
  timestampUtc: string; firstAddress: string; lastAddress: string; mode: string; tagCount: number
  requestCount: number; successCount: number; failedCount: number; deferredCount: number; elapsedMs: number
}
interface Trace { tagId: string; expiresUtc: string; sampleUtc?: string; queueMs?: number; readMs?: number }
type RankingSort = 'gap' | 'misses'
interface SlowTag {
  tagId: string; periodMs: number; expected: number; onTime: number; late: number; failed: number; missed: number
  maxGapMs?: number; gapStartUtc?: string; gapEndUtc?: string; gapOngoing: boolean; gapEndReason?: 'Sample' | 'ReadFailure'; gapError?: string
}
interface AcquisitionEvent { timestampUtc: string; kind: string; address: string; message: string }
interface ConnectionIncident { startedUtc: string; recoveredUtc?: string | null; message: string }
interface TagDetail {
  row: SlowTag; name: string; group: string; address: string; endUtc?: string; observedAtUtc: string; incidents: ConnectionIncident[]
}
interface Report {
  observedAtUtc: string; connectionIncidents: ConnectionIncident[]
  observedSinceUtc?: string; periods: number[]; expected: number; onTime: number; late: number; failed: number
  missed: number; onTimePercent?: number; maxGapMs?: number; p95GapMs?: number; isolatedCount: number
  slowTags: SlowTag[]; batches: Batch[]; events: AcquisitionEvent[]; trace?: Trace
}
const props = defineProps<{ device: DeviceConfig; active: boolean }>()
const report = shallowRef<Report>()
const period = ref(0)
const sort = ref<RankingSort>('gap')
const loadedSort = ref<RankingSort>()
const rankingPanel = ref<HTMLElement>()
const tracePanel = ref<HTMLElement>()
const tableSpace = ref(900)
const detailOpen = ref(false)
const detail = shallowRef<TagDetail>()
let scrollToTrace = false
let rankingObserver: ResizeObserver | undefined
const tagId = ref<string>()
const ownedTag = ref<string>()
const loadError = ref('')
const traceError = ref('')
const starting = ref(false)
const trace = shallowRef<Trace>()
const finished = ref(false)
const savedPush = shallowRef<typeof browserTrace.value>()
const pushed = computed(() => ownedTag.value ? browserTrace.value : savedPush.value)
const periodOptions = computed(() => [
  { label: tx('全部周期'), value: 0 },
  ...Array.from(new Set([...(report.value?.periods ?? []), ...(period.value ? [period.value] : [])]))
    .sort((a, b) => a - b).map(ms => ({ label: ms + ' ms', value: ms }))
])
const deviceTags = computed(() => {
  const map = new Map<string, { id: string; name: string; group: string; address: string; traceable: boolean }>()
  function add(list: DeviceConfig['tags'], group: string, enabled: boolean) {
    for (const tag of list) map.set(tag.id.toLowerCase(), { id: tag.id, name: tag.name, group, address: tag.address,
      traceable: enabled && tag.enabled && tag.accessMode !== 'WriteOnly' && !tag.isVirtual })
  }
  add(props.device.tags ?? [], '', true)
  for (const group of props.device.groups ?? []) add(group.tags ?? [], group.name, group.enabled)
  return map
})
const tags = computed(() => Array.from(deviceTags.value.values()).filter(tag => tag.traceable)
  .map(tag => ({ value: tag.id, label: (tag.group ? tag.group + ' / ' : '') + tag.name + ' · ' + tag.address })))
const findTag = (id: string) => deviceTags.value.get(id.toLowerCase())
const tagName = (id: string) => findTag(id)?.name || id
const tagAddress = (id: string) => findTag(id)?.address || '—'
const groupName = (id: string) => {
  const tag = findTag(id)
  return tag ? tag.group || tx('直属标签') : '—'
}
const offSchedule = (row: SlowTag) => row.late + row.failed + row.missed
const slowRowKey = (row: SlowTag) => row.tagId + '/' + row.periodMs
const slowRows = computed(() => loadedSort.value === sort.value ? report.value?.slowTags ?? [] : [])
const longestTag = computed(() => {
  const first = slowRows.value[0]
  return sort.value === 'gap' && first?.maxGapMs != null ? tagName(first.tagId) + ' · ' + groupName(first.tagId) : ''
})
const rankingColumns = computed(() => {
  const compact = tableSpace.value < 470
  const showAddress = tableSpace.value >= 680
  return [
    { key: 'rank', title: tx('排名'), width: 46 },
    { key: 'tag', title: tx('点位') },
    ...(showAddress ? [{ key: 'address', title: tx('地址'), width: 112, ellipsis: true }, { key: 'period', title: tx('周期'), width: 90 }] : []),
    ...(!compact || sort.value === 'gap' ? [{ key: 'gap', title: tx('最长断档'), width: compact ? 96 : 118 }] : []),
    ...(!compact || sort.value === 'misses' ? [{ key: 'misses', title: tx('未按时 / 应采'), width: compact ? 100 : 133 }] : []),
    { key: 'detail', title: tx('操作'), width: 70, fixed: 'right' as const }
  ]
})
const rankingWidth = computed(() => rankingColumns.value.reduce((w, column) => w + (column.width ?? 100), 0))
const batchModes: Record<string, string> = { Batch: '批量读取', RecoveryBatch: '恢复批读', Single: '单点读取', Probe: '恢复探测' }
const eventKinds: Record<string, string> = { DeviceConnectionFailure: '设备连接失败', DeviceConnectionRecovered: '设备恢复连接',
  TagIsolated: '点位已隔离', TagProbePending: '等待恢复探测', TagRecovered: '点位已恢复' }
const columns = computed(() => [
  { key: 'time', title: tx('时间'), width: 95 }, { key: 'range', title: tx('地址范围'), ellipsis: true },
  { key: 'mode', title: tx('读取方式'), width: 90 }, { dataIndex: 'tagCount', title: tx('点数'), width: 54 },
  { key: 'results', title: tx('成功 / 失败 / 延后'), width: 155 }, { key: 'elapsed', title: tx('耗时'), width: 86 }
])
const batchKey = (row: Batch, index?: number) => [row.timestampUtc, row.firstAddress, index].join('/')
const count = (n?: number) => n == null ? '—' : n.toLocaleString(locale.value)
const ms = (n?: number) => n == null ? '—' : n.toLocaleString(locale.value, { maximumFractionDigits: 1 }) + ' ms'
const time = (s?: string) => s ? new Date(s).toLocaleTimeString(locale.value, { hour12: false }) : '—'
const sampleTime = (s?: string) => s ? time(s) + '.' + String(new Date(s).getMilliseconds()).padStart(3, '0') : '—'
const localStamp = (s?: string) => s ? new Date(s).toLocaleDateString(locale.value) + ' ' + sampleTime(s) : '—'
const endpoint = () => '/api/diagnostics/acquisition/' + encodeURIComponent(props.device.id)
let pollTimer: number | undefined
let traceTimer: number | undefined
let pollRequest: AbortController | undefined
let traceRequest: AbortController | undefined
let stopBrowserTrace: (() => void) | undefined
let mounted = false

async function poll() {
  window.clearTimeout(pollTimer)
  pollRequest?.abort()
  if (!mounted || !props.active || document.hidden) return
  const ctl = new AbortController()
  pollRequest = ctl
  const requestedSort = sort.value
  const query = new URLSearchParams({ slowSort: requestedSort })
  if (period.value) query.set('periodMs', String(period.value))
  try {
    const result = await request<ApiResult<Report>>(endpoint() + '?' + query.toString(), { signal: ctl.signal })
    if (!result.success) throw new Error(result.errorMessage || tx('读取诊断失败'))
    if (!ctl.signal.aborted) {
      report.value = result.data
      loadedSort.value = requestedSort
      loadError.value = ''
      if (ownedTag.value) {
        if (result.data.trace?.tagId === ownedTag.value) trace.value = result.data.trace
        else finishTrace()
      }
    }
  } catch (e) {
    if (!ctl.signal.aborted) loadError.value = e instanceof Error ? e.message : tx('读取诊断失败')
  } finally {
    if (!ctl.signal.aborted) pollTimer = window.setTimeout(poll, 2000)
  }
}

function stopTrace() {
  window.clearTimeout(traceTimer)
  traceRequest?.abort()
  stopBrowserTrace?.()
  ownedTag.value = undefined
  starting.value = false
}
function finishTrace() {
  savedPush.value = browserTrace.value
  finished.value = true
  stopTrace()
}
async function startTrace() {
  if (!tagId.value || starting.value || !props.active) return
  const chosen = tagId.value
  const ctl = new AbortController()
  traceRequest = ctl
  starting.value = true
  traceError.value = ''
  try {
    const result = await request<ApiResult<Trace>>(endpoint() + '/trace/' + encodeURIComponent(chosen), { method: 'POST', signal: ctl.signal })
    if (!result.success) throw new Error(result.errorMessage || tx('无法启动跟踪'))
    if (ctl.signal.aborted) return
    trace.value = result.data
    savedPush.value = undefined
    finished.value = false
    ownedTag.value = chosen
    stopBrowserTrace = observeBrowserTrace(props.device.id, chosen, 60000)
    traceTimer = window.setTimeout(finishTrace, 60000)
    void poll()
  } catch (e) {
    if (!ctl.signal.aborted) traceError.value = e instanceof Error ? e.message : tx('无法启动跟踪')
  } finally {
    if (!ctl.signal.aborted) starting.value = false
  }
}
function visibilityChanged() {
  if (!props.active || document.hidden) stopTrace()
  void poll()
}
function showRanking(mode: RankingSort) {
  sort.value = mode
  rankingPanel.value?.focus({ preventScroll: true })
  rankingPanel.value?.scrollIntoView({ block: 'start' })
}
function compareUtc(first: string, second: string) {
  const milliseconds = Date.parse(first) - Date.parse(second)
  if (milliseconds !== 0) return milliseconds
  // .NET records four more fractional digits than JavaScript Date retains.
  const subMsTicks = (value: string) => Number(value.match(/\.\d{3}(\d{1,4})/)?.[1]?.padEnd(4, '0') ?? 0)
  return subMsTicks(first) - subMsTicks(second)
}
function describe(row: SlowTag, source: Report): TagDetail {
  const startUtc = row.gapStartUtc
  const endUtc = row.gapOngoing ? source.observedAtUtc : row.gapEndUtc
  const incidents = startUtc && endUtc ? (source.connectionIncidents ?? []).filter(incident =>
    compareUtc(incident.startedUtc, endUtc) <= 0 && (!incident.recoveredUtc || compareUtc(incident.recoveredUtc, startUtc) >= 0)) : []
  return { row, endUtc, incidents, observedAtUtc: source.observedAtUtc,
    name: tagName(row.tagId), group: groupName(row.tagId), address: tagAddress(row.tagId) }
}
function openDetail(row: SlowTag) {
  if (!report.value) return
  detail.value = describe(row, report.value)
  detailOpen.value = true
}
function traceDetail() {
  const tag = detail.value && findTag(detail.value.row.tagId)
  if (!tag?.traceable || ownedTag.value || starting.value) return
  tagId.value = tag.id
  detailOpen.value = false
  scrollToTrace = true
  void startTrace()
}
function afterDetailClose() {
  if (scrollToTrace) {
    scrollToTrace = false
    tracePanel.value?.focus({ preventScroll: true })
    tracePanel.value?.scrollIntoView({ block: 'nearest' })
  }
}
watch(() => props.active, visibilityChanged)
watch(period, () => { detailOpen.value = false; report.value = undefined; void poll() })
watch(sort, () => { void poll() })
watch(tagId, () => { stopTrace(); trace.value = undefined; savedPush.value = undefined; finished.value = false; traceError.value = '' }, { flush: 'sync' })
onMounted(() => {
  mounted = true
  rankingObserver = new ResizeObserver(([entry]) => { tableSpace.value = entry.contentRect.width })
  if (rankingPanel.value) rankingObserver.observe(rankingPanel.value)
  document.addEventListener('visibilitychange', visibilityChanged)
  void poll()
})
onBeforeUnmount(() => {
  mounted = false
  rankingObserver?.disconnect()
  document.removeEventListener('visibilitychange', visibilityChanged)
  window.clearTimeout(pollTimer)
  pollRequest?.abort()
  stopTrace()
})
</script>

<style scoped>
.acquisition-diagnostics { container-type: inline-size; flex: 1; min-width: 0; min-height: 0; overflow: auto; padding: 2px 4px 12px 0; }
.diag-heading, .diag-box > header, .trace-controls, .period-picker { display: flex; align-items: center; flex-wrap: wrap; gap: 8px; }
.diag-heading, .diag-box > header { justify-content: space-between; }
.diag-heading, .diag-columns, .slow-ranking { margin-bottom: 12px; }
h3, p { margin: 0; }
h3 { font-size: 1em; font-weight: 600; }
small, .diag-scope, .diag-events time { color: var(--gateway-muted, #516477); }
.period-picker .ant-select { min-width: 110px; }
.diag-metrics, .diag-stages { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 8px; }
.diag-metrics > *, .diag-box { border: 1px solid var(--gateway-border, #d7e2ed); border-radius: 6px; background: white; min-width: 0; }
.diag-metrics > *, .diag-stages > div { display: flex; flex-direction: column; gap: 3px; padding: 8px 12px; overflow-wrap: anywhere; }
.diag-metrics button { font: inherit; color: inherit; text-align: start; cursor: pointer; }
.diag-metrics button:is(:hover, :focus-visible), .diag-metrics button[aria-pressed=true] { border-color: #6083a5; }
.diag-metrics button > span { display: flex; align-items: center; justify-content: space-between; gap: 4px; }
.diag-metrics button .anticon { color: #52769a; transform: rotate(-45deg); }
.metric-tag { max-width: 100%; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.diag-metrics strong { font-size: 1.55em; font-variant-numeric: tabular-nums; }
.diag-scope { margin: 7px 0 12px; line-height: 1.55; overflow-wrap: anywhere; }
.diag-columns { display: grid; grid-template-columns: minmax(0, 3fr) minmax(235px, 2fr); gap: 10px; }
.diag-box { padding: 10px; }
.diag-box > header { margin-bottom: 9px; }
.slow-ranking :deep(.ant-table-cell) { font-variant-numeric: tabular-nums; }
.rank-tag { display: grid; min-width: 0; }
.rank-tag > * { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.rank-tag b { font-weight: 500; }
.rank-number, .rank-measure { font-weight: 600; color: #345c85; }
.rank-footer, .detail-meta, .gap-heading { display: flex; align-items: center; flex-wrap: wrap; gap: 4px 14px; }
.rank-footer { justify-content: space-between; margin-top: 7px; }
.rank-detail { display: grid; gap: 12px; max-height: max(150px, calc(100dvh - 180px)); overflow: auto; overflow-wrap: anywhere; }
.detail-meta, .detail-path { color: var(--gateway-muted, #516477); }
.detail-counts { display: grid; grid-template-columns: repeat(4, minmax(0,1fr)); margin: 0; gap: 6px; }
.detail-counts > div, .detail-gap { background: #f3f7fb; padding: 8px 10px; border-radius: 4px; }
.detail-counts dt { color: var(--gateway-muted, #516477); }
.detail-counts dd { font-weight: 600; margin: 3px 0 0; }
.detail-gap { display: grid; gap: 4px; }
.detail-read-error { margin-top: 4px; color: #912e23; }
.detail-read-error p { white-space: pre-wrap; }
.detail-incidents { list-style: none; margin: 4px 0; padding: 0; max-height: 150px; overflow: auto; }
.detail-incidents > li { padding: 5px 0; border-bottom: 1px solid #e5edf4; }
@media (max-width: 480px) { .detail-counts { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
.diag-columns :deep(.ant-table-wrapper), .diag-events { max-height: 300px; overflow: auto; }
.diag-events { padding: 0; margin: 0; list-style: none; }
.diag-events li { display: flex; align-items: baseline; gap: 10px; padding: 6px 0; border-bottom: 1px solid #edf1f5; overflow-wrap: anywhere; }
.diag-events time { flex: none; }
.diag-box :deep(.ant-empty) { margin-block: 16px; }
.trace-controls { max-width: 100%; }
.trace-controls .ant-select { width: 260px; max-width: 100%; min-width: 0; flex: 1 1 160px; }
.diag-stages > div { background: #f3f7fb; border-radius: 4px; }
.diag-box .diag-scope:last-child { margin-bottom: 0; }
@container (max-width: 800px) { .diag-columns { grid-template-columns: minmax(0, 1fr); } }
@container (max-width: 560px) {
  .diag-metrics, .diag-stages { grid-template-columns: repeat(2, minmax(0,1fr)); }
  .trace-controls { width: 100%; }
}
</style>

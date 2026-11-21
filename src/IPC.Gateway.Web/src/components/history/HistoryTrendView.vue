<template>
  <section class="history-trend">
    <form class="trend-filters" @submit.prevent="load">
      <label v-for="field in fields" :key="field.level">
        <span>{{ tx(field.label) }}</span>
        <HistoryPointSelect :level="field.level" :label="tx(field.label)" :filters="ancestors(field.level)"
          :selected="scope[field.level]" :disabled="field.parent && !scope[field.parent]"
          :active="active" @change="selectScope(field.level, $event)" />
      </label>
      <label class="trend-points">
        <span>{{ trendText('选择数值点位') }} · {{ selected.length }}/{{ maxTrendPoints }}</span>
        <a-select mode="multiple" label-in-value allow-clear show-search :filter-option="false"
          :aria-label="trendText('选择数值点位')" :value="selectedValues" :options="pointOptions"
          :max-tag-count="2" :loading="loadingOptions" :search-value="search" :open="pickerOpen"
          :placeholder="trendText(readyScope ? '选择数值点位' : '请先选择通道、设备和分组')"
          @dropdown-visible-change="pickerOpen = $event" @search="search = $event" @change="selectPoints">
          <template #notFoundContent>{{ tx(loadingOptions ? '加载中' : '无匹配项') }}</template>
        </a-select>
      </label>
      <div class="trend-time">
        <span>{{ tx('时间范围') }}</span>
        <a-range-picker v-model:value="range" show-time value-format="YYYY-MM-DDTHH:mm:ss"
          format="YYYY-MM-DD HH:mm:ss" :placeholder="[tx('开始时间'), tx('结束时间')]" />
      </div>
      <div class="trend-actions">
        <a-button v-if="loading" @click="cancelQuery">{{ tx('取消查询') }}</a-button>
        <a-button v-else type="primary" html-type="submit" :disabled="!validQuery">{{ trendText('查询曲线') }}</a-button>
      </div>
      <small class="trend-limit">{{ trendText('最多选择 6 个点位，切换通道、设备或分组会保留已选点位。') }}</small>
    </form>
    <a-alert v-if="enabled === false" type="info" show-icon :message="tx('历史记录已关闭，仍可查询已保存的数据。')" />
    <a-alert v-if="optionError || queryError" type="error" show-icon :message="trendText(queryError || optionError)" />
    <a-alert v-if="plots.length && missing.length" type="info" show-icon
      :message="trendText('部分点位在所选时段没有可绘制的有效数值')"
      :description="missing.map(item => item.path || item.name).join(' · ')" />
    <div v-if="plots.length && resultRange" class="trend-charts" :aria-busy="loading">
      <HistoryTrendChart v-for="plot in plots" :key="plot.unit" :unit="plot.unit" :lines="plot.lines" :range="resultRange" />
    </div>
    <div v-else class="trend-empty">
      <a-spin v-if="loading" />
      <a-empty v-else :description="trendText(result ? '所选时间范围内没有可绘制的有效数值' : '请先选择数值点位和时间范围')" />
    </div>
    <small v-if="plots.length" class="trend-description">{{ trendText('本地时间 · 工程值 · 缩放查看细节') }}</small>
  </section>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onDeactivated, reactive, ref, shallowRef, watch } from 'vue'
import type { HistoryRecordFilters, HistoryRecordOption } from '../../api'
import { locale, tx } from '../../i18n'
import HistoryPointSelect from './HistoryPointSelect.vue'
import HistoryTrendChart, { type TrendLine } from './HistoryTrendChart.vue'
import { historyQueryError } from './historyQueryError'
import { getTrend, getTrendOptions, maxTrendPoints, trendPointKey, type TrendPoint, type TrendSeries } from './historyTrendApi'
import { trendText } from './historyTrendText'

const props = defineProps<{ enabled?: boolean; active: boolean }>()
type ScopeLevel = 'channel' | 'device' | 'group'
const fields: { level: ScopeLevel; label: string; parent?: ScopeLevel }[] = [
  { level: 'channel', label: '通道' }, { level: 'device', label: '设备', parent: 'channel' },
  { level: 'group', label: '分组', parent: 'device' }
]
interface SelectedPoint {
  point: TrendPoint
  key: string
  name: string
  path: string
}
interface PickerValue { value: string; label?: string }

const scope = reactive<Partial<Record<ScopeLevel, HistoryRecordOption>>>({})
const readyScope = computed(() => !!scope.channel && !!scope.device && !!scope.group)
const selected = shallowRef<SelectedPoint[]>([])
const candidates = shallowRef<SelectedPoint[]>([])
const pickerOpen = ref(false)
const search = ref('')
const loadingOptions = ref(false)
const optionError = ref('')
const loading = ref(false)
const queryError = ref('')
const result = shallowRef<TrendSeries[]>()
const resultRange = ref<[number, number]>()
let controller: AbortController | undefined

function ancestors(level: ScopeLevel): HistoryRecordFilters {
  if (level === 'channel') return {}
  if (level === 'device') return { channelId: scope.channel?.id }
  return { channelId: scope.channel?.id, deviceId: scope.device?.id }
}

function selectScope(level: ScopeLevel, option?: HistoryRecordOption) {
  if (scope[level]?.id === option?.id) return
  scope[level] = option
  for (const field of fields.slice(fields.findIndex(field => field.level === level) + 1)) delete scope[field.level]
  candidates.value = []
  pickerOpen.value = false
  search.value = ''
}

const selectedValues = computed<PickerValue[]>(() => selected.value.map(item => ({ value: item.key, label: item.name })))
const pointOptions = computed(() => {
  const keys = new Set(selected.value.map(item => item.key))
  return candidates.value.map(item => ({
    value: item.key, label: item.name, title: item.path, disabled: keys.size >= maxTrendPoints && !keys.has(item.key)
  }))
})

watch([pickerOpen, search, () => scope.channel?.id, () => scope.device?.id, () => scope.group?.id, () => props.active, locale],
  (_, __, onCleanup) => {
    candidates.value = []
    optionError.value = ''
    loadingOptions.value = false
    const c = scope.channel?.id, d = scope.device?.id, g = scope.group?.id
    if (!pickerOpen.value || !props.active || !c || !d || g == null) return
    const request = new AbortController()
    loadingOptions.value = true
    const timer = window.setTimeout(async () => {
      try {
        const options = await getTrendOptions(c, d, g, search.value.trim().slice(0, 256), locale.value, request.signal)
        if (request.signal.aborted) return
        candidates.value = options.map(option => {
          const point = { channelId: c, deviceId: d, groupId: g, tagId: option.id }
          return { point, key: trendPointKey(point), name: option.name || option.id, path: option.path || option.name || option.id }
        })
      } catch (cause) {
        if (!request.signal.aborted) optionError.value = historyQueryError(cause, '读取点位候选失败')
      } finally {
        if (!request.signal.aborted) loadingOptions.value = false
      }
    }, search.value ? 220 : 0)
    onCleanup(() => { clearTimeout(timer); request.abort() })
  })

function selectPoints(values: PickerValue[]) {
  const byKey = new Map([...candidates.value, ...selected.value].map(item => [item.key, item]))
  selected.value = values.slice(0, maxTrendPoints).flatMap(value => {
    const item = byKey.get(value.value)
    return item ? [item] : []
  })
  search.value = ''
}

const now = new Date()
function localInputTime(date: Date) {
  return new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 19)
}
const range = ref<[string, string] | null>([localInputTime(new Date(+now - 3600000)), localInputTime(now)])
const validQuery = computed(() => selected.value.length > 0 && !!range.value && Date.parse(range.value[0]) < Date.parse(range.value[1]))
const colors = ['#3d73a8', '#177f69', '#ad691f', '#7555a2', '#ba4e57', '#27828c']
const plots = computed(() => {
  const grouped = new Map<string, TrendLine[]>()
  if (!result.value) return []
  const selection = new Map(selected.value.map((item, index) => [item.key, { ...item, color: colors[index] }]))
  for (const series of result.value) {
    const key = trendPointKey(series.point)
    const item = selection.get(key)
    if (!item) continue
    const byUnit = new Map<string, TrendSeries['samples']>()
    for (const sample of series.samples) {
      if (!byUnit.has(sample.unit)) byUnit.set(sample.unit, [])
      byUnit.get(sample.unit)!.push(sample)
    }
    for (const [unit, samples] of byUnit) {
      if (!grouped.has(unit)) grouped.set(unit, [])
      grouped.get(unit)!.push({ key, name: item.name, color: item.color, path: item.path, samples })
    }
  }
  return Array.from(grouped, ([unit, lines]) => ({ unit, lines }))
})
const missing = computed(() => {
  if (!result.value) return []
  const empty = new Set(result.value.filter(series => !series.samples.length).map(series => trendPointKey(series.point)))
  return selected.value.filter(item => empty.has(item.key))
})

function cancelQuery() {
  controller?.abort()
  controller = undefined
  loading.value = false
}

function stop() {
  pickerOpen.value = false
  search.value = ''
  cancelQuery()
}

watch([selected, range], () => {
  cancelQuery()
  result.value = undefined
  queryError.value = ''
})
watch(() => props.active, active => { if (!active) stop() })
onDeactivated(stop)
onBeforeUnmount(stop)

async function load() {
  if (!props.active || !validQuery.value || !range.value) return
  cancelQuery()
  const request = controller = new AbortController()
  loading.value = true
  queryError.value = ''
  const from = new Date(range.value[0]), to = new Date(range.value[1])
  try {
    const response = await getTrend(selected.value.map(item => item.point), from.toISOString(), to.toISOString(), locale.value, request.signal)
    if (request.signal.aborted) return
    resultRange.value = [from.getTime(), to.getTime()]
    result.value = response
  } catch (cause) {
    if (!request.signal.aborted) queryError.value = historyQueryError(cause, '历史查询失败')
  } finally {
    if (!request.signal.aborted) loading.value = false
  }
}
</script>

<style scoped>
.history-trend { height: 100%; min-height: 0; width: 100%; display: flex; flex-direction: column; gap: 10px; container-type: inline-size; overflow: auto; }
.trend-filters { display: grid; grid-template-columns: repeat(3, minmax(105px, .8fr)) minmax(180px, 1.5fr) minmax(290px, 2fr) auto; align-items: end; gap: 9px 10px; padding: 12px; border: 1px solid var(--gateway-border); border-radius: 8px; background: white; }
.trend-filters > label, .trend-time { display: flex; flex-direction: column; min-width: 0; gap: 5px; }
.trend-filters :deep(.ant-select), .trend-time :deep(.ant-picker) { width: 100%; min-width: 0; }
.trend-limit { grid-column: 1 / -1; }
.trend-limit, .trend-description { color: var(--gateway-muted, #52677d); }
.trend-charts { display: grid; flex: 1 0 auto; grid-auto-rows: minmax(285px, 1fr); gap: 10px; }
.trend-empty { flex: 1 0 auto; min-height: 200px; display: grid; place-items: center; border: 1px solid var(--gateway-border); border-radius: 8px; background: white; }
.trend-actions { white-space: nowrap; }
@container (max-width: 1190px) {
  .trend-filters { grid-template-columns: repeat(4, minmax(0, 1fr)); }
  .trend-time { grid-column: span 2; }
}
@container (max-width: 635px) {
  .trend-filters { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}
</style>

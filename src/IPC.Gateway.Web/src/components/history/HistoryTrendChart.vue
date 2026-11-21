<template>
  <section class="trend-chart" :aria-label="unit || trendText('无单位')">
    <header class="trend-chart-heading">
      <strong>{{ unit || trendText('无单位') }}</strong>
      <div class="trend-legend" :aria-label="trendText('切换曲线显示')">
        <a-tooltip v-for="line in lines" :key="line.key" :title="line.path">
          <button type="button" :aria-pressed="!hidden.has(line.key)" @click="toggle(line.key)">
            <span class="swatch" :style="{ backgroundColor: line.color }" aria-hidden="true" />{{ line.name }}
          </button>
        </a-tooltip>
      </div>
    </header>
    <div ref="container" class="trend-chart-plot" :aria-label="trendText('点位曲线')" />
  </section>
</template>

<script lang="ts">
import type { TrendSample } from './historyTrendApi'

export interface TrendLine {
  key: string
  name: string
  path: string
  color: string
  samples: TrendSample[]
}
</script>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'
import { LineChart, type LineSeriesOption } from 'echarts/charts'
import { DataZoomComponent, GridComponent, TooltipComponent } from 'echarts/components'
import { init, use, type EChartsType } from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import { locale, tx } from '../../i18n'
import { qualityText } from '../../utils/display'
import { trendText } from './historyTrendText'

use([LineChart, DataZoomComponent, GridComponent, TooltipComponent, CanvasRenderer])

const props = defineProps<{
  unit: string
  lines: TrendLine[]
  range: [number, number]
}>()

interface TrendDatum {
  value: [number, number | null]
  history?: TrendSample
  symbolSize?: number
}

const hidden = ref(new Set<string>())
const container = ref<HTMLElement | null>(null)
const chart = shallowRef<EChartsType>()
const processingNames: Record<string, string> = { aligned: '时间对齐', aggregate: '聚合' }
let sizeObserver: ResizeObserver | undefined

function toggle(key: string) {
  if (hidden.value.has(key)) hidden.value.delete(key)
  else hidden.value.add(key)
}

function dataFor(samples: TrendSample[]) {
  const values: TrendDatum[] = []
  let previous: TrendSample | undefined
  for (let i = 0; i < samples.length; i++) {
    const sample = samples[i]
    const time = Date.parse(sample.timestamp)
    if (previous && previous.segment !== sample.segment)
      values.push({ value: [(Date.parse(previous.timestamp) + time) / 2, null], symbolSize: 0 })
    const isolated = previous?.segment !== sample.segment && samples[i + 1]?.segment !== sample.segment
    values.push({ value: [time, sample.value], history: sample, symbolSize: isolated ? 6 : 0 })
    previous = sample
  }
  return values
}

function tooltip(value: unknown) {
  const entries = (Array.isArray(value) ? value : [value]) as Array<{ seriesName?: string; data?: TrendDatum }>
  const time = new Intl.DateTimeFormat(locale.value, {
    year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit',
    second: '2-digit', fractionalSecondDigits: 3, hour12: false
  })
  return entries.flatMap(entry => {
    const sample = entry.data?.history
    if (!sample) return []
    const method = sample.processing && processingNames[sample.processing]
    return [[entry.seriesName, sample.valueText, sample.unit].filter(Boolean).join('  ') +
      '\n' + time.format(new Date(sample.timestamp)) + ' · ' + tx(qualityText(sample.quality)) +
      (method ? ' · ' + trendText(method) : '') + '\n' + sample.path]
  }).join('\n\n')
}

function render(resetRange: boolean) {
  if (!chart.value) return
  const series: LineSeriesOption[] = props.lines.filter(line => !hidden.value.has(line.key)).map(line => ({
    id: line.key, name: line.name, type: 'line', color: line.color, lineStyle: { width: 2 },
    smooth: 0.35, smoothMonotone: 'x', connectNulls: false, data: dataFor(line.samples), showSymbol: true
  }))
  const axisTick = new Intl.DateTimeFormat(locale.value, props.range[1] - props.range[0] > 24 * 3600 * 1000
    ? { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hour12: false }
    : { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false })
  chart.value.setOption({
    animation: false,
    textStyle: { fontFamily: '"Cascadia Code", "Microsoft YaHei", sans-serif', color: '#30485d' },
    tooltip: { trigger: 'axis', renderMode: 'richText', confine: true, formatter: tooltip },
    grid: { left: 18, right: 24, top: 16, bottom: 47, containLabel: true },
    xAxis: { type: 'time', min: props.range[0], max: props.range[1],
      axisLabel: { hideOverlap: true, formatter: (value: number) => axisTick.format(new Date(value)) } },
    yAxis: { type: 'value', scale: true, splitLine: { lineStyle: { color: '#dae6f0' } } },
    dataZoom: [{ type: 'inside', filterMode: 'none' }, { type: 'slider', bottom: 8, height: 19,
      filterMode: 'none', showDetail: false, showDataShadow: false }],
    series
  }, resetRange ? { notMerge: true } : { replaceMerge: ['series'] })
}

watch([() => props.lines, () => props.range], () => { hidden.value.clear(); render(true) })
watch([hidden, locale], () => render(false), { deep: true })
onMounted(() => {
  const element = container.value
  if (!element) return
  sizeObserver = new ResizeObserver(() => {
    if (!element.clientWidth || !element.clientHeight) return
    if (!chart.value) {
      chart.value = init(element, undefined, { renderer: 'canvas' })
      render(true)
    } else chart.value.resize()
  })
  sizeObserver.observe(element)
})
onBeforeUnmount(() => { sizeObserver?.disconnect(); chart.value?.dispose() })
</script>

<style scoped>
.trend-chart { display: flex; flex-direction: column; min-width: 0; min-height: 280px; border: 1px solid var(--gateway-border); border-radius: 8px; background: white; }
.trend-chart-heading { display: flex; align-items: center; justify-content: space-between; flex-wrap: wrap; gap: 6px 14px; padding: 10px 14px 4px; }
.trend-legend { display: flex; flex-wrap: wrap; gap: 6px 10px; }
.trend-legend button { display: inline-flex; align-items: center; gap: 6px; background: #f4f8fc; border: 1px solid transparent; border-radius: 5px; color: inherit; font: inherit; text-align: start; padding: 3px 8px; max-width: 100%; overflow-wrap: anywhere; cursor: pointer; }
.trend-legend button[aria-pressed=false] { opacity: .65; text-decoration: line-through; }
.swatch { width: 15px; height: 3px; border-radius: 2px; flex: none; }
.trend-chart-plot { flex: 1; width: 100%; min-height: 220px; }
</style>

<template>
  <div class="topology-canvas">
    <div ref="container" class="topology-canvas__chart" />
    <div v-if="empty" class="topology-canvas__empty">
      <strong>{{ tx('暂无拓扑数据') }}</strong>
      <span>{{ tx('请先在设备管理中添加设备、分组和标签。') }}</span>
    </div>
  </div>
</template>

<script setup lang="ts">
import { locale } from '../../i18n'
import { localizeChartOption } from '../../i18n/charts'
import { tx } from '../../i18n'
import { onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'
import { GraphChart } from 'echarts/charts'
import { LegendComponent, TooltipComponent } from 'echarts/components'
import { init, use, type EChartsType } from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import type { EChartsOption } from 'echarts'
import { buildTopologyOption } from './topologyOption'
import type { DeviceTopologyModel, TopologyNode } from './topologyTypes'

use([GraphChart, LegendComponent, TooltipComponent, CanvasRenderer])

const props = defineProps<{
  model: DeviceTopologyModel
  selectedNodeId: string
  empty: boolean
  fitToken: number
}>()

const emit = defineEmits<{
  'select-node': [node: TopologyNode]
}>()

const container = ref<HTMLDivElement | null>(null)
const chart = shallowRef<EChartsType | null>(null)
let observer: ResizeObserver | undefined
let lastViewState: TopologyGraphViewState = {}
let fitAll = false

onMounted(() => {
  if (!container.value) return
  chart.value = init(
    container.value,
    { textStyle: { fontFamily: '"Cascadia Code", "Microsoft YaHei", sans-serif', fontSize: 13.333 } },
    { renderer: 'canvas' }
  )
  updateOption()
  chart.value.on('click', (params) => {
    const node = (params as any).data?.rawNode as TopologyNode | undefined
    if (node) emit('select-node', node)
  })
  chart.value.on('graphRoam', () => {
    fitAll = false
    lastViewState = readGraphViewState()
  })
  observer = new ResizeObserver(() => {
    const element = container.value
    const instance = chart.value
    if (!element || !instance || !element.clientWidth || !element.clientHeight) return
    if (element.clientWidth === instance.getWidth() && element.clientHeight === instance.getHeight()) return
    instance.resize()
    updateOption()
  })
  observer.observe(container.value)
})

onBeforeUnmount(() => {
  observer?.disconnect()
  chart.value?.dispose()
})

watch([() => props.model, () => props.selectedNodeId, locale], () => updateOption())

watch(
  () => props.fitToken,
  () => {
    fitAll = true
    lastViewState = {}
    updateOption(false)
  }
)

interface TopologyGraphViewState {
  zoom?: number
  center?: unknown[]
}

function updateOption(preserveView = true) {
  const instance = chart.value
  if (!instance) return

  const option = localizeChartOption(buildTopologyOption(props.model, props.selectedNodeId, instance.getWidth(), instance.getHeight(), fitAll))
  const nextOption = preserveView ? withGraphViewState(option, lastViewState) : option
  instance.setOption(nextOption, !preserveView)
}

function readGraphViewState(): TopologyGraphViewState {
  const instance = chart.value
  if (!instance) return {}

  const currentOption = instance.getOption() as { series?: unknown[] }
  const series = Array.isArray(currentOption.series) ? (currentOption.series[0] as Record<string, unknown>) : undefined
  const zoom = typeof series?.zoom === 'number' ? series.zoom : undefined
  const center = Array.isArray(series?.center) ? [...series.center] : undefined
  return { zoom, center }
}

function withGraphViewState(option: EChartsOption, state: TopologyGraphViewState): EChartsOption {
  if (state.zoom === undefined && state.center === undefined) return option

  const source = option as { series?: unknown }
  const series = Array.isArray(source.series)
    ? ([...source.series] as Record<string, unknown>[])
    : source.series
      ? [source.series as Record<string, unknown>]
      : []

  if (series.length === 0) return option

  series[0] = {
    ...series[0],
    ...(state.zoom === undefined ? {} : { zoom: state.zoom }),
    ...(state.center === undefined ? {} : { center: state.center })
  }

  return {
    ...option,
    series
  }
}
</script>

<style scoped>
.topology-canvas {
  position: relative;
  min-height: 0;
  overflow: hidden;
  border: 1px solid #dbe4ef;
  border-radius: 8px;
  background:
    linear-gradient(#e8eef6 1px, transparent 1px), linear-gradient(90deg, #e8eef6 1px, transparent 1px), #f8fafc;
  background-size: 28px 28px;
}

.topology-canvas__chart {
  position: absolute;
  inset: 0;
}

.topology-canvas__empty {
  position: absolute;
  inset: 0;
  display: grid;
  place-content: center;
  gap: 8px;
  color: #475569;
  text-align: center;
  pointer-events: none;
}

.topology-canvas__empty strong {
  color: #0f172a;
  font-size: calc(18px + 1pt);
}
</style>

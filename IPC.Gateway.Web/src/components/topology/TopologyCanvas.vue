<template>
  <div class="topology-canvas">
    <div ref="container" class="topology-canvas__chart" />
    <div v-if="empty" class="topology-canvas__empty">
      <strong>暂无拓扑数据</strong>
      <span>请先在设备管理中添加设备、分组和标签。</span>
    </div>
  </div>
</template>

<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'
import { GraphChart } from 'echarts/charts'
import { LegendComponent, TooltipComponent } from 'echarts/components'
import { init, use, type EChartsType } from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import type { EChartsOption } from 'echarts'
import type { TopologyNode } from './topologyTypes'

use([GraphChart, LegendComponent, TooltipComponent, CanvasRenderer])

const props = defineProps<{
  option: EChartsOption
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

onMounted(async () => {
  await nextTick()
  if (!container.value) return
  chart.value = init(container.value, undefined, { renderer: 'canvas' })
  chart.value.setOption(props.option, true)
  chart.value.on('click', params => {
    const node = (params as any).data?.rawNode as TopologyNode | undefined
    if (node) emit('select-node', node)
  })
  chart.value.on('graphRoam', () => {
    lastViewState = readGraphViewState()
  })
  observer = new ResizeObserver(() => chart.value?.resize())
  observer.observe(container.value)
})

onBeforeUnmount(() => {
  observer?.disconnect()
  chart.value?.dispose()
})

watch(
  () => props.option,
  option => updateOption(option, true),
  { deep: true }
)

watch(
  () => props.fitToken,
  () => {
    lastViewState = {}
    chart.value?.setOption(props.option, true)
    chart.value?.resize()
  }
)

interface TopologyGraphViewState {
  zoom?: number
  center?: unknown[]
}

function updateOption(option: EChartsOption, preserveView: boolean) {
  const instance = chart.value
  if (!instance) return

  const nextOption = preserveView ? withGraphViewState(option, lastViewState) : option
  instance.setOption(nextOption, !preserveView)
}

function readGraphViewState(): TopologyGraphViewState {
  const instance = chart.value
  if (!instance) return {}

  const currentOption = instance.getOption() as { series?: unknown[] }
  const series = Array.isArray(currentOption.series) ? currentOption.series[0] as Record<string, unknown> : undefined
  const zoom = typeof series?.zoom === 'number' ? series.zoom : undefined
  const center = Array.isArray(series?.center) ? [...series.center] : undefined
  return { zoom, center }
}

function withGraphViewState(option: EChartsOption, state: TopologyGraphViewState): EChartsOption {
  if (state.zoom === undefined && state.center === undefined) return option

  const source = option as { series?: unknown }
  const series = Array.isArray(source.series)
    ? [...source.series] as Record<string, unknown>[]
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
  min-height: 620px;
  overflow: hidden;
  border: 1px solid #dbe4ef;
  border-radius: 8px;
  background:
    linear-gradient(#e8eef6 1px, transparent 1px),
    linear-gradient(90deg, #e8eef6 1px, transparent 1px),
    #f8fafc;
  background-size: 28px 28px;
}

.topology-canvas__chart {
  width: 100%;
  height: 100%;
  min-height: 620px;
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
  font-size: 18px;
}
</style>

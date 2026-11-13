<template><div ref="container" class="big-screen-chart" /></template>

<script setup lang="ts">
import { locale } from '../../i18n'
import { localizeChartOption } from '../../i18n/charts'
import { onActivated, onBeforeUnmount, onDeactivated, onMounted, ref, shallowRef, watch } from 'vue'
import { BarChart, GaugeChart, LineChart, PieChart } from 'echarts/charts'
import { GridComponent, LegendComponent, TooltipComponent } from 'echarts/components'
import { init, use, type EChartsType } from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import type { EChartsOption } from 'echarts'

use([BarChart, GaugeChart, LineChart, PieChart, GridComponent, LegendComponent, TooltipComponent, CanvasRenderer])

const props = defineProps<{
  option: EChartsOption
}>()

const container = ref<HTMLDivElement | null>(null)
const chart = shallowRef<EChartsType | null>(null)
let observer: ResizeObserver | undefined

onMounted(() => {
  if (!container.value) return
  chart.value = init(
    container.value,
    { textStyle: { fontFamily: '"Cascadia Code", "Microsoft YaHei", sans-serif', fontSize: 13.333 } },
    { renderer: 'canvas' }
  )
  chart.value.setOption(localizeChartOption(props.option), true)
  observer = new ResizeObserver(() => {
    const element = container.value
    const instance = chart.value
    if (!element?.clientWidth || !element.clientHeight || !instance) return
    if (instance.getWidth() !== element.clientWidth || instance.getHeight() !== element.clientHeight) instance.resize()
  })
  observer.observe(container.value)
})

onBeforeUnmount(() => {
  observer?.disconnect()
  chart.value?.dispose()
})

const optionUpdates = watch(
  [() => props.option, locale],
  ([option]) => {
    chart.value?.setOption(localizeChartOption(option), true)
  },
  { deep: true }
)

onDeactivated(() => {
  observer?.disconnect()
  optionUpdates.pause()
})
onActivated(() => {
  if (container.value) observer?.observe(container.value)
  optionUpdates.resume()
})
</script>

<style scoped>
.big-screen-chart {
  width: 100%;
  height: 100%;
  min-height: 0;
}
</style>

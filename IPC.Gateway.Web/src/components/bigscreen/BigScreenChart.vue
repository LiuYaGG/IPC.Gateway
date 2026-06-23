<template>
  <div ref="container" class="big-screen-chart" />
</template>

<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'
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

onMounted(async () => {
  await nextTick()
  if (!container.value) return
  chart.value = init(container.value, undefined, { renderer: 'canvas' })
  chart.value.setOption(props.option, true)
  observer = new ResizeObserver(() => chart.value?.resize())
  observer.observe(container.value)
})

onBeforeUnmount(() => {
  observer?.disconnect()
  chart.value?.dispose()
})

watch(
  () => props.option,
  option => {
    chart.value?.setOption(option, true)
  },
  { deep: true }
)
</script>

<style scoped>
.big-screen-chart {
  width: 100%;
  height: 100%;
  min-height: 0;
}
</style>

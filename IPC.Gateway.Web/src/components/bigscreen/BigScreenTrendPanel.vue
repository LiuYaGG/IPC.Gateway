<template>
  <section class="big-panel big-panel--wide">
    <header>
      <span>采集趋势</span>
      <small>{{ trend.length }} samples</small>
    </header>
    <BigScreenChart :option="option" />
  </section>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import type { EChartsOption } from 'echarts'
import type { TrendSample } from '../DashboardView.vue'
import BigScreenChart from './BigScreenChart.vue'

const props = defineProps<{
  trend: TrendSample[]
}>()

const option = computed<EChartsOption>(() => ({
  backgroundColor: 'transparent',
  grid: { left: 36, right: 22, top: 30, bottom: 28 },
  tooltip: { trigger: 'axis' },
  legend: { top: 0, right: 0, textStyle: { color: '#cbd5e1' } },
  xAxis: {
    type: 'category',
    data: props.trend.map(item => new Date(item.timestamp).toLocaleTimeString('zh-CN', { hour12: false })),
    axisLabel: { color: '#94a3b8' },
    axisLine: { lineStyle: { color: '#334155' } }
  },
  yAxis: [
    { type: 'value', min: 0, max: 100, axisLabel: { color: '#94a3b8' }, splitLine: { lineStyle: { color: '#1e293b' } } },
    { type: 'value', min: 0, axisLabel: { color: '#94a3b8' }, splitLine: { show: false } }
  ],
  series: [
    { name: '成功率', type: 'line', smooth: true, yAxisIndex: 0, data: props.trend.map(item => item.successRate), symbol: 'none', lineStyle: { width: 3, color: '#22d3ee' }, areaStyle: { color: 'rgba(34, 211, 238, 0.14)' } },
    { name: '在线设备', type: 'line', smooth: true, yAxisIndex: 1, data: props.trend.map(item => item.onlineDeviceCount), symbol: 'none', lineStyle: { width: 2, color: '#5eead4' } },
    { name: '异常标签', type: 'bar', yAxisIndex: 1, data: props.trend.map(item => item.badTagCount), itemStyle: { color: '#fb7185' }, barMaxWidth: 10 }
  ]
}))
</script>

<style scoped>
.big-panel {
  display: grid;
  grid-template-rows: auto minmax(240px, 1fr);
  gap: 12px;
  min-height: 320px;
  padding: 16px;
  border: 1px solid rgba(148, 163, 184, 0.18);
  border-radius: 8px;
  background: #0f1d2e;
}

.big-panel header {
  display: flex;
  justify-content: space-between;
  color: #f8fafc;
}

.big-panel small {
  color: #94a3b8;
}
</style>

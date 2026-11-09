<template>
  <section class="big-panel big-panel--wide">
    <header>
      <span>{{ tx('采集趋势') }}</span>
      <small>{{ trend.length }} {{ tx('条采样') }}</small>
    </header>
    <BigScreenChart :option="option" />
  </section>
</template>

<script setup lang="ts">
import { tx } from '../../i18n'
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
  tooltip: {
    trigger: 'axis',
    backgroundColor: '#ffffff',
    borderColor: '#e0e9f2',
    textStyle: { color: '#000000', fontSize: 13.333 }
  },
  legend: { top: 0, right: 0, textStyle: { color: '#000000' } },
  xAxis: {
    type: 'category',
    data: props.trend.map((item) => new Date(item.timestamp).toLocaleTimeString('zh-CN', { hour12: false })),
    axisLabel: { color: '#7a8a9e' },
    axisLine: { lineStyle: { color: '#d9e4ef' } }
  },
  yAxis: [
    {
      type: 'value',
      min: 0,
      max: 100,
      axisLabel: { color: '#7a8a9e' },
      splitLine: { lineStyle: { color: '#edf2f7' } }
    },
    { type: 'value', min: 0, axisLabel: { color: '#7a8a9e' }, splitLine: { show: false } }
  ],
  series: [
    {
      name: '成功率',
      type: 'line',
      smooth: true,
      yAxisIndex: 0,
      data: props.trend.map((item) => item.successRate),
      symbol: 'none',
      lineStyle: { width: 3, color: '#567b9f' },
      areaStyle: { color: 'rgba(86, 123, 159, 0.10)' }
    },
    {
      name: '在线设备',
      type: 'line',
      smooth: true,
      yAxisIndex: 1,
      data: props.trend.map((item) => item.onlineDeviceCount),
      symbol: 'none',
      lineStyle: { width: 2, color: '#519778' }
    },
    {
      name: '异常标签',
      type: 'bar',
      yAxisIndex: 1,
      data: props.trend.map((item) => item.badTagCount),
      itemStyle: { color: '#cb6464' },
      barMaxWidth: 10
    }
  ]
}))
</script>

<style scoped>
.big-panel {
  display: grid;
  grid-template-rows: auto minmax(0, 1fr);
  gap: 12px;
  min-height: 0px;
  padding: 16px;
  border: 1px solid #e0e9f2;
  border-radius: 6px;
  background: #ffffff;
}

.big-panel header {
  display: flex;
  justify-content: space-between;
  color: #000000;
}

.big-panel small {
  color: #7a8a9e;
}
</style>

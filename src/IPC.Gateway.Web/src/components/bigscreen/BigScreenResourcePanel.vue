<template>
  <section class="big-panel">
    <header>
      <span>{{ tx('系统资源') }}</span>
      <small>{{ status?.system?.source || 'runtime' }}</small>
    </header>
    <BigScreenChart :option="option" />
  </section>
</template>

<script setup lang="ts">
import { tx } from '../../i18n'
import { computed } from 'vue'
import type { EChartsOption } from 'echarts'
import type { GatewayStatus } from '../../api'
import BigScreenChart from './BigScreenChart.vue'

const props = defineProps<{
  status: GatewayStatus | null
}>()

const option = computed<EChartsOption>(() => ({
  series: [
    gauge('CPU', props.status?.system?.cpuUsagePercent ?? 0, 18, '#567b9f'),
    gauge('内存', props.status?.system?.memoryUsagePercent ?? 0, 50, '#519778'),
    gauge('队列', props.status?.scheduler?.queue?.utilizationPercent ?? 0, 82, '#bf9147')
  ]
}))

function gauge(name: string, value: number, centerX: number, color: string) {
  return {
    type: 'gauge' as const,
    center: [`${centerX}%`, '56%'],
    radius: '43%',
    min: 0,
    max: 100,
    progress: { show: true, width: 8, itemStyle: { color } },
    axisLine: { lineStyle: { width: 8, color: [[1, '#edf2f7']] as [number, string][] } },
    axisTick: { show: false },
    splitLine: { show: false },
    axisLabel: { show: false },
    pointer: { show: false },
    title: { offsetCenter: [0, '64%'], color: '#000000', fontSize: 13.333 },
    detail: {
      valueAnimation: true,
      offsetCenter: [0, '4%'],
      color: '#000000',
      fontSize: 17.333,
      formatter: '{value}%'
    },
    data: [{ name, value: Number(value.toFixed(1)) }]
  }
}
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

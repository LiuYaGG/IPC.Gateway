<template>
  <section class="big-panel">
    <header>
      <span>系统资源</span>
      <small>{{ status?.system?.source || 'runtime' }}</small>
    </header>
    <BigScreenChart :option="option" />
  </section>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import type { EChartsOption } from 'echarts'
import type { GatewayStatus } from '../../api'
import BigScreenChart from './BigScreenChart.vue'

const props = defineProps<{
  status: GatewayStatus | null
}>()

const option = computed<EChartsOption>(() => ({
  series: [
    gauge('CPU', props.status?.system?.cpuUsagePercent ?? 0, 18, '#38bdf8'),
    gauge('内存', props.status?.system?.memoryUsagePercent ?? 0, 50, '#2dd4bf'),
    gauge('队列', props.status?.scheduler?.queue?.utilizationPercent ?? 0, 82, '#fbbf24')
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
    axisLine: { lineStyle: { width: 8, color: [[1, '#1e293b']] as [number, string][] } },
    axisTick: { show: false },
    splitLine: { show: false },
    axisLabel: { show: false },
    pointer: { show: false },
    title: { offsetCenter: [0, '64%'], color: '#cbd5e1', fontSize: 12 },
    detail: { valueAnimation: true, offsetCenter: [0, '4%'], color: '#f8fafc', fontSize: 16, formatter: '{value}%' },
    data: [{ name, value: Number(value.toFixed(1)) }]
  }
}
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

<template>
  <section class="big-panel">
    <header>
      <span>设备分布</span>
      <small>{{ devices.length }} devices</small>
    </header>
    <div class="distribution-grid">
      <BigScreenChart :option="deviceOption" />
      <BigScreenChart :option="protocolOption" />
    </div>
  </section>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import type { EChartsOption } from 'echarts'
import type { DeviceRuntimeStatus } from '../../api'
import { countDevices, protocolDistribution } from './bigScreenModel'
import BigScreenChart from './BigScreenChart.vue'

const props = defineProps<{
  devices: DeviceRuntimeStatus[]
}>()

const deviceOption = computed<EChartsOption>(() => {
  const counts = countDevices(props.devices)
  return {
    tooltip: { trigger: 'item' },
    series: [{
      name: '设备状态',
      type: 'pie',
      radius: ['54%', '78%'],
      data: [
        { name: '在线', value: counts.online, itemStyle: { color: '#2dd4bf' } },
        { name: '离线', value: counts.offline, itemStyle: { color: '#fb7185' } },
        { name: '停用', value: counts.disabled, itemStyle: { color: '#64748b' } }
      ],
      label: { color: '#cbd5e1' }
    }]
  }
})

const protocolOption = computed<EChartsOption>(() => {
  const items = protocolDistribution(props.devices)
  return {
    grid: { left: 34, right: 10, top: 12, bottom: 28 },
    tooltip: { trigger: 'axis' },
    xAxis: { type: 'category', data: items.map(item => item.name), axisLabel: { color: '#94a3b8', rotate: 28 }, axisLine: { lineStyle: { color: '#334155' } } },
    yAxis: { type: 'value', axisLabel: { color: '#94a3b8' }, splitLine: { lineStyle: { color: '#1e293b' } } },
    series: [{ type: 'bar', data: items.map(item => item.count), barMaxWidth: 20, itemStyle: { color: '#38bdf8', borderRadius: [4, 4, 0, 0] } }]
  }
})
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

.distribution-grid {
  display: grid;
  grid-template-columns: minmax(0, 0.85fr) minmax(0, 1.15fr);
  gap: 10px;
  min-height: 0;
}
</style>

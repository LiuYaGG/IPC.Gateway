<template>
  <section class="big-panel">
    <header>
      <span>{{ tx('设备分布') }}</span>
      <small>{{ devices.length }} {{ tx('台设备') }}</small>
    </header>
    <div class="distribution-grid">
      <BigScreenChart :option="deviceOption" />
      <BigScreenChart :option="protocolOption" />
    </div>
  </section>
</template>

<script setup lang="ts">
import { tx } from '../../i18n'
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
    tooltip: {
      trigger: 'item',
      backgroundColor: '#ffffff',
      borderColor: '#e0e9f2',
      textStyle: { color: '#000000', fontSize: 13.333 }
    },
    series: [
      {
        name: '设备状态',
        type: 'pie',
        radius: ['54%', '78%'],
        data: [
          { name: '在线', value: counts.online, itemStyle: { color: '#519778' } },
          { name: '离线', value: counts.offline, itemStyle: { color: '#cb6464' } },
          { name: '停用', value: counts.disabled, itemStyle: { color: '#64748b' } }
        ],
        label: { color: '#000000' }
      }
    ]
  }
})

const protocolOption = computed<EChartsOption>(() => {
  const items = protocolDistribution(props.devices)
  return {
    grid: { left: 34, right: 10, top: 12, bottom: 28 },
    tooltip: {
      trigger: 'axis',
      backgroundColor: '#ffffff',
      borderColor: '#e0e9f2',
      textStyle: { color: '#000000', fontSize: 13.333 }
    },
    xAxis: {
      type: 'category',
      data: items.map((item) => item.name),
      axisLabel: { color: '#7a8a9e', rotate: 28 },
      axisLine: { lineStyle: { color: '#d9e4ef' } }
    },
    yAxis: { type: 'value', axisLabel: { color: '#7a8a9e' }, splitLine: { lineStyle: { color: '#edf2f7' } } },
    series: [
      {
        type: 'bar',
        data: items.map((item) => item.count),
        barMaxWidth: 20,
        itemStyle: { color: '#567b9f', borderRadius: [4, 4, 0, 0] }
      }
    ]
  }
})
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

.distribution-grid {
  display: grid;
  grid-template-columns: minmax(0, 0.85fr) minmax(0, 1.15fr);
  gap: 10px;
  min-height: 0;
}
</style>

<template>
  <section class="big-kpi-strip">
    <article v-for="item in items" :key="item.label" class="big-kpi" :class="`big-kpi--${item.tone}`">
      <span>{{ item.label }}</span>
      <strong>{{ item.value }}</strong>
      <small>{{ item.hint }}</small>
    </article>
  </section>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import type { GatewayStatus } from '../../api'
import { formatBytes, formatNumber } from '../../utils/format'
import { averageSuccessRate, percent } from './bigScreenModel'

const props = defineProps<{
  status: GatewayStatus | null
}>()

const items = computed(() => {
  const status = props.status
  const devices = status?.devices ?? []
  const onlineRate = percent(status?.onlineDeviceCount ?? 0, status?.deviceCount ?? 0)
  const successRate = averageSuccessRate(devices)
  const tagGoodRate = percent(status?.goodTagCount ?? 0, status?.tagCount ?? 0)
  const queue = status?.scheduler?.queue
  const queueRate = Number(queue?.utilizationPercent ?? 0)
  const cpu = Number((status?.system?.cpuUsagePercent ?? 0).toFixed(1))
  const memory = Number((status?.system?.memoryUsagePercent ?? 0).toFixed(1))
  return [
    {
      label: '在线设备',
      value: `${status?.onlineDeviceCount ?? 0}/${status?.deviceCount ?? 0}`,
      hint: `${formatNumber(onlineRate, 1)}% 在线率`,
      tone: onlineRate >= 90 ? 'good' : onlineRate >= 60 ? 'warn' : 'bad'
    },
    {
      label: '采集成功',
      value: `${formatNumber(successRate, 1)}%`,
      hint: `${formatNumber(totalSuccessfulReads(devices))}/${formatNumber(totalReads(devices))} 次`,
      tone: successRate >= 95 ? 'good' : successRate >= 80 ? 'warn' : 'bad'
    },
    {
      label: '标签质量',
      value: `${status?.goodTagCount ?? 0}/${status?.tagCount ?? 0}`,
      hint: `Good ${formatNumber(tagGoodRate, 1)}% · Bad ${status?.badTagCount ?? 0}`,
      tone: tagGoodRate >= 95 ? 'good' : tagGoodRate >= 80 ? 'warn' : 'bad'
    },
    {
      label: '队列水位',
      value: `${queue?.pendingCount ?? 0}/${queue?.queueLimit ?? 0}`,
      hint: `运行 ${queue?.runningCount ?? 0} · 水位 ${formatNumber(queueRate, 1)}%`,
      tone: queueRate < 70 ? 'good' : queueRate < 90 ? 'warn' : 'bad'
    },
    {
      label: '系统资源',
      value: `CPU ${cpu}%`,
      hint: `MEM ${memory}% · ${formatBytes(status?.system?.usedMemoryBytes ?? 0)}`,
      tone: Math.max(cpu, memory) < 75 ? 'good' : Math.max(cpu, memory) < 90 ? 'warn' : 'bad'
    }
  ]
})

function totalReads(devices: Array<{ totalReads?: number }>) {
  return devices.reduce((sum, item) => sum + Number(item.totalReads || 0), 0)
}

function totalSuccessfulReads(devices: Array<{ successfulReads?: number }>) {
  return devices.reduce((sum, item) => sum + Number(item.successfulReads || 0), 0)
}
</script>

<style scoped>
.big-kpi-strip {
  display: grid;
  grid-template-columns: repeat(5, minmax(0, 1fr));
  gap: 12px;
}

.big-kpi {
  display: grid;
  gap: 6px;
  min-width: 0;
  padding: 16px;
  border: 1px solid rgba(148, 163, 184, 0.18);
  border-radius: 8px;
  background: #0f1d2e;
}

.big-kpi span,
.big-kpi small {
  overflow: hidden;
  color: #94a3b8;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.big-kpi strong {
  overflow: hidden;
  color: #f8fafc;
  font-size: 24px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.big-kpi--good strong {
  color: #5eead4;
}

.big-kpi--warn strong {
  color: #fbbf24;
}

.big-kpi--bad strong {
  color: #fb7185;
}

@media (max-width: 1280px) {
  .big-kpi-strip {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}
</style>

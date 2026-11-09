<template>
  <section class="big-kpi-strip">
    <article v-for="item in items" :key="item.label" class="big-kpi" :class="`big-kpi--${item.tone}`">
      <span>{{ tx(item.label) }}</span>
      <strong>{{ item.value }}</strong>
      <small>{{ item.hint }}</small>
    </article>
  </section>
</template>

<script setup lang="ts">
import { formatMessage } from '../../i18n'
import { tx } from '../../i18n'
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
      hint: formatMessage('{0}% 在线率', [formatNumber(onlineRate, 1)]),
      tone: onlineRate >= 90 ? 'good' : onlineRate >= 60 ? 'warn' : 'bad'
    },
    {
      label: '采集成功',
      value: `${formatNumber(successRate, 1)}%`,
      hint: formatMessage('{0}/{1} 次', [
        formatNumber(totalSuccessfulReads(devices)),
        formatNumber(totalReads(devices))
      ]),
      tone: successRate >= 95 ? 'good' : successRate >= 80 ? 'warn' : 'bad'
    },
    {
      label: '标签质量',
      value: `${status?.goodTagCount ?? 0}/${status?.tagCount ?? 0}`,
      hint: formatMessage('正常 {0}% · 异常 {1}', [formatNumber(tagGoodRate, 1), status?.badTagCount ?? 0]),
      tone: tagGoodRate >= 95 ? 'good' : tagGoodRate >= 80 ? 'warn' : 'bad'
    },
    {
      label: '队列水位',
      value: `${queue?.pendingCount ?? 0}/${queue?.queueLimit ?? 0}`,
      hint: formatMessage('运行 {0} · 水位 {1}%', [queue?.runningCount ?? 0, formatNumber(queueRate, 1)]),
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
  padding: 12px 14px;
  border: 1px solid #e0e9f2;
  border-radius: 6px;
  background: #ffffff;
}

.big-kpi span,
.big-kpi small {
  overflow: hidden;
  color: #7a8a9e;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.big-kpi strong {
  overflow: hidden;
  color: #000000;
  font-size: calc(22px + 1pt);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.big-kpi--good strong {
  color: #519778;
}

.big-kpi--warn strong {
  color: #bf9147;
}

.big-kpi--bad strong {
  color: #cb6464;
}

@media (max-width: 1180px) {
  .big-kpi-strip {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}
</style>

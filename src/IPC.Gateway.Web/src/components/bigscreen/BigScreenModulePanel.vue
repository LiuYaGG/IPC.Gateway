<template>
  <section class="big-panel module-panel">
    <header>
      <span>{{ tx('核心链路') }}</span>
      <small>{{ tx('网关模块') }}</small>
    </header>
    <div class="module-grid">
      <article v-for="item in modules" :key="item.name" :class="`module-card module-card--${item.tone}`">
        <span>{{ item.name }}</span>
        <strong>{{ tx(statusText(item.state)) }}</strong>
        <small>{{ item.detail }}</small>
      </article>
    </div>
  </section>
</template>

<script setup lang="ts">
import { formatMessage } from '../../i18n'
import { tx } from '../../i18n'
import { statusText } from '../../utils/display'
import { computed } from 'vue'
import type { GatewayStatus } from '../../api'
import { formatBytes, formatNumber } from '../../utils/format'

const props = defineProps<{
  status: GatewayStatus | null
}>()

const modules = computed(() => {
  const status = props.status
  return [
    {
      name: '采集调度',
      state: status?.scheduler?.healthStatus || '-',
      detail: formatMessage('排队 {0} · 慢任务 {1}', [
        status?.scheduler?.queue?.pendingCount ?? 0,
        status?.scheduler?.totalSlow ?? 0
      ]),
      tone: normalizeTone(status?.scheduler?.healthStatus)
    },
    {
      name: 'MQTT',
      state: status?.mqtt?.isConnected ? '已连接' : status?.mqtt?.isRunning ? '重连中' : '未运行',
      detail: formatMessage('{0} · 积压 {1}', [status?.mqtt?.broker || '-', status?.mqtt?.outboxPendingCount ?? 0]),
      tone: status?.mqtt?.isConnected ? 'good' : 'warn'
    },
    {
      name: '历史库',
      state: status?.history?.isRunning ? '写入中' : '未运行',
      detail: formatMessage('{0} · 压缩 {1}', [
        formatBytes(status?.history?.totalBytes ?? 0),
        status?.history?.compressedFileCount ?? 0
      ]),
      tone: status?.history?.isRunning ? 'good' : 'normal'
    },
    {
      name: '规则引擎',
      state: status?.flowRuleEngine?.isRunning ? '运行中' : '未运行',
      detail: formatMessage('active {0} · 触发 {1}', [
        status?.flowRuleEngine?.activeRuleCount ?? 0,
        formatNumber(status?.flowRuleEngine?.triggeredCount ?? 0)
      ]),
      tone: status?.flowRuleEngine?.isRunning ? 'good' : 'normal'
    },
    {
      name: 'OPC UA',
      state: status?.opcUa?.isRunning ? '运行中' : '未运行',
      detail: formatMessage('节点 {0} · 更新 {1}', [
        status?.opcUa?.tagNodeCount ?? 0,
        formatNumber(status?.opcUa?.valueUpdateCount ?? 0)
      ]),
      tone: status?.opcUa?.isRunning ? 'good' : 'normal'
    }
  ]
})

function normalizeTone(value?: string) {
  const text = String(value || '').toLowerCase()
  if (text.includes('healthy')) return 'good'
  if (text.includes('unhealthy')) return 'bad'
  if (text.includes('degraded')) return 'warn'
  return 'normal'
}
</script>

<style scoped>
.big-panel {
  display: grid;
  gap: 12px;
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

.module-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
}

.module-card {
  display: grid;
  gap: 6px;
  padding: 12px;
  border-left: 3px solid #64748b;
  border-radius: 6px;
  background: #f5f8fc;
}

.module-card span,
.module-card small {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.module-card strong {
  color: #000000;
}

.module-card--good {
  border-left-color: #519778;
}

.module-card--warn {
  border-left-color: #bf9147;
}

.module-card--bad {
  border-left-color: #cb6464;
}
</style>

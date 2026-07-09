<template>
  <section class="big-panel module-panel">
    <header>
      <span>核心链路</span>
      <small>gateway modules</small>
    </header>
    <div class="module-grid">
      <article v-for="item in modules" :key="item.name" :class="`module-card module-card--${item.tone}`">
        <span>{{ item.name }}</span>
        <strong>{{ item.state }}</strong>
        <small>{{ item.detail }}</small>
      </article>
    </div>
  </section>
</template>

<script setup lang="ts">
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
      detail: `排队 ${status?.scheduler?.queue?.pendingCount ?? 0} · 慢任务 ${status?.scheduler?.totalSlow ?? 0}`,
      tone: normalizeTone(status?.scheduler?.healthStatus)
    },
    {
      name: 'MQTT',
      state: status?.mqtt?.isConnected ? '已连接' : status?.mqtt?.isRunning ? '重连中' : '未运行',
      detail: `${status?.mqtt?.broker || '-'} · 积压 ${status?.mqtt?.outboxPendingCount ?? 0}`,
      tone: status?.mqtt?.isConnected ? 'good' : 'warn'
    },
    {
      name: '历史库',
      state: status?.history?.isRunning ? '写入中' : '未运行',
      detail: `${formatBytes(status?.history?.totalBytes ?? 0)} · 压缩 ${status?.history?.compressedFileCount ?? 0}`,
      tone: status?.history?.isRunning ? 'good' : 'normal'
    },
    {
      name: '流程规则',
      state: status?.flowRuleEngine?.isRunning ? '运行中' : '未运行',
      detail: `active ${status?.flowRuleEngine?.activeRuleCount ?? 0} · 触发 ${formatNumber(status?.flowRuleEngine?.triggeredCount ?? 0)}`,
      tone: status?.flowRuleEngine?.isRunning ? 'good' : 'normal'
    },
    {
      name: 'OPC UA',
      state: status?.opcUa?.isRunning ? '运行中' : '未运行',
      detail: `节点 ${status?.opcUa?.tagNodeCount ?? 0} · 更新 ${formatNumber(status?.opcUa?.valueUpdateCount ?? 0)}`,
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
  border-radius: 8px;
  background: #132235;
}

.module-card span,
.module-card small {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.module-card strong {
  color: #f8fafc;
}

.module-card--good {
  border-left-color: #2dd4bf;
}

.module-card--warn {
  border-left-color: #fbbf24;
}

.module-card--bad {
  border-left-color: #fb7185;
}
</style>

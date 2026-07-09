<template>
  <section class="big-hero" :class="`big-hero--${tone}`">
    <div class="big-hero__main">
      <p>IPC Gateway Command Screen</p>
      <h1>{{ title }}</h1>
      <span>{{ subtitle }}</span>
    </div>
    <div class="big-hero__score">
      <small>健康指数</small>
      <strong>{{ score }}</strong>
      <span>{{ healthText(health?.status) }}</span>
    </div>
    <div class="big-hero__chips">
      <span :class="{ active: status?.isRunning }">网关 {{ status?.isRunning ? '运行' : '停止' }}</span>
      <span :class="{ active: status?.mqtt?.isConnected }">MQTT {{ status?.mqtt?.isConnected ? '在线' : '离线' }}</span>
      <span :class="{ active: status?.history?.isRunning }">历史库 {{ status?.history?.isRunning ? '写入' : '停止' }}</span>
      <span :class="{ active: status?.flowRuleEngine?.isRunning }">规则 {{ status?.flowRuleEngine?.isRunning ? '运行' : '停止' }}</span>
      <span :class="{ active: status?.opcUa?.isRunning }">OPC UA {{ status?.opcUa?.isRunning ? '运行' : '停止' }}</span>
    </div>
  </section>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import type { GatewayHealthResponse, GatewayStatus } from '../../api'
import { formatDateTime } from '../../utils/format'
import { healthScore, healthText, healthTone } from './bigScreenModel'

const props = defineProps<{
  status: GatewayStatus | null
  health: GatewayHealthResponse | null
}>()

const score = computed(() => healthScore(props.status, props.health))
const tone = computed(() => healthTone(props.health?.status))
const title = computed(() => props.status?.projectName || props.health?.projectName || 'IPC Gateway')
const subtitle = computed(() => {
  const started = props.status?.startedTime || props.health?.startedTime
  return started ? `启动时间 ${formatDateTime(started)}` : '等待运行状态同步'
})
</script>

<style scoped>
.big-hero {
  display: grid;
  grid-template-columns: minmax(320px, 1fr) 140px minmax(360px, 0.9fr);
  gap: 18px;
  align-items: center;
  min-height: 150px;
  padding: 24px;
  border: 1px solid rgba(125, 211, 252, 0.24);
  border-radius: 8px;
  color: #f8fafc;
  background: radial-gradient(circle at 12% 16%, rgba(20, 184, 166, 0.34), transparent 30%),
    linear-gradient(135deg, #102033 0%, #123a4b 48%, #1f2937 100%);
}

.big-hero--bad {
  background: linear-gradient(135deg, #3b1720, #233044 58%, #111827);
}

.big-hero--warn {
  background: linear-gradient(135deg, #3d2b12, #173044 58%, #111827);
}

.big-hero__main p,
.big-hero__score small,
.big-hero__main span {
  margin: 0;
  color: rgba(226, 232, 240, 0.72);
}

.big-hero__main h1 {
  margin: 8px 0;
  font-size: 34px;
  line-height: 1.15;
}

.big-hero__score {
  display: grid;
  place-items: center;
  gap: 4px;
  padding: 16px;
  border: 1px solid rgba(255, 255, 255, 0.16);
  border-radius: 8px;
  background: rgba(15, 23, 42, 0.34);
}

.big-hero__score strong {
  color: #67e8f9;
  font-size: 42px;
  line-height: 1;
}

.big-hero__chips {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
}

.big-hero__chips span {
  padding: 10px 12px;
  border: 1px solid rgba(148, 163, 184, 0.24);
  border-radius: 8px;
  color: #cbd5e1;
  background: rgba(15, 23, 42, 0.42);
}

.big-hero__chips .active {
  border-color: rgba(45, 212, 191, 0.48);
  color: #ccfbf1;
  background: rgba(13, 148, 136, 0.22);
}

@media (max-width: 1180px) {
  .big-hero {
    grid-template-columns: 1fr;
  }
}
</style>

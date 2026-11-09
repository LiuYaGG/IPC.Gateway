<template>
  <section class="big-hero" :class="`big-hero--${tone}`">
    <div class="big-hero__main">
      <p>{{ tx('IPC Gateway 运行总览') }}</p>
      <h1>{{ title }}</h1>
      <span>{{ tx(subtitle) }}</span>
    </div>
    <div class="big-hero__score">
      <small>{{ tx('健康指数') }}</small>
      <strong>{{ score }}</strong>
      <span>{{ tx(statusText(healthText(health?.status))) }}</span>
    </div>
    <div class="big-hero__chips">
      <span :class="{ active: status?.isRunning }">
        {{ tx('网关') }} {{ status?.isRunning ? tx('运行') : tx('停止') }}
      </span>
      <span :class="{ active: status?.mqtt?.isConnected }">
        MQTT {{ status?.mqtt?.isConnected ? tx('在线') : tx('离线') }}
      </span>
      <span :class="{ active: status?.history?.isRunning }">
        {{ tx('历史库') }} {{ status?.history?.isRunning ? tx('写入') : tx('停止') }}
      </span>
      <span :class="{ active: status?.flowRuleEngine?.isRunning }">
        {{ tx('规则') }} {{ status?.flowRuleEngine?.isRunning ? tx('运行') : tx('停止') }}
      </span>
      <span :class="{ active: status?.opcUa?.isRunning }">
        OPC UA {{ status?.opcUa?.isRunning ? tx('运行') : tx('停止') }}
      </span>
    </div>
  </section>
</template>

<script setup lang="ts">
import { formatMessage } from '../../i18n'
import { tx } from '../../i18n'
import { statusText } from '../../utils/display'
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
  return started ? formatMessage('启动时间 {0}', [formatDateTime(started)]) : '等待运行状态同步'
})
</script>

<style scoped>
.big-hero {
  display: grid;
  grid-template-columns: minmax(320px, 1fr) 140px minmax(360px, 0.9fr);
  gap: 18px;
  align-items: center;
  min-height: 104px;
  padding: 12px 20px;
  border: 1px solid #e0e9f2;
  border-radius: 6px;
  color: #000000;
  background: #ffffff;
}

.big-hero--bad {
  background: #ffffff;
  border-left: 3px solid #cb6464;
}

.big-hero--warn {
  background: #ffffff;
  border-left: 3px solid #bf9147;
}

.big-hero__main p,
.big-hero__score small,
.big-hero__main span {
  margin: 0;
  color: #7a8a9e;
}

.big-hero__main h1 {
  margin: 8px 0;
  font-size: calc(24px + 1pt);
  line-height: 1.15;
}

.big-hero__score {
  display: grid;
  place-items: center;
  gap: 4px;
  padding: 16px;
  border: 1px solid #e0e9f2;
  border-radius: 6px;
  background: #f5f8fc;
}

.big-hero__score strong {
  color: #567b9f;
  font-size: calc(30px + 1pt);
  line-height: 1;
}

.big-hero__chips {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
}

.big-hero__chips span {
  padding: 7px 10px;
  border: 1px solid #e0e9f2;
  border-radius: 6px;
  color: #000000;
  background: #f5f8fc;
}

.big-hero__chips .active {
  border-color: #dceee2;
  color: #427d63;
  background: #edf7f1;
}

@media (max-width: 1180px) {
  .big-hero {
    grid-template-columns: 1fr;
  }
}
</style>

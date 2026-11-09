<template>
  <section class="topology-summary">
    <article>
      <span>{{ tx('在线设备') }}</span>
      <strong>{{ summary.onlineDevices }}/{{ summary.devices }}</strong>
      <small>{{ summary.offlineDevices }} {{ tx('离线，') }}{{ summary.disabledDevices }} {{ tx('停用') }}</small>
    </article>
    <article>
      <span>{{ tx('配置通道') }}</span>
      <strong>{{ summary.channels }}</strong>
      <small>{{ tx('按通道汇聚设备与调度链路') }}</small>
    </article>
    <article>
      <span>{{ tx('分组/标签') }}</span>
      <strong>{{ summary.groups }}/{{ summary.tags }}</strong>
      <small>{{ summary.hiddenTags }} {{ tx('个标签折叠') }}</small>
    </article>
    <article :class="{ 'is-alert': summary.recentErrors > 0 }">
      <span>{{ tx('近期异常') }}</span>
      <strong>{{ summary.recentErrors }}</strong>
      <small>{{ tx('点击异常节点查看详情') }}</small>
    </article>
  </section>
</template>

<script setup lang="ts">
import { tx } from '../../i18n'
import type { TopologySummary as Summary } from './topologyTypes'

defineProps<{
  summary: Summary
}>()
</script>

<style scoped>
.topology-summary {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px;
}

.topology-summary article {
  display: grid;
  gap: 4px;
  min-width: 0;
  padding: 14px;
  border: 1px solid #dbe4ef;
  border-radius: 8px;
  background: #ffffff;
}

.topology-summary span {
  color: #64748b;
  font-size: calc(12px + 1pt);
}

.topology-summary strong {
  color: #0f172a;
  font-size: calc(26px + 1pt);
  line-height: 1.1;
}

.topology-summary small {
  overflow: hidden;
  color: #64748b;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.topology-summary .is-alert strong {
  color: #dc2626;
}

@media (max-width: 1180px) {
  .topology-summary {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}
</style>

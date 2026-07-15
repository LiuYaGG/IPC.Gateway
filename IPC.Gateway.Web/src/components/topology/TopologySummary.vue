<template>
  <section class="topology-summary">
    <article>
      <span>在线设备</span>
      <strong>{{ summary.onlineDevices }}/{{ summary.devices }}</strong>
      <small>{{ summary.offlineDevices }} 离线，{{ summary.disabledDevices }} 停用</small>
    </article>
    <article>
      <span>配置通道</span>
      <strong>{{ summary.channels }}</strong>
      <small>按通道汇聚设备与调度链路</small>
    </article>
    <article>
      <span>分组/标签</span>
      <strong>{{ summary.groups }}/{{ summary.tags }}</strong>
      <small>{{ summary.hiddenTags }} 个标签折叠</small>
    </article>
    <article :class="{ 'is-alert': summary.recentErrors > 0 }">
      <span>近期异常</span>
      <strong>{{ summary.recentErrors }}</strong>
      <small>点击异常节点查看详情</small>
    </article>
  </section>
</template>

<script setup lang="ts">
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
  font-size: 12px;
}

.topology-summary strong {
  color: #0f172a;
  font-size: 26px;
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

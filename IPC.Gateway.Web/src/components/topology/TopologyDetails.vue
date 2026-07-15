<template>
  <aside class="topology-details">
    <header>
      <span>节点详情</span>
      <el-tag :type="tagType" effect="dark">{{ toneLabel(node?.tone || 'normal') }}</el-tag>
    </header>

    <template v-if="node">
      <h3>{{ node.meta.title }}</h3>
      <p>{{ nodeTypeLabel }}</p>
      <dl>
        <template v-for="row in node.meta.rows" :key="row.label">
          <dt>{{ row.label }}</dt>
          <dd>{{ row.value || '-' }}</dd>
        </template>
      </dl>

      <section class="topology-details__errors">
        <strong>最近错误</strong>
        <button
          v-for="error in node.meta.errors"
          :key="`${error.timestamp}-${error.message}`"
          type="button"
          @click="emit('select-error', error)"
        >
          <span>{{ error.message || '未知错误' }}</span>
          <small>{{ formatDateTime(error.timestamp) }}</small>
        </button>
        <em v-if="node.meta.errors.length === 0">暂无错误</em>
      </section>
    </template>

    <div v-else class="topology-details__empty">
      点击拓扑节点查看设备、分组、标签或服务状态。
    </div>
  </aside>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import type { RuntimeErrorDetail } from '../../api'
import { formatDateTime } from '../../utils/format'
import { toneLabel } from './topologyStatus'
import type { TopologyNode } from './topologyTypes'

const props = defineProps<{
  node: TopologyNode | null
}>()

const emit = defineEmits<{
  'select-error': [error: RuntimeErrorDetail]
}>()

const tagType = computed(() => {
  if (props.node?.tone === 'good') return 'success'
  if (props.node?.tone === 'warn') return 'warning'
  if (props.node?.tone === 'bad') return 'danger'
  if (props.node?.tone === 'disabled') return 'info'
  return 'primary'
})

const nodeTypeLabel = computed(() => {
  if (!props.node) return ''
  if (props.node.type === 'gateway') return '边缘网关'
  if (props.node.type === 'channel') return '配置通道'
  if (props.node.type === 'device') return '采集设备'
  if (props.node.type === 'group') return '设备分组'
  if (props.node.type === 'tag') return '采集标签'
  if (props.node.type === 'tagSummary') return '标签聚合'
  return '网关服务'
})
</script>

<style scoped>
.topology-details {
  display: grid;
  align-content: start;
  gap: 14px;
  min-height: 620px;
  padding: 16px;
  border: 1px solid #dbe4ef;
  border-radius: 8px;
  background: #ffffff;
}

.topology-details header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  color: #64748b;
  font-size: 12px;
}

.topology-details h3 {
  margin: 0;
  overflow-wrap: anywhere;
  color: #0f172a;
  font-size: 20px;
}

.topology-details p {
  margin: -8px 0 0;
  color: #64748b;
}

.topology-details dl {
  display: grid;
  grid-template-columns: 78px minmax(0, 1fr);
  gap: 10px 12px;
  margin: 0;
}

.topology-details dt {
  color: #64748b;
}

.topology-details dd {
  min-width: 0;
  margin: 0;
  overflow-wrap: anywhere;
  color: #0f172a;
}

.topology-details__errors {
  display: grid;
  gap: 8px;
  padding-top: 10px;
  border-top: 1px solid #e2e8f0;
}

.topology-details__errors strong {
  color: #0f172a;
}

.topology-details__errors button {
  display: grid;
  gap: 3px;
  padding: 9px 10px;
  border: 1px solid #fecdd3;
  border-radius: 8px;
  background: #fff1f2;
  color: #9f1239;
  text-align: left;
  cursor: pointer;
}

.topology-details__errors small,
.topology-details__errors em,
.topology-details__empty {
  color: #64748b;
}

.topology-details__errors em {
  font-style: normal;
}
</style>

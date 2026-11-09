<template>
  <section>
    <a-row :gutter="14" class="queue-cards">
      <a-col :xs="12" :sm="6">
        <a-card size="small">
          <small>{{ tx('待写入') }}</small>
          <strong>{{ queue.pendingCount }}</strong>
        </a-card>
      </a-col>
      <a-col :xs="12" :sm="6">
        <a-card size="small">
          <small>{{ tx('失败文件') }}</small>
          <strong>{{ queue.failedCount }}</strong>
        </a-card>
      </a-col>
      <a-col :xs="12" :sm="6">
        <a-card size="small">
          <small>{{ tx('成功写入') }}</small>
          <strong>{{ queue.succeededCount }}</strong>
        </a-card>
      </a-col>
      <a-col :xs="12" :sm="6">
        <a-card size="small">
          <small>{{ tx('重试次数') }}</small>
          <strong>{{ queue.retriedCount }}</strong>
        </a-card>
      </a-col>
    </a-row>
    <a-alert
      v-if="queue.lastError"
      :message="tx(queue.lastError)"
      type="error"
      show-icon
      :closable="false"
      class="runtime-error"
    />
    <GatewayTable
      resize-key="components.scripting.ScriptRuntimePanel.vue.0"
      :data-source="rows"
      :row-key="(_row: any, index: any) => index"
      size="small"
      :pagination="false"
      :columns="
        localizeColumns([
          { key: 'column-0', title: tx('脚本'), dataIndex: 'name', width: '150' },
          { key: 'column-1', title: tx('状态'), width: '105' },
          { key: 'column-2', title: tx('执行次数'), dataIndex: 'executionCount', width: '95' },
          { key: 'column-3', title: tx('失败次数'), dataIndex: 'failureCount', width: '95' },
          { key: 'column-4', title: tx('最近完成'), width: '165' },
          { key: 'column-5', title: tx('耗时(ms)'), dataIndex: 'lastDurationMilliseconds', width: '100' },
          { key: 'column-6', title: tx('最近错误'), dataIndex: 'lastError', width: '220', ellipsis: true },
          { key: 'column-7', title: '', width: 140 }
        ])
      "
      :scroll="{ x: 1070 }"
    >
      <template #bodyCell="tableCell">
        <template v-if="tableCell.column.key === 'column-1'">
          <template v-for="scope in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
            <a-tag :color="tagColor(stateType(scope.row.state))">{{ tx(statusText(scope.row.state)) }}</a-tag>
          </template>
        </template>
        <template v-if="tableCell.column.key === 'column-4'">
          <template v-for="scope in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
            {{ formatTime(scope.row.lastFinishedUtc) }}
          </template>
        </template>
        <template v-if="tableCell.column.key === 'column-7'">
          <template v-for="scope in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
            <div class="log-list" v-if="scope.row.recentLogs.length">
              <p v-for="(log, index) in scope.row.recentLogs" :key="index">
                <time>{{ formatTime(log.timestampUtc) }}</time>
                <b>{{ tx(statusText(log.level)) }}</b>
                <span>{{ log.message }}</span>
              </p>
            </div>
            <a-empty v-else :description="tx('暂无脚本日志')" />
          </template>
        </template>
      </template>
      <template #emptyText><a-empty :description="tx(tx('脚本尚未运行'))" /></template>
    </GatewayTable>
  </section>
</template>

<script setup lang="ts">
import { tx, localizeColumns } from '../../i18n'
import { statusText } from '../../utils/display'
import { tagColor } from '../../utils/ui'
import { computed } from 'vue'
import type { GatewayScriptDefinition, ScriptQueueStatus, ScriptRuntimeStatus } from '../../scriptingApi'

const props = defineProps<{
  scripts: GatewayScriptDefinition[]
  statuses: ScriptRuntimeStatus[]
  queue: ScriptQueueStatus
}>()

const rows = computed(() =>
  props.statuses.map((status) => ({
    ...status,
    name: props.scripts.find((script) => script.id === status.scriptId)?.name ?? status.scriptId
  }))
)

function formatTime(value?: string) {
  return value ? new Date(value).toLocaleString() : '-'
}

function stateType(state: string) {
  if (state === 'Succeeded') return 'success'
  if (state === 'Failed' || state === 'TimedOut') return 'danger'
  if (state === 'Running') return 'warning'
  return 'info'
}
</script>

<style scoped>
.queue-cards {
  margin-bottom: 16px;
}
.queue-cards small,
.queue-cards strong {
  display: block;
}
.queue-cards small {
  color: var(--ui-text-color-secondary);
}
.queue-cards strong {
  margin-top: 7px;
  font-size: calc(24px + 1pt);
}
.runtime-error {
  margin-bottom: 14px;
}
.log-list {
  padding: 4px 24px;
}
.log-list p {
  display: grid;
  grid-template-columns: 170px 90px 1fr;
  gap: 10px;
  margin: 6px 0;
}
.log-list time {
  color: var(--ui-text-color-secondary);
}
</style>

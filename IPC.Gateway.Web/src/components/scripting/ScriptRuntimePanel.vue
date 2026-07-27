<template>
  <section>
    <el-row :gutter="14" class="queue-cards">
      <el-col :xs="12" :sm="6"><el-card shadow="never"><small>待写入</small><strong>{{ queue.pendingCount }}</strong></el-card></el-col>
      <el-col :xs="12" :sm="6"><el-card shadow="never"><small>失败文件</small><strong>{{ queue.failedCount }}</strong></el-card></el-col>
      <el-col :xs="12" :sm="6"><el-card shadow="never"><small>成功写入</small><strong>{{ queue.succeededCount }}</strong></el-card></el-col>
      <el-col :xs="12" :sm="6"><el-card shadow="never"><small>重试次数</small><strong>{{ queue.retriedCount }}</strong></el-card></el-col>
    </el-row>
    <el-alert v-if="queue.lastError" :title="queue.lastError" type="error" show-icon :closable="false" class="runtime-error" />

    <el-table :data="rows" empty-text="脚本尚未运行">
      <el-table-column prop="name" label="脚本" min-width="150" />
      <el-table-column label="状态" width="105"><template #default="scope"><el-tag :type="stateType(scope.row.state)">{{ scope.row.state }}</el-tag></template></el-table-column>
      <el-table-column prop="executionCount" label="执行次数" width="95" />
      <el-table-column prop="failureCount" label="失败次数" width="95" />
      <el-table-column label="最近完成" min-width="165"><template #default="scope">{{ formatTime(scope.row.lastFinishedUtc) }}</template></el-table-column>
      <el-table-column prop="lastDurationMilliseconds" label="耗时(ms)" width="100" />
      <el-table-column prop="lastError" label="最近错误" min-width="220" show-overflow-tooltip />
      <el-table-column type="expand">
        <template #default="scope">
          <div class="log-list" v-if="scope.row.recentLogs.length">
            <p v-for="(log, index) in scope.row.recentLogs" :key="index"><time>{{ formatTime(log.timestampUtc) }}</time><b>{{ log.level }}</b><span>{{ log.message }}</span></p>
          </div>
          <el-empty v-else description="暂无脚本日志" :image-size="48" />
        </template>
      </el-table-column>
    </el-table>
  </section>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import type { GatewayScriptDefinition, ScriptQueueStatus, ScriptRuntimeStatus } from '../../scriptingApi'

const props = defineProps<{ scripts: GatewayScriptDefinition[]; statuses: ScriptRuntimeStatus[]; queue: ScriptQueueStatus }>()

const rows = computed(() => props.statuses.map(status => ({
  ...status,
  name: props.scripts.find(script => script.id === status.scriptId)?.name ?? status.scriptId
})))

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
.queue-cards { margin-bottom: 16px; }
.queue-cards small, .queue-cards strong { display: block; }
.queue-cards small { color: var(--el-text-color-secondary); }
.queue-cards strong { margin-top: 7px; font-size: 24px; }
.runtime-error { margin-bottom: 14px; }
.log-list { padding: 4px 24px; }
.log-list p { display: grid; grid-template-columns: 170px 90px 1fr; gap: 10px; margin: 6px 0; }
.log-list time { color: var(--el-text-color-secondary); }
</style>

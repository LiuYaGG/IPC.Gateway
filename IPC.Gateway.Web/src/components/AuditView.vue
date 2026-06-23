<template>
  <section class="view-stack audit-view">
    <section class="audit-toolbar">
      <el-form-item label="目标">
        <el-input v-model="filters.target" clearable placeholder="config:project" @keyup.enter="refreshAudit" />
      </el-form-item>
      <el-form-item label="结果">
        <el-select v-model="filters.outcome" placeholder="全部">
          <el-option label="全部" value="" />
          <el-option label="成功" value="success" />
          <el-option label="参数错误" value="bad_request" />
          <el-option label="未找到" value="not_found" />
          <el-option label="异常" value="error" />
        </el-select>
      </el-form-item>
      <el-form-item label="用户">
        <el-input v-model="filters.username" clearable placeholder="admin" @keyup.enter="refreshAudit" />
      </el-form-item>
      <el-form-item label="时间">
        <el-date-picker
          v-model="timeRange"
          type="datetimerange"
          value-format="YYYY-MM-DDTHH:mm:ss"
          range-separator="-"
          start-placeholder="开始"
          end-placeholder="结束"
          unlink-panels
        />
      </el-form-item>
      <el-form-item label="条数">
        <el-input-number v-model="filters.limit" :min="1" :max="500" :step="50" controls-position="right" />
      </el-form-item>
      <el-button :icon="Refresh" :loading="loading" type="primary" @click="refreshAudit()">刷新</el-button>
      <el-button v-if="canExportAudit" :icon="Download" :loading="exporting" @click="exportCsv">导出</el-button>
      <el-button @click="resetFilters">重置</el-button>
    </section>

    <el-card shadow="never" class="panel-card">
      <template #header>
        <div class="card-header">
          <span>配置审计</span>
          <div class="card-actions">
            <el-tag type="info">{{ rangeLabel }}</el-tag>
            <el-button :disabled="loading || pageOffset <= 0" @click="previousPage">上一页</el-button>
            <el-button :disabled="loading || !page.hasMore" @click="nextPage">下一页</el-button>
          </div>
        </div>
      </template>

      <el-table :data="entries" :loading="loading" height="620" empty-text="暂无审计日志">
        <el-table-column label="时间" min-width="150">
          <template #default="{ row }">{{ formatDateTime(row.timestamp) }}</template>
        </el-table-column>
        <el-table-column label="结果" width="110">
          <template #default="{ row }">
            <el-tag :type="outcomeType(row.outcome)" effect="light">{{ outcomeText(row.outcome) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="存储" width="90">
          <template #default="{ row }">
            <el-tag :type="row.source === 'database' ? 'success' : 'info'" effect="plain">{{ sourceText(row.source) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="target" label="目标" min-width="150" show-overflow-tooltip />
        <el-table-column label="操作者" min-width="150">
          <template #default="{ row }">
            <div class="audit-principal">
              <strong>{{ row.userName || '-' }}</strong>
              <span>{{ row.role || '-' }}</span>
            </div>
          </template>
        </el-table-column>
        <el-table-column prop="remoteIpAddress" label="来源 IP" min-width="130" show-overflow-tooltip />
        <el-table-column label="请求" min-width="220" show-overflow-tooltip>
          <template #default="{ row }">{{ requestText(row) }}</template>
        </el-table-column>
        <el-table-column prop="traceId" label="TraceId" min-width="190" show-overflow-tooltip />
        <el-table-column label="错误" min-width="220" show-overflow-tooltip>
          <template #default="{ row }">
            <span :class="{ 'audit-error': row.errorMessage }">{{ row.errorMessage || '-' }}</span>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </section>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { Download, Refresh } from '@element-plus/icons-vue'
import { exportAuditLogs, loadAuditLogs, type GatewayAuditLogEntry } from '../api'
import { formatDateTime } from '../utils/format'
import { PERMISSIONS, usePermissions } from '../utils/permissions'

const { hasPermission } = usePermissions()
const loading = ref(false)
const exporting = ref(false)
const entries = ref<GatewayAuditLogEntry[]>([])
const timeRange = ref<[string, string] | []>([])
const pageOffset = ref(0)
const page = reactive({
  limit: 100,
  returned: 0,
  hasMore: false
})
const filters = reactive({
  target: '',
  outcome: '',
  username: '',
  limit: 100
})
const canExportAudit = computed(() => hasPermission(PERMISSIONS.auditExport))
const rangeLabel = computed(() => {
  if (!entries.value.length) return '0 条'
  return `${pageOffset.value + 1}-${pageOffset.value + entries.value.length} 条`
})

onMounted(() => refreshAudit())

async function refreshAudit(resetOffset = true) {
  if (resetOffset) pageOffset.value = 0
  loading.value = true
  try {
    const result = await loadAuditLogs(buildQuery())
    entries.value = result.items
    pageOffset.value = result.offset
    page.limit = result.limit
    page.returned = result.returned
    page.hasMore = result.hasMore
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '审计日志加载失败')
  } finally {
    loading.value = false
  }
}

function resetFilters() {
  filters.target = ''
  filters.outcome = ''
  filters.username = ''
  filters.limit = 100
  timeRange.value = []
  pageOffset.value = 0
  refreshAudit()
}

function previousPage() {
  pageOffset.value = Math.max(0, pageOffset.value - filters.limit)
  refreshAudit(false)
}

function nextPage() {
  if (!page.hasMore) return
  pageOffset.value += filters.limit
  refreshAudit(false)
}

async function exportCsv() {
  exporting.value = true
  try {
    const blob = await exportAuditLogs({ ...buildQuery(), offset: 0, limit: 500 })
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = `gateway-audit-${new Date().toISOString().replace(/[:.]/g, '-')}.csv`
    document.body.appendChild(link)
    link.click()
    link.remove()
    window.setTimeout(() => URL.revokeObjectURL(url), 0)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '审计日志导出失败')
  } finally {
    exporting.value = false
  }
}

function buildQuery() {
  const [from, to] = timeRange.value
  return {
    target: filters.target.trim(),
    outcome: filters.outcome,
    username: filters.username.trim(),
    limit: filters.limit,
    offset: pageOffset.value,
    from,
    to
  }
}

function requestText(row: GatewayAuditLogEntry) {
  const method = row.method || '-'
  const path = row.path || '-'
  return `${method} ${path}`
}

function sourceText(source: string) {
  return source === 'database' ? '数据库' : source === 'file' ? '文件' : '-'
}

function outcomeText(outcome: string) {
  switch ((outcome || '').toLowerCase()) {
    case 'success':
      return '成功'
    case 'bad_request':
      return '参数错误'
    case 'not_found':
      return '未找到'
    case 'error':
      return '异常'
    default:
      return outcome || '-'
  }
}

function outcomeType(outcome: string) {
  switch ((outcome || '').toLowerCase()) {
    case 'success':
      return 'success'
    case 'bad_request':
      return 'warning'
    case 'not_found':
      return 'info'
    case 'error':
      return 'danger'
    default:
      return 'info'
  }
}
</script>

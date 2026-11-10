<template>
  <section class="view-stack audit-view">
    <a-form layout="vertical" class="audit-toolbar">
      <a-form-item :label="tx('目标')">
        <a-input
          v-model:value="filters.target"
          allow-clear
          placeholder="config:project"
          @press-enter="refreshAudit()"
        />
      </a-form-item>
      <a-form-item :label="tx('结果')">
        <a-select
          v-model:value="filters.outcome"
          :options="outcomeOptions.map((option) => ({ ...option, label: tx(option.label) }))"
        />
      </a-form-item>
      <a-form-item :label="tx('操作者')">
        <a-input
          v-model:value="filters.username"
          allow-clear
          :placeholder="tx('操作者账号')"
          @press-enter="refreshAudit()"
        />
      </a-form-item>
      <a-form-item :label="tx('时间')">
        <a-range-picker
          v-model:value="timeRange"
          show-time
          value-format="YYYY-MM-DDTHH:mm:ss"
          :placeholder="[tx('开始时间'), tx('结束时间')]"
        />
      </a-form-item>
      <div class="audit-toolbar__actions">
        <a-button :loading="loading" type="primary" @click="refreshAudit()">
          <template #icon>
            <span class="ui-icon"><Refresh /></span>
          </template>
          {{ tx('查询') }}
        </a-button>
        <a-button v-if="canExportAudit" :loading="exporting" @click="exportCsv">
          <template #icon>
            <span class="ui-icon"><Download /></span>
          </template>
          {{ tx('导出') }}
        </a-button>
        <a-button @click="resetFilters">{{ tx('重置') }}</a-button>
      </div>
    </a-form>
    <a-card size="small" :bordered="false" class="panel-card audit-table-panel">
      <template #title>
        <div class="card-header">
          <span>{{ tx('配置审计') }}</span>
          <span class="audit-range-label">{{ tx(rangeLabel) }}</span>
        </div>
      </template>
      <AutoHeightTable
        :paging="false"
        :columns="localizeColumns(columns)"
        :data-source="entries"
        :loading="loading"
        :row-key="auditRowKey"
        :scroll-x="1480"
      >
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'timestamp'">{{ formatDateTime(record.timestamp) }}</template>
          <template v-else-if="column.key === 'outcome'">
            <a-tag :color="outcomeColor(record.outcome)">{{ tx(outcomeText(record.outcome)) }}</a-tag>
          </template>
          <template v-else-if="column.key === 'source'">
            <a-tag :color="record.source === 'database' ? 'success' : 'default'">
              {{ tx(sourceText(record.source)) }}
            </a-tag>
          </template>
          <template v-else-if="column.key === 'principal'">
            <div class="audit-principal">
              <strong>{{ record.userName || '-' }}</strong>
              <span>{{ record.role || '-' }}</span>
            </div>
          </template>
          <template v-else-if="column.key === 'request'">{{ tx(requestText(record)) }}</template>
          <template v-else-if="column.key === 'error'">
            <span :class="{ 'audit-error': record.errorMessage }">{{ tx(record.errorMessage || '-') }}</span>
          </template>
        </template>
        <template #emptyText><a-empty :description="tx('暂无审计日志')" /></template>
      </AutoHeightTable>
      <TablePager
        :total="page.totalCount"
        :current="Math.floor(pageOffset / page.limit) + 1"
        :page-size="page.limit"
        :disabled="loading"
        @change="changeAuditPage"
      />
    </a-card>
  </section>
</template>

<script setup lang="ts">
import { formatMessage } from '../i18n'
import { notify } from '../utils/ui'
import { tx, localizeColumns } from '../i18n'
import { computed, onMounted, reactive, ref } from 'vue'

import { DownloadOutlined as Download, ReloadOutlined as Refresh } from '@ant-design/icons-vue'
import { exportAuditLogs, loadAuditLogs, type GatewayAuditLogEntry } from '../api'
import { formatDateTime } from '../utils/format'
import AutoHeightTable from './AutoHeightTable.vue'
import TablePager from './controls/TablePager.vue'
import { PERMISSIONS, usePermissions } from '../utils/permissions'

const { hasPermission } = usePermissions()
const loading = ref(false)
const exporting = ref(false)
const entries = ref<GatewayAuditLogEntry[]>([])
const timeRange = ref<[string, string] | null>(null)
const pageOffset = ref(0)
let loadedOffset = 0
const page = reactive({
  totalCount: 0,
  limit: 20,
  returned: 0,
  hasMore: false
})
const filters = reactive({
  target: '',
  outcome: '',
  username: '',
  limit: 20
})
const columns = [
  { title: '时间', key: 'timestamp', width: 168 },
  { title: '结果', key: 'outcome', width: 90 },
  { title: '存储', key: 'source', width: 80 },
  { title: '目标', key: 'target', dataIndex: 'target', width: 180, ellipsis: true },
  { title: '操作者', key: 'principal', width: 130 },
  { title: '来源 IP', key: 'ip', dataIndex: 'remoteIpAddress', width: 130, ellipsis: true },
  { title: '请求', key: 'request', width: 240, ellipsis: true },
  { title: 'TraceId', key: 'trace', dataIndex: 'traceId', width: 210, ellipsis: true },
  { title: '错误', key: 'error', width: 220, ellipsis: true }
]
const outcomeOptions = [
  { label: '全部结果', value: '' },
  { label: '成功', value: 'success' },
  { label: '参数错误', value: 'bad_request' },
  { label: '未找到', value: 'not_found' },
  { label: '异常', value: 'error' }
]
const auditRowKey = (record: GatewayAuditLogEntry) => entries.value.indexOf(record)
function outcomeColor(outcome: string) {
  return (
    ({ success: 'success', bad_request: 'warning', not_found: 'default', error: 'error' } as Record<string, string>)[
      (outcome || '').toLowerCase()
    ] ?? 'default'
  )
}
const canExportAudit = computed(() => hasPermission(PERMISSIONS.auditExport))
const rangeLabel = computed(() => {
  if (!entries.value.length) return '0 条'
  return formatMessage('{0}-{1} 条', [pageOffset.value + 1, pageOffset.value + entries.value.length])
})

onMounted(() => refreshAudit())

let auditRequest = 0
async function refreshAudit(resetOffset = true) {
  const requestId = ++auditRequest
  if (resetOffset) pageOffset.value = 0
  loading.value = true
  try {
    const result = await loadAuditLogs(buildQuery())
    if (requestId !== auditRequest) return
    entries.value = result.items
    pageOffset.value = result.offset
    loadedOffset = result.offset
    page.limit = result.limit
    page.returned = result.returned
    page.hasMore = result.hasMore
    page.totalCount = result.totalCount ?? result.offset + result.returned + (result.hasMore ? 1 : 0)
  } catch (error) {
    if (requestId !== auditRequest) return
    pageOffset.value = loadedOffset
    notify.error(error instanceof Error ? error.message : '审计日志加载失败')
  } finally {
    if (requestId === auditRequest) loading.value = false
  }
}

function resetFilters() {
  filters.target = ''
  filters.outcome = ''
  filters.username = ''
  filters.limit = 20
  timeRange.value = null
  pageOffset.value = 0
  refreshAudit()
}

function changeAuditPage(current: number, size: number) {
  filters.limit = size
  pageOffset.value = (current - 1) * size
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
    notify.error(error instanceof Error ? error.message : '审计日志导出失败')
  } finally {
    exporting.value = false
  }
}

function buildQuery() {
  const [from, to] = timeRange.value ?? []
  return {
    target: filters.target.trim(),
    outcome: filters.outcome,
    username: filters.username.trim(),
    limit: Math.min(500, Math.max(1, Number(filters.limit) || 100)),
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

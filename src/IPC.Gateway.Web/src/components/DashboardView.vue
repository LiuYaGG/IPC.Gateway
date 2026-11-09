<template>
  <section class="view-stack">
    <div class="dashboard-hero">
      <section class="dashboard-health-panel" :class="`dashboard-health-panel--${dashboardTone}`">
        <div class="dashboard-health-panel__main">
          <p class="eyebrow">Gateway Health</p>
          <h3>{{ dashboardLead }}</h3>
          <span>{{ status?.projectName || health?.projectName || 'IPC Gateway' }}</span>
        </div>
        <div class="dashboard-rings">
          <GaugeRing
            :label="tx('在线率')"
            :value="onlineRate"
            suffix="%"
            :caption="formatMessage('{0}/{1} 设备', [status?.onlineDeviceCount ?? 0, status?.deviceCount ?? 0])"
            :tone="onlineRate >= 90 ? 'good' : onlineRate >= 60 ? 'warn' : 'bad'"
          />
          <GaugeRing
            :label="tx('采集成功')"
            :value="successRate"
            suffix="%"
            :caption="`${formatNumber(totalSuccessfulReads)} / ${formatNumber(totalReads)}`"
            :tone="successRate >= 95 ? 'good' : successRate >= 80 ? 'warn' : 'bad'"
          />
          <GaugeRing
            :label="tx('资源压力')"
            :value="resourcePressure"
            suffix="%"
            :caption="`CPU ${cpuUsage}% · MEM ${memoryUsage}%`"
            :tone="resourcePressure < 70 ? 'good' : resourcePressure < 85 ? 'warn' : 'bad'"
          />
        </div>
      </section>
      <section class="dashboard-live-panel">
        <div class="dashboard-live-panel__head">
          <div>
            <span>{{ tx('设备状态分布') }}</span>
            <strong>{{ tx(deviceStripLabel) }}</strong>
          </div>
          <a-tag :color="tagColor(healthTagType(health?.status))">
            {{ tx(statusText(health?.status ?? 'UNKNOWN')) }}
          </a-tag>
        </div>
        <div class="device-status-strip" :aria-label="tx(deviceStripLabel)">
          <span
            v-for="segment in deviceStatusSegments"
            :key="segment.key"
            :class="`device-status-strip__segment device-status-strip__segment--${segment.key}`"
            :style="{ flexGrow: segment.count }"
            :title="tx(`${segment.label} ${segment.count}`)"
          />
        </div>
        <div class="dashboard-live-metrics">
          <div>
            <span>{{ tx('采集队列') }}</span>
            <strong>{{ tx(schedulerQueueText) }}</strong>
          </div>
          <div>
            <span>{{ tx('超时') }}</span>
            <strong>{{ schedulerTimeoutCount }}</strong>
          </div>
          <div>
            <span>{{ tx('MQTT 积压') }}</span>
            <strong>{{ status?.mqtt?.outboxPendingCount ?? 0 }}</strong>
          </div>
          <div>
            <span>{{ tx('最近错误') }}</span>
            <strong>{{ status?.recentErrors?.length ?? 0 }}</strong>
          </div>
        </div>
      </section>
    </div>
    <div class="metric-grid">
      <MetricCard
        :label="tx('设备')"
        :value="status?.deviceCount ?? 0"
        :hint="formatMessage('{0} 启用', [status?.enabledDeviceCount ?? 0])"
        :icon="Cpu"
      />
      <MetricCard
        :label="tx('在线')"
        :value="status?.onlineDeviceCount ?? 0"
        :tone="onlineRate >= 90 ? 'good' : 'warn'"
        :hint="formatMessage('{0}% 在线率', [onlineRate])"
        :icon="Connection"
      />
      <MetricCard
        :label="tx('标签')"
        :value="status?.tagCount ?? 0"
        :hint="formatMessage('{0} 正常 · {1} 异常', [status?.goodTagCount ?? 0, status?.badTagCount ?? 0])"
        :icon="DataLine"
      />
      <MetricCard
        :label="tx('就绪')"
        :value="readinessState"
        :tone="readinessTone(health?.status)"
        :icon="CircleCheck"
      />
      <MetricCard
        :label="tx('成功率')"
        :value="`${successRate}%`"
        :tone="successRate >= 95 ? 'good' : 'warn'"
        :icon="DataLine"
      />
      <MetricCard label="CPU" :value="`${cpuUsage}%`" :tone="cpuUsage < 80 ? 'good' : 'warn'" :icon="Odometer" />
      <MetricCard
        :label="tx('内存')"
        :value="`${memoryUsage}%`"
        :tone="memoryUsage < 85 ? 'good' : 'warn'"
        :icon="Monitor"
      />
      <MetricCard
        label="MQTT"
        :value="mqttState"
        :tone="status?.mqtt?.isConnected ? 'good' : 'warn'"
        :icon="Promotion"
      />
      <MetricCard
        :label="tx('规则引擎')"
        :value="ruleState"
        :tone="status?.flowRuleEngine?.isRunning ? 'good' : 'normal'"
        :icon="Operation"
      />
    </div>
    <a-card class="panel-card readiness-card" size="small">
      <template #title>
        <div class="card-header">
          <span>{{ tx('运行就绪') }}</span>
          <a-tag :color="tagColor(healthTagType(health?.status))">
            {{ tx(statusText(health?.status ?? tx('未知'))) }}
          </a-tag>
        </div>
      </template>
      <div v-if="health" class="readiness-layout">
        <div class="readiness-summary">
          <div>
            <span>{{ tx('服务') }}</span>
            <strong>{{ health.service || '-' }}</strong>
          </div>
          <div>
            <span>{{ tx('版本') }}</span>
            <strong>{{ health.version || '-' }}</strong>
          </div>
          <div>
            <span>{{ tx('运行时长') }}</span>
            <strong>{{ formatDurationSeconds(health.uptimeSeconds) }}</strong>
          </div>
          <div>
            <span>{{ tx('采样时间') }}</span>
            <strong>{{ formatDateTime(health.timestamp) }}</strong>
          </div>
          <div>
            <span>{{ tx('组件') }}</span>
            <strong>{{ health.components.length }}</strong>
          </div>
          <div>
            <span>{{ tx('异常') }}</span>
            <strong>{{ healthComponentCounts.unhealthy }} / {{ healthComponentCounts.degraded }}</strong>
          </div>
        </div>
        <a-form v-if="storageHealth" :model="storageHealthDraft" class="readiness-settings" layout="vertical">
          <a-form-item :label="tx('降级可用空间(MB)')">
            <gateway-input
              v-model.number="storageHealthDraft.degradedAvailableMegabytes"
              type="number"
              inputmode="decimal"
              :disabled="savingStorageHealth || !canPersistStorageHealth"
              :min="0"
              :max="1048576"
              :step="128"
            />
          </a-form-item>
          <a-form-item :label="tx('不健康可用空间(MB)')">
            <gateway-input
              v-model.number="storageHealthDraft.unhealthyAvailableMegabytes"
              type="number"
              inputmode="decimal"
              :disabled="savingStorageHealth || !canPersistStorageHealth"
              :min="0"
              :max="1048576"
              :step="64"
            />
          </a-form-item>
          <a-form-item :label="tx('降级可用比例(%)')">
            <gateway-input
              v-model.number="storageHealthDraft.degradedAvailablePercent"
              type="number"
              inputmode="decimal"
              :disabled="savingStorageHealth || !canPersistStorageHealth"
              :min="0"
              :max="100"
              :step="1"
            />
          </a-form-item>
          <a-form-item :label="tx('不健康可用比例(%)')">
            <gateway-input
              v-model.number="storageHealthDraft.unhealthyAvailablePercent"
              type="number"
              inputmode="decimal"
              :disabled="savingStorageHealth || !canPersistStorageHealth"
              :min="0"
              :max="100"
              :step="1"
            />
          </a-form-item>
          <a-form-item label=" ">
            <a-button
              v-if="canPersistStorageHealth"
              type="primary"
              :loading="savingStorageHealth"
              :disabled="!storageHealthDirty"
              @click="persistStorageHealth"
            >
              <template #icon>
                <span class="ui-icon"><component :is="CircleCheck" /></span>
              </template>
              {{ tx('保存阈值') }}
            </a-button>
          </a-form-item>
        </a-form>
        <div class="readiness-components">
          <div
            v-for="component in healthComponents"
            :key="component.name"
            class="readiness-component"
            :class="`readiness-component--${component.status.toLowerCase()}`"
          >
            <div class="readiness-component__head">
              <strong>{{ tx(componentLabel(component.name)) }}</strong>
              <a-tag :color="tagColor(healthTagType(component.status))">{{ tx(statusText(component.status)) }}</a-tag>
            </div>
            <p>{{ component.message || '-' }}</p>
            <small v-if="componentDetail(component)">{{ componentDetail(component) }}</small>
          </div>
        </div>
      </div>
      <a-empty v-else :description="tx('暂无就绪数据')" />
    </a-card>
    <div class="runtime-grid">
      <a-card class="panel-card" size="small">
        <template #title>
          <div class="card-header">
            <span>{{ tx('采集趋势') }}</span>
            <a-tag :color="tagColor('info')">{{ trend.length }} samples</a-tag>
          </div>
        </template>
        <div class="trend-grid">
          <StatusSparkline
            :title="tx('成功率')"
            suffix="%"
            :max="100"
            :values="trend.map((item) => item.successRate)"
          />
          <StatusSparkline
            :title="tx('在线设备')"
            :max="Math.max(status?.deviceCount ?? 1, 1)"
            :values="trend.map((item) => item.onlineDeviceCount)"
          />
          <StatusSparkline :title="tx('异常标签')" :values="trend.map((item) => item.badTagCount)" />
        </div>
      </a-card>
      <a-card class="panel-card" size="small">
        <template #title>
          <div class="card-header">
            <span>{{ tx('模块状态') }}</span>
            <a-tag :color="tagColor(status?.isRunning ? 'success' : 'info')">
              {{ status?.isRunning ? tx('运行中') : tx('已停止') }}
            </a-tag>
          </div>
        </template>
        <div class="module-list">
          <div>
            <span>MQTT</span>
            <strong>{{ status?.mqtt?.broker || '-' }}</strong>
            <a-tag :color="tagColor(status?.mqtt?.isConnected ? 'success' : 'warning')">
              {{ status?.mqtt?.isConnected ? tx('已连接') : tx('未连接') }}
            </a-tag>
          </div>
          <div>
            <span>{{ tx('历史库') }}</span>
            <strong>{{ historySize }}</strong>
            <a-tag :color="tagColor(status?.history?.isRunning ? 'success' : 'info')">
              {{ status?.history?.isRunning ? tx('写入中') : tx('未运行') }}
            </a-tag>
          </div>
          <div>
            <span>{{ tx('规则引擎') }}</span>
            <strong>
              {{ status?.flowRuleEngine?.enabledRuleCount ?? 0 }} / {{ status?.flowRuleEngine?.ruleCount ?? 0 }}
            </strong>
            <a-tag :color="tagColor(status?.flowRuleEngine?.isRunning ? 'success' : 'info')">
              {{ status?.flowRuleEngine?.isRunning ? tx('运行中') : tx('未运行') }}
            </a-tag>
          </div>
          <div>
            <span>{{ tx('系统资源') }}</span>
            <strong>{{ memorySummary }}</strong>
            <a-tag :color="tagColor(cpuUsage < 80 && memoryUsage < 85 ? 'success' : 'warning')">
              CPU {{ cpuUsage }}%
            </a-tag>
          </div>
        </div>
      </a-card>
    </div>
    <a-card class="panel-card" size="small">
      <template #title>
        <div class="card-header">
          <span>{{ tx('设备状态') }}</span>
          <a-tag :color="tagColor('info')">{{ status?.devices?.length ?? 0 }} rows</a-tag>
        </div>
      </template>
      <GatewayTable
        resize-key="components.DashboardView.vue.0"
        :data-source="status?.devices || []"
        row-key="deviceId"
        size="small"
        :pagination="false"
        :columns="
          localizeColumns([
            { key: 'column-0', title: tx('设备'), dataIndex: 'deviceName', width: '150' },
            { key: 'column-1', title: tx('协议'), dataIndex: 'protocol', width: '130' },
            { key: 'column-2', title: tx('状态'), width: '120' },
            { key: 'column-3', title: tx('成功率'), dataIndex: 'successRate', width: '120' },
            { key: 'column-4', title: tx('耗时(ms)'), dataIndex: 'lastTaskDurationMs', width: '110' },
            { key: 'column-5', title: tx('超时'), dataIndex: 'timeoutCount', width: '90' },
            { key: 'column-6', title: tx('最近错误'), dataIndex: 'lastError', width: '240', ellipsis: true }
          ])
        "
        :scroll="{ x: 960, y: 300 }"
      >
        <template #bodyCell="tableCell">
          <template v-if="tableCell.column.key === 'column-2'">
            <template v-for="{ row } in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
              <a-tag :color="tagColor(row.isConnected ? 'success' : row.enabled ? 'warning' : 'info')">
                {{ tx(statusText(row.status)) }}
              </a-tag>
            </template>
          </template>
          <template v-if="tableCell.column.key === 'column-3'">
            <template v-for="{ row } in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
              {{ Number(row.successRate ?? 0).toFixed(1) }}%
            </template>
          </template>
        </template>
      </GatewayTable>
    </a-card>
    <a-card class="panel-card" size="small">
      <template #title>
        <div class="card-header">
          <span>{{ tx('最近错误') }}</span>
          <a-tag :color="tagColor((status?.recentErrors?.length ?? 0) > 0 ? 'danger' : 'success')">
            {{ status?.recentErrors?.length ?? 0 }}
          </a-tag>
        </div>
      </template>
      <GatewayTable
        resize-key="components.DashboardView.vue.1"
        :data-source="status?.recentErrors || []"
        :row-key="(_row: any, index: any) => index"
        size="small"
        :pagination="false"
        :columns="
          localizeColumns([
            { key: 'column-0', title: tx('时间'), dataIndex: 'timestamp', width: '150' },
            { key: 'column-1', title: tx('设备'), dataIndex: 'deviceName', width: '140' },
            { key: 'column-2', title: tx('标签'), dataIndex: 'tagName', width: '140' },
            { key: 'column-3', title: tx('错误'), dataIndex: 'message', width: '260', ellipsis: true },
            { key: 'column-4', title: tx('操作'), width: '90' }
          ])
        "
        :scroll="{ x: 780, y: 240 }"
        :custom-row="(record: any) => ({ onClick: () => emit('select-error', record) })"
      >
        <template #bodyCell="tableCell">
          <template v-if="tableCell.column.key === 'column-0'">
            <template v-for="{ row } in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
              {{ formatDateTime(row.timestamp) }}
            </template>
          </template>
          <template v-if="tableCell.column.key === 'column-4'">
            <template v-for="{ row } in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
              <div class="table-actions table-actions--compact">
                <a-button size="small" @click.stop="emit('select-error', row)" type="link">{{ tx('详情') }}</a-button>
              </div>
            </template>
          </template>
        </template>
      </GatewayTable>
    </a-card>
  </section>
</template>

<script setup lang="ts">
import { formatMessage } from '../i18n'
import { tx, localizeColumns } from '../i18n'
import { statusText } from '../utils/display'
import { tagColor } from '../utils/ui'
import { computed, reactive, watch } from 'vue'
import {
  CheckCircleOutlined as CircleCheck,
  ApartmentOutlined as Connection,
  DeploymentUnitOutlined as Cpu,
  LineChartOutlined as DataLine,
  DesktopOutlined as Monitor,
  DashboardOutlined as Odometer,
  AppstoreOutlined as Operation,
  SendOutlined as Promotion
} from '@ant-design/icons-vue'
import type {
  GatewayHealthComponent,
  GatewayHealthResponse,
  GatewayStatus,
  RuntimeErrorDetail,
  StorageHealthConfig
} from '../api'
import { formatBytes, formatDateTime, formatDurationSeconds, formatNumber } from '../utils/format'
import MetricCard from './MetricCard.vue'
import StatusSparkline from './StatusSparkline.vue'

export interface TrendSample {
  timestamp: number
  successRate: number
  onlineDeviceCount: number
  badTagCount: number
}

const props = defineProps<{
  status: GatewayStatus | null
  health: GatewayHealthResponse | null
  storageHealth: StorageHealthConfig | null
  savingStorageHealth: boolean
  canPersistStorageHealth?: boolean
  trend: TrendSample[]
}>()

const emit = defineEmits<{
  'select-error': [error: RuntimeErrorDetail]
  'persist-storage-health': [options: StorageHealthConfig]
}>()

const storageHealthDraft = reactive<StorageHealthConfig>({
  degradedAvailableMegabytes: 0,
  unhealthyAvailableMegabytes: 0,
  degradedAvailablePercent: 0,
  unhealthyAvailablePercent: 0
})

const storageHealthDirty = computed(() =>
  props.storageHealth ? !sameStorageHealth(storageHealthDraft, props.storageHealth) : false
)

watch(
  () => props.storageHealth,
  (value, oldValue) => {
    if (!value) return
    if (oldValue && storageHealthDirty.value) return
    Object.assign(storageHealthDraft, cloneStorageHealth(value))
  },
  { immediate: true }
)

function persistStorageHealth() {
  emit('persist-storage-health', cloneStorageHealth(storageHealthDraft))
}

function cloneStorageHealth(value: StorageHealthConfig): StorageHealthConfig {
  return {
    degradedAvailableMegabytes: Number(value.degradedAvailableMegabytes ?? 0),
    unhealthyAvailableMegabytes: Number(value.unhealthyAvailableMegabytes ?? 0),
    degradedAvailablePercent: Number(value.degradedAvailablePercent ?? 0),
    unhealthyAvailablePercent: Number(value.unhealthyAvailablePercent ?? 0)
  }
}

function sameStorageHealth(left: StorageHealthConfig, right: StorageHealthConfig) {
  return (
    Number(left.degradedAvailableMegabytes ?? 0) === Number(right.degradedAvailableMegabytes ?? 0) &&
    Number(left.unhealthyAvailableMegabytes ?? 0) === Number(right.unhealthyAvailableMegabytes ?? 0) &&
    Number(left.degradedAvailablePercent ?? 0) === Number(right.degradedAvailablePercent ?? 0) &&
    Number(left.unhealthyAvailablePercent ?? 0) === Number(right.unhealthyAvailablePercent ?? 0)
  )
}

const successRate = computed(() => {
  const devices = props.status?.devices ?? []
  if (devices.length === 0) return 0
  const total = devices.reduce((sum, item) => sum + (item.successRate || 0), 0)
  return Number((total / devices.length).toFixed(1))
})

const mqttState = computed(() =>
  props.status?.mqtt?.isConnected ? '已连接' : props.status?.mqtt?.isRunning ? '重连中' : '未运行'
)
const ruleState = computed(() => formatMessage('{0} 个活跃', [props.status?.flowRuleEngine?.activeRuleCount ?? 0]))
const readinessState = computed(() => statusText(props.health?.status))
const healthComponents = computed(() => props.health?.components ?? [])
const healthComponentCounts = computed(() => {
  const components = props.health?.components ?? []
  return {
    unhealthy: components.filter((item) => normalizeStatus(item.status) === 'unhealthy').length,
    degraded: components.filter((item) => normalizeStatus(item.status) === 'degraded').length
  }
})
const historySize = computed(() => formatBytes(props.status?.history?.totalBytes ?? 0))
const cpuUsage = computed(() => Number((props.status?.system?.cpuUsagePercent ?? 0).toFixed(1)))
const memoryUsage = computed(() => Number((props.status?.system?.memoryUsagePercent ?? 0).toFixed(1)))
const memorySummary = computed(() => {
  const used = formatBytes(props.status?.system?.usedMemoryBytes ?? 0)
  const total = formatBytes(props.status?.system?.totalMemoryBytes ?? 0)
  return `${used} / ${total}`
})
const totalReads = computed(() => (props.status?.devices ?? []).reduce((sum, item) => sum + (item.totalReads || 0), 0))
const totalSuccessfulReads = computed(() =>
  (props.status?.devices ?? []).reduce((sum, item) => sum + (item.successfulReads || 0), 0)
)
const onlineRate = computed(() => {
  const total = props.status?.deviceCount ?? 0
  if (total <= 0) return 0
  return Number((((props.status?.onlineDeviceCount ?? 0) / total) * 100).toFixed(1))
})
const resourcePressure = computed(() => Number(Math.max(cpuUsage.value, memoryUsage.value).toFixed(1)))
const schedulerQueueText = computed(() => {
  const queue = props.status?.scheduler?.queue
  if (!queue) return '0 / 0'
  return `${queue.pendingCount ?? 0} / ${queue.queueLimit ?? 0}`
})
const schedulerTimeoutCount = computed(() => {
  const timeout = props.status?.scheduler?.timeout
  return (timeout?.recentPollTimeoutCount ?? 0) + (timeout?.recentReadTimeoutCount ?? 0)
})
const dashboardTone = computed<CardTone>(() => readinessTone(props.health?.status))
const dashboardLead = computed(() => {
  const normalized = normalizeStatus(props.health?.status)
  if (normalized === 'healthy') return '系统运行平稳'
  if (normalized === 'degraded') return '系统处于降级状态'
  if (normalized === 'unhealthy') return '系统需要处理'
  return props.status?.isRunning ? '正在采集运行数据' : '网关未运行'
})
const deviceStatusSegments = computed(() => {
  const devices = props.status?.devices ?? []
  const online = devices.filter((item) => item.isConnected).length
  const disabled = devices.filter((item) => !item.enabled).length
  const offline = devices.filter((item) => item.enabled && !item.isConnected).length
  const segments = [
    { key: 'online', label: '在线', count: online },
    { key: 'offline', label: '离线', count: offline },
    { key: 'disabled', label: '停用', count: disabled }
  ]
  if (devices.length === 0) return [{ key: 'empty', label: '暂无设备', count: 1 }]
  return segments.filter((item) => item.count > 0)
})
const deviceStripLabel = computed(() =>
  deviceStatusSegments.value.map((item) => `${item.label} ${item.count}`).join(' · ')
)

type TagType = 'success' | 'warning' | 'danger' | 'info'
type CardTone = 'normal' | 'good' | 'warn' | 'bad'

const componentLabels: Record<string, string> = {
  gateway: '网关',
  configuration: '配置',
  mqtt: 'MQTT',
  mqttOutboxStorage: 'MQTT 缓存磁盘',
  history: '历史库',
  historyStorage: '历史库磁盘',
  ruleEngine: '规则引擎',
  scheduler: '采集调度',
  systemResources: '系统资源'
}

function readinessTone(status?: string): CardTone {
  const normalized = normalizeStatus(status)
  if (normalized === 'healthy') return 'good'
  if (normalized === 'degraded') return 'warn'
  if (normalized === 'unhealthy') return 'bad'
  return 'normal'
}

function healthTagType(status?: string): TagType {
  const normalized = normalizeStatus(status)
  if (normalized === 'healthy') return 'success'
  if (normalized === 'degraded') return 'warning'
  if (normalized === 'unhealthy') return 'danger'
  return 'info'
}

function componentLabel(name: string) {
  return componentLabels[name] ?? name
}

function componentDetail(component: GatewayHealthComponent) {
  const data = component.data ?? {}
  if (component.name === 'mqttOutboxStorage' || component.name === 'historyStorage') {
    const path = readText(data, 'path') || '-'
    const available = formatBytes(readNumber(data, 'availableBytes'))
    const total = formatBytes(readNumber(data, 'totalBytes'))
    const usage = formatNumber(readNumber(data, 'usagePercent'), 1)
    const degraded = formatBytes(readNumber(data, 'degradedAvailableBytes'))
    const unhealthy = formatBytes(readNumber(data, 'unhealthyAvailableBytes'))
    return formatMessage('{0} · 可用 {1} / {2} · 使用率 {3}% · 阈值 {4} / {5}', [
      path,
      available,
      total,
      usage,
      degraded,
      unhealthy
    ])
  }

  if (component.name === 'mqtt') {
    return formatMessage('积压 {0} · 最老 {1} · 隔离 {2} · 连续失败 {3}', [
      readNumber(data, 'outboxPendingCount'),
      formatDurationSeconds(readNumber(data, 'outboxOldestPendingAgeSeconds')),
      readNumber(data, 'outboxQuarantineCount'),
      readNumber(data, 'publishConsecutiveFailureCount')
    ])
  }

  if (component.name === 'systemResources') {
    const cpu = formatNumber(readNumber(data, 'cpuUsagePercent'), 1)
    const memory = formatNumber(readNumber(data, 'memoryUsagePercent'), 1)
    const workers = readNumber(data, 'threadPoolAvailableWorkerThreads')
    const maxWorkers = readNumber(data, 'threadPoolMaxWorkerThreads')
    const workingSet = formatBytes(readNumber(data, 'processWorkingSetBytes'))
    return formatMessage('CPU {0}% · 内存 {1}% · Worker {2}/{3} · 进程 {4}', [
      cpu,
      memory,
      workers,
      maxWorkers,
      workingSet
    ])
  }

  if (component.name === 'scheduler') {
    const timeoutCount = readNumber(data, 'recentPollTimeoutCount') + readNumber(data, 'recentReadTimeoutCount')
    const pressure = readNumber(data, 'utilizationPercent')
    const backpressure = readBoolean(data, 'backpressureActive') || readBoolean(data, 'queueBackpressureActive')
    const pressureText = backpressure ? '背压中' : formatMessage('水位 {0}%', [formatNumber(pressure, 1)])
    return formatMessage('队列 {0} / {1} · {2} · 运行 {3} · 限流 {4} · 延后 {5} · 拒绝 {6} · 超时 {7}', [
      readNumber(data, 'pendingCount'),
      readNumber(data, 'queueLimit'),
      pressureText,
      readNumber(data, 'runningCount'),
      readNumber(data, 'rateLimitedCount'),
      readNumber(data, 'backpressureThrottledCount'),
      readNumber(data, 'rejectedCount'),
      timeoutCount
    ])
  }

  if (component.name === 'history') {
    const files = readNumber(data, 'valueFiles') + readNumber(data, 'alarmFiles') + readNumber(data, 'publishFiles')
    return formatMessage('{0} · 占用 {1} · 文件 {2}', [
      readText(data, 'directory') || '-',
      formatBytes(readNumber(data, 'totalBytes')),
      files
    ])
  }

  if (component.name === 'ruleEngine') {
    return formatMessage('启用 {0} / {1} · 活跃 {2} · 失败 {3}', [
      readNumber(data, 'enabledRuleCount'),
      readNumber(data, 'ruleCount'),
      readNumber(data, 'activeRuleCount'),
      readNumber(data, 'failedEvaluationCount')
    ])
  }

  if (component.name === 'configuration') {
    return formatMessage('错误 {0} · 警告 {1}', [readArrayLength(data, 'errors'), readArrayLength(data, 'warnings')])
  }

  if (component.name === 'gateway') {
    return `${readText(data, 'projectName') || props.status?.projectName || '-'} · ${readText(data, 'configurationStore') || '-'}`
  }

  return component.message
}

function normalizeStatus(status?: string) {
  return (status ?? '').trim().toLowerCase()
}

function readNumber(data: Record<string, unknown>, key: string) {
  const value = data[key]
  if (typeof value === 'number' && Number.isFinite(value)) return value
  if (typeof value === 'string') {
    const parsed = Number(value)
    if (Number.isFinite(parsed)) return parsed
  }
  return 0
}

function readText(data: Record<string, unknown>, key: string) {
  const value = data[key]
  return typeof value === 'string' ? value : ''
}

function readBoolean(data: Record<string, unknown>, key: string) {
  const value = data[key]
  if (typeof value === 'boolean') return value
  if (typeof value === 'string') return value.toLowerCase() === 'true'
  return false
}

function readArrayLength(data: Record<string, unknown>, key: string) {
  const value = data[key]
  return Array.isArray(value) ? value.length : 0
}
</script>

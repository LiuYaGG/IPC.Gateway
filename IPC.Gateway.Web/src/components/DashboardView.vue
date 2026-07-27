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
            label="在线率"
            :value="onlineRate"
            suffix="%"
            :caption="`${status?.onlineDeviceCount ?? 0}/${status?.deviceCount ?? 0} 设备`"
            :tone="onlineRate >= 90 ? 'good' : onlineRate >= 60 ? 'warn' : 'bad'"
          />
          <GaugeRing
            label="采集成功"
            :value="successRate"
            suffix="%"
            :caption="`${formatNumber(totalSuccessfulReads)} / ${formatNumber(totalReads)}`"
            :tone="successRate >= 95 ? 'good' : successRate >= 80 ? 'warn' : 'bad'"
          />
          <GaugeRing
            label="资源压力"
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
            <span>设备状态分布</span>
            <strong>{{ deviceStripLabel }}</strong>
          </div>
          <el-tag size="small" :type="healthTagType(health?.status)">
            {{ health?.status ?? 'UNKNOWN' }}
          </el-tag>
        </div>
        <div class="device-status-strip" :aria-label="deviceStripLabel">
          <span
            v-for="segment in deviceStatusSegments"
            :key="segment.key"
            :class="`device-status-strip__segment device-status-strip__segment--${segment.key}`"
            :style="{ flexGrow: segment.count }"
            :title="`${segment.label} ${segment.count}`"
          />
        </div>
        <div class="dashboard-live-metrics">
          <div>
            <span>采集队列</span>
            <strong>{{ schedulerQueueText }}</strong>
          </div>
          <div>
            <span>超时</span>
            <strong>{{ schedulerTimeoutCount }}</strong>
          </div>
          <div>
            <span>MQTT 积压</span>
            <strong>{{ status?.mqtt?.outboxPendingCount ?? 0 }}</strong>
          </div>
          <div>
            <span>最近错误</span>
            <strong>{{ status?.recentErrors?.length ?? 0 }}</strong>
          </div>
        </div>
      </section>
    </div>

    <div class="metric-grid">
      <MetricCard label="设备" :value="status?.deviceCount ?? 0" :hint="`${status?.enabledDeviceCount ?? 0} 启用`" :icon="Cpu" />
      <MetricCard label="在线" :value="status?.onlineDeviceCount ?? 0" :tone="onlineRate >= 90 ? 'good' : 'warn'" :hint="`${onlineRate}% 在线率`" :icon="Connection" />
      <MetricCard label="标签" :value="status?.tagCount ?? 0" :hint="`${status?.goodTagCount ?? 0} 正常 · ${status?.badTagCount ?? 0} 异常`" :icon="DataLine" />
      <MetricCard label="就绪" :value="readinessState" :tone="readinessTone(health?.status)" :icon="CircleCheck" />
      <MetricCard label="成功率" :value="`${successRate}%`" :tone="successRate >= 95 ? 'good' : 'warn'" :icon="DataLine" />
      <MetricCard label="CPU" :value="`${cpuUsage}%`" :tone="cpuUsage < 80 ? 'good' : 'warn'" :icon="Odometer" />
      <MetricCard label="内存" :value="`${memoryUsage}%`" :tone="memoryUsage < 85 ? 'good' : 'warn'" :icon="Monitor" />
      <MetricCard label="MQTT" :value="mqttState" :tone="status?.mqtt?.isConnected ? 'good' : 'warn'" :icon="Promotion" />
      <MetricCard label="规则引擎" :value="ruleState" :tone="status?.flowRuleEngine?.isRunning ? 'good' : 'normal'" :icon="Operation" />
    </div>

    <el-card shadow="never" class="panel-card readiness-card">
      <template #header>
        <div class="card-header">
          <span>运行就绪</span>
          <el-tag size="small" :type="healthTagType(health?.status)">
            {{ health?.status ?? '未知' }}
          </el-tag>
        </div>
      </template>

      <div v-if="health" class="readiness-layout">
        <div class="readiness-summary">
          <div>
            <span>服务</span>
            <strong>{{ health.service || '-' }}</strong>
          </div>
          <div>
            <span>版本</span>
            <strong>{{ health.version || '-' }}</strong>
          </div>
          <div>
            <span>运行时长</span>
            <strong>{{ formatDurationSeconds(health.uptimeSeconds) }}</strong>
          </div>
          <div>
            <span>采样时间</span>
            <strong>{{ formatDateTime(health.timestamp) }}</strong>
          </div>
          <div>
            <span>组件</span>
            <strong>{{ health.components.length }}</strong>
          </div>
          <div>
            <span>异常</span>
            <strong>{{ healthComponentCounts.unhealthy }} / {{ healthComponentCounts.degraded }}</strong>
          </div>
        </div>

        <el-form v-if="storageHealth" :model="storageHealthDraft" label-position="top" class="readiness-settings">
          <el-form-item label="降级可用空间(MB)">
            <el-input v-model.number="storageHealthDraft.degradedAvailableMegabytes" type="number" inputmode="decimal" :disabled="savingStorageHealth || !canPersistStorageHealth" :min="0" :max="1048576" :step="128" />
          </el-form-item>
          <el-form-item label="不健康可用空间(MB)">
            <el-input v-model.number="storageHealthDraft.unhealthyAvailableMegabytes" type="number" inputmode="decimal" :disabled="savingStorageHealth || !canPersistStorageHealth" :min="0" :max="1048576" :step="64" />
          </el-form-item>
          <el-form-item label="降级可用比例(%)">
            <el-input v-model.number="storageHealthDraft.degradedAvailablePercent" type="number" inputmode="decimal" :disabled="savingStorageHealth || !canPersistStorageHealth" :min="0" :max="100" :step="1" />
          </el-form-item>
          <el-form-item label="不健康可用比例(%)">
            <el-input v-model.number="storageHealthDraft.unhealthyAvailablePercent" type="number" inputmode="decimal" :disabled="savingStorageHealth || !canPersistStorageHealth" :min="0" :max="100" :step="1" />
          </el-form-item>
          <el-form-item label=" ">
            <el-button v-if="canPersistStorageHealth" type="primary" :icon="CircleCheck" :loading="savingStorageHealth" :disabled="!storageHealthDirty" @click="persistStorageHealth">保存阈值</el-button>
          </el-form-item>
        </el-form>

        <div class="readiness-components">
          <div
            v-for="component in healthComponents"
            :key="component.name"
            class="readiness-component"
            :class="`readiness-component--${component.status.toLowerCase()}`"
          >
            <div class="readiness-component__head">
              <strong>{{ componentLabel(component.name) }}</strong>
              <el-tag size="small" :type="healthTagType(component.status)">
                {{ component.status }}
              </el-tag>
            </div>
            <p>{{ component.message || '-' }}</p>
            <small v-if="componentDetail(component)">{{ componentDetail(component) }}</small>
          </div>
        </div>
      </div>

      <el-empty v-else description="暂无就绪数据" />
    </el-card>

    <div class="runtime-grid">
      <el-card shadow="never" class="panel-card">
        <template #header>
          <div class="card-header">
            <span>采集趋势</span>
            <el-tag size="small" type="info">{{ trend.length }} samples</el-tag>
          </div>
        </template>
        <div class="trend-grid">
          <StatusSparkline title="成功率" suffix="%" :max="100" :values="trend.map(item => item.successRate)" />
          <StatusSparkline title="在线设备" :max="Math.max(status?.deviceCount ?? 1, 1)" :values="trend.map(item => item.onlineDeviceCount)" />
          <StatusSparkline title="异常标签" :values="trend.map(item => item.badTagCount)" />
        </div>
      </el-card>

      <el-card shadow="never" class="panel-card">
        <template #header>
          <div class="card-header">
            <span>模块状态</span>
            <el-tag size="small" :type="status?.isRunning ? 'success' : 'info'">
              {{ status?.isRunning ? '运行中' : '已停止' }}
            </el-tag>
          </div>
        </template>
        <div class="module-list">
          <div>
            <span>MQTT</span>
            <strong>{{ status?.mqtt?.broker || '-' }}</strong>
            <el-tag size="small" :type="status?.mqtt?.isConnected ? 'success' : 'warning'">
              {{ status?.mqtt?.isConnected ? '已连接' : '未连接' }}
            </el-tag>
          </div>
          <div>
            <span>历史库</span>
            <strong>{{ historySize }}</strong>
            <el-tag size="small" :type="status?.history?.isRunning ? 'success' : 'info'">
              {{ status?.history?.isRunning ? '写入中' : '未运行' }}
            </el-tag>
          </div>
          <div>
            <span>规则引擎</span>
            <strong>{{ status?.flowRuleEngine?.enabledRuleCount ?? 0 }} / {{ status?.flowRuleEngine?.ruleCount ?? 0 }}</strong>
            <el-tag size="small" :type="status?.flowRuleEngine?.isRunning ? 'success' : 'info'">
              {{ status?.flowRuleEngine?.isRunning ? '运行中' : '未运行' }}
            </el-tag>
          </div>
          <div>
            <span>系统资源</span>
            <strong>{{ memorySummary }}</strong>
            <el-tag size="small" :type="cpuUsage < 80 && memoryUsage < 85 ? 'success' : 'warning'">
              CPU {{ cpuUsage }}%
            </el-tag>
          </div>
        </div>
      </el-card>
    </div>

    <el-card shadow="never" class="panel-card">
      <template #header>
        <div class="card-header">
          <span>设备状态</span>
          <el-tag size="small" type="info">{{ status?.devices?.length ?? 0 }} rows</el-tag>
        </div>
      </template>
      <el-table :data="status?.devices || []" height="300" row-key="deviceId">
        <el-table-column prop="deviceName" label="设备" min-width="150" />
        <el-table-column prop="protocol" label="协议" width="130" />
        <el-table-column label="状态" width="120">
          <template #default="{ row }">
            <el-tag :type="row.isConnected ? 'success' : row.enabled ? 'warning' : 'info'">
              {{ row.status }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="successRate" label="成功率" width="120">
          <template #default="{ row }">{{ Number(row.successRate ?? 0).toFixed(1) }}%</template>
        </el-table-column>
        <el-table-column prop="lastTaskDurationMs" label="耗时(ms)" width="110" />
        <el-table-column prop="timeoutCount" label="超时" width="90" />
        <el-table-column prop="lastError" label="最近错误" min-width="240" show-overflow-tooltip />
      </el-table>
    </el-card>

    <el-card shadow="never" class="panel-card">
      <template #header>
        <div class="card-header">
          <span>最近错误</span>
          <el-tag size="small" :type="(status?.recentErrors?.length ?? 0) > 0 ? 'danger' : 'success'">
            {{ status?.recentErrors?.length ?? 0 }}
          </el-tag>
        </div>
      </template>
      <el-table :data="status?.recentErrors || []" height="240" @row-click="emit('select-error', $event)">
        <el-table-column prop="timestamp" label="时间" width="150">
          <template #default="{ row }">{{ formatDateTime(row.timestamp) }}</template>
        </el-table-column>
        <el-table-column prop="deviceName" label="设备" width="140" />
        <el-table-column prop="tagName" label="标签" width="140" />
        <el-table-column prop="message" label="错误" min-width="260" show-overflow-tooltip />
        <el-table-column label="操作" width="90">
          <template #default="{ row }">
            <div class="table-actions table-actions--compact">
              <el-button size="small" text type="primary" @click.stop="emit('select-error', row)">详情</el-button>
            </div>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </section>
</template>

<script setup lang="ts">
import { computed, reactive, watch } from 'vue'
import { CircleCheck, Connection, Cpu, DataLine, Monitor, Odometer, Operation, Promotion } from '@element-plus/icons-vue'
import type { GatewayHealthComponent, GatewayHealthResponse, GatewayStatus, RuntimeErrorDetail, StorageHealthConfig } from '../api'
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

const storageHealthDirty = computed(() => props.storageHealth ? !sameStorageHealth(storageHealthDraft, props.storageHealth) : false)

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
  return Number(left.degradedAvailableMegabytes ?? 0) === Number(right.degradedAvailableMegabytes ?? 0) &&
    Number(left.unhealthyAvailableMegabytes ?? 0) === Number(right.unhealthyAvailableMegabytes ?? 0) &&
    Number(left.degradedAvailablePercent ?? 0) === Number(right.degradedAvailablePercent ?? 0) &&
    Number(left.unhealthyAvailablePercent ?? 0) === Number(right.unhealthyAvailablePercent ?? 0)
}

const successRate = computed(() => {
  const devices = props.status?.devices ?? []
  if (devices.length === 0) return 0
  const total = devices.reduce((sum, item) => sum + (item.successRate || 0), 0)
  return Number((total / devices.length).toFixed(1))
})

const mqttState = computed(() => (props.status?.mqtt?.isConnected ? '已连接' : props.status?.mqtt?.isRunning ? '重连中' : '未运行'))
const ruleState = computed(() => `${props.status?.flowRuleEngine?.activeRuleCount ?? 0} active`)
const readinessState = computed(() => props.health?.status ?? '未知')
const healthComponents = computed(() => props.health?.components ?? [])
const healthComponentCounts = computed(() => {
  const components = props.health?.components ?? []
  return {
    unhealthy: components.filter(item => normalizeStatus(item.status) === 'unhealthy').length,
    degraded: components.filter(item => normalizeStatus(item.status) === 'degraded').length
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
const totalSuccessfulReads = computed(() => (props.status?.devices ?? []).reduce((sum, item) => sum + (item.successfulReads || 0), 0))
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
  const online = devices.filter(item => item.isConnected).length
  const disabled = devices.filter(item => !item.enabled).length
  const offline = devices.filter(item => item.enabled && !item.isConnected).length
  const segments = [
    { key: 'online', label: '在线', count: online },
    { key: 'offline', label: '离线', count: offline },
    { key: 'disabled', label: '停用', count: disabled }
  ]
  if (devices.length === 0) return [{ key: 'empty', label: '暂无设备', count: 1 }]
  return segments.filter(item => item.count > 0)
})
const deviceStripLabel = computed(() => deviceStatusSegments.value.map(item => `${item.label} ${item.count}`).join(' · '))

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
    return `${path} · 可用 ${available} / ${total} · 使用率 ${usage}% · 阈值 ${degraded} / ${unhealthy}`
  }

  if (component.name === 'mqtt') {
    return `积压 ${readNumber(data, 'outboxPendingCount')} · 最老 ${formatDurationSeconds(readNumber(data, 'outboxOldestPendingAgeSeconds'))} · 隔离 ${readNumber(data, 'outboxQuarantineCount')} · 连续失败 ${readNumber(data, 'publishConsecutiveFailureCount')}`
  }

  if (component.name === 'systemResources') {
    const cpu = formatNumber(readNumber(data, 'cpuUsagePercent'), 1)
    const memory = formatNumber(readNumber(data, 'memoryUsagePercent'), 1)
    const workers = readNumber(data, 'threadPoolAvailableWorkerThreads')
    const maxWorkers = readNumber(data, 'threadPoolMaxWorkerThreads')
    const workingSet = formatBytes(readNumber(data, 'processWorkingSetBytes'))
    return `CPU ${cpu}% · 内存 ${memory}% · Worker ${workers}/${maxWorkers} · 进程 ${workingSet}`
  }

  if (component.name === 'scheduler') {
    const timeoutCount = readNumber(data, 'recentPollTimeoutCount') + readNumber(data, 'recentReadTimeoutCount')
    const pressure = readNumber(data, 'utilizationPercent')
    const backpressure = readBoolean(data, 'backpressureActive') || readBoolean(data, 'queueBackpressureActive')
    const pressureText = backpressure ? '背压中' : `水位 ${formatNumber(pressure, 1)}%`
    return `队列 ${readNumber(data, 'pendingCount')} / ${readNumber(data, 'queueLimit')} · ${pressureText} · 运行 ${readNumber(data, 'runningCount')} · 限流 ${readNumber(data, 'rateLimitedCount')} · 延后 ${readNumber(data, 'backpressureThrottledCount')} · 拒绝 ${readNumber(data, 'rejectedCount')} · 超时 ${timeoutCount}`
  }

  if (component.name === 'history') {
    const files = readNumber(data, 'valueFiles') + readNumber(data, 'alarmFiles') + readNumber(data, 'publishFiles')
    return `${readText(data, 'directory') || '-'} · 占用 ${formatBytes(readNumber(data, 'totalBytes'))} · 文件 ${files}`
  }

  if (component.name === 'ruleEngine') {
    return `启用 ${readNumber(data, 'enabledRuleCount')} / ${readNumber(data, 'ruleCount')} · 活跃 ${readNumber(data, 'activeRuleCount')} · 失败 ${readNumber(data, 'failedEvaluationCount')}`
  }

  if (component.name === 'configuration') {
    return `错误 ${readArrayLength(data, 'errors')} · 警告 ${readArrayLength(data, 'warnings')}`
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

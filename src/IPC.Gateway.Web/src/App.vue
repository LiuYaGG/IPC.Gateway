<template>
  <a-config-provider :theme="gatewayTheme" :locale="antLocale" component-size="small">
    <main v-if="!authenticated" class="login-shell" :inert="upgradeUploadState.active || undefined">
      <LoginMonetBackdrop />
      <header class="login-brand">
        <strong>IPC Gateway</strong>
        <span>{{ tx('工业边缘网关') }}</span>
      </header>
      <LanguageSwitcher class="login-language" />
      <section class="login-panel">
        <div class="login-panel__header">
          <div class="login-panel__brand"><GatewayLogo /><p class="eyebrow">IPC Gateway</p></div>
          <h1>{{ tx('边缘计算网关') }}</h1>
          <span>Manufacturing Edge Console</span>
        </div>
        <div class="login-status-strip" aria-hidden="true">
          <span>
            <i></i>
            MQTT TLS
          </span>
          <span>
            <i></i>
            OPC UA
          </span>
          <span>
            <i></i>
            Rules
          </span>
        </div>
        <a-form :model="loginForm" @submit.prevent="handleLogin" layout="vertical">
          <a-form-item :label="tx('用户名')">
            <gateway-input v-model="loginForm.username" autocomplete="username" />
          </a-form-item>
          <a-form-item :label="tx('密码')">
            <gateway-input v-model="loginForm.password" type="password" autocomplete="current-password" show-password />
          </a-form-item>
          <LoginCaptcha ref="captchaRef" />
          <a-button type="primary" :loading="loading" class="full-button" @click="handleLogin">
            {{ tx('登录') }}
          </a-button>
        </a-form>
      </section>
    </main>
    <a-layout v-else class="app-shell" :inert="upgradeUploadState.active || undefined" :aria-busy="upgradeUploadState.active">
      <a-layout-sider
        :width="208"
        :collapsed-width="64"
        :collapsed="sidebarCollapsed"
        :class="['sidebar', { 'sidebar--collapsed': sidebarCollapsed }]"
        :trigger="null"
      >
        <div class="brand">
          <span class="brand__logo ui-icon"><Cpu /></span>
          <div v-if="!sidebarCollapsed" class="brand__text">
            <span class="brand__name">IPC Gateway</span>
            <small>{{ tx('工业边缘网关') }}</small>
          </div>
        </div>
        <GatewayNavigation v-model="activeView" :collapsed="sidebarCollapsed" :can-access="canAccessView" />
      </a-layout-sider>
      <a-layout class="workspace-shell">
        <a-layout-header class="topbar">
          <div class="topbar__navigation">
            <a-tooltip :title="tx(sidebarCollapsed ? tx('展开菜单') : tx('收起菜单'))">
              <a-button
                type="text"
                class="sidebar-toggle"
                :aria-label="tx(sidebarCollapsed ? tx('展开菜单') : tx('收起菜单'))"
                @click="toggleSidebar"
              >
                <span class="ui-icon"><component :is="sidebarCollapsed ? Expand : Fold" /></span>
              </a-button>
            </a-tooltip>
            <a-breadcrumb>
              <a-breadcrumb-item>
                <span class="ui-icon"><HomeFilled /></span>
              </a-breadcrumb-item>
              <a-breadcrumb-item>{{ tx(pageSection) }}</a-breadcrumb-item>
              <a-breadcrumb-item>{{ tx(pageTitle) }}</a-breadcrumb-item>
            </a-breadcrumb>
          </div>
          <div class="top-actions">
            <LanguageSwitcher />
            <div class="refresh-state">
              <span>{{ autoRefresh ? tx('实时推送') : tx('手动刷新') }}</span>
              <a-switch v-model:checked="autoRefresh" size="small" :aria-label="tx('实时推送')" />
            </div>
            <span class="refresh-time">{{ tx('最近刷新：') }}{{ tx(lastRefreshLabel) }}</span>
            <a-tooltip :title="tx('刷新数据')">
              <a-button type="text" :loading="loading" :aria-label="tx('刷新数据')" @click="refresh({ silent: false })">
                <template #icon>
                  <span class="ui-icon"><Refresh /></span>
                </template>
              </a-button>
            </a-tooltip>
            <AccountDropdown :display-name="currentUserName" :username="currentLoginName" @password="passwordDialogVisible = true" @logout="handleLogout" />
          </div>
        </a-layout-header>
        <a-layout-content
          class="content"
          :class="{ 'content--fill': activeView === 'devices' || activeView === 'audit' }"
        >
          <div class="page-heading">
            <h1>{{ tx(pageTitle) }}</h1>
          </div>
          <div
            class="page-body"
            :class="{
              'page-body--fill': activeView === 'devices' || activeView === 'audit',
              'page-body--big': activeView === 'bigScreen',
              'page-body--tables-fill': ['flowRules', 'scripts', 'models', 'security', 'users', 'roles', 'permissions'].includes(
                activeView
              )
            }"
          >
            <BigScreenView
              v-if="activeView === 'bigScreen'"
              :status="status"
              :health="health"
              :trend="trend"
              @select-error="openError"
            />
            <DeviceTopologyView
              v-else-if="activeView === 'topology'"
              :project="project"
              :status="status"
              @select-error="openError"
            />
            <DashboardView
              v-else-if="activeView === 'dashboard'"
              :status="status"
              :health="health"
              :storage-health="storageHealth"
              :saving-storage-health="loading"
              :can-persist-storage-health="hasPermission(PERMISSIONS.dashboardStorageHealthEdit)"
              :trend="trend"
              @persist-storage-health="persistStorageHealth"
              @select-error="openError"
            />
            <DevicesView
              v-else-if="activeView === 'devices'"
              :state-scope="deviceViewScope(currentUserIdentity, project?.projectId ?? '')"
              :project="project"
              :runtime-devices="status?.devices ?? []"
              :runtime-tags="status?.tags ?? []"
              @changed="handleDevicesChanged"
              @editing-state="deviceEditing = $event"
            />
            <FlowRulesView
              v-else-if="activeView === 'flowRules'"
              :project="project"
              :status="status?.flowRuleEngine"
              @changed="handleRulesChanged"
              @editing-state="flowRulesEditing = $event"
            />
            <MqttView
              v-else-if="activeView === 'mqtt'"
              :mqtt="mqtt"
              :status="status?.mqtt"
              @persist-mqtt="persistMqtt"
            />
            <OpcUaView
              v-else-if="activeView === 'opcUa'"
              :opc-ua="opcUa"
              :status="status?.opcUa"
              @persist-opcua="persistOpcUa"
            />
            <HistoryView
              v-else-if="activeView === 'history'"
              :history="history"
              :status="status?.history"
              :saving="loading"
              :can-save="hasPermission(PERMISSIONS.historyEdit)"
              @persist-history="persistHistory"
            />
            <ScriptingView v-else-if="activeView === 'scripts'" :project="project" />
            <ModelCenterView
              v-else-if="activeView === 'models'"
              :project="project"
              :runtime-tags="status?.tags ?? []"
            />
            <AuditView v-else-if="activeView === 'audit'" />
            <SecurityView v-else-if="activeView === 'security'" />
            <MaintenanceView v-else-if="activeView === 'maintenance'" />
            <UsersView v-else-if="activeView === 'users'" />
            <RolesView v-else-if="activeView === 'roles'" />
            <PermissionsView v-else-if="activeView === 'permissions'" />
          </div>
        </a-layout-content>
      </a-layout>
    </a-layout>
    <ChangePasswordDialog v-model="passwordDialogVisible" />
    <ErrorDetailDrawer v-model:visible="errorDrawerVisible" :error="selectedError" />
    <UpgradeUploadOverlay />
  </a-config-provider>
</template>

<script setup lang="ts">
import { notify } from './utils/ui'
import { tx } from './i18n'
import { computed, nextTick, onBeforeUnmount, onMounted, provide, reactive, ref, watch } from 'vue'

import {
  HomeFilled,
  ContainerOutlined as Box,
  ApartmentOutlined as Connection,
  DeploymentUnitOutlined as Cpu,
  LineChartOutlined as DataLine,
  FileTextOutlined as Document,
  MenuUnfoldOutlined as Expand,
  MenuFoldOutlined as Fold,
  LockOutlined as Lock,
  DesktopOutlined as Monitor,
  AppstoreOutlined as Operation,
  SendOutlined as Promotion,
  ReloadOutlined as Refresh,
  ShareAltOutlined as Share,
  LogoutOutlined as SwitchButton,
  AuditOutlined as Tickets,
  TeamOutlined as UserFilled
} from '@ant-design/icons-vue'
import {
  loadSync,
  loadReadyHealth,
  loadCurrentUser,
  login,
  logout,
  saveHistory,
  saveMqtt,
  saveOpcUa,
  saveStorageHealth,
  type DeviceRuntimeStatus,
  type GatewayHealthResponse,
  type GatewayStatus,
  type HistoryConfig,
  type OpcUaServerConfig,
  type ProjectConfig,
  type RuntimeDevicesChangedEvent,
  type RuntimeEventEnvelope,
  type RuntimeErrorDetail,
  type RuntimeStatusPatchEvent,
  type RuntimeTagsChangedEvent,
  type StorageHealthConfig,
  type SyncPayload,
  type TagValueSnapshot
} from './api'
import DashboardView, { type TrendSample } from './components/DashboardView.vue'
import AuditView from './components/AuditView.vue'
import BigScreenView from './components/bigscreen/BigScreenView.vue'
import ChangePasswordDialog from './components/ChangePasswordDialog.vue'
import DeviceTopologyView from './components/topology/DeviceTopologyView.vue'
import DevicesView from './components/DevicesView.vue'
import ErrorDetailDrawer from './components/ErrorDetailDrawer.vue'
import FlowRulesView from './components/FlowRulesView.vue'
import HistoryView from './components/history/HistoryView.vue'
import LoginCaptcha from './components/LoginCaptcha.vue'
import AccountDropdown from './components/AccountDropdown.vue'
import GatewayLogo from './components/GatewayLogo.vue'
import UpgradeUploadOverlay from './components/UpgradeUploadOverlay.vue'
import { upgradeUploadState } from './utils/upgradeUploadState'
import LoginMonetBackdrop from './components/LoginMonetBackdrop.vue'
import GatewayNavigation from './components/GatewayNavigation.vue'
import antZhCn from 'ant-design-vue/es/locale/zh_CN'
import antEnUs from 'ant-design-vue/es/locale/en_US'
import LanguageSwitcher from './components/LanguageSwitcher.vue'
import { locale } from './i18n'
import { gatewayTheme } from './theme'
const antLocale = computed(() => (locale.value === 'en-US' ? antEnUs : antZhCn))
import MaintenanceView from './components/MaintenanceView.vue'
import ModelCenterView from './components/models/ModelCenterView.vue'
import MqttView from './components/MqttView.vue'
import OpcUaView from './components/OpcUaView.vue'
import PermissionsView from './components/PermissionsView.vue'
import RolesView from './components/RolesView.vue'
import SecurityView from './components/SecurityView.vue'
import ScriptingView from './components/scripting/ScriptingView.vue'
import UsersView from './components/UsersView.vue'
import { formatDateTime } from './utils/format'
import { clearDeviceViewStates, deviceViewScope } from './utils/deviceViewState'
import { createRuntimeEventGuard, shouldApplyRuntimeSnapshot } from './utils/runtimePush'
import { createPermissionSet, PermissionContextKey, PERMISSIONS, normalizePermission } from './utils/permissions'

const defaultStartupView = 'bigScreen'
const authenticated = ref(localStorage.getItem('ipc.gateway.authenticated') === 'true')
const currentUserIdentity = ref('')
const currentUserName = ref('')
const currentLoginName = ref('')
const activeView = ref(defaultStartupView)
const sidebarCollapsed = ref(localStorage.getItem('ipc.gateway.sidebarCollapsed') === 'true')
const loading = ref(false)
const autoRefresh = ref(true)
const lastRefreshTime = ref('')
const sync = ref<SyncPayload | null>(null)
const health = ref<GatewayHealthResponse | null>(null)
const storageHealth = ref<StorageHealthConfig | null>(null)
const project = ref<ProjectConfig | null>(null)
const mqtt = ref<Record<string, any> | null>(null)
const opcUa = ref<OpcUaServerConfig | null>(null)
const history = ref<HistoryConfig | null>(null)
const trend = ref<TrendSample[]>([])
const selectedError = ref<RuntimeErrorDetail | null>(null)
const errorDrawerVisible = ref(false)
const passwordDialogVisible = ref(false)
const deviceEditing = ref(false)
const flowRulesEditing = ref(false)
const currentPermissions = ref<string[]>([])
const loginForm = reactive({ username: '', password: '' })
const captchaRef = ref<InstanceType<typeof LoginCaptcha> | null>(null)
let refreshController: AbortController | undefined
let runtimeEvents: EventSource | undefined

const status = computed(() =>
  sync.value ? sanitizeStatus(sync.value.status, project.value ?? sync.value.project) : null
)
const permissionSet = computed(() => createPermissionSet(currentPermissions.value))
const lastRefreshLabel = computed(() => (lastRefreshTime.value ? formatDateTime(lastRefreshTime.value) : '-'))
const pageTitle = computed(
  () =>
    ({
      bigScreen: '大屏总览',
      topology: '设备拓扑',
      dashboard: '运行总览',
      devices: '设备管理',
      flowRules: '规则引擎',
      mqtt: 'MQTT',
      opcUa: 'OPC UA Server',
      history: '历史库',
      scripts: '脚本中心',
      models: '模型中心',
      audit: '审计日志',
      security: '工业安全',
      maintenance: '安装升级',
      users: '人员管理',
      roles: '角色管理',
      permissions: '权限分配'
    })[activeView.value] || '大屏总览'
)

const pageSection = computed(() => {
  if (['bigScreen', 'topology', 'dashboard'].includes(activeView.value)) return '运行'
  if (['devices', 'flowRules', 'history', 'scripts', 'models'].includes(activeView.value)) return '配置'
  if (['mqtt', 'opcUa'].includes(activeView.value)) return '通讯'
  return '系统'
})

const viewPermissions: Record<string, string[]> = {
  bigScreen: [PERMISSIONS.bigScreenView],
  topology: [PERMISSIONS.topologyView],
  dashboard: [PERMISSIONS.dashboardView],
  devices: [PERMISSIONS.devicesView],
  flowRules: [PERMISSIONS.flowRulesView],
  mqtt: [PERMISSIONS.mqttView],
  opcUa: [PERMISSIONS.opcUaView],
  history: [PERMISSIONS.historyView],
  scripts: [PERMISSIONS.scriptsView],
  models: [PERMISSIONS.modelsView],
  audit: [PERMISSIONS.auditView],
  security: [PERMISSIONS.securityView],
  maintenance: [PERMISSIONS.maintenanceView],
  users: [PERMISSIONS.usersView],
  roles: [PERMISSIONS.rolesView],
  permissions: [PERMISSIONS.permissionsView]
}
const viewOrder = [
  'bigScreen',
  'topology',
  'dashboard',
  'devices',
  'flowRules',
  'history',
  'scripts',
  'models',
  'mqtt',
  'opcUa',
  'audit',
  'security',
  'maintenance',
  'users',
  'roles',
  'permissions'
]

provide(PermissionContextKey, {
  permissions: currentPermissions,
  permissionSet,
  hasPermission,
  hasAnyPermission
})

onMounted(() => {
  window.addEventListener('keydown', handleLoginEnter, true)
  if (authenticated.value) initializeAuthenticatedSession()
  syncAutoRefresh()
})

onBeforeUnmount(() => {
  window.removeEventListener('keydown', handleLoginEnter, true)
  stopRuntimeEvents()
  refreshController?.abort()
})

watch([authenticated, autoRefresh], syncAutoRefresh)
watch(authenticated, async (value) => {
  if (!value) {
    currentUserIdentity.value = ''
    currentUserName.value = ''
    currentLoginName.value = ''
    await nextTick()
    clearDeviceViewStates()
  }
})
watch(currentPermissions, ensureActiveViewAllowed)

function handleLoginEnter(event: KeyboardEvent) {
  if (authenticated.value || event.key !== 'Enter' || event.isComposing) return

  event.preventDefault()
  event.stopPropagation()
  void handleLogin()
}

async function handleLogin() {
  if (loading.value) return
  if (!captchaRef.value?.validate()) return

  loading.value = true
  try {
    await login(loginForm.username, loginForm.password)
    localStorage.setItem('ipc.gateway.authenticated', 'true')
    authenticated.value = true
    await loadSessionPermissions()
    await refresh({ silent: false })
  } catch (error) {
    captchaRef.value?.refresh()
    notify.error(error instanceof Error ? error.message : '登录失败')
  } finally {
    loading.value = false
  }
}

async function handleLogout() {
  stopRuntimeEvents()
  refreshController?.abort()
  await logout().catch(() => undefined)
  localStorage.removeItem('ipc.gateway.authenticated')
  currentPermissions.value = []
  authenticated.value = false
}

async function refresh(options: { silent: boolean }) {
  stopRuntimeEvents()
  refreshController?.abort()
  const controller = new AbortController()
  refreshController = controller
  if (!options.silent) loading.value = true
  try {
    const [data, healthData] = await Promise.all([
      loadSync(controller.signal),
      loadReadyHealth(controller.signal).catch((error) => {
        if (isAbortError(error)) throw error
        return null
      })
    ])
    if (controller.signal.aborted || refreshController !== controller) return
    applySync(data, options, healthData)
  } catch (error) {
    if (isAbortError(error)) return
    if (!options.silent) notify.error(error instanceof Error ? error.message : '加载失败')
    if (error instanceof Error && error.message.includes('登录已过期')) {
      localStorage.removeItem('ipc.gateway.authenticated')
      currentPermissions.value = []
      authenticated.value = false
    }
  } finally {
    if (refreshController === controller) {
      refreshController = undefined
      if (authenticated.value && autoRefresh.value) startRuntimeEvents()
    }
    if (!options.silent) loading.value = false
  }
}

async function initializeAuthenticatedSession() {
  loading.value = true
  try {
    await loadSessionPermissions()
    await refresh({ silent: false })
  } catch (error) {
    notify.error(error instanceof Error ? error.message : '会话加载失败')
    if (error instanceof Error && error.message.includes('登录已过期')) {
      localStorage.removeItem('ipc.gateway.authenticated')
      currentPermissions.value = []
      authenticated.value = false
    }
  } finally {
    loading.value = false
  }
}

async function loadSessionPermissions() {
  const session = await loadCurrentUser()
  currentUserIdentity.value = session.user?.id || session.user?.username || ''
  currentUserName.value = session.user?.displayName || session.user?.username || ''
  currentLoginName.value = session.user?.username || ''
  currentPermissions.value = session.permissions ?? []
  ensureActiveViewAllowed()
}

function hasPermission(permission: string) {
  return permissionSet.value.has(normalizePermission(permission))
}

function hasAnyPermission(permissions: string[]) {
  return permissions.some((permission) => hasPermission(permission))
}

function canAccessView(view: string) {
  return hasAnyPermission(viewPermissions[view] ?? [])
}

function ensureActiveViewAllowed() {
  if (!authenticated.value || canAccessView(activeView.value)) return
  activeView.value = viewOrder.find(canAccessView) ?? defaultStartupView
}

function isAbortError(error: unknown) {
  return error instanceof DOMException && error.name === 'AbortError'
}

function applySync(data: SyncPayload, options: { silent: boolean }, healthData: GatewayHealthResponse | null) {
  const preserveProjectConfig =
    shouldPreserveActiveConfigDraft('project', options) ||
    (options.silent &&
      ((activeView.value === 'devices' && deviceEditing.value) ||
        (activeView.value === 'flowRules' && flowRulesEditing.value)))
  const preserveMqttConfig = shouldPreserveActiveConfigDraft('mqtt', options)
  const preserveOpcUaConfig = shouldPreserveActiveConfigDraft('opcUa', options)
  const preserveHistoryConfig = shouldPreserveActiveConfigDraft('history', options)
  // A full HTTP snapshot may have been captured before the latest SSE update.
  // Keep newer values only for identities still present in the returned snapshot.
  if (sync.value) {
    const returnedKeys = new Set((data.status.tags ?? []).map(runtimeTagKey))
    data.status.tags = mergeRuntimeTags(data.status.tags ?? [],
      (sync.value.status.tags ?? []).filter(tag => returnedKeys.has(runtimeTagKey(tag))))
  }
  sync.value = data
  health.value = healthData
  lastRefreshTime.value = new Date().toISOString()
  if (!preserveProjectConfig) {
    project.value = structuredClone(data.project)
  }
  if (!preserveMqttConfig) {
    mqtt.value = structuredClone(data.mqtt)
  }
  if (!preserveOpcUaConfig) {
    opcUa.value = structuredClone(data.opcUa)
  }
  if (!preserveHistoryConfig) {
    history.value = structuredClone(data.history)
  }
  storageHealth.value = structuredClone(data.storageHealth)
  pushTrend(data)
}

function startRuntimeEvents() {
  if (runtimeEvents || typeof EventSource === 'undefined') return

  const source = new EventSource('/api/config/status/events', { withCredentials: true })
  runtimeEvents = source
  const guard = createRuntimeEventGuard()
  source.addEventListener('hello', (event) => {
    if (runtimeEvents !== source) return
    const envelope = readRuntimeEnvelope<unknown>(event)
    if (envelope) guard.reset(envelope.timestamp, envelope.sequence)
  })
  function receive<T>(event: Event): T | null {
    if (runtimeEvents !== source) return null
    const envelope = readRuntimeEnvelope<T>(event)
    if (!envelope) return null
    const action = guard.check(envelope)
    if (action === 'resync') {
      stopRuntimeEvents()
      if (authenticated.value && autoRefresh.value) startRuntimeEvents()
      return null
    }
    return action === 'accept' ? envelope.data : null
  }
  source.addEventListener('tags', (event) => {
    const payload = receive<RuntimeTagsChangedEvent>(event)
    if (payload) applyRuntimeTags(payload)
  })
  source.addEventListener('devices', (event) => {
    const payload = receive<RuntimeDevicesChangedEvent>(event)
    if (payload) applyRuntimeDevices(payload)
  })
  source.addEventListener('status', (event) => {
    const payload = receive<RuntimeStatusPatchEvent>(event)
    if (payload) applyRuntimeStatusPatch(payload)
  })
  source.addEventListener('heartbeat', (event) => {
    receive<unknown>(event)
  })
}

function stopRuntimeEvents() {
  runtimeEvents?.close()
  runtimeEvents = undefined
}

function readRuntimeEnvelope<T>(event: Event): RuntimeEventEnvelope<T> | null {
  const data = (event as MessageEvent<string>).data
  if (!data) return null
  try {
    const envelope = JSON.parse(data) as RuntimeEventEnvelope<T>
    return envelope
  } catch {
    return null
  }
}

function applyRuntimeTags(payload: RuntimeTagsChangedEvent) {
  if (!sync.value || !payload.tags?.length) return

  const currentStatus = sync.value.status
  const tags = mergeRuntimeTags(currentStatus.tags ?? [], payload.tags)
  if (tags === currentStatus.tags) return

  sync.value.status = { ...currentStatus, tags }
  lastRefreshTime.value = new Date().toISOString()
}

function applyRuntimeDevices(payload: RuntimeDevicesChangedEvent) {
  if (!sync.value) return

  const currentStatus = sync.value.status
  const devices = mergeRuntimeDevices(
    currentStatus.devices ?? [],
    payload.devices ?? [],
    payload.removedDeviceKeys ?? []
  )
  if (devices === currentStatus.devices) return

  sync.value.status = { ...currentStatus, devices }
  lastRefreshTime.value = new Date().toISOString()
}

function applyRuntimeStatusPatch(payload: RuntimeStatusPatchEvent) {
  if (!sync.value || !payload.status) return

  const currentStatus = sync.value.status
  const patch = payload.status
  const nextStatus: GatewayStatus = {
    ...currentStatus,
    ...patch,
    devices: patch.devices?.length ? patch.devices : currentStatus.devices,
    tags: currentStatus.tags
  }

  sync.value.status = nextStatus
  lastRefreshTime.value = new Date().toISOString()
  pushTrend({ ...sync.value, status: nextStatus })
}

function mergeRuntimeTags(current: TagValueSnapshot[], changed: TagValueSnapshot[]) {
  if (changed.length === 0) return current

  const index = new Map<string, number>()
  current.forEach((tag, itemIndex) => {
    const key = runtimeTagKey(tag)
    if (key) index.set(key, itemIndex)
  })

  let modified = false
  const next = current.slice()
  for (const tag of changed) {
    const key = runtimeTagKey(tag)
    if (!key) continue

    const itemIndex = index.get(key)
    if (itemIndex === undefined) {
      index.set(key, next.length)
      next.push(tag)
      modified = true
      continue
    }

    if (shouldApplyRuntimeSnapshot(next[itemIndex].timestamp, tag.timestamp) && !sameRuntimeTag(next[itemIndex], tag)) {
      next[itemIndex] = { ...next[itemIndex], ...tag }
      modified = true
    }
  }

  return modified ? next : current
}

function mergeRuntimeDevices(current: DeviceRuntimeStatus[], changed: DeviceRuntimeStatus[], removedKeys: string[]) {
  if (changed.length === 0 && removedKeys.length === 0) return current

  const removed = new Set(removedKeys.map(normalizeKey).filter(Boolean))
  const next =
    removed.size > 0
      ? current.filter((device) => !removed.has(normalizeKey(runtimeDeviceKey(device))))
      : current.slice()
  const index = new Map<string, number>()
  next.forEach((device, itemIndex) => {
    const key = runtimeDeviceKey(device)
    if (key) index.set(key, itemIndex)
  })

  let modified = removed.size > 0 && next.length !== current.length
  for (const device of changed) {
    const key = runtimeDeviceKey(device)
    if (!key) continue

    const itemIndex = index.get(key)
    if (itemIndex === undefined) {
      index.set(key, next.length)
      next.push(device)
      modified = true
      continue
    }

    if (!sameRuntimeDevice(next[itemIndex], device)) {
      next[itemIndex] = { ...next[itemIndex], ...device }
      modified = true
    }
  }

  return modified ? next : current
}

function runtimeTagKey(tag: TagValueSnapshot) {
  return `id:${tagIdentityKey(tag.channelId, tag.deviceId, tag.groupId, tag.tagId)}`
}

function runtimeDeviceKey(device: DeviceRuntimeStatus) {
  return `id:${deviceIdentityKey(device.channelId, device.deviceId)}`
}

function sameRuntimeTag(left: TagValueSnapshot, right: TagValueSnapshot) {
  return (
    left.valueText === right.valueText &&
    left.rawValueText === right.rawValueText &&
    left.quality === right.quality &&
    left.timestamp === right.timestamp &&
    left.errorMessage === right.errorMessage &&
    left.cleaningApplied === right.cleaningApplied &&
    left.cleaningAction === right.cleaningAction &&
    left.cleaningMessage === right.cleaningMessage
  )
}

function sameRuntimeDevice(left: DeviceRuntimeStatus, right: DeviceRuntimeStatus) {
  return (
    left.isConnected === right.isConnected &&
    left.isPolling === right.isPolling &&
    left.status === right.status &&
    left.consecutiveFailures === right.consecutiveFailures &&
    left.totalReads === right.totalReads &&
    left.successfulReads === right.successfulReads &&
    left.failedReads === right.failedReads &&
    left.successRate === right.successRate &&
    left.lastPollTime === right.lastPollTime &&
    left.lastSuccessTime === right.lastSuccessTime &&
    left.lastFailureTime === right.lastFailureTime &&
    left.nextPollTime === right.nextPollTime &&
    left.lastTaskStatus === right.lastTaskStatus &&
    left.lastTaskDurationMs === right.lastTaskDurationMs &&
    left.slowPollCount === right.slowPollCount &&
    left.timeoutCount === right.timeoutCount &&
    left.lastError === right.lastError
  )
}

function shouldPreserveActiveConfigDraft(view: string, options: { silent: boolean }) {
  // Silent refresh updates runtime status, but must not replace the active editor draft while the user is typing.
  return options.silent && activeView.value === view
}

function pushTrend(data: SyncPayload) {
  const visibleStatus = sanitizeStatus(data.status, data.project)
  const devices = visibleStatus?.devices ?? []
  const successRate =
    devices.length === 0 ? 0 : devices.reduce((sum, item) => sum + (item.successRate || 0), 0) / devices.length
  trend.value = [
    ...trend.value,
    {
      timestamp: Date.now(),
      successRate: Number(successRate.toFixed(1)),
      onlineDeviceCount: visibleStatus?.onlineDeviceCount ?? 0,
      badTagCount: visibleStatus?.badTagCount ?? 0
    }
  ].slice(-48)
}

function sanitizeStatus(
  source: GatewayStatus | null | undefined,
  currentProject: ProjectConfig | null | undefined
): GatewayStatus | null {
  if (!source) return null
  if (!currentProject) return source

  const projectDevices = currentProject.devices ?? []
  const activeChannelIds = new Set(
    (currentProject.channels ?? [])
      .filter(isConfigEnabled)
      .map((channel) => normalizeKey(channel.id))
      .filter(Boolean)
  )
  const activeDevices = projectDevices.filter(
    (device) => isConfigEnabled(device) && activeChannelIds.has(normalizeKey(device.channelId))
  )
  const deviceKeys = new Set(activeDevices.map((device) => deviceIdentityKey(device.channelId, device.id)))
  const tagScope = buildProjectTagScope(activeDevices)
  const devices = filterRuntimeDevices(source.devices ?? [], deviceKeys)
  const tags = filterRuntimeTags(source.tags ?? [], tagScope.tagKeys)
  const goodTagCount = tags.filter((tag) => normalizeKey(tag.quality) === 'good').length
  const badTagCount = tags.filter((tag) => {
    const quality = normalizeKey(tag.quality)
    return !!quality && quality !== 'good' && quality !== 'unknown'
  }).length
  const recentErrors = (source.recentErrors ?? [])
    .filter((error) => isRuntimeErrorInScope(error, deviceKeys, tagScope.tagKeys))
    .filter((error) => isRuntimeErrorActive(error, devices, tags))

  return {
    ...source,
    deviceCount: activeDevices.length,
    groupCount: tagScope.groupCount,
    tagCount: tagScope.tagCount,
    enabledDeviceCount: activeDevices.length,
    onlineDeviceCount: devices.filter((device) => device.isConnected).length,
    goodTagCount,
    badTagCount,
    noDataTagCount: Math.max(0, tagScope.tagCount - goodTagCount - badTagCount),
    devices,
    tags,
    recentErrors
  }
}

function filterRuntimeDevices(devices: DeviceRuntimeStatus[], deviceKeys: Set<string>) {
  if (deviceKeys.size === 0) return []
  return devices.filter((device) => deviceKeys.has(deviceIdentityKey(device.channelId, device.deviceId)))
}

function buildProjectTagScope(devices: ProjectConfig['devices']) {
  const tagKeys = new Set<string>()
  let groupCount = 0
  let tagCount = 0

  for (const device of devices) {
    if (!isConfigEnabled(device)) continue

    for (const tag of device.tags ?? []) {
      if (!isConfigEnabled(tag)) continue
      tagCount += 1
      tagKeys.add(tagIdentityKey(device.channelId, device.id, '', tag.id))
    }

    for (const group of device.groups ?? []) {
      if (!isConfigEnabled(group)) continue
      groupCount += 1
      for (const tag of group.tags ?? []) {
        if (!isConfigEnabled(tag)) continue
        tagCount += 1
        tagKeys.add(tagIdentityKey(device.channelId, device.id, group.id, tag.id))
      }
    }
  }

  return { tagKeys, groupCount, tagCount }
}

function filterRuntimeTags(tags: TagValueSnapshot[], tagKeys: Set<string>) {
  if (tagKeys.size === 0) return []
  return tags.filter((tag) => tagKeys.has(tagIdentityKey(tag.channelId, tag.deviceId, tag.groupId, tag.tagId)))
}

function isRuntimeErrorInScope(error: RuntimeErrorDetail, deviceKeys: Set<string>, tagKeys: Set<string>) {
  const deviceId = normalizeKey(error.deviceId)
  if (!deviceId) return true
  if (!deviceKeys.has(deviceIdentityKey(error.channelId, error.deviceId))) return false

  const tagId = normalizeKey(error.tagId)
  if (!tagId) return true
  return tagKeys.has(tagIdentityKey(error.channelId, error.deviceId, error.groupId, error.tagId))
}

function isRuntimeErrorActive(error: RuntimeErrorDetail, devices: DeviceRuntimeStatus[], tags: TagValueSnapshot[]) {
  const category = normalizeKey(error.category)
  if (category === 'deviceconnectionrecovered') return false

  if (category.startsWith('deviceconnection') && normalizeKey(error.deviceId)) {
    const device = devices.find(
      (item) => deviceIdentityKey(item.channelId, item.deviceId) === deviceIdentityKey(error.channelId, error.deviceId)
    )
    if (device) return device.enabled !== false && !device.isConnected
  }

  if (category === 'tagread' && normalizeKey(error.tagId)) {
    const tag = tags.find(
      (item) =>
        tagIdentityKey(item.channelId, item.deviceId, item.groupId, item.tagId) ===
        tagIdentityKey(error.channelId, error.deviceId, error.groupId, error.tagId)
    )
    if (tag) return normalizeKey(tag.quality) === 'readerror'
  }

  return true
}

function deviceIdentityKey(channelId: string | null | undefined, deviceId: string | null | undefined) {
  return [channelId, deviceId].map(normalizeKey).join('/')
}

function tagIdentityKey(
  channelId: string | null | undefined,
  deviceId: string | null | undefined,
  groupId: string | null | undefined,
  tagId: string | null | undefined
) {
  return [channelId, deviceId, groupId, tagId].map(normalizeKey).join('/')
}

function normalizeKey(value: string | null | undefined) {
  return (value ?? '').trim().toLowerCase()
}

function isConfigEnabled(item: { enabled?: boolean } | null | undefined) {
  return item?.enabled !== false
}

function syncAutoRefresh() {
  stopRuntimeEvents()
  if (!authenticated.value || !autoRefresh.value || refreshController) return

  startRuntimeEvents()
}

function toggleSidebar() {
  sidebarCollapsed.value = !sidebarCollapsed.value
  localStorage.setItem('ipc.gateway.sidebarCollapsed', String(sidebarCollapsed.value))
}

async function handleDevicesChanged() {
  deviceEditing.value = false
  await refresh({ silent: true })
}

async function handleRulesChanged() {
  await refresh({ silent: true })
}

async function persistMqtt() {
  if (!hasPermission(PERMISSIONS.mqttEdit)) {
    notify.warning('当前用户没有保存 MQTT 配置权限')
    return
  }
  if (!mqtt.value) return
  loading.value = true
  try {
    mqtt.value = await saveMqtt(mqtt.value)
    notify.success('已保存')
    await refresh({ silent: true })
  } catch (error) {
    notify.error(error instanceof Error ? error.message : '保存失败')
  } finally {
    loading.value = false
  }
}

async function persistOpcUa() {
  if (!hasPermission(PERMISSIONS.opcUaEdit)) {
    notify.warning('当前用户没有保存 OPC UA Server 配置权限')
    return
  }
  if (!opcUa.value) return
  loading.value = true
  try {
    opcUa.value = await saveOpcUa(opcUa.value)
    notify.success('已保存')
    await refresh({ silent: true })
  } catch (error) {
    notify.error(error instanceof Error ? error.message : '保存失败')
  } finally {
    loading.value = false
  }
}

async function persistHistory(options: HistoryConfig) {
  if (!hasPermission(PERMISSIONS.historyEdit)) {
    notify.warning('当前用户没有保存历史库配置权限')
    return
  }
  loading.value = true
  try {
    history.value = await saveHistory(options)
    notify.success('已保存')
    await refresh({ silent: true })
  } catch (error) {
    notify.error(error instanceof Error ? error.message : '保存失败')
  } finally {
    loading.value = false
  }
}

async function persistStorageHealth(options?: StorageHealthConfig) {
  if (!hasPermission(PERMISSIONS.dashboardStorageHealthEdit)) {
    notify.warning('当前用户没有保存历史库健康阈值权限')
    return
  }
  const nextOptions = options ?? storageHealth.value
  if (!nextOptions) return
  loading.value = true
  try {
    storageHealth.value = await saveStorageHealth(nextOptions)
    notify.success('已保存')
    await refresh({ silent: true })
  } catch (error) {
    notify.error(error instanceof Error ? error.message : '保存失败')
  } finally {
    loading.value = false
  }
}

function openError(error: RuntimeErrorDetail) {
  selectedError.value = error
  errorDrawerVisible.value = true
}
</script>

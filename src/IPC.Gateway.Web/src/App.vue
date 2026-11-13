<template>
  <a-config-provider :theme="gatewayTheme" :locale="antLocale" component-size="small">
    <main v-if="!authenticated || showStartupPanel" class="login-shell" :inert="upgradeUploadState.active || configurationSaving || undefined">
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
        <a-alert v-if="startupMessage" role="status" show-icon :type="startupState === 'failed' ? 'error' : 'info'" :message="tx(startupMessage)" />
        <a-button v-if="authenticated" @click="handleLogout">{{ tx('退出登录') }}</a-button>
        <a-form v-else :model="loginForm" @submit.prevent="handleLogin" layout="vertical">
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
    <a-layout v-else class="app-shell" :inert="upgradeUploadState.active || configurationSaving || undefined" :aria-busy="upgradeUploadState.active || configurationSaving">
      <a-layout-sider
        :width="208"
        :collapsed-width="64"
        :collapsed="sidebarCollapsed"
        :class="['sidebar', { 'sidebar--collapsed': sidebarCollapsed }]"
        :trigger="null"
      >
        <div class="brand">
          <GatewayLogo class="brand__logo" />
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
              <span>{{ tx(autoRefresh ? streamState : '手动刷新') }}</span>
              <a-switch v-model:checked="autoRefresh" :disabled="!canStream" size="small" :aria-label="tx('实时推送')" />
            </div>
            <span class="refresh-time">{{ tx('最近刷新：') }}{{ tx(lastRefreshLabel) }}</span>
            <a-tooltip :title="tx('刷新数据')">
              <a-button type="text" :loading="loading" :aria-label="tx('刷新数据')" @click="refresh({ silent: false, resetView: activeView })">
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
            :key="sessionScopeRevision"
            ref="pageBody"
            class="page-body"
            :class="{
              'page-body--fill': activeView === 'devices' || activeView === 'audit',
              'page-body--big': activeView === 'bigScreen',
              'page-body--tables-fill': ['flowRules', 'scripts', 'models', 'security', 'users', 'roles', 'permissions'].includes(
                activeView
              )
            }"
          >
            <KeepAlive v-if="canAccessView(activeView)">
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
                :failed-tags-by-device="failedTagsByDevice"
                :runtime-tags="runtimeTags"
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
                :runtime-devices="visibleDevices"
                :runtime-tags="runtimeTags"
                :enabled-tag-keys="projectScope.tagKeys"
                :good-tags-by-device="goodTagsByDevice"
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
                :saving="loading"
                :reset-revision="mqttResetRevision"
                @persist-mqtt="persistMqtt"
              />
              <OpcUaView
                v-else-if="activeView === 'opcUa'"
                :opc-ua="opcUa"
                :status="status?.opcUa"
                :saving="loading"
                :reset-revision="opcUaResetRevision"
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
              <MaintenanceView v-else-if="activeView === 'maintenance'" :account="currentUserIdentity" />
              <UsersView v-else-if="activeView === 'users'" />
              <RolesView v-else-if="activeView === 'roles'" />
              <PermissionsView v-else-if="activeView === 'permissions'" />
            </KeepAlive>
          </div>
        </a-layout-content>
      </a-layout>
    </a-layout>
    <ChangePasswordDialog v-model="passwordDialogVisible" />
    <ErrorDetailDrawer v-model:visible="errorDrawerVisible" :error="selectedError" />
    <UpgradeUploadOverlay />
    <ConfigurationSaveOverlay />
  </a-config-provider>
</template>

<script setup lang="ts">
import { notify } from './utils/ui'
import { tx } from './i18n'
import { computed, nextTick, onBeforeUnmount, onMounted, provide, reactive, ref, shallowReactive, shallowRef, watch } from 'vue'

import {
  HomeFilled,
  ContainerOutlined as Box,
  ApartmentOutlined as Connection,
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
  GatewayStartupError,
  SessionExpiredError,
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
  type RuntimeErrorDetail,
  type StorageHealthConfig,
  type SyncPayload,
  type TagValueSnapshot
} from './api'
import DashboardView, { type TrendSample } from './components/DashboardView.vue'
import AuditView from './components/AuditView.vue'
import BigScreenView from './components/bigscreen/BigScreenView.vue'
import ChangePasswordDialog from './components/ChangePasswordDialog.vue'
import DeviceTopologyView from './components/topology/DeviceTopologyView.vue'
import { isFailedTopologyQuality } from './components/topology/channelTopologyModel'
import DevicesView from './components/DevicesView.vue'
import ErrorDetailDrawer from './components/ErrorDetailDrawer.vue'
import FlowRulesView from './components/FlowRulesView.vue'
import HistoryView from './components/history/HistoryView.vue'
import LoginCaptcha from './components/LoginCaptcha.vue'
import AccountDropdown from './components/AccountDropdown.vue'
import GatewayLogo from './components/GatewayLogo.vue'
import UpgradeUploadOverlay from './components/UpgradeUploadOverlay.vue'
import ConfigurationSaveOverlay from './components/ConfigurationSaveOverlay.vue'
import { configurationSaving } from './utils/configurationSaveState'
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
import { connectRuntimeEvents, deviceKey, setLatestTag, tagKey, type RuntimeChanges } from './utils/runtimeStream'
import { useGatewayStartup } from './utils/gatewayStartup'
import { createPermissionSet, PermissionContextKey, PERMISSIONS, normalizePermission } from './utils/permissions'

const defaultStartupView = 'bigScreen'
const authenticated = ref(localStorage.getItem('ipc.gateway.authenticated') === 'true')
const { state: startupState, check: checkStartup, observe: observeStartup } = useGatewayStartup()
const startupMessage = computed(() => {
  switch (startupState.value) {
    case 'checking': return authenticated.value ? '正在检查网关状态…' : ''
    case 'starting': return '网关正在初始化，完成后会自动加载运行数据。'
    case 'failed': return '网关初始化失败，请查看服务端启动日志。'
    case 'stopping': return '网关正在停止，正在等待服务重新启动。'
    case 'unavailable': return '暂时无法获取网关启动状态，正在重新连接。'
    default: return ''
  }
})
const currentUserIdentity = ref('')
const currentUserName = ref('')
const currentLoginName = ref('')
const activeView = ref(defaultStartupView)
const pageBody = ref<HTMLElement>()
const pageScroll = new Map<string, { top: number; left: number }>()
const mqttResetRevision = ref(0)
const opcUaResetRevision = ref(0)
const sidebarCollapsed = ref(localStorage.getItem('ipc.gateway.sidebarCollapsed') === 'true')
const loading = ref(false)
const autoRefresh = ref(true)
const lastRefreshTime = ref('')
const sync = ref<SyncPayload | null>(null)
const showStartupPanel = computed(() => {
  if (startupState.value === 'ready') return false
  if (!sync.value) return true
  // Keep the upgrade progress view mounted across the automatic service restart.
  return activeView.value !== 'maintenance' && (startupState.value === 'starting' || startupState.value === 'failed' || startupState.value === 'stopping')
})
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
let closeStream: (() => void) | undefined
let retryTimer: number | undefined
let retries = 0
let unmounted = false
let httpProjectRevision: string | undefined
const sessionScopeRevision = ref(0)
const streamState = ref('连接中')
const runtimeTags = shallowRef(shallowReactive(new Map<string, TagValueSnapshot>()))
const runtimeDevices = shallowRef(shallowReactive(new Map<string, DeviceRuntimeStatus>()))
const permissionSet = computed(() => createPermissionSet(currentPermissions.value))
const canStream = computed(() => hasAnyPermission([
  PERMISSIONS.dashboardView, PERMISSIONS.bigScreenView, PERMISSIONS.topologyView, PERMISSIONS.devicesView,
  PERMISSIONS.flowRulesView, PERMISSIONS.mqttView, PERMISSIONS.opcUaView, PERMISSIONS.historyView
]))
const projectScope = computed(() => buildProjectScope(sync.value?.project ?? null))
const qualityCounts = reactive({ good: 0, bad: 0 })
const goodTagsByDevice = shallowReactive(new Map<string, number>())
const failedTagsByDevice = shallowReactive(new Map<string, number>())
const visibleDevices = computed(() => Array.from(runtimeDevices.value.values())
  .filter(device => projectScope.value.deviceKeys.has(deviceKey(device))))
const visibleTags = computed(() => {
  const tags: TagValueSnapshot[] = []
  for (const [key, value] of runtimeTags.value)
    if (projectScope.value.tagKeys.has(key)) tags.push(value)
  return tags
})
const status = computed(() => sync.value ? sanitizeStatus(sync.value.status) : null)
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
    })[activeView.value] || '暂无可访问的页面'
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
  document.addEventListener('visibilitychange', onVisibilityChanged)
})

onBeforeUnmount(() => {
  unmounted = true
  document.removeEventListener('visibilitychange', onVisibilityChanged)
  window.removeEventListener('keydown', handleLoginEnter, true)
  stopRuntimeEvents()
  refreshController?.abort()
})

watch([authenticated, autoRefresh], syncAutoRefresh)
watch(startupState, async (state, _, onCleanup) => {
  if (state === 'ready' && authenticated.value) void refresh({ silent: false })
  else if (state !== 'ready') stopRuntimeEvents()
  if (state !== 'starting' || !authenticated.value || currentUserIdentity.value) return
  const controller = new AbortController()
  onCleanup(() => controller.abort())
  try {
    await loadCurrentUser(controller.signal)
  } catch (error) {
    if (!controller.signal.aborted && error instanceof SessionExpiredError) {
      localStorage.removeItem('ipc.gateway.authenticated')
      authenticated.value = false
    }
  }
})
watch(authenticated, async (value) => {
  if (!value) {
    clearSessionData()
    currentUserIdentity.value = ''
    currentUserName.value = ''
    currentLoginName.value = ''
    await nextTick()
    clearDeviceViewStates()
  }
})
watch(currentPermissions, ensureActiveViewAllowed)
watch([activeView, sessionScopeRevision], async ([view, scope], [previous, previousScope]) => {
  const body = pageBody.value
  if (!body || scope !== previousScope) return
  pageScroll.set(previous, { top: body.scrollTop, left: body.scrollLeft })
  await nextTick()
  if (view !== activeView.value || body !== pageBody.value) return
  const position = pageScroll.get(view)
  body.scrollTo(position?.left ?? 0, position?.top ?? 0)
})

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
    if (startupState.value === 'ready') await refresh({ silent: false })
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

async function refresh(options: { silent: boolean; reconnectOnly?: boolean; resetView?: string }) {
  if (startupState.value !== 'ready') return
  stopRuntimeEvents()
  refreshController?.abort()
  const controller = new AbortController()
  refreshController = controller
  if (!options.silent) loading.value = true
  if (autoRefresh.value) streamState.value = '正在同步'
  let completed = false
  try {
    const session = await loadCurrentUser(controller.signal)
    if (controller.signal.aborted || !authenticated.value) return
    if (!session.user) throw new SessionExpiredError()
    const identity = session.user.id || session.user.username || ''
    const permissions = session.permissions ?? []
    const allowed = createPermissionSet(permissions)
    const scopeChanged = currentUserIdentity.value && (identity !== currentUserIdentity.value ||
      allowed.size !== permissionSet.value.size || [...permissionSet.value].some(p => !allowed.has(p)))
    if (scopeChanged) clearSessionData()
    currentUserIdentity.value = identity
    currentUserName.value = session.user.displayName || session.user.username || ''
    currentLoginName.value = session.user.username || ''
    currentPermissions.value = permissions
    ensureActiveViewAllowed()
    if (options.reconnectOnly && sync.value && !scopeChanged) {
      completed = true
      return
    }
    const [data, healthData] = await Promise.all([
      loadSync(controller.signal),
      (canAccessView('dashboard') || canAccessView('bigScreen') ? loadReadyHealth(controller.signal) : Promise.resolve(null)).catch(error => {
        if (isAbortError(error)) throw error
        return null
      })
    ])
    if (controller.signal.aborted || !authenticated.value) return
    applySync(data, options, healthData)
    if (options.resetView === 'mqtt') mqttResetRevision.value++
    if (options.resetView === 'opcUa') opcUaResetRevision.value++
    completed = true
  } catch (error) {
    if (controller.signal.aborted || isAbortError(error)) return
    if (error instanceof GatewayStartupError) {
      observeStartup(error.state)
      return
    }
    if (error instanceof SessionExpiredError) {
      localStorage.removeItem('ipc.gateway.authenticated')
      currentPermissions.value = []
      authenticated.value = false
      return
    }
    if (!options.silent) notify.error(error instanceof Error ? error.message : '加载失败')
  } finally {
    if (refreshController === controller) {
      refreshController = undefined
      if (completed) startRuntimeEvents()
      else if (!controller.signal.aborted) scheduleReconnect()
    }
    if (!options.silent) loading.value = false
  }
}

let deferredProject: ProjectConfig | undefined
watch([deviceEditing, flowRulesEditing], ([device, rules]) => {
  if (!device && !rules && deferredProject) {
    project.value = structuredClone(deferredProject)
    deferredProject = undefined
  }
})

function clearSessionData() {
  deferredProject = undefined
  pageScroll.clear()
  sessionScopeRevision.value++
  sync.value = null
  project.value = null
  mqtt.value = opcUa.value = history.value = null
  storageHealth.value = health.value = null
  runtimeTags.value = shallowReactive(new Map())
  goodTagsByDevice.clear()
  failedTagsByDevice.clear()
  qualityCounts.good = qualityCounts.bad = 0
  runtimeDevices.value = shallowReactive(new Map())
  trend.value = []
  selectedError.value = null
  errorDrawerVisible.value = false
  lastRefreshTime.value = ''
  deviceEditing.value = flowRulesEditing.value = false
  httpProjectRevision = undefined
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
  activeView.value = viewOrder.find(canAccessView) ?? ''
}

function isAbortError(error: unknown) {
  return error instanceof DOMException && error.name === 'AbortError'
}

function applySync(data: SyncPayload, options: { silent: boolean }, healthData: GatewayHealthResponse | null) {
  if (sync.value && sync.value.project.projectId !== data.project.projectId) clearSessionData()
  const preserveProjectConfig = options.silent && (deviceEditing.value || flowRulesEditing.value)
  httpProjectRevision = data.status.projectRevision
  sync.value = { ...data, status: { ...data.status, tags: [], devices: [] } }
  replaceTags(new Map((data.status.tags ?? []).map(tag => [tagKey(tag), tag])))
  runtimeDevices.value = shallowReactive(new Map((data.status.devices ?? []).map(device => [deviceKey(device), device])))
  health.value = healthData
  lastRefreshTime.value = new Date().toISOString()
  if (!preserveProjectConfig || !project.value) {
    deferredProject = undefined
    project.value = structuredClone(data.project)
  } else {
    deferredProject = data.project
  }
  mqtt.value = structuredClone(data.mqtt)
  opcUa.value = structuredClone(data.opcUa)
  history.value = structuredClone(data.history)
  storageHealth.value = structuredClone(data.storageHealth)
  pushTrend()
}

function startRuntimeEvents() {
  if (startupState.value !== 'ready' || !authenticated.value || !autoRefresh.value || refreshController || document.hidden || unmounted || closeStream) return
  if (!canStream.value || typeof EventSource === 'undefined') {
    streamState.value = '推送不可用'
    return
  }
  if (!sync.value) return
  streamState.value = retries ? '重连中' : '连接中'
  closeStream = connectRuntimeEvents({
    projectRevision: () => httpProjectRevision,
    opened: () => { streamState.value = '正在同步' },
    snapshot: changes => {
      applyStreamChanges(changes, true)
      retries = 0
      streamState.value = '实时推送'
    },
    update: changes => applyStreamChanges(changes, false),
    reconnect: () => {
      closeStream = undefined
      scheduleReconnect()
    },
    resync: () => {
      closeStream = undefined
      void refresh({ silent: true })
    }
  })
}

function countQuality(key: string, tag: TagValueSnapshot | undefined, delta: number) {
  if (!tag || !projectScope.value.tagKeys.has(key)) return
  const quality = normalizeKey(tag.quality)
  if (quality === 'good') {
    qualityCounts.good += delta
    if (projectScope.value.tagKeys.get(key)) {
      const device = deviceIdentityKey(tag.channelId, tag.deviceId)
      goodTagsByDevice.set(device, (goodTagsByDevice.get(device) ?? 0) + delta)
    }
  } else if (quality && quality !== 'unknown') {
    qualityCounts.bad += delta
    if (projectScope.value.tagKeys.get(key) && isFailedTopologyQuality(quality)) {
      const device = deviceIdentityKey(tag.channelId, tag.deviceId)
      const count = (failedTagsByDevice.get(device) ?? 0) + delta
      if (count > 0) failedTagsByDevice.set(device, count)
      else failedTagsByDevice.delete(device)
    }
  }
}

function replaceTags(tags: Map<string, TagValueSnapshot>) {
  runtimeTags.value = shallowReactive(tags)
  goodTagsByDevice.clear()
  failedTagsByDevice.clear()
  qualityCounts.good = qualityCounts.bad = 0
  for (const [key, tag] of tags) countQuality(key, tag, 1)
}

function applyStreamChanges(changes: RuntimeChanges, full: boolean) {
  if (!sync.value) return
  if (full) {
    replaceTags(changes.tags)
    runtimeDevices.value = shallowReactive(changes.devices)
  } else {
    for (const [key, tag] of changes.tags) {
      const current = runtimeTags.value.get(key)
      if (setLatestTag(runtimeTags.value, key, tag) && current?.quality !== tag.quality) {
        countQuality(key, current, -1)
        countQuality(key, tag, 1)
      }
    }
    for (const key of changes.removedDevices) runtimeDevices.value.delete(key)
    for (const [key, device] of changes.devices) runtimeDevices.value.set(key, device)
  }
  if (changes.status) {
    sync.value.status = { ...sync.value.status, ...changes.status, tags: [], devices: [] }
    pushTrend()
  }
  lastRefreshTime.value = new Date().toISOString()
}

function scheduleReconnect() {
  if (startupState.value !== 'ready' || unmounted || !autoRefresh.value || document.hidden || !authenticated.value || retryTimer !== undefined) return
  streamState.value = '重连中'
  const delay = Math.min(1000 * 2 ** Math.min(retries++, 4), 15000)
  retryTimer = window.setTimeout(async () => {
    retryTimer = undefined
    await checkStartup()
    if (startupState.value === 'ready' && authenticated.value && !unmounted && !document.hidden)
      void refresh({ silent: true, reconnectOnly: true })
  }, delay)
}

function stopRuntimeEvents() {
  closeStream?.()
  closeStream = undefined
  window.clearTimeout(retryTimer)
  retryTimer = undefined
}

function onVisibilityChanged() {
  if (document.hidden) {
    stopRuntimeEvents()
    if (autoRefresh.value) streamState.value = '已暂停'
  } else if (authenticated.value && autoRefresh.value && !refreshController) {
    void refresh({ silent: true, reconnectOnly: true })
  }
}

function pushTrend() {
  const visibleStatus = status.value
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

function buildProjectScope(currentProject: ProjectConfig | null) {
  const activeChannelIds = new Set((currentProject?.channels ?? []).filter(isConfigEnabled).map(c => normalizeKey(c.id)))
  const activeDevices = (currentProject?.devices ?? []).filter(device =>
    isConfigEnabled(device) && activeChannelIds.has(normalizeKey(device.channelId)))
  const deviceKeys = new Set(activeDevices.map((device) => deviceIdentityKey(device.channelId, device.id)))
  return { deviceCount: activeDevices.length, deviceKeys, ...buildProjectTagScope(activeDevices) }
}

function sanitizeStatus(source: GatewayStatus): GatewayStatus {
  const { deviceCount, deviceKeys, tagKeys, groupCount, tagCount } = projectScope.value
  const devices = visibleDevices.value
  const { good: goodTagCount, bad: badTagCount } = qualityCounts
  const recentErrors = (source.recentErrors ?? []).filter(error =>
    isRuntimeErrorInScope(error, deviceKeys, tagKeys) && isRuntimeErrorActive(error))

  return {
    ...source,
    deviceCount,
    groupCount,
    tagCount,
    enabledDeviceCount: deviceCount,
    onlineDeviceCount: devices.filter((device) => device.isConnected).length,
    goodTagCount,
    badTagCount,
    noDataTagCount: Math.max(0, tagCount - goodTagCount - badTagCount),
    devices,
    get tags() { return visibleTags.value },
    recentErrors
  }
}

function buildProjectTagScope(devices: ProjectConfig['devices']) {
  const tagKeys = new Map<string, boolean>()
  let groupCount = 0
  let tagCount = 0

  for (const device of devices) {
    if (!isConfigEnabled(device)) continue

    for (const tag of device.tags ?? []) {
      if (!isConfigEnabled(tag)) continue
      tagCount += 1
      tagKeys.set(tagIdentityKey(device.channelId, device.id, '', tag.id), normalizeKey(tag.accessMode) !== 'writeonly')
    }

    for (const group of device.groups ?? []) {
      if (!isConfigEnabled(group)) continue
      groupCount += 1
      for (const tag of group.tags ?? []) {
        if (!isConfigEnabled(tag)) continue
        tagCount += 1
        tagKeys.set(tagIdentityKey(device.channelId, device.id, group.id, tag.id), normalizeKey(tag.accessMode) !== 'writeonly')
      }
    }
  }

  return { tagKeys, groupCount, tagCount }
}

function isRuntimeErrorInScope(error: RuntimeErrorDetail, deviceKeys: Set<string>, tagKeys: ReadonlyMap<string, boolean>) {
  const deviceId = normalizeKey(error.deviceId)
  if (!deviceId) return true
  if (!deviceKeys.has(deviceIdentityKey(error.channelId, error.deviceId))) return false

  const tagId = normalizeKey(error.tagId)
  if (!tagId) return true
  return tagKeys.has(tagIdentityKey(error.channelId, error.deviceId, error.groupId, error.tagId))
}

function isRuntimeErrorActive(error: RuntimeErrorDetail) {
  const category = normalizeKey(error.category)
  if (category === 'deviceconnectionrecovered') return false

  if (category.startsWith('deviceconnection') && normalizeKey(error.deviceId)) {
    const device = runtimeDevices.value.get(deviceIdentityKey(error.channelId, error.deviceId))
    if (device) return device.enabled !== false && !device.isConnected
  }

  if (category === 'tagread' && normalizeKey(error.tagId)) {
    const tag = runtimeTags.value.get(tagIdentityKey(error.channelId, error.deviceId, error.groupId, error.tagId))
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
  retries = 0
  if (sync.value && authenticated.value && autoRefresh.value && !refreshController && !document.hidden)
    void refresh({ silent: true, reconnectOnly: true })
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

async function persistMqtt(config: SyncPayload['mqtt'], acceptSaved: (config: SyncPayload['mqtt']) => void) {
  if (!hasPermission(PERMISSIONS.mqttEdit)) {
    notify.warning('当前用户没有保存 MQTT 配置权限')
    return
  }
  const scope = sessionScopeRevision.value
  loading.value = true
  try {
    const saved = await saveMqtt(config)
    if (scope !== sessionScopeRevision.value) return
    mqtt.value = saved
    acceptSaved(saved)
    notify.success('已保存')
    await refresh({ silent: true })
  } catch (error) {
    notify.error(error instanceof Error ? error.message : '保存失败')
  } finally {
    loading.value = false
  }
}

async function persistOpcUa(config: OpcUaServerConfig, acceptSaved: (config: OpcUaServerConfig) => void) {
  if (!hasPermission(PERMISSIONS.opcUaEdit)) {
    notify.warning('当前用户没有保存 OPC UA Server 配置权限')
    return
  }
  const scope = sessionScopeRevision.value
  loading.value = true
  try {
    const saved = await saveOpcUa(config)
    if (scope !== sessionScopeRevision.value) return
    opcUa.value = saved
    acceptSaved(saved)
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

<template>
  <main v-if="!authenticated" class="login-shell">
    <LoginIndustrialBackdrop />
    <section class="login-panel">
      <div class="login-panel__header">
        <p class="eyebrow">IPC Gateway</p>
        <h1>边缘计算网关</h1>
        <span>Manufacturing Edge Console</span>
      </div>
      <div class="login-status-strip" aria-hidden="true">
        <span><i></i> MQTT TLS</span>
        <span><i></i> OPC UA</span>
        <span><i></i> Rules</span>
      </div>
      <el-form :model="loginForm" label-position="top" @submit.prevent="handleLogin">
        <el-form-item label="用户名">
          <el-input v-model="loginForm.username" autocomplete="username" />
        </el-form-item>
        <el-form-item label="密码">
          <el-input v-model="loginForm.password" type="password" autocomplete="current-password" show-password />
        </el-form-item>
        <LoginCaptcha ref="captchaRef" />
        <el-button type="primary" :loading="loading" class="full-button" @click="handleLogin">登录</el-button>
      </el-form>
    </section>
  </main>

  <el-container v-else class="app-shell">
    <el-aside :width="sidebarWidth" :class="['sidebar', { 'sidebar--collapsed': sidebarCollapsed }]">
      <div class="brand">
        <div class="brand__mark">
          <el-icon class="brand__logo"><Cpu /></el-icon>
          <span class="brand__name">IPC Gateway</span>
        </div>
        <el-tooltip :content="sidebarCollapsed ? '展开菜单' : '收起菜单'" placement="right">
          <el-button
            class="sidebar-toggle"
            :icon="sidebarCollapsed ? Expand : Fold"
            circle
            text
            :aria-label="sidebarCollapsed ? '展开菜单' : '收起菜单'"
            @click="toggleSidebar"
          />
        </el-tooltip>
      </div>
      <el-menu
        v-if="!sidebarCollapsed"
        key="sidebar-menu"
        :default-active="activeView"
        :default-openeds="sidebarDefaultOpeneds"
        class="nav"
        @select="activeView = $event"
      >
        <el-sub-menu index="nav-runtime">
          <template #title>
            <el-icon><DataLine /></el-icon>
            <span>运行</span>
          </template>
          <el-menu-item v-if="canAccessView('bigScreen')" index="bigScreen">
            <el-icon><Monitor /></el-icon>
            <span>大屏总览</span>
          </el-menu-item>
          <el-menu-item v-if="canAccessView('topology')" index="topology">
            <el-icon><Connection /></el-icon>
            <span>设备拓扑</span>
          </el-menu-item>
          <el-menu-item v-if="canAccessView('dashboard')" index="dashboard">
            <el-icon><DataLine /></el-icon>
            <span>运行总览</span>
          </el-menu-item>
        </el-sub-menu>
        <el-sub-menu index="nav-config">
          <template #title>
            <el-icon><Operation /></el-icon>
            <span>配置</span>
          </template>
          <el-menu-item v-if="canAccessView('devices')" index="devices">
            <el-icon><Connection /></el-icon>
            <span>设备管理</span>
          </el-menu-item>
          <el-menu-item v-if="canAccessView('flowRules')" index="flowRules">
            <el-icon><Share /></el-icon>
            <span>流程规则</span>
          </el-menu-item>
          <el-menu-item v-if="canAccessView('rules')" index="rules">
            <el-icon><Operation /></el-icon>
            <span>规则引擎</span>
          </el-menu-item>
          <el-menu-item v-if="canAccessView('history')" index="history">
            <el-icon><Document /></el-icon>
            <span>历史库</span>
          </el-menu-item>
        </el-sub-menu>
        <el-sub-menu index="nav-communication">
          <template #title>
            <el-icon><Promotion /></el-icon>
            <span>通讯</span>
          </template>
          <el-menu-item v-if="canAccessView('mqtt')" index="mqtt">
            <el-icon><Promotion /></el-icon>
            <span>MQTT</span>
          </el-menu-item>
          <el-menu-item v-if="canAccessView('opcUa')" index="opcUa">
            <el-icon><Connection /></el-icon>
            <span>OPC UA Server</span>
          </el-menu-item>
        </el-sub-menu>
        <el-sub-menu index="nav-system">
          <template #title>
            <el-icon><Document /></el-icon>
            <span>系统</span>
          </template>
          <el-menu-item v-if="canAccessView('project')" index="project">
            <el-icon><Document /></el-icon>
            <span>项目配置</span>
          </el-menu-item>
          <el-menu-item v-if="canAccessView('audit')" index="audit">
            <el-icon><Tickets /></el-icon>
            <span>审计日志</span>
          </el-menu-item>
          <el-menu-item v-if="canAccessView('security')" index="security">
            <el-icon><Lock /></el-icon>
            <span>工业安全</span>
          </el-menu-item>
          <el-menu-item v-if="canAccessView('maintenance')" index="maintenance">
            <el-icon><Box /></el-icon>
            <span>安装升级</span>
          </el-menu-item>
          <el-menu-item v-if="canAccessView('users')" index="users">
            <el-icon><User /></el-icon>
            <span>人员管理</span>
          </el-menu-item>
          <el-menu-item v-if="canAccessView('roles')" index="roles">
            <el-icon><UserFilled /></el-icon>
            <span>角色管理</span>
          </el-menu-item>
          <el-menu-item v-if="canAccessView('permissions')" index="permissions">
            <el-icon><Operation /></el-icon>
            <span>权限分配</span>
          </el-menu-item>
        </el-sub-menu>
      </el-menu>
    </el-aside>

    <el-container>
      <el-header class="topbar">
        <div class="topbar__title">
          <h2>{{ pageTitle }}</h2>
          <span>{{ sync?.status.configurationStore || 'SqlSugar' }}</span>
        </div>
        <div class="top-actions">
          <div class="refresh-state">
            <el-switch v-model="autoRefresh" size="small" />
            <span>{{ autoRefresh ? '实时刷新' : '手动刷新' }}</span>
            <small>{{ lastRefreshLabel }}</small>
          </div>
          <el-select v-model="refreshIntervalMs" class="interval-select" size="small">
            <el-option label="2s" :value="2000" />
            <el-option label="3s" :value="3000" />
            <el-option label="5s" :value="5000" />
            <el-option label="10s" :value="10000" />
          </el-select>
          <el-button :icon="Refresh" :loading="loading" circle @click="refresh({ silent: false })" />
          <el-tooltip content="修改密码" placement="bottom">
            <el-button :icon="Lock" circle aria-label="修改密码" @click="passwordDialogVisible = true" />
          </el-tooltip>
          <el-button :icon="SwitchButton" circle @click="handleLogout" />
        </div>
      </el-header>

      <el-main class="content">
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
          :project="project"
          :runtime-devices="status?.devices ?? []"
          :runtime-tags="status?.tags ?? []"
          @changed="handleDevicesChanged"
          @editing-state="deviceEditing = $event"
        />
        <RulesView
          v-else-if="activeView === 'rules'"
          :project="project"
          :status="status?.ruleEngine"
          @changed="handleRulesChanged"
          @editing-state="rulesEditing = $event"
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
        <AuditView v-else-if="activeView === 'audit'" />
        <SecurityView v-else-if="activeView === 'security'" />
        <MaintenanceView v-else-if="activeView === 'maintenance'" />
        <UsersView v-else-if="activeView === 'users'" />
        <RolesView v-else-if="activeView === 'roles'" />
        <PermissionsView v-else-if="activeView === 'permissions'" />
        <ProjectView
          v-else
          :project-json="projectJson"
          :can-save="hasPermission(PERMISSIONS.projectEdit)"
          @update:project-json="projectJson = $event"
          @save-json="saveProjectJson"
        />
      </el-main>
    </el-container>
  </el-container>

  <ChangePasswordDialog v-model="passwordDialogVisible" />
  <ErrorDetailDrawer v-model:visible="errorDrawerVisible" :error="selectedError" />
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, provide, reactive, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { Box, Connection, Cpu, DataLine, Document, Expand, Fold, Lock, Monitor, Operation, Promotion, Refresh, Share, SwitchButton, Tickets, User, UserFilled } from '@element-plus/icons-vue'
import {
  loadSync,
  loadReadyHealth,
  loadCurrentUser,
  login,
  logout,
  saveHistory,
  saveMqtt,
  saveOpcUa,
  saveProject,
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
import DevicesView from './components/DevicesView.vue'
import ErrorDetailDrawer from './components/ErrorDetailDrawer.vue'
import FlowRulesView from './components/FlowRulesView.vue'
import HistoryView from './components/history/HistoryView.vue'
import LoginCaptcha from './components/LoginCaptcha.vue'
import LoginIndustrialBackdrop from './components/LoginIndustrialBackdrop.vue'
import MaintenanceView from './components/MaintenanceView.vue'
import MqttView from './components/MqttView.vue'
import OpcUaView from './components/OpcUaView.vue'
import PermissionsView from './components/PermissionsView.vue'
import ProjectView from './components/ProjectView.vue'
import RolesView from './components/RolesView.vue'
import RulesView from './components/RulesView.vue'
import SecurityView from './components/SecurityView.vue'
import UsersView from './components/UsersView.vue'
import { formatDateTime } from './utils/format'
import { createPermissionSet, PermissionContextKey, PERMISSIONS, normalizePermission } from './utils/permissions'

const defaultStartupView = 'bigScreen'
const authenticated = ref(localStorage.getItem('ipc.gateway.authenticated') === 'true')
const activeView = ref(defaultStartupView)
const sidebarCollapsed = ref(localStorage.getItem('ipc.gateway.sidebarCollapsed') === 'true')
const sidebarDefaultOpeneds: string[] = []
const loading = ref(false)
const autoRefresh = ref(true)
const refreshIntervalMs = ref(3000)
const lastRefreshTime = ref('')
const sync = ref<SyncPayload | null>(null)
const health = ref<GatewayHealthResponse | null>(null)
const storageHealth = ref<StorageHealthConfig | null>(null)
const project = ref<ProjectConfig | null>(null)
const mqtt = ref<Record<string, any> | null>(null)
const opcUa = ref<OpcUaServerConfig | null>(null)
const history = ref<HistoryConfig | null>(null)
const projectJson = ref('')
const trend = ref<TrendSample[]>([])
const selectedError = ref<RuntimeErrorDetail | null>(null)
const errorDrawerVisible = ref(false)
const passwordDialogVisible = ref(false)
const deviceEditing = ref(false)
const rulesEditing = ref(false)
const flowRulesEditing = ref(false)
const currentPermissions = ref<string[]>([])
const loginForm = reactive({ username: '', password: '' })
const captchaRef = ref<InstanceType<typeof LoginCaptcha> | null>(null)
let refreshTimer: number | undefined
let refreshController: AbortController | undefined

const status = computed(() => sync.value ? sanitizeStatus(sync.value.status, project.value ?? sync.value.project) : null)
const permissionSet = computed(() => createPermissionSet(currentPermissions.value))
const sidebarWidth = computed(() => (sidebarCollapsed.value ? '76px' : '248px'))
const lastRefreshLabel = computed(() => lastRefreshTime.value ? formatDateTime(lastRefreshTime.value) : '-')
const pageTitle = computed(() => ({
  bigScreen: '大屏总览',
  topology: '设备拓扑',
  dashboard: '运行总览',
  devices: '设备管理',
  rules: '规则引擎',
  flowRules: '流程规则',
  mqtt: 'MQTT',
  opcUa: 'OPC UA Server',
  history: '历史库',
  audit: '审计日志',
  security: '工业安全',
  maintenance: '安装升级',
  users: '人员管理',
  roles: '角色管理',
  permissions: '权限分配',
  project: '项目配置'
}[activeView.value] || '大屏总览'))

const viewPermissions: Record<string, string[]> = {
  bigScreen: [PERMISSIONS.bigScreenView],
  topology: [PERMISSIONS.topologyView],
  dashboard: [PERMISSIONS.dashboardView],
  devices: [PERMISSIONS.devicesView],
  flowRules: [PERMISSIONS.flowRulesView],
  rules: [PERMISSIONS.rulesView],
  mqtt: [PERMISSIONS.mqttView],
  opcUa: [PERMISSIONS.opcUaView],
  history: [PERMISSIONS.historyView],
  project: [PERMISSIONS.projectView],
  audit: [PERMISSIONS.auditView],
  security: [PERMISSIONS.securityView],
  maintenance: [PERMISSIONS.maintenanceView],
  users: [PERMISSIONS.usersView],
  roles: [PERMISSIONS.rolesView],
  permissions: [PERMISSIONS.permissionsView]
}
const viewOrder = ['bigScreen', 'topology', 'dashboard', 'devices', 'flowRules', 'rules', 'history', 'mqtt', 'opcUa', 'project', 'audit', 'security', 'maintenance', 'users', 'roles', 'permissions']

provide(PermissionContextKey, {
  permissions: currentPermissions,
  permissionSet,
  hasPermission,
  hasAnyPermission
})

onMounted(() => {
  if (authenticated.value) initializeAuthenticatedSession()
  syncAutoRefresh()
})

onBeforeUnmount(() => {
  stopAutoRefresh()
  refreshController?.abort()
})

watch([authenticated, autoRefresh, refreshIntervalMs], syncAutoRefresh)
watch(currentPermissions, ensureActiveViewAllowed)

async function handleLogin() {
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
    ElMessage.error(error instanceof Error ? error.message : '登录失败')
  } finally {
    loading.value = false
  }
}

async function handleLogout() {
  stopAutoRefresh()
  refreshController?.abort()
  await logout().catch(() => undefined)
  localStorage.removeItem('ipc.gateway.authenticated')
  currentPermissions.value = []
  authenticated.value = false
}

async function refresh(options: { silent: boolean }) {
  refreshController?.abort()
  const controller = new AbortController()
  refreshController = controller
  if (!options.silent) loading.value = true
  try {
    const [data, healthData] = await Promise.all([
      loadSync(controller.signal),
      loadReadyHealth(controller.signal).catch(error => {
        if (isAbortError(error)) throw error
        return null
      })
    ])
    if (controller.signal.aborted || refreshController !== controller) return
    applySync(data, options, healthData)
  } catch (error) {
    if (isAbortError(error)) return
    if (!options.silent) ElMessage.error(error instanceof Error ? error.message : '加载失败')
    if (error instanceof Error && error.message.includes('登录已过期')) {
      localStorage.removeItem('ipc.gateway.authenticated')
      currentPermissions.value = []
      authenticated.value = false
    }
  } finally {
    if (refreshController === controller) refreshController = undefined
    if (!options.silent) loading.value = false
  }
}

async function initializeAuthenticatedSession() {
  loading.value = true
  try {
    await loadSessionPermissions()
    await refresh({ silent: false })
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '会话加载失败')
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
  currentPermissions.value = session.permissions ?? []
  ensureActiveViewAllowed()
}

function hasPermission(permission: string) {
  return permissionSet.value.has(normalizePermission(permission))
}

function hasAnyPermission(permissions: string[]) {
  return permissions.some(permission => hasPermission(permission))
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
  const preserveProjectConfig = shouldPreserveActiveConfigDraft('project', options) || (options.silent && (
    (activeView.value === 'devices' && deviceEditing.value) ||
    (activeView.value === 'rules' && rulesEditing.value) ||
    (activeView.value === 'flowRules' && flowRulesEditing.value)
  ))
  const preserveMqttConfig = shouldPreserveActiveConfigDraft('mqtt', options)
  const preserveOpcUaConfig = shouldPreserveActiveConfigDraft('opcUa', options)
  const preserveHistoryConfig = shouldPreserveActiveConfigDraft('history', options)
  sync.value = data
  health.value = healthData
  lastRefreshTime.value = new Date().toISOString()
  if (!preserveProjectConfig) {
    project.value = structuredClone(data.project)
    projectJson.value = JSON.stringify(data.project, null, 2)
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

function shouldPreserveActiveConfigDraft(view: string, options: { silent: boolean }) {
  // Silent refresh updates runtime status, but must not replace the active editor draft while the user is typing.
  return options.silent && activeView.value === view
}

function pushTrend(data: SyncPayload) {
  const visibleStatus = sanitizeStatus(data.status, data.project)
  const devices = visibleStatus?.devices ?? []
  const successRate = devices.length === 0
    ? 0
    : devices.reduce((sum, item) => sum + (item.successRate || 0), 0) / devices.length
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

function sanitizeStatus(source: GatewayStatus | null | undefined, currentProject: ProjectConfig | null | undefined): GatewayStatus | null {
  if (!source) return null
  if (!currentProject) return source

  const projectDevices = currentProject.devices ?? []
  const deviceIds = new Set(projectDevices.map(device => normalizeKey(device.id)).filter(Boolean))
  const deviceNames = new Set(projectDevices.map(device => normalizeKey(device.name)).filter(Boolean))
  const tagScope = buildProjectTagScope(projectDevices)
  const devices = filterRuntimeDevices(source.devices ?? [], deviceIds, deviceNames)
  const tags = filterRuntimeTags(source.tags ?? [], tagScope.tagIds, tagScope.tagPaths)
  const goodTagCount = tags.filter(tag => normalizeKey(tag.quality) === 'good').length
  const badTagCount = tags.filter(tag => {
    const quality = normalizeKey(tag.quality)
    return !!quality && quality !== 'good' && quality !== 'unknown'
  }).length
  const recentErrors = (source.recentErrors ?? []).filter(error => {
    const deviceName = normalizeKey(error.deviceName)
    return !deviceName || deviceNames.has(deviceName)
  })

  return {
    ...source,
    deviceCount: projectDevices.length,
    groupCount: tagScope.groupCount,
    tagCount: tagScope.tagCount,
    enabledDeviceCount: projectDevices.filter(device => device.enabled).length,
    onlineDeviceCount: devices.filter(device => device.isConnected).length,
    goodTagCount,
    badTagCount,
    noDataTagCount: Math.max(0, tagScope.tagCount - goodTagCount - badTagCount),
    devices,
    tags,
    recentErrors
  }
}

function filterRuntimeDevices(devices: DeviceRuntimeStatus[], deviceIds: Set<string>, deviceNames: Set<string>) {
  if (deviceIds.size === 0 && deviceNames.size === 0) return []
  return devices.filter(device => {
    const id = normalizeKey(device.deviceId)
    const name = normalizeKey(device.deviceName)
    return (!!id && deviceIds.has(id)) || (!!name && deviceNames.has(name))
  })
}

function buildProjectTagScope(devices: ProjectConfig['devices']) {
  const tagIds = new Set<string>()
  const tagPaths = new Set<string>()
  let groupCount = 0
  let tagCount = 0

  for (const device of devices) {
    for (const tag of device.tags ?? []) {
      tagCount += 1
      addTagScope(tagIds, tagPaths, device.id, device.name, '', '', tag.id, tag.name)
    }

    for (const group of device.groups ?? []) {
      groupCount += 1
      for (const tag of group.tags ?? []) {
        tagCount += 1
        addTagScope(tagIds, tagPaths, device.id, device.name, group.id, group.name, tag.id, tag.name)
      }
    }
  }

  return { tagIds, tagPaths, groupCount, tagCount }
}

function addTagScope(
  tagIds: Set<string>,
  tagPaths: Set<string>,
  deviceId: string,
  deviceName: string,
  groupId: string,
  groupName: string,
  tagId: string,
  tagName: string
) {
  const id = normalizeKey(tagId)
  if (id) tagIds.add(id)
  addIfNotEmpty(tagPaths, tagPathKey(deviceName, groupName, tagName))
  addIfNotEmpty(tagPaths, tagPathKey(deviceId, groupId, tagName))
}

function filterRuntimeTags(tags: TagValueSnapshot[], tagIds: Set<string>, tagPaths: Set<string>) {
  if (tagIds.size === 0 && tagPaths.size === 0) return []
  return tags.filter(tag => {
    const id = normalizeKey(tag.tagId)
    return (!!id && tagIds.has(id)) ||
      tagPaths.has(tagPathKey(tag.deviceName, tag.groupName, tag.tagName)) ||
      tagPaths.has(tagPathKey(tag.deviceId, tag.groupId, tag.tagName))
  })
}

function tagPathKey(device: string | null | undefined, group: string | null | undefined, tag: string | null | undefined) {
  return [device, group, tag].map(normalizeKey).join('/')
}

function addIfNotEmpty(set: Set<string>, value: string) {
  if (value.replace(/\//g, '')) set.add(value)
}

function normalizeKey(value: string | null | undefined) {
  return (value ?? '').trim().toLowerCase()
}

function syncAutoRefresh() {
  stopAutoRefresh()
  if (!authenticated.value || !autoRefresh.value) return
  refreshTimer = window.setInterval(() => refresh({ silent: true }), refreshIntervalMs.value)
}

function stopAutoRefresh() {
  if (refreshTimer !== undefined) {
    window.clearInterval(refreshTimer)
    refreshTimer = undefined
  }
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

async function saveProjectJson() {
  if (!hasPermission(PERMISSIONS.projectEdit)) {
    ElMessage.warning('当前用户没有保存项目配置权限')
    return
  }
  try {
    const parsed = JSON.parse(projectJson.value) as ProjectConfig
    loading.value = true
    project.value = await saveProject(parsed)
    projectJson.value = JSON.stringify(project.value, null, 2)
    ElMessage.success('已保存')
    await refresh({ silent: true })
  } catch (error) {
    ElMessage.error(error instanceof SyntaxError ? 'JSON 格式不正确' : error instanceof Error ? error.message : '保存失败')
  } finally {
    loading.value = false
  }
}

async function persistMqtt() {
  if (!hasPermission(PERMISSIONS.mqttEdit)) {
    ElMessage.warning('当前用户没有保存 MQTT 配置权限')
    return
  }
  if (!mqtt.value) return
  loading.value = true
  try {
    mqtt.value = await saveMqtt(mqtt.value)
    ElMessage.success('已保存')
    await refresh({ silent: true })
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存失败')
  } finally {
    loading.value = false
  }
}

async function persistOpcUa() {
  if (!hasPermission(PERMISSIONS.opcUaEdit)) {
    ElMessage.warning('当前用户没有保存 OPC UA Server 配置权限')
    return
  }
  if (!opcUa.value) return
  loading.value = true
  try {
    opcUa.value = await saveOpcUa(opcUa.value)
    ElMessage.success('已保存')
    await refresh({ silent: true })
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存失败')
  } finally {
    loading.value = false
  }
}

async function persistHistory(options: HistoryConfig) {
  if (!hasPermission(PERMISSIONS.historyEdit)) {
    ElMessage.warning('当前用户没有保存历史库配置权限')
    return
  }
  loading.value = true
  try {
    history.value = await saveHistory(options)
    ElMessage.success('已保存')
    await refresh({ silent: true })
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存失败')
  } finally {
    loading.value = false
  }
}

async function persistStorageHealth(options?: StorageHealthConfig) {
  if (!hasPermission(PERMISSIONS.dashboardStorageHealthEdit)) {
    ElMessage.warning('当前用户没有保存历史库健康阈值权限')
    return
  }
  const nextOptions = options ?? storageHealth.value
  if (!nextOptions) return
  loading.value = true
  try {
    storageHealth.value = await saveStorageHealth(nextOptions)
    ElMessage.success('已保存')
    await refresh({ silent: true })
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存失败')
  } finally {
    loading.value = false
  }
}

function openError(error: RuntimeErrorDetail) {
  selectedError.value = error
  errorDrawerVisible.value = true
}
</script>

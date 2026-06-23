<template>
  <section class="device-workspace" @click="closeContextMenu">
    <div class="device-toolbar">
      <el-input v-model="deviceFilterInput" clearable placeholder="按设备名称筛选">
        <template #prefix>
          <el-icon><Search /></el-icon>
        </template>
      </el-input>
      <el-tag type="info">{{ filteredDevices.length }} / {{ devices.length }}</el-tag>
      <el-button :icon="Refresh" @click="emit('changed')">刷新</el-button>
      <el-button v-if="canCreateDevice" type="primary" :icon="Plus" @click="openCreate">新增设备</el-button>
    </div>

    <div class="device-manager">
      <el-card shadow="never" class="panel-card device-tree-panel">
        <template #header>
          <div class="card-header">
            <span>设备树</span>
            <el-tag size="small" type="info">{{ totalGroupCount }} 组</el-tag>
          </div>
        </template>

        <div ref="treeScroller" class="device-tree device-tree-virtual" @scroll="onTreeScroll">
          <div class="device-tree-virtual__spacer" :style="{ height: `${virtualTreeHeight}px` }">
            <div
              v-for="row in visibleTreeRows"
              :key="row.node.key"
              :class="['device-tree-row', { 'is-current': row.node.key === selectedNodeKey }]"
              :style="{ transform: `translateY(${row.top}px)` }"
              @click.stop="selectTreeNode(row.node)"
              @contextmenu.prevent.stop="openContextMenu($event, row.node)"
            >
              <button
                type="button"
                class="device-tree-row__toggle"
                :style="{ marginLeft: `${row.depth * 18}px` }"
                :disabled="!row.expandable"
                @click.stop="toggleTreeNode(row.node)"
              >
                {{ row.expandable ? (expandedTreeKeys.has(row.node.key) ? '▾' : '▸') : '' }}
              </button>
              <el-icon>
                <component :is="treeNodeIcon(row.node)" />
              </el-icon>
              <span class="device-tree-node__label">{{ row.node.label }}</span>
              <span class="device-tree-node__badges">
                <el-tag size="small" :type="treeStatusType(row.node)">
                  {{ treeStatusText(row.node) }}
                </el-tag>
                <el-tag size="small" type="info">{{ treeTagCount(row.node) }} 标签</el-tag>
              </span>
            </div>
          </div>
        </div>
      </el-card>

      <el-card shadow="never" class="panel-card device-detail-panel">
        <template #header>
          <div class="card-header">
            <div class="detail-title">
              <span>{{ detailTitle }}</span>
              <small>{{ detailSubtitle }}</small>
            </div>
            <div class="card-actions">
              <el-button v-if="selectedNode.type === 'device' && selectedDevice && canCreateGroup" size="small" @click="openCreateGroup(selectedDevice)">新增分组</el-button>
              <el-button v-if="selectedDevice && canCreateTag" size="small" type="primary" @click="openCreateTag(selectedDevice, selectedGroup)">新增标签</el-button>
              <el-button v-if="selectedGroup && canEditGroup" size="small" @click="openEditGroup(selectedGroup)">编辑分组</el-button>
              <el-button v-if="selectedGroup && canDeleteGroup" size="small" type="danger" @click="removeGroup(selectedGroup)">删除分组</el-button>
              <el-button v-if="selectedDevice && canEditDevice" size="small" @click="openEdit(selectedDevice)">编辑设备</el-button>
              <el-button v-if="selectedDevice && canDeleteDevice" size="small" type="danger" @click="removeDevice(selectedDevice)">删除设备</el-button>
              <el-button v-if="!selectedDevice && canCreateDevice" size="small" type="primary" :icon="Plus" @click="openCreate">新增设备</el-button>
            </div>
          </div>
        </template>

        <div v-if="selectedNode.type === 'root'" class="device-overview">
          <el-table :data="pagedFilteredDevices" row-key="id" height="520">
            <el-table-column prop="name" label="设备名称" min-width="170" fixed />
            <el-table-column label="协议" width="150">
              <template #default="{ row }">{{ protocolLabel(row.protocol) }}</template>
            </el-table-column>
            <el-table-column label="连接参数" min-width="220" show-overflow-tooltip>
              <template #default="{ row }">{{ connectionSummary(row) }}</template>
            </el-table-column>
            <el-table-column label="状态" width="110">
              <template #default="{ row }">
                <el-tag :type="deviceRuntime(row)?.isConnected ? 'success' : row.enabled ? 'warning' : 'info'">
                  {{ deviceRuntime(row)?.status || (row.enabled ? '未连接' : '停用') }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="defaultScanRateMs" label="周期(ms)" width="110" />
            <el-table-column label="标签" width="90">
              <template #default="{ row }">{{ countDeviceTags(row) }}</template>
            </el-table-column>
            <el-table-column label="成功率" width="100">
              <template #default="{ row }">{{ Number(deviceRuntime(row)?.successRate ?? 0).toFixed(1) }}%</template>
            </el-table-column>
            <el-table-column label="操作" width="178" fixed="right">
              <template #default="{ row }">
                <div class="table-actions table-actions--compact">
                  <el-button v-if="canEditDevice" size="small" text type="primary" @click="openEdit(row)">编辑</el-button>
                  <el-button v-if="canDeleteDevice" size="small" text type="danger" @click="removeDevice(row)">删除</el-button>
                </div>
              </template>
            </el-table-column>
            <template #empty>
              <el-empty :description="deviceEmptyText">
                <el-button v-if="canCreateDevice" type="primary" :icon="Plus" @click="openCreate">新增设备</el-button>
              </el-empty>
            </template>
          </el-table>
          <el-pagination
            v-if="filteredDevices.length > devicePageSize"
            v-model:current-page="devicePage"
            v-model:page-size="devicePageSize"
            class="device-pagination"
            layout="total, sizes, prev, pager, next"
            :page-sizes="[50, 100, 200, 500]"
            :total="filteredDevices.length"
          />
        </div>

        <div v-else class="device-detail">
          <div v-if="selectedDevice" class="device-summary">
            <div>
              <span>协议</span>
              <strong>{{ protocolLabel(selectedDevice.protocol) }}</strong>
            </div>
            <div>
              <span>连接</span>
              <strong>{{ connectionSummary(selectedDevice) }}</strong>
            </div>
            <div>
              <span>状态</span>
              <el-tag :type="deviceRuntime(selectedDevice)?.isConnected ? 'success' : selectedDevice.enabled ? 'warning' : 'info'">
                {{ deviceRuntime(selectedDevice)?.status || (selectedDevice.enabled ? '未连接' : '停用') }}
              </el-tag>
            </div>
            <div>
              <span>成功率</span>
              <strong>{{ Number(deviceRuntime(selectedDevice)?.successRate ?? 0).toFixed(1) }}%</strong>
            </div>
          </div>

          <div
            v-if="selectedTags.length"
            ref="tagScroller"
            class="device-tag-table"
            @scroll="onTagScroll"
          >
            <div class="device-tag-table__head">
              <span>标签</span>
              <span>地址</span>
              <span>原始值</span>
              <span>当前值</span>
              <span>清洗</span>
              <span>质量</span>
              <span>更新时间</span>
              <span>最近错误</span>
              <span>数据类型</span>
              <span>缩放</span>
              <span>表地址</span>
              <span>数据标识</span>
              <span>表类型</span>
              <span>周期(ms)</span>
              <span>操作</span>
            </div>
            <div class="device-tag-table__body" :style="{ height: `${virtualTagHeight}px` }">
              <div
                v-for="row in visibleTagRows"
                :key="row.tag.id || `${row.index}-${row.tag.name}`"
                class="device-tag-table__row"
                :style="{ transform: `translateY(${row.top}px)` }"
              >
                <span class="strong-cell" :title="row.tag.name">{{ row.tag.name }}</span>
                <span :title="row.tag.address">{{ row.tag.address || '-' }}</span>
                <span :title="tagRawValue(row.tag)">{{ tagRawValue(row.tag) }}</span>
                <span :title="tagCurrentValue(row.tag)">{{ tagCurrentValue(row.tag) }}</span>
                <span :title="tagCleaningText(row.tag)">{{ tagCleaningText(row.tag) }}</span>
                <span>
                  <el-tag size="small" :type="tagQualityType(row.tag)">{{ tagQualityText(row.tag) }}</el-tag>
                </span>
                <span :title="tagUpdatedTime(row.tag)">{{ tagUpdatedTime(row.tag) }}</span>
                <span :title="tagLastError(row.tag)">{{ tagLastError(row.tag) }}</span>
                <span>{{ row.tag.dataType }}</span>
                <span :title="tagScalingText(row.tag)">{{ tagScalingText(row.tag) }}</span>
                <span :title="row.tag.meterAddress">{{ row.tag.meterAddress || '-' }}</span>
                <span :title="row.tag.meterDataIdentifier">{{ row.tag.meterDataIdentifier || '-' }}</span>
                <span>{{ row.tag.meterType || '-' }}</span>
                <span>{{ row.tag.scanRateMs || selectedDevice?.defaultScanRateMs || 1000 }}</span>
                <span class="device-tag-table__actions table-actions">
                  <el-button v-if="hasTagWritePermission" size="small" text type="primary" :disabled="!canWriteTag(row.tag)" @click.stop="openWriteTag(row.tag)">写入</el-button>
                  <el-button v-if="canEditTag" size="small" text type="primary" @click.stop="openEditTag(row.tag)">编辑</el-button>
                  <el-button v-if="canDeleteTag" size="small" text type="danger" @click.stop="removeTag(row.tag)">删除</el-button>
                </span>
              </div>
            </div>
          </div>
          <el-empty v-else :description="tagEmptyText" class="device-tag-empty">
            <el-button v-if="selectedDevice && canCreateTag" type="primary" :icon="Plus" @click="openCreateTag(selectedDevice, selectedGroup)">新增标签</el-button>
          </el-empty>
        </div>
      </el-card>
    </div>

    <el-drawer v-model="drawerVisible" :title="editingId ? '编辑设备' : '新增设备'" size="640px">
      <el-form v-if="form" label-width="130px" :model="form" class="device-form">
        <el-divider content-position="left">基础信息</el-divider>
        <el-form-item label="设备名称" required>
          <el-input v-model="form.name" placeholder="例如：锅炉房PLC" />
        </el-form-item>
        <div class="form-grid">
          <el-form-item label="协议" required>
            <el-select v-model="form.protocol" filterable @change="changeProtocol">
              <el-option v-for="item in protocolOptions" :key="item.value" :label="item.label" :value="item.value" />
            </el-select>
          </el-form-item>
          <el-form-item label="启用">
            <el-switch v-model="form.enabled" />
          </el-form-item>
        </div>
        <div class="form-grid">
          <el-form-item label="采集周期(ms)">
            <el-input-number v-model="form.defaultScanRateMs" :min="100" :max="3600000" />
          </el-form-item>
          <el-form-item label="失败重试(ms)">
            <el-input-number v-model="form.failureRetryDelayMs" :min="100" :max="3600000" />
          </el-form-item>
          <el-form-item label="最大重试(ms)">
            <el-input-number v-model="form.maxFailureRetryDelayMs" :min="100" :max="3600000" />
          </el-form-item>
        </div>

        <el-divider content-position="left">连接参数</el-divider>
        <DeviceConnectionFields :device="form" :protocol="form.protocol" />
      </el-form>

      <template #footer>
        <div class="drawer-footer">
          <el-button @click="drawerVisible = false">取消</el-button>
          <el-button v-if="canSaveCurrentDevice" type="primary" :loading="saving" @click="saveDevice">保存</el-button>
        </div>
      </template>
    </el-drawer>

    <el-drawer v-model="groupDrawerVisible" :title="editingGroupId ? '编辑分组' : '新增分组'" size="480px">
      <DeviceGroupForm v-if="groupForm" :model="groupForm" />
      <template #footer>
        <div class="drawer-footer">
          <el-button @click="groupDrawerVisible = false">取消</el-button>
          <el-button v-if="canSaveCurrentGroup" type="primary" :loading="groupSaving" @click="saveGroup">保存</el-button>
        </div>
      </template>
    </el-drawer>

    <el-drawer v-model="tagDrawerVisible" :title="editingTagId ? '编辑标签' : '新增标签'" size="640px">
      <DeviceTagForm ref="tagFormComponent" v-if="tagForm" :model="tagForm" :device-protocol="tagTargetDevice?.protocol || ''" />
      <template #footer>
        <div class="drawer-footer">
          <el-button @click="tagDrawerVisible = false">取消</el-button>
          <el-button v-if="canSaveCurrentTag" type="primary" :loading="tagSaving" @click="saveTag">保存</el-button>
        </div>
      </template>
    </el-drawer>

    <el-drawer v-model="writeDrawerVisible" title="写入标签值" size="460px">
      <el-form v-if="writeTargetTag" label-width="110px" class="device-form">
        <el-form-item label="设备">
          <el-input :model-value="writeTargetDevice?.name || ''" disabled />
        </el-form-item>
        <el-form-item label="分组">
          <el-input :model-value="writeTargetGroup?.name || '直属标签'" disabled />
        </el-form-item>
        <el-form-item label="标签">
          <el-input :model-value="writeTargetTag.name" disabled />
        </el-form-item>
        <el-form-item label="数据类型">
          <el-input :model-value="writeTargetTag.dataType" disabled />
        </el-form-item>
        <el-form-item label="当前值">
          <el-input :model-value="tagCurrentValue(writeTargetTag)" disabled />
        </el-form-item>
        <el-form-item label="写入值" required :error="writeError">
          <el-input v-model="writeValueText" :placeholder="writePlaceholder(writeTargetTag)" />
        </el-form-item>
      </el-form>
      <template #footer>
        <div class="drawer-footer">
          <el-button @click="writeDrawerVisible = false">取消</el-button>
          <el-button v-if="writeTargetTag && canWriteTag(writeTargetTag)" type="primary" :loading="writeSaving" @click="submitWriteTag">写入</el-button>
        </div>
      </template>
    </el-drawer>

    <div
      v-if="contextMenu.visible"
      class="tree-context-menu"
      :style="{ left: `${contextMenu.x}px`, top: `${contextMenu.y}px` }"
      @click.stop
      @contextmenu.prevent
    >
      <button v-for="action in contextActions" :key="action.key" type="button" @click="runContextAction(action.key)">
        <el-icon><component :is="action.icon" /></el-icon>
        <span>{{ action.label }}</span>
      </button>
    </div>
  </section>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, reactive, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Collection, CollectionTag, Connection, Delete, Edit, Folder, FolderAdd, Plus, Refresh, Search } from '@element-plus/icons-vue'
import {
  createDeviceTag,
  createDevice,
  createGroup,
  createGroupTag,
  deleteDevice,
  deleteGroup as deleteGroupRequest,
  deleteTag as deleteTagRequest,
  updateGroup,
  updateTag,
  updateDevice,
  writeTag,
  type DeviceConfig,
  type DeviceRuntimeStatus,
  type GroupConfig,
  type ProjectConfig,
  type TagConfig,
  type TagValueSnapshot
} from '../api'
import DeviceConnectionFields from './DeviceConnectionFields.vue'
import DeviceGroupForm from './DeviceGroupForm.vue'
import DeviceTagForm from './DeviceTagForm.vue'
import {
  applyProtocolDefaults,
  cloneDevice,
  createDeviceDraft,
  normalizeDevice,
  protocolLabel,
  protocolOptions
} from '../utils/deviceDefaults'
import { cloneGroup, cloneTag, createGroupDraft, createTagDraft, normalizeGroup, normalizeTag } from '../utils/tagDefaults'
import { formatDateTime } from '../utils/format'
import { PERMISSIONS, usePermissions } from '../utils/permissions'

type TreeNodeType = 'root' | 'device' | 'group'

interface DeviceTreeNode {
  key: string
  type: TreeNodeType
  label: string
  device?: DeviceConfig
  group?: GroupConfig
  children?: DeviceTreeNode[]
}

interface ContextAction {
  key: string
  label: string
  icon: unknown
}

interface DeviceTreeRow {
  node: DeviceTreeNode
  depth: number
  top: number
  expandable: boolean
}

interface TagVirtualRow {
  tag: TagConfig
  index: number
  top: number
}

const props = defineProps<{
  project: ProjectConfig | null
  runtimeDevices: DeviceRuntimeStatus[]
  runtimeTags: TagValueSnapshot[]
}>()

const emit = defineEmits<{
  changed: []
  editingState: [editing: boolean]
}>()

const { hasPermission } = usePermissions()
const treeRowHeight = 38
const treeOverscan = 8
const treeViewportRows = 16
const tagRowHeight = 44
const tagHeaderHeight = 42
const tagOverscan = 10
const tagViewportRows = 12
const treeScroller = ref<HTMLElement | null>(null)
const treeScrollTop = ref(0)
const tagScroller = ref<HTMLElement | null>(null)
const tagScrollTop = ref(0)
const expandedTreeKeys = ref(new Set<string>(['root']))
const deviceFilterInput = ref('')
const deviceNameFilter = ref('')
const devicePage = ref(1)
const devicePageSize = ref(100)
const selectedNodeKey = ref('root')
const drawerVisible = ref(false)
const saving = ref(false)
const editingId = ref('')
const form = ref<DeviceConfig | null>(null)
const groupDrawerVisible = ref(false)
const groupSaving = ref(false)
const editingGroupId = ref('')
const groupTargetDevice = ref<DeviceConfig | null>(null)
const groupForm = ref<GroupConfig | null>(null)
const tagDrawerVisible = ref(false)
const tagSaving = ref(false)
const editingTagId = ref('')
const tagTargetDevice = ref<DeviceConfig | null>(null)
const tagTargetGroup = ref<GroupConfig | null>(null)
const tagForm = ref<TagConfig | null>(null)
const tagFormComponent = ref<InstanceType<typeof DeviceTagForm> | null>(null)
const writeDrawerVisible = ref(false)
const writeSaving = ref(false)
const writeTargetDevice = ref<DeviceConfig | null>(null)
const writeTargetGroup = ref<GroupConfig | null>(null)
const writeTargetTag = ref<TagConfig | null>(null)
const writeValueText = ref('')
const writeError = ref('')
const contextMenu = reactive({
  visible: false,
  x: 0,
  y: 0,
  node: null as DeviceTreeNode | null
})
let filterTimer: number | undefined

const editingActive = computed(() =>
  drawerVisible.value ||
  groupDrawerVisible.value ||
  tagDrawerVisible.value ||
  writeDrawerVisible.value ||
  saving.value ||
  groupSaving.value ||
  tagSaving.value ||
  writeSaving.value)

const canCreateDevice = computed(() => hasPermission(PERMISSIONS.devicesCreate))
const canEditDevice = computed(() => hasPermission(PERMISSIONS.devicesEdit))
const canDeleteDevice = computed(() => hasPermission(PERMISSIONS.devicesDelete))
const canCreateGroup = computed(() => hasPermission(PERMISSIONS.groupsCreate))
const canEditGroup = computed(() => hasPermission(PERMISSIONS.groupsEdit))
const canDeleteGroup = computed(() => hasPermission(PERMISSIONS.groupsDelete))
const canCreateTag = computed(() => hasPermission(PERMISSIONS.tagsCreate))
const canEditTag = computed(() => hasPermission(PERMISSIONS.tagsEdit))
const canDeleteTag = computed(() => hasPermission(PERMISSIONS.tagsDelete))
const hasTagWritePermission = computed(() => hasPermission(PERMISSIONS.tagsWrite))
const canSaveCurrentDevice = computed(() => editingId.value ? canEditDevice.value : canCreateDevice.value)
const canSaveCurrentGroup = computed(() => editingGroupId.value ? canEditGroup.value : canCreateGroup.value)
const canSaveCurrentTag = computed(() => editingTagId.value ? canEditTag.value : canCreateTag.value)

const devices = computed(() => (props.project?.devices ?? []).map(cloneDevice))
const runtimeMap = computed(() => {
  const map = new Map<string, DeviceRuntimeStatus>()
  for (const item of props.runtimeDevices ?? []) {
    if (item.deviceId) map.set(item.deviceId.toLowerCase(), item)
    if (item.deviceName) map.set(item.deviceName.toLowerCase(), item)
  }
  return map
})

const runtimeTagMap = computed(() => {
  const map = new Map<string, TagValueSnapshot>()
  for (const item of props.runtimeTags ?? []) {
    if (item.tagId) map.set(`id:${normalizeKey(item.tagId)}`, item)
    addRuntimeTagPath(map, item.deviceName, item.groupName, item.tagName, item)
    addRuntimeTagPath(map, item.deviceId, item.groupId, item.tagName, item)
  }
  return map
})

const filteredDevices = computed(() => {
  const keyword = deviceNameFilter.value.trim().toLowerCase()
  if (!keyword) return devices.value
  return devices.value.filter(device => device.name.toLowerCase().includes(keyword))
})

const treeData = computed<DeviceTreeNode[]>(() => [
  {
    key: 'root',
    type: 'root',
    label: '全部设备',
    children: filteredDevices.value.map(device => ({
      key: deviceNodeKey(device),
      type: 'device',
      label: device.name || '未命名设备',
      device,
      children: (device.groups ?? []).map(group => ({
        key: groupNodeKey(device, group),
        type: 'group',
        label: group.name || '未命名分组',
        device,
        group
      }))
    }))
  }
])

const totalGroupCount = computed(() => filteredDevices.value.reduce((sum, device) => sum + (device.groups?.length ?? 0), 0))
const totalTagCount = computed(() => filteredDevices.value.reduce((sum, device) => sum + countDeviceTags(device), 0))

const selectedNode = computed<DeviceTreeNode>(() => findNode(treeData.value[0], selectedNodeKey.value) ?? treeData.value[0])
const selectedDevice = computed(() => selectedNode.value.type === 'device' || selectedNode.value.type === 'group' ? selectedNode.value.device : undefined)
const selectedGroup = computed(() => selectedNode.value.type === 'group' ? selectedNode.value.group : undefined)
const selectedTags = computed<TagConfig[]>(() => {
  if (selectedGroup.value) return selectedGroup.value.tags ?? []
  if (selectedDevice.value) return selectedDevice.value.tags ?? []
  return []
})
const flatTreeRows = computed<DeviceTreeRow[]>(() => {
  const rows: Array<Omit<DeviceTreeRow, 'top'>> = []
  pushTreeNode(rows, treeData.value[0], 0)
  return rows.map((row, index) => ({ ...row, top: index * treeRowHeight }))
})
const virtualTreeHeight = computed(() => flatTreeRows.value.length * treeRowHeight)
const visibleTreeRows = computed(() => {
  const start = Math.max(0, Math.floor(treeScrollTop.value / treeRowHeight) - treeOverscan)
  const count = treeViewportRows + treeOverscan * 2
  return flatTreeRows.value.slice(start, start + count)
})
const virtualTagHeight = computed(() => selectedTags.value.length * tagRowHeight)
const visibleTagRows = computed<TagVirtualRow[]>(() => {
  const bodyScrollTop = Math.max(0, tagScrollTop.value - tagHeaderHeight)
  const start = Math.max(0, Math.floor(bodyScrollTop / tagRowHeight) - tagOverscan)
  const count = tagViewportRows + tagOverscan * 2
  return selectedTags.value.slice(start, start + count).map((tag, offset) => {
    const index = start + offset
    return { tag, index, top: index * tagRowHeight }
  })
})
const pagedFilteredDevices = computed(() => paginate(filteredDevices.value, devicePage.value, devicePageSize.value))

const detailTitle = computed(() => {
  if (selectedNode.value.type === 'device') return selectedDevice.value?.name || '设备详情'
  if (selectedNode.value.type === 'group') return selectedGroup.value?.name || '分组标签'
  return '设备概览'
})

const detailSubtitle = computed(() => {
  if (selectedNode.value.type === 'device') return '设备直属标签'
  if (selectedNode.value.type === 'group') return `${selectedDevice.value?.name || ''} / 分组标签`
  return '全部设备运行与配置摘要'
})

const deviceEmptyText = computed(() => deviceNameFilter.value.trim() ? '没有匹配的设备' : '还没有设备')
const tagEmptyText = computed(() => {
  if (selectedNode.value.type === 'group') return '当前分组还没有标签'
  if (selectedNode.value.type === 'device') return '当前设备还没有直属标签'
  return '请选择设备或分组查看标签'
})

watch(editingActive, value => emit('editingState', value), { immediate: true })
watch(deviceFilterInput, value => {
  if (filterTimer !== undefined) window.clearTimeout(filterTimer)
  filterTimer = window.setTimeout(() => {
    deviceNameFilter.value = value
    devicePage.value = 1
    treeScrollTop.value = 0
    treeScroller.value?.scrollTo({ top: 0 })
  }, 220)
})
watch(() => filteredDevices.value.length, () => {
  devicePage.value = clampPage(devicePage.value, filteredDevices.value.length, devicePageSize.value)
  if (!findNode(treeData.value[0], selectedNodeKey.value)) selectedNodeKey.value = 'root'
})
watch(selectedNodeKey, key => {
  tagScrollTop.value = 0
  tagScroller.value?.scrollTo({ top: 0 })
  expandSelectedNodePath(key)
})

onBeforeUnmount(() => {
  if (filterTimer !== undefined) window.clearTimeout(filterTimer)
})

const contextActions = computed<ContextAction[]>(() => {
  const node = contextMenu.node
  if (!node || node.type === 'root') {
    return canCreateDevice.value ? [{ key: 'add-device', label: '新增设备', icon: Plus }] : []
  }
  if (node.type === 'device') {
    return [
      canEditDevice.value ? { key: 'edit-device', label: '编辑设备', icon: Edit } : null,
      canDeleteDevice.value ? { key: 'delete-device', label: '删除设备', icon: Delete } : null,
      canCreateGroup.value ? { key: 'add-group', label: '新增分组', icon: FolderAdd } : null,
      canCreateTag.value ? { key: 'add-tag', label: '新增标签', icon: CollectionTag } : null
    ].filter(Boolean) as ContextAction[]
  }
  return [
    canEditGroup.value ? { key: 'edit-group', label: '编辑分组', icon: Edit } : null,
    canDeleteGroup.value ? { key: 'delete-group', label: '删除分组', icon: Delete } : null,
    canCreateTag.value ? { key: 'add-tag', label: '新增标签', icon: CollectionTag } : null
  ].filter(Boolean) as ContextAction[]
})

function selectTreeNode(node: DeviceTreeNode) {
  selectedNodeKey.value = node.key
  expandSelectedNodePath(node.key)
}

function openContextMenu(event: MouseEvent, node: DeviceTreeNode) {
  event.preventDefault()
  event.stopPropagation()
  selectTreeNode(node)
  contextMenu.node = node
  if (contextActions.value.length === 0) {
    contextMenu.visible = false
    return
  }
  contextMenu.x = event.clientX
  contextMenu.y = event.clientY
  contextMenu.visible = true
}

function closeContextMenu() {
  contextMenu.visible = false
}

function runContextAction(action: string) {
  const node = contextMenu.node
  closeContextMenu()
  if (!node) return

  if (action === 'add-device') openCreate()
  else if (action === 'edit-device' && node.device) openEdit(node.device)
  else if (action === 'delete-device' && node.device) removeDevice(node.device)
  else if (action === 'add-group' && node.device) openCreateGroup(node.device)
  else if (action === 'edit-group' && node.group) openEditGroup(node.group, node.device)
  else if (action === 'delete-group' && node.group) removeGroup(node.group, node.device)
  else if (action === 'add-tag' && node.device) openCreateTag(node.device, node.type === 'group' ? node.group : undefined)
}

function treeNodeIcon(node: DeviceTreeNode) {
  if (node.type === 'device') return Connection
  if (node.type === 'group') return Folder
  return Collection
}

function pushTreeNode(rows: Array<Omit<DeviceTreeRow, 'top'>>, node: DeviceTreeNode, depth: number) {
  const expandable = (node.children?.length ?? 0) > 0
  rows.push({ node, depth, expandable })
  if (!expandable || !expandedTreeKeys.value.has(node.key)) return
  for (const child of node.children ?? []) pushTreeNode(rows, child, depth + 1)
}

function onTreeScroll() {
  treeScrollTop.value = treeScroller.value?.scrollTop ?? 0
}

function onTagScroll() {
  tagScrollTop.value = tagScroller.value?.scrollTop ?? 0
}

function toggleTreeNode(node: DeviceTreeNode) {
  if (!(node.children?.length)) return
  const next = new Set(expandedTreeKeys.value)
  if (next.has(node.key)) next.delete(node.key)
  else next.add(node.key)
  next.add('root')
  expandedTreeKeys.value = next
}

function expandSelectedNodePath(key: string) {
  const next = new Set(expandedTreeKeys.value)
  next.add('root')
  if (key.startsWith('group:')) {
    const separator = key.lastIndexOf(':')
    if (separator > 'group:'.length) next.add(`device:${key.slice('group:'.length, separator)}`)
  }
  expandedTreeKeys.value = next
}

function paginate<T>(items: T[], page: number, pageSize: number) {
  const size = Math.max(1, pageSize)
  const current = clampPage(page, items.length, size)
  const start = (current - 1) * size
  return items.slice(start, start + size)
}

function clampPage(page: number, total: number, pageSize: number) {
  const maxPage = Math.max(1, Math.ceil(total / Math.max(1, pageSize)))
  return Math.min(Math.max(1, page), maxPage)
}

function treeStatusType(node: DeviceTreeNode) {
  if (node.type === 'root') return filteredDevices.value.some(device => deviceRuntime(device)?.isConnected) ? 'success' : 'info'
  if (node.type === 'group') return node.group?.enabled ? 'success' : 'info'
  return deviceRuntime(node.device)?.isConnected ? 'success' : node.device?.enabled ? 'warning' : 'info'
}

function treeStatusText(node: DeviceTreeNode) {
  if (node.type === 'root') return `${filteredDevices.value.length} 设备`
  if (node.type === 'group') return node.group?.enabled ? '启用' : '停用'
  return deviceRuntime(node.device)?.status || (node.device?.enabled ? '未连接' : '停用')
}

function treeTagCount(node: DeviceTreeNode) {
  if (node.type === 'root') return totalTagCount.value
  if (node.type === 'group') return node.group?.tags?.length ?? 0
  return node.device ? countDeviceTags(node.device) : 0
}

function findNode(node: DeviceTreeNode | undefined, key: string): DeviceTreeNode | undefined {
  if (!node) return undefined
  if (node.key === key) return node
  for (const child of node.children ?? []) {
    const found = findNode(child, key)
    if (found) return found
  }
  return undefined
}

function deviceNodeKey(device: DeviceConfig) {
  return `device:${device.id || device.name}`
}

function groupNodeKey(device: DeviceConfig, group: GroupConfig) {
  return `group:${device.id || device.name}:${group.id || group.name}`
}

function deviceRuntime(device?: DeviceConfig) {
  if (!device) return undefined
  return runtimeMap.value.get((device.id || '').toLowerCase()) ?? runtimeMap.value.get((device.name || '').toLowerCase())
}

function tagSnapshot(tag: TagConfig) {
  const byId = runtimeTagMap.value.get(`id:${normalizeKey(tag.id)}`)
  if (byId) return byId

  const device = selectedDevice.value
  const group = selectedGroup.value
  return runtimeTagMap.value.get(`path:${tagPathKey(device?.name, group?.name, tag.name)}`) ??
    runtimeTagMap.value.get(`path:${tagPathKey(device?.id, group?.id, tag.name)}`)
}

function tagCurrentValue(tag: TagConfig) {
  const snapshot = tagSnapshot(tag)
  const value = snapshot?.valueText || snapshot?.rawValueText || ''
  if (!value) return '-'
  const unit = snapshot?.unit || tag.unit
  return unit ? `${value} ${unit}` : value
}

function tagRawValue(tag: TagConfig) {
  const snapshot = tagSnapshot(tag)
  return snapshot?.rawValueText || '-'
}

function tagCleaningText(tag: TagConfig) {
  const snapshot = tagSnapshot(tag)
  if (snapshot?.cleaningApplied) {
    return snapshot.cleaningMessage || snapshot.cleaningAction || '已清洗'
  }
  if (tag.cleaning?.enabled) return '已启用'
  return '-'
}

function tagQualityType(tag: TagConfig) {
  const quality = normalizeKey(tagSnapshot(tag)?.quality)
  if (quality === 'good') return 'success'
  if (quality === 'unknown' || !quality) return 'info'
  if (quality === 'filtered') return 'info'
  if (quality === 'outofrange' || quality === 'spike') return 'warning'
  if (quality.includes('connect')) return 'warning'
  return 'danger'
}

function tagQualityText(tag: TagConfig) {
  const quality = tagSnapshot(tag)?.quality || 'Unknown'
  const labels: Record<string, string> = {
    good: '正常',
    unknown: '无数据',
    bad: '异常',
    readerror: '读取失败',
    notconnected: '未连接',
    disabled: '停用',
    outofrange: '越界',
    filtered: '已过滤',
    spike: '毛刺'
  }
  return labels[normalizeKey(quality)] ?? quality
}

function tagUpdatedTime(tag: TagConfig) {
  return formatDateTime(tagSnapshot(tag)?.timestamp)
}

function tagLastError(tag: TagConfig) {
  return tagSnapshot(tag)?.errorMessage || '-'
}

function tagScalingText(tag: TagConfig) {
  if (!tag.scaling?.enabled) return '-'
  const multiplier = Number(tag.scaling.multiplier)
  const offset = Number(tag.scaling.offset)
  const multiplierText = Number.isFinite(multiplier) ? `x${multiplier}` : 'x?'
  if (!Number.isFinite(offset) || offset === 0) return multiplierText
  return offset > 0 ? `${multiplierText}+${offset}` : `${multiplierText}${offset}`
}

function canWriteTag(tag: TagConfig) {
  return hasTagWritePermission.value && normalizeKey(tag.accessMode) !== 'readonly'
}

function openWriteTag(tag: TagConfig) {
  if (!selectedDevice.value || !canWriteTag(tag)) return
  writeTargetDevice.value = selectedDevice.value
  writeTargetGroup.value = selectedGroup.value ?? null
  writeTargetTag.value = tag
  writeValueText.value = ''
  writeError.value = ''
  writeDrawerVisible.value = true
}

function writePlaceholder(tag: TagConfig) {
  const type = normalizeKey(tag.dataType)
  if (type.includes('array')) return '多个值用英文逗号分隔'
  if (type === 'bool' || type === 'coil' || type === 'discreteinput') return 'true / false 或 1 / 0'
  if (type === 'string') return '输入字符串'
  return '输入数字'
}

async function submitWriteTag() {
  if (!writeTargetDevice.value || !writeTargetTag.value) return
  const validation = validateWriteValue(writeTargetTag.value, writeValueText.value)
  writeError.value = validation
  if (validation) return

  writeSaving.value = true
  try {
    const result = await writeTag({
      deviceName: writeTargetDevice.value.name,
      groupName: writeTargetGroup.value?.name || '',
      tagName: writeTargetTag.value.name,
      dataType: writeTargetTag.value.dataType,
      valueText: writeValueText.value.trim(),
      timeoutMilliseconds: 10000
    })
    if (!result.success) {
      writeError.value = result.errorMessage || '写入失败'
      ElMessage.error(writeError.value)
      return
    }
    ElMessage.success(result.currentValueText ? `写入成功，当前值 ${result.currentValueText}` : '写入成功')
    writeDrawerVisible.value = false
    emit('changed')
  } catch (error) {
    writeError.value = error instanceof Error ? error.message : '写入失败'
    ElMessage.error(writeError.value)
  } finally {
    writeSaving.value = false
  }
}

function validateWriteValue(tag: TagConfig, valueText: string) {
  const text = valueText.trim()
  if (!text) return '请输入写入值'
  if (!canWriteTag(tag)) return '当前标签没有写入权限'

  const type = normalizeKey(tag.dataType)
  const values = type.includes('array')
    ? text.split(',').map(item => item.trim()).filter(Boolean)
    : [text]

  if (type.includes('array') && values.length === 0) return '请输入至少一个数组值'

  for (const value of values) {
    const error = validateScalarValue(type.replace('array', ''), value)
    if (error) return error
  }
  return ''
}

function validateScalarValue(type: string, value: string) {
  if (type === 'bool' || type === 'coil' || type === 'discreteinput') {
    return /^(true|false|1|0)$/i.test(value) ? '' : '布尔值只能是 true、false、1 或 0'
  }
  if (type === 'string') return ''
  if (type === 'float' || type === 'double') {
    return Number.isFinite(Number(value)) ? '' : '请输入合法数字'
  }

  if (!/^-?\d+$/.test(value)) return '请输入整数'
  const number = Number(value)
  if (!Number.isSafeInteger(number)) return '整数超出安全范围'
  const ranges: Record<string, [number, number]> = {
    int16: [-32768, 32767],
    uint16: [0, 65535],
    int32: [-2147483648, 2147483647],
    uint32: [0, 4294967295],
    int64: [Number.MIN_SAFE_INTEGER, Number.MAX_SAFE_INTEGER],
    uint64: [0, Number.MAX_SAFE_INTEGER]
  }
  const range = ranges[type]
  if (range && (number < range[0] || number > range[1])) return `数值范围应为 ${range[0]} 到 ${range[1]}`
  return ''
}

function addRuntimeTagPath(map: Map<string, TagValueSnapshot>, device: string, group: string, tag: string, snapshot: TagValueSnapshot) {
  const key = tagPathKey(device, group, tag)
  if (key.replace(/\//g, '')) map.set(`path:${key}`, snapshot)
}

function tagPathKey(device: string | null | undefined, group: string | null | undefined, tag: string | null | undefined) {
  return [device, group, tag].map(normalizeKey).join('/')
}

function normalizeKey(value: string | null | undefined) {
  return (value ?? '').trim().toLowerCase()
}

function openCreate() {
  if (!canCreateDevice.value) {
    ElMessage.warning('当前用户没有新增设备权限')
    return
  }
  editingId.value = ''
  form.value = createDeviceDraft()
  drawerVisible.value = true
}

function openEdit(device: DeviceConfig) {
  if (!canEditDevice.value) {
    ElMessage.warning('当前用户没有编辑设备权限')
    return
  }
  editingId.value = device.id
  form.value = cloneDevice(device)
  drawerVisible.value = true
}

function changeProtocol(protocol: string) {
  if (!form.value) return
  applyProtocolDefaults(form.value, protocol)
}

async function saveDevice() {
  if (!form.value) return
  if (!canSaveCurrentDevice.value) {
    ElMessage.warning('当前用户没有保存设备权限')
    return
  }
  const payload = normalizeDevice(cloneDevice(form.value))
  payload.connection.protocol = payload.protocol
  if (!payload.name.trim()) {
    ElMessage.warning('请输入设备名称')
    return
  }

  saving.value = true
  try {
    if (editingId.value) {
      await updateDevice(editingId.value, payload)
      ElMessage.success('设备已更新')
      selectedNodeKey.value = deviceNodeKey(payload)
    } else {
      const created = await createDevice(payload)
      ElMessage.success('设备已新增')
      selectedNodeKey.value = deviceNodeKey(created)
    }
    drawerVisible.value = false
    emit('changed')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存失败')
  } finally {
    saving.value = false
  }
}

async function removeDevice(device: DeviceConfig) {
  if (!canDeleteDevice.value) {
    ElMessage.warning('当前用户没有删除设备权限')
    return
  }
  try {
    await ElMessageBox.confirm(`确认删除设备“${device.name}”？`, '删除设备', {
      type: 'warning',
      confirmButtonText: '删除',
      cancelButtonText: '取消'
    })
    await deleteDevice(device.id)
    if (selectedDevice.value?.id === device.id) selectedNodeKey.value = 'root'
    ElMessage.success('设备已删除')
    emit('changed')
  } catch (error) {
    if (error !== 'cancel' && error !== 'close') {
      ElMessage.error(error instanceof Error ? error.message : '删除失败')
    }
  }
}

function openCreateGroup(device: DeviceConfig) {
  if (!canCreateGroup.value) {
    ElMessage.warning('当前用户没有新增分组权限')
    return
  }
  selectedNodeKey.value = deviceNodeKey(device)
  editingGroupId.value = ''
  groupTargetDevice.value = device
  groupForm.value = createGroupDraft(device)
  groupDrawerVisible.value = true
}

function openEditGroup(group: GroupConfig, device = selectedDevice.value) {
  if (!canEditGroup.value) {
    ElMessage.warning('当前用户没有编辑分组权限')
    return
  }
  if (!device) return
  selectedNodeKey.value = groupNodeKey(device, group)
  editingGroupId.value = group.id
  groupTargetDevice.value = device
  groupForm.value = cloneGroup(group)
  groupDrawerVisible.value = true
}

async function saveGroup() {
  if (!groupForm.value || !groupTargetDevice.value) return
  if (!canSaveCurrentGroup.value) {
    ElMessage.warning('当前用户没有保存分组权限')
    return
  }
  const payload = normalizeGroup(cloneGroup(groupForm.value))
  if (!payload.name.trim()) {
    ElMessage.warning('请输入分组名称')
    return
  }

  groupSaving.value = true
  try {
    let saved: GroupConfig
    if (editingGroupId.value) {
      saved = await updateGroup(editingGroupId.value, payload)
      ElMessage.success('分组已更新')
    } else {
      saved = await createGroup(groupTargetDevice.value.id, payload)
      ElMessage.success('分组已新增')
    }
    selectedNodeKey.value = groupNodeKey(groupTargetDevice.value, saved)
    groupDrawerVisible.value = false
    emit('changed')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存分组失败')
  } finally {
    groupSaving.value = false
  }
}

async function removeGroup(group: GroupConfig, device = selectedDevice.value) {
  if (!canDeleteGroup.value) {
    ElMessage.warning('当前用户没有删除分组权限')
    return
  }
  try {
    await ElMessageBox.confirm(`确认删除分组“${group.name}”？分组下标签也会被移除。`, '删除分组', {
      type: 'warning',
      confirmButtonText: '删除',
      cancelButtonText: '取消'
    })
    await deleteGroupRequest(group.id)
    selectedNodeKey.value = device ? deviceNodeKey(device) : 'root'
    ElMessage.success('分组已删除')
    emit('changed')
  } catch (error) {
    if (error !== 'cancel' && error !== 'close') {
      ElMessage.error(error instanceof Error ? error.message : '删除分组失败')
    }
  }
}

function openCreateTag(device: DeviceConfig, group?: GroupConfig) {
  if (!canCreateTag.value) {
    ElMessage.warning('当前用户没有新增标签权限')
    return
  }
  selectedNodeKey.value = group ? groupNodeKey(device, group) : deviceNodeKey(device)
  editingTagId.value = ''
  tagTargetDevice.value = device
  tagTargetGroup.value = group ?? null
  tagForm.value = createTagDraft(device, group)
  tagDrawerVisible.value = true
}

function openEditTag(tag: TagConfig) {
  if (!canEditTag.value) {
    ElMessage.warning('当前用户没有编辑标签权限')
    return
  }
  const device = selectedDevice.value
  if (!device) return
  editingTagId.value = tag.id
  tagTargetDevice.value = device
  tagTargetGroup.value = selectedGroup.value ?? null
  tagForm.value = cloneTag({ ...tag, protocol: tag.protocol || device.protocol })
  tagDrawerVisible.value = true
}

async function saveTag() {
  if (!tagForm.value || !tagTargetDevice.value) return
  if (!canSaveCurrentTag.value) {
    ElMessage.warning('当前用户没有保存标签权限')
    return
  }
  if (!(await tagFormComponent.value?.validate())) {
    ElMessage.warning('请先补全标签必填信息')
    return
  }

  const payload = normalizeTag(cloneTag(tagForm.value))
  payload.protocol = payload.protocol || tagTargetDevice.value.protocol
  if (!payload.name.trim()) {
    ElMessage.warning('请输入标签名称')
    return
  }

  tagSaving.value = true
  try {
    if (editingTagId.value) {
      await updateTag(editingTagId.value, payload)
      ElMessage.success('标签已更新')
    } else if (tagTargetGroup.value) {
      await createGroupTag(tagTargetGroup.value.id, payload)
      ElMessage.success('标签已新增')
    } else {
      await createDeviceTag(tagTargetDevice.value.id, payload)
      ElMessage.success('标签已新增')
    }
    selectedNodeKey.value = tagTargetGroup.value
      ? groupNodeKey(tagTargetDevice.value, tagTargetGroup.value)
      : deviceNodeKey(tagTargetDevice.value)
    tagDrawerVisible.value = false
    emit('changed')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存标签失败')
  } finally {
    tagSaving.value = false
  }
}

async function removeTag(tag: TagConfig) {
  if (!canDeleteTag.value) {
    ElMessage.warning('当前用户没有删除标签权限')
    return
  }
  try {
    await ElMessageBox.confirm(`确认删除标签“${tag.name}”？`, '删除标签', {
      type: 'warning',
      confirmButtonText: '删除',
      cancelButtonText: '取消'
    })
    await deleteTagRequest(tag.id)
    ElMessage.success('标签已删除')
    emit('changed')
  } catch (error) {
    if (error !== 'cancel' && error !== 'close') {
      ElMessage.error(error instanceof Error ? error.message : '删除标签失败')
    }
  }
}

function countDeviceTags(device: DeviceConfig) {
  return (device.tags?.length ?? 0) + (device.groups ?? []).reduce((total, group) => total + (group.tags?.length ?? 0), 0)
}

function connectionSummary(device: DeviceConfig) {
  const connection = device.connection
  if (device.protocol === 'VirtualPlc') return connection.host || 'default'
  if (device.protocol === 'OpcDa') return `${connection.host || 'localhost'} / ${connection.opcDaServerProgId || '-'}`
  if (device.protocol === 'Plugin') return `${connection.driverId || '-'} ${connection.host || ''}`.trim()
  if (['ModbusRtu', 'MitsubishiSerial', 'MitsubishiQlSerial', 'Dlt6452007', 'Cjt1882004'].includes(device.protocol)) {
    return `${connection.host || 'COM1'} @ ${connection.port || 9600}`
  }
  return `${connection.host || '-'}:${connection.port || 0}`
}
</script>

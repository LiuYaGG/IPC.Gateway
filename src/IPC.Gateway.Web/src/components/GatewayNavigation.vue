<template>
  <a-menu
    class="gateway-menu"
    mode="inline"
    :inline-collapsed="collapsed"
    :selected-keys="[modelValue]"
    :open-keys="collapsed ? popupKeys : openKeys"
    @open-change="changeOpenKeys"
    @click="selectItem"
  >
    <a-sub-menu
      v-for="group in visibleGroups"
      :key="group.key"
      :title="tx(group.label)"
      popup-class-name="gateway-menu-popup"
    >
      <template #icon>
        <span class="ui-icon"><component :is="group.icon" /></span>
      </template>
      <a-menu-item v-for="item in group.items" :key="item.key" :title="tx(item.label)">
        <template #icon>
          <span class="ui-icon"><component :is="item.icon" /></span>
        </template>
        <span>{{ tx(item.label) }}</span>
      </a-menu-item>
    </a-sub-menu>
  </a-menu>
</template>

<script setup lang="ts">
import { tx } from '../i18n'
import { computed, ref, watch } from 'vue'
import {
  ContainerOutlined as Box,
  ApartmentOutlined as Connection,
  DeploymentUnitOutlined as Cpu,
  LineChartOutlined as DataLine,
  FileTextOutlined as Document,
  LockOutlined as Lock,
  DesktopOutlined as Monitor,
  AppstoreOutlined as Operation,
  SendOutlined as Promotion,
  ShareAltOutlined as Share,
  AuditOutlined as Tickets,
  UserOutlined as User,
  TeamOutlined as UserFilled
} from '@ant-design/icons-vue'

const props = defineProps<{ modelValue: string; collapsed: boolean; canAccess: (key: string) => boolean }>()
const emit = defineEmits<{ 'update:modelValue': [key: string] }>()
const groups = [
  {
    key: 'runtime',
    label: '运行',
    icon: DataLine,
    items: [
      { key: 'bigScreen', label: '大屏总览', icon: Monitor },
      { key: 'topology', label: '设备拓扑', icon: Connection },
      { key: 'dashboard', label: '运行总览', icon: DataLine }
    ]
  },
  {
    key: 'config',
    label: '配置',
    icon: Operation,
    items: [
      { key: 'devices', label: '设备管理', icon: Connection },
      { key: 'flowRules', label: '规则引擎', icon: Share },
      { key: 'history', label: '历史库', icon: Document },
      { key: 'scripts', label: '脚本中心', icon: Document },
      { key: 'models', label: '模型中心', icon: Cpu }
    ]
  },
  {
    key: 'communication',
    label: '通讯',
    icon: Promotion,
    items: [
      { key: 'mqtt', label: 'MQTT', icon: Promotion },
      { key: 'opcUa', label: 'OPC UA Server', icon: Connection }
    ]
  },
  {
    key: 'system',
    label: '系统',
    icon: Box,
    items: [
      { key: 'audit', label: '审计日志', icon: Tickets },
      { key: 'security', label: '工业安全', icon: Lock },
      { key: 'maintenance', label: '安装升级', icon: Box },
      { key: 'users', label: '人员管理', icon: User },
      { key: 'roles', label: '角色管理', icon: UserFilled },
      { key: 'permissions', label: '权限分配', icon: Operation }
    ]
  }
]
const visibleGroups = computed(() =>
  groups
    .map((group) => ({ ...group, items: group.items.filter((item) => props.canAccess(item.key)) }))
    .filter((group) => group.items.length)
)
const storageKey = 'ipc.gateway.navigation.openGroups'
const openKeys = ref<string[]>(readOpenGroups())
const popupKeys = ref<string[]>([])
function readOpenGroups() {
  try {
    const value = JSON.parse(localStorage.getItem(storageKey) ?? 'null')
    if (Array.isArray(value)) return value.filter((key): key is string => groups.some((group) => group.key === key))
  } catch {
    /* Navigation remains usable when browser storage is disabled. */
  }
  return groups.map((group) => group.key)
}
function changeOpenKeys(keys: (string | number)[]) {
  if (props.collapsed) {
    popupKeys.value = keys.map(String)
    return
  }
  openKeys.value = keys.map(String)
  try {
    localStorage.setItem(storageKey, JSON.stringify(openKeys.value))
  } catch {
    /* Keep in-memory preferences. */
  }
}
watch(
  () => props.modelValue,
  (key) => {
    const group = groups.find((group) => group.items.some((item) => item.key === key))
    if (group && !openKeys.value.includes(group.key)) {
      openKeys.value = [...openKeys.value, group.key]
      try {
        localStorage.setItem(storageKey, JSON.stringify(openKeys.value))
      } catch {
        /* Keep in-memory preferences. */
      }
    }
  },
  { immediate: true }
)
function selectItem(info: { key: string | number }) {
  const key = String(info.key)
  if (props.canAccess(key)) emit('update:modelValue', key)
}
</script>

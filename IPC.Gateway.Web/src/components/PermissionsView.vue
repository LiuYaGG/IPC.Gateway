<template>
  <section class="view-stack permissions-view">
    <el-card shadow="never" class="panel-card permission-card">
      <template #header>
        <div class="card-header">
          <div class="detail-title">
            <span>权限分配</span>
            <small>选择角色后查看人员，并按菜单和按钮分配权限</small>
          </div>
          <div class="card-actions">
            <el-button :icon="Refresh" :loading="loading" @click="load">刷新</el-button>
            <el-button
              v-if="canEditSelectedRole"
              type="primary"
              :icon="Check"
              :loading="saving"
              :disabled="!dirty"
              @click="save"
            >
              保存权限
            </el-button>
          </div>
        </div>
      </template>

      <div class="permission-layout">
        <aside class="permission-column permission-role-tree">
          <div class="permission-column__head">
            <div>
              <strong>角色树</strong>
              <small>{{ roles.length }} 个角色</small>
            </div>
          </div>
          <el-tree
            :data="roleTreeData"
            node-key="id"
            default-expand-all
            highlight-current
            :current-node-key="selectedRoleName"
            :props="roleTreeProps"
            class="permission-tree"
            @node-click="handleRoleNodeClick"
          >
            <template #default="{ data }">
              <div :class="['permission-tree-node', { 'is-group': !data.role }]">
                <span>{{ data.label }}</span>
                <el-tag v-if="data.role" size="small" type="info">{{ data.role.userCount }} 人</el-tag>
              </div>
            </template>
          </el-tree>
        </aside>

        <section class="permission-column permission-members">
          <div class="permission-column__head">
            <div>
              <strong>{{ selectedRole?.displayName || selectedRole?.name || '角色人员' }}</strong>
              <small>{{ selectedRole ? `${selectedRoleUsers.length} / ${selectedRole.userCount} 人` : '请选择角色' }}</small>
            </div>
            <el-tag v-if="selectedRole" :type="selectedRole.isSystem ? 'warning' : 'primary'">
              {{ selectedRole.isSystem ? '系统角色' : '自定义角色' }}
            </el-tag>
          </div>

          <el-alert
            v-if="usersLoadError"
            :title="usersLoadError"
            type="warning"
            :closable="false"
            class="permission-alert"
          />

          <el-table
            v-if="selectedRole"
            v-loading="usersLoading"
            :data="selectedRoleUsers"
            row-key="username"
            height="calc(100vh - 326px)"
            empty-text="当前角色暂无人员"
          >
            <el-table-column prop="username" label="账号" min-width="130" />
            <el-table-column prop="displayName" label="姓名" min-width="130" />
            <el-table-column label="状态" width="86">
              <template #default="{ row }">
                <el-tag size="small" :type="row.enabled ? 'success' : 'info'">
                  {{ row.enabled ? '启用' : '停用' }}
                </el-tag>
              </template>
            </el-table-column>
          </el-table>
          <el-empty v-else description="请选择左侧角色" />
        </section>

        <section class="permission-column permission-rights">
          <div class="permission-column__head">
            <div>
              <strong>角色权限</strong>
              <small>{{ selectedRole ? `已选 ${permissionsForSave.length} 项` : '请选择角色' }}</small>
            </div>
            <div class="permission-actions">
              <el-button :disabled="!canEditSelectedRole" @click="selectAll">全选</el-button>
              <el-button :disabled="!canEditSelectedRole" @click="clearAll">清空</el-button>
            </div>
          </div>

          <el-alert
            v-if="isAdminRole"
            title="管理员默认拥有全部权限，运行时不会限制管理员账号。"
            type="info"
            :closable="false"
            class="permission-alert"
          />
          <el-alert
            v-else-if="selectedRole && !canEditPermissions"
            title="当前账号没有编辑权限分配的权限。"
            type="warning"
            :closable="false"
            class="permission-alert"
          />

          <el-input
            v-if="selectedRole"
            v-model="keyword"
            clearable
            :prefix-icon="Search"
            placeholder="搜索菜单或按钮"
            class="permission-search"
          />

          <el-scrollbar v-if="selectedRole" class="permission-scroll">
            <el-tree
              ref="permissionTreeRef"
              :data="permissionTreeData"
              node-key="id"
              show-checkbox
              check-strictly
              default-expand-all
              :props="permissionTreeProps"
              :filter-node-method="filterPermissionNode"
              class="permission-tree permission-tree--check"
              @check="handlePermissionCheck"
            >
              <template #default="{ data }">
                <div :class="['permission-tree-node', `is-${data.kind}`]">
                  <span>{{ data.label }}</span>
                </div>
              </template>
            </el-tree>
          </el-scrollbar>
          <el-empty v-else description="请选择左侧角色" />
        </section>
      </div>
    </el-card>
  </section>
</template>

<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { Check, Refresh, Search } from '@element-plus/icons-vue'
import {
  loadRolePermissions,
  loadRoles,
  loadUsers,
  updateRolePermissions,
  type GatewayPermissionInfo,
  type GatewayRoleInfo,
  type GatewayUserInfo
} from '../api'
import { PERMISSIONS, usePermissions } from '../utils/permissions'

interface RoleTreeNode {
  id: string
  label: string
  role?: GatewayRoleInfo
  children?: RoleTreeNode[]
}

interface PermissionTreeNode {
  id: string
  label: string
  kind: 'menu' | 'button'
  permissionKey: string
  disabled: boolean
  children?: PermissionTreeNode[]
}

const { hasPermission } = usePermissions()
const roles = ref<GatewayRoleInfo[]>([])
const users = ref<GatewayUserInfo[]>([])
const permissions = ref<GatewayPermissionInfo[]>([])
const loading = ref(false)
const usersLoading = ref(false)
const saving = ref(false)
const selectedRoleName = ref('')
const draftPermissions = ref<string[]>([])
const originalPermissions = ref<string[]>([])
const keyword = ref('')
const usersLoadError = ref('')
const permissionTreeRef = ref<any>(null)

const roleTreeProps = { children: 'children', label: 'label' }
const permissionTreeProps = { children: 'children', label: 'label', disabled: 'disabled' }

const canEditPermissions = computed(() => hasPermission(PERMISSIONS.permissionsEdit))
const canViewUsers = computed(() => hasPermission(PERMISSIONS.usersView))
const selectedRole = computed(() => roles.value.find(role => role.name === selectedRoleName.value) ?? null)
const isAdminRole = computed(() => selectedRole.value?.name.toLowerCase() === 'admin')
const allPermissionKeys = computed(() => permissions.value.map(item => item.key))
const displayedPermissions = computed(() => isAdminRole.value ? allPermissionKeys.value : draftPermissions.value)
const permissionsForSave = computed(() => collectPermissionsForSave(draftPermissions.value))
const canEditSelectedRole = computed(() => canEditPermissions.value && !!selectedRole.value && !isAdminRole.value)
const dirty = computed(() => serialize(permissionsForSave.value) !== serialize(originalPermissions.value))
const selectedRoleUsers = computed(() => {
  const roleName = selectedRoleName.value.toLowerCase()
  if (!roleName) return []
  return users.value.filter(user => user.role.toLowerCase() === roleName)
})

const roleTreeData = computed<RoleTreeNode[]>(() => {
  const systemRoles = roles.value.filter(role => role.isSystem)
  const customRoles = roles.value.filter(role => !role.isSystem)
  const groups: RoleTreeNode[] = []
  if (systemRoles.length) {
    groups.push({
      id: 'role-group-system',
      label: '系统角色',
      children: systemRoles.map(toRoleTreeNode)
    })
  }
  if (customRoles.length) {
    groups.push({
      id: 'role-group-custom',
      label: '自定义角色',
      children: customRoles.map(toRoleTreeNode)
    })
  }
  return groups
})

const permissionTreeData = computed<PermissionTreeNode[]>(() => {
  const groups = new Map<string, GatewayPermissionInfo[]>()
  for (const permission of permissions.value) {
    const groupName = permission.page || permission.group || permission.name
    const items = groups.get(groupName) ?? []
    items.push(permission)
    groups.set(groupName, items)
  }

  return Array.from(groups.entries()).map(([name, items]) => {
    const page = items.find(item => item.action === 'view') ?? items[0]
    const children = items
      .filter(item => item.key !== page.key)
      .sort((left, right) => left.name.localeCompare(right.name, 'zh-Hans-CN'))
      .map(item => toPermissionTreeNode(item, 'button'))

    return {
      id: page.key,
      label: name,
      kind: 'menu' as const,
      permissionKey: page.key,
      disabled: !canEditSelectedRole.value,
      children
    }
  }).sort((left, right) => left.label.localeCompare(right.label, 'zh-Hans-CN'))
})

watch(selectedRole, () => syncDraftFromRole(), { immediate: true })
watch([displayedPermissions, permissionTreeData], () => syncPermissionTree(), { immediate: true })
watch(keyword, value => permissionTreeRef.value?.filter(value))

onMounted(() => load())

async function load() {
  loading.value = true
  usersLoadError.value = ''
  try {
    const [roleItems, permissionItems] = await Promise.all([loadRoles(), loadRolePermissions()])
    roles.value = roleItems
    permissions.value = permissionItems
    if (!selectedRoleName.value || !roles.value.some(role => role.name === selectedRoleName.value)) {
      selectedRoleName.value = roles.value.find(role => role.name.toLowerCase() !== 'admin')?.name ?? roles.value[0]?.name ?? ''
    }
    syncDraftFromRole()
    await loadRoleUsers()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '权限数据加载失败')
  } finally {
    loading.value = false
  }
}

async function loadRoleUsers() {
  users.value = []
  usersLoadError.value = ''
  if (!canViewUsers.value) {
    usersLoadError.value = '当前账号没有查看人员列表权限，只显示角色人员数量。'
    return
  }

  usersLoading.value = true
  try {
    users.value = await loadUsers()
  } catch (error) {
    usersLoadError.value = error instanceof Error ? error.message : '人员列表加载失败'
  } finally {
    usersLoading.value = false
  }
}

function handleRoleNodeClick(node: RoleTreeNode) {
  if (!node.role) return
  selectRole(node.role.name)
}

function selectRole(roleName: string) {
  selectedRoleName.value = roleName
  syncDraftFromRole()
}

function syncDraftFromRole() {
  const role = selectedRole.value
  const next = role ? normalize(role.permissions) : []
  draftPermissions.value = isAdminRole.value ? [...allPermissionKeys.value] : next
  originalPermissions.value = isAdminRole.value ? [...allPermissionKeys.value] : next
  syncPermissionTree()
}

function syncPermissionTree() {
  nextTick(() => {
    permissionTreeRef.value?.setCheckedKeys(displayedPermissions.value, false)
    if (keyword.value) permissionTreeRef.value?.filter(keyword.value)
  })
}

function handlePermissionCheck(_node: PermissionTreeNode, state: { checkedKeys: string[] }) {
  if (!canEditSelectedRole.value) {
    syncPermissionTree()
    return
  }
  draftPermissions.value = normalize(state.checkedKeys)
}

function selectAll() {
  draftPermissions.value = [...allPermissionKeys.value]
}

function clearAll() {
  draftPermissions.value = []
}

async function save() {
  if (!selectedRole.value || !canEditSelectedRole.value) return

  saving.value = true
  try {
    const saved = await updateRolePermissions(selectedRole.value.name, permissionsForSave.value)
    const index = roles.value.findIndex(role => role.name === saved.name)
    if (index >= 0) roles.value.splice(index, 1, saved)
    syncDraftFromRole()
    ElMessage.success('角色权限已保存')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '权限分配保存失败')
  } finally {
    saving.value = false
  }
}

function collectPermissionsForSave(values: string[]) {
  const selected = new Set(normalize(values))
  const menus = permissionTreeData.value
  for (const menu of menus) {
    const hasCheckedButton = (menu.children ?? []).some(child => selected.has(child.permissionKey))
    if (hasCheckedButton) selected.add(menu.permissionKey)
  }
  return normalize(Array.from(selected))
}

function filterPermissionNode(value: string, data: PermissionTreeNode) {
  const text = value.trim().toLowerCase()
  if (!text) return true
  return data.label.toLowerCase().includes(text) ||
    (data.children ?? []).some(child => child.label.toLowerCase().includes(text))
}

function toRoleTreeNode(role: GatewayRoleInfo): RoleTreeNode {
  return {
    id: role.name,
    label: role.displayName || role.name,
    role
  }
}

function toPermissionTreeNode(permission: GatewayPermissionInfo, kind: 'menu' | 'button'): PermissionTreeNode {
  return {
    id: permission.key,
    label: permission.name,
    kind,
    permissionKey: permission.key,
    disabled: !canEditSelectedRole.value
  }
}

function normalize(values: string[]) {
  return Array.from(new Set(values.map(value => value.trim()).filter(Boolean))).sort((left, right) => left.localeCompare(right))
}

function serialize(values: string[]) {
  return normalize(values).join('|')
}
</script>

<style scoped>
.permission-card :deep(.el-card__body) {
  padding: 16px;
}

.permission-layout {
  display: grid;
  grid-template-columns: 260px minmax(320px, 0.85fr) minmax(380px, 1.15fr);
  gap: 16px;
  min-height: calc(100vh - 210px);
}

.permission-column {
  min-width: 0;
  border: 1px solid var(--el-border-color-light);
  border-radius: 8px;
  background: var(--el-bg-color);
  overflow: hidden;
}

.permission-column__head {
  display: flex;
  min-height: 64px;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 14px;
  border-bottom: 1px solid var(--el-border-color-lighter);
  background: var(--el-bg-color-page);
}

.permission-column__head > div:first-child {
  display: flex;
  min-width: 0;
  flex-direction: column;
  gap: 4px;
}

.permission-column__head small {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.permission-actions {
  display: flex;
  gap: 8px;
}

.permission-tree {
  padding: 10px;
  --el-tree-node-hover-bg-color: var(--el-color-primary-light-9);
}

.permission-tree-node {
  display: flex;
  min-width: 0;
  width: 100%;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  overflow: hidden;
}

.permission-tree-node span {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.permission-tree-node.is-group span {
  color: var(--el-text-color-secondary);
  font-weight: 600;
}

.permission-tree-node.is-menu span {
  font-weight: 600;
}

.permission-tree-node.is-button span {
  color: var(--el-text-color-regular);
}

.permission-members :deep(.el-table) {
  border-radius: 0;
}

.permission-rights {
  display: flex;
  flex-direction: column;
}

.permission-alert {
  margin: 12px 12px 0;
}

.permission-search {
  width: calc(100% - 24px);
  margin: 12px;
}

.permission-scroll {
  height: calc(100vh - 350px);
}

.permission-tree--check {
  padding-top: 0;
}

.permission-tree--check :deep(.el-tree-node__content) {
  height: 34px;
}

@media (max-width: 1280px) {
  .permission-layout {
    grid-template-columns: 240px minmax(0, 1fr);
  }

  .permission-rights {
    grid-column: 1 / -1;
  }
}

@media (max-width: 820px) {
  .permission-layout {
    grid-template-columns: 1fr;
  }

  .permission-rights {
    grid-column: auto;
  }
}
</style>

<template>
  <section class="view-stack users-view">
    <el-card shadow="never" class="panel-card">
      <template #header>
        <div class="card-header">
          <div class="detail-title">
            <span>人员管理</span>
            <small>维护 Web 登录人员、角色和启用状态</small>
          </div>
          <div class="card-actions">
            <el-button :icon="Refresh" :loading="loading" @click="load">刷新</el-button>
            <el-button v-if="canCreateUser" type="primary" :icon="Plus" @click="openCreate">新增人员</el-button>
          </div>
        </div>
      </template>

      <div class="user-toolbar">
        <el-input
          v-model="keyword"
          clearable
          :prefix-icon="Search"
          placeholder="按账号、姓名或角色筛选"
        />
        <el-select v-model="roleFilter" clearable placeholder="角色筛选">
          <el-option
            v-for="role in roles"
            :key="role.name"
            :label="role.displayName || role.name"
            :value="role.name"
          />
        </el-select>
        <el-select v-model="enabledFilter" clearable placeholder="状态筛选">
          <el-option label="启用" value="enabled" />
          <el-option label="停用" value="disabled" />
        </el-select>
      </div>

      <el-table v-loading="loading" :data="filteredUsers" row-key="username" height="calc(100vh - 316px)">
        <el-table-column prop="username" label="账号" min-width="150" fixed />
        <el-table-column prop="displayName" label="姓名" min-width="150" />
        <el-table-column label="角色" min-width="150">
          <template #default="{ row }">
            <span class="user-role-cell">
              <el-tag size="small" type="primary">{{ roleLabel(row.role) }}</el-tag>
            </span>
          </template>
        </el-table-column>
        <el-table-column label="状态" width="92">
          <template #default="{ row }">
            <el-tag size="small" :type="row.enabled ? 'success' : 'info'">
              {{ row.enabled ? '启用' : '停用' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="创建时间" width="170">
          <template #default="{ row }">{{ formatDateTime(row.createdTime) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="260" fixed="right">
          <template #default="{ row }">
            <div class="table-actions table-actions--compact">
              <el-button v-if="canEditUser" size="small" text type="primary" :icon="Edit" @click="openEdit(row)">编辑</el-button>
              <el-button
                v-if="canResetUserPassword"
                size="small"
                text
                type="warning"
                :icon="Key"
                @click="openPasswordReset(row)"
              >
                重置密码
              </el-button>
              <el-button
                v-if="canDeleteUser"
                size="small"
                text
                type="danger"
                :icon="Delete"
                :disabled="isDefaultAdmin(row)"
                @click="remove(row)"
              >
                删除
              </el-button>
            </div>
          </template>
        </el-table-column>
        <template #empty>
          <el-empty description="暂无人员">
            <el-button v-if="canCreateUser" type="primary" :icon="Plus" @click="openCreate">新增人员</el-button>
          </el-empty>
        </template>
      </el-table>
    </el-card>

    <el-drawer v-model="drawerVisible" :title="editingUsername ? '编辑人员' : '新增人员'" size="560px" destroy-on-close>
      <el-form v-if="form" label-width="96px" :model="form" class="user-form">
        <el-form-item label="账号" required :error="fieldErrors.username">
          <el-input
            v-model="form.username"
            :disabled="!!editingUsername"
            placeholder="例如：operator01"
            maxlength="64"
          />
        </el-form-item>
        <el-form-item label="姓名" required :error="fieldErrors.displayName">
          <el-input v-model="form.displayName" placeholder="例如：产线操作员" maxlength="128" />
        </el-form-item>
        <el-form-item label="角色" required :error="fieldErrors.role">
          <el-select v-model="form.role" placeholder="请选择角色" class="full-field">
            <el-option
              v-for="role in selectableRoles"
              :key="role.name"
              :label="role.displayName || role.name"
              :value="role.name"
              :disabled="!role.enabled"
            >
              <span>{{ role.displayName || role.name }}</span>
              <small class="select-option-note">{{ role.name }}</small>
            </el-option>
          </el-select>
        </el-form-item>
        <el-form-item label="启用">
          <el-switch v-model="form.enabled" />
        </el-form-item>
        <el-form-item :label="editingUsername ? '新密码' : '密码'" :required="!editingUsername" :error="fieldErrors.password">
          <el-input
            v-model="form.password"
            type="password"
            show-password
            autocomplete="new-password"
            :placeholder="editingUsername ? '留空则不修改密码' : '请输入登录密码'"
          />
        </el-form-item>
      </el-form>

      <template #footer>
        <div class="drawer-footer">
          <el-button @click="drawerVisible = false">取消</el-button>
          <el-button v-if="canSaveCurrentUser" type="primary" :loading="saving" @click="save">保存</el-button>
        </div>
      </template>
    </el-drawer>

    <ResetUserPasswordDialog
      v-model="passwordResetVisible"
      :username="passwordResetUser?.username ?? ''"
      :display-name="passwordResetUser?.displayName ?? ''"
      @saved="load"
    />
  </section>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Delete, Edit, Key, Plus, Refresh, Search } from '@element-plus/icons-vue'
import {
  createUser,
  deleteUser,
  loadRoles,
  loadUsers,
  updateUser,
  type GatewayRoleInfo,
  type GatewayUserInfo,
  type GatewayUserSaveRequest
} from '../api'
import ResetUserPasswordDialog from './ResetUserPasswordDialog.vue'
import { formatDateTime } from '../utils/format'
import { PERMISSIONS, usePermissions } from '../utils/permissions'

interface UserForm {
  username: string
  displayName: string
  role: string
  enabled: boolean
  password: string
}

const { hasPermission } = usePermissions()
const users = ref<GatewayUserInfo[]>([])
const roles = ref<GatewayRoleInfo[]>([])
const loading = ref(false)
const saving = ref(false)
const drawerVisible = ref(false)
const passwordResetVisible = ref(false)
const passwordResetUser = ref<GatewayUserInfo | null>(null)
const editingUsername = ref('')
const keyword = ref('')
const roleFilter = ref('')
const enabledFilter = ref('')
const form = ref<UserForm | null>(null)
const fieldErrors = reactive({
  username: '',
  displayName: '',
  role: '',
  password: ''
})

const roleMap = computed(() => new Map(roles.value.map(role => [role.name.toLowerCase(), role])))
const canCreateUser = computed(() => hasPermission(PERMISSIONS.usersCreate))
const canEditUser = computed(() => hasPermission(PERMISSIONS.usersEdit))
const canResetUserPassword = computed(() => hasPermission(PERMISSIONS.usersPasswordReset))
const canDeleteUser = computed(() => hasPermission(PERMISSIONS.usersDelete))
const canSaveCurrentUser = computed(() => editingUsername.value ? canEditUser.value : canCreateUser.value)
const selectableRoles = computed(() => {
  const current = form.value?.role.toLowerCase()
  return roles.value.filter(role => role.enabled || role.name.toLowerCase() === current)
})
const filteredUsers = computed(() => {
  const text = keyword.value.trim().toLowerCase()
  return users.value.filter(user => {
    const matchesKeyword = !text ||
      user.username.toLowerCase().includes(text) ||
      user.displayName.toLowerCase().includes(text) ||
      roleLabel(user.role).toLowerCase().includes(text)
    const matchesRole = !roleFilter.value || user.role.toLowerCase() === roleFilter.value.toLowerCase()
    const matchesEnabled = !enabledFilter.value ||
      (enabledFilter.value === 'enabled' && user.enabled) ||
      (enabledFilter.value === 'disabled' && !user.enabled)
    return matchesKeyword && matchesRole && matchesEnabled
  })
})

onMounted(() => load())

async function load() {
  loading.value = true
  try {
    const [userItems, roleItems] = await Promise.all([loadUsers(), loadRoles()])
    users.value = userItems
    roles.value = roleItems
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '人员数据加载失败')
  } finally {
    loading.value = false
  }
}

function openCreate() {
  if (!canCreateUser.value) {
    ElMessage.warning('当前用户没有新增人员权限')
    return
  }
  editingUsername.value = ''
  form.value = {
    username: '',
    displayName: '',
    role: defaultRoleName(),
    enabled: true,
    password: ''
  }
  clearErrors()
  drawerVisible.value = true
}

function openEdit(user: GatewayUserInfo) {
  if (!canEditUser.value) {
    ElMessage.warning('当前用户没有编辑人员权限')
    return
  }
  editingUsername.value = user.username
  form.value = {
    username: user.username,
    displayName: user.displayName,
    role: user.role,
    enabled: user.enabled,
    password: ''
  }
  clearErrors()
  drawerVisible.value = true
}

function openPasswordReset(user: GatewayUserInfo) {
  if (!canResetUserPassword.value) {
    ElMessage.warning('当前用户没有重置人员密码权限')
    return
  }
  passwordResetUser.value = user
  passwordResetVisible.value = true
}

async function save() {
  if (!form.value || !validate()) return
  if (!canSaveCurrentUser.value) {
    ElMessage.warning('当前用户没有保存人员权限')
    return
  }

  const payload: GatewayUserSaveRequest = {
    username: form.value.username.trim(),
    displayName: form.value.displayName.trim(),
    role: form.value.role,
    enabled: form.value.enabled,
    password: form.value.password
  }

  saving.value = true
  try {
    if (editingUsername.value) await updateUser(editingUsername.value, payload)
    else await createUser(payload)
    ElMessage.success('人员已保存')
    drawerVisible.value = false
    await load()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存人员失败')
  } finally {
    saving.value = false
  }
}

async function remove(user: GatewayUserInfo) {
  if (!canDeleteUser.value) {
    ElMessage.warning('当前用户没有删除人员权限')
    return
  }
  if (isDefaultAdmin(user)) return
  const confirmed = await ElMessageBox.confirm(`确认删除人员“${user.displayName || user.username}”？`, '删除人员', {
    type: 'warning',
    confirmButtonText: '删除',
    cancelButtonText: '取消'
  }).then(() => true).catch(() => false)
  if (!confirmed) return

  try {
    await deleteUser(user.username)
    ElMessage.success('人员已删除')
    await load()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '删除人员失败')
  }
}

function validate() {
  clearErrors()
  if (!form.value) return false
  const username = form.value.username.trim()
  if (!/^[A-Za-z][A-Za-z0-9_.-]{2,63}$/.test(username)) {
    fieldErrors.username = '账号需以字母开头，长度 3-64，仅支持字母、数字、下划线、短横线和点'
  }
  if (!form.value.displayName.trim()) fieldErrors.displayName = '请输入姓名'
  if (!form.value.role) fieldErrors.role = '请选择角色'
  if (!editingUsername.value && !form.value.password.trim()) fieldErrors.password = '新增人员必须填写密码'
  if (form.value.password && form.value.password.length < 8) fieldErrors.password = '密码长度不能少于 8 位'
  return !fieldErrors.username && !fieldErrors.displayName && !fieldErrors.role && !fieldErrors.password
}

function clearErrors() {
  fieldErrors.username = ''
  fieldErrors.displayName = ''
  fieldErrors.role = ''
  fieldErrors.password = ''
}

function roleLabel(roleName: string) {
  const role = roleMap.value.get(roleName.toLowerCase())
  return role?.displayName || roleName || '-'
}

function defaultRoleName() {
  return roles.value.find(role => role.enabled && role.name === 'Viewer')?.name ||
    roles.value.find(role => role.enabled)?.name ||
    ''
}

function isDefaultAdmin(user: GatewayUserInfo) {
  return user.username.toLowerCase() === 'admin'
}
</script>

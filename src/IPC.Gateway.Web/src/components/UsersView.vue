<template>
  <section class="view-stack users-view">
    <a-card class="panel-card" size="small">
      <template #title>
        <div class="card-header">
          <div class="detail-title">
            <span>{{ tx('人员管理') }}</span>
            <small>{{ tx('维护 Web 登录人员、角色和启用状态') }}</small>
          </div>
          <div class="card-actions">
            <a-button :loading="loading" @click="load">
              <template #icon>
                <span class="ui-icon"><component :is="Refresh" /></span>
              </template>
              {{ tx('刷新') }}
            </a-button>
            <a-button v-if="canCreateUser" type="primary" @click="openCreate">
              <template #icon>
                <span class="ui-icon"><component :is="Plus" /></span>
              </template>
              {{ tx('新增人员') }}
            </a-button>
          </div>
        </div>
      </template>
      <div class="user-toolbar">
        <gateway-input
          v-model="keyword"
          allow-clear
          :placeholder="tx('按账号、姓名或角色筛选')"
          :prefix-icon="Search"
        />
        <gateway-select v-model="roleFilter" allow-clear :placeholder="tx('角色筛选')">
          <a-select-option
            v-for="role in roles"
            :key="role.name"
            :label="role.displayName || role.name"
            :value="role.name"
          >
            {{ role.displayName || role.name }}
          </a-select-option>
        </gateway-select>
        <gateway-select v-model="enabledFilter" allow-clear :placeholder="tx('状态筛选')">
          <a-select-option :label="tx('启用')" value="enabled">{{ tx('启用') }}</a-select-option>
          <a-select-option :label="tx('停用')" value="disabled">{{ tx('停用') }}</a-select-option>
        </gateway-select>
      </div>
      <GatewayTable
        resize-key="components.UsersView.vue.0"
        :loading="loading"
        :data-source="filteredUsers"
        row-key="username"
        size="small"
        :pagination="false"
        :columns="
          localizeColumns([
            { key: 'column-0', title: tx('账号'), dataIndex: 'username', width: '150', fixed: 'left' },
            { key: 'column-1', title: tx('姓名'), dataIndex: 'displayName', width: '150' },
            { key: 'column-2', title: tx('角色'), width: '150' },
            { key: 'column-3', title: tx('状态'), width: '92' },
            { key: 'column-4', title: tx('创建时间'), width: '170' },
            { key: 'column-5', title: tx('操作'), width: '260', fixed: 'right' }
          ])
        "
        :scroll="{ x: 972, y: NaN }"
      >
        <template #bodyCell="tableCell">
          <template v-if="tableCell.column.key === 'column-2'">
            <template v-for="{ row } in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
              <span class="user-role-cell">
                <a-tag :color="tagColor('primary')">{{ tx(roleLabel(row.role)) }}</a-tag>
              </span>
            </template>
          </template>
          <template v-if="tableCell.column.key === 'column-3'">
            <template v-for="{ row } in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
              <a-tag :color="tagColor(row.enabled ? 'success' : 'info')">
                {{ row.enabled ? tx('启用') : tx('停用') }}
              </a-tag>
            </template>
          </template>
          <template v-if="tableCell.column.key === 'column-4'">
            <template v-for="{ row } in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
              {{ formatDateTime(row.createdTime) }}
            </template>
          </template>
          <template v-if="tableCell.column.key === 'column-5'">
            <template v-for="{ row } in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
              <div class="table-actions table-actions--compact">
                <a-button v-if="canEditUser" size="small" @click="openEdit(row)" type="link">
                  <template #icon>
                    <span class="ui-icon"><component :is="Edit" /></span>
                  </template>
                  {{ tx('编辑') }}
                </a-button>
                <a-button v-if="canResetUserPassword" size="small" @click="openPasswordReset(row)" type="link">
                  <template #icon>
                    <span class="ui-icon"><component :is="Key" /></span>
                  </template>
                  {{ tx('重置密码') }}
                </a-button>
                <a-button
                  v-if="canDeleteUser"
                  size="small"
                  :disabled="isDefaultAdmin(row)"
                  @click="remove(row)"
                  type="link"
                  danger
                >
                  <template #icon>
                    <span class="ui-icon"><component :is="Delete" /></span>
                  </template>
                  {{ tx('删除') }}
                </a-button>
              </div>
            </template>
          </template>
        </template>
        <template #emptyText>
          <a-empty :description="tx('暂无人员')">
            <a-button v-if="canCreateUser" type="primary" @click="openCreate">
              <template #icon>
                <span class="ui-icon"><component :is="Plus" /></span>
              </template>
              {{ tx('新增人员') }}
            </a-button>
          </a-empty>
        </template>
      </GatewayTable>
    </a-card>
    <a-modal
      centered
      destroy-on-close
      :mask-closable="false"
      v-model:open="drawerVisible"
      :title="tx(editingUsername ? tx('编辑人员') : tx('新增人员'))"
      width="560px"
    >
      <a-form
        v-if="form"
        :model="form"
        class="user-form"
        :label-col="{ style: { width: '96px' } }"
        :wrapper-col="{ style: { flex: 1 } }"
        label-align="left"
      >
        <a-form-item
          :label="tx('账号')"
          required
          :help="tx(fieldErrors.username)"
          :validate-status="fieldErrors.username ? 'error' : undefined"
        >
          <gateway-input
            v-model="form.username"
            :disabled="!!editingUsername"
            :placeholder="tx('例如：operator01')"
            maxlength="64"
          />
        </a-form-item>
        <a-form-item
          :label="tx('姓名')"
          required
          :help="tx(fieldErrors.displayName)"
          :validate-status="fieldErrors.displayName ? 'error' : undefined"
        >
          <gateway-input v-model="form.displayName" :placeholder="tx('例如：产线操作员')" maxlength="128" />
        </a-form-item>
        <a-form-item
          :label="tx('角色')"
          required
          :help="tx(fieldErrors.role)"
          :validate-status="fieldErrors.role ? 'error' : undefined"
        >
          <gateway-select v-model="form.role" :placeholder="tx('请选择角色')" class="full-field">
            <a-select-option
              v-for="role in selectableRoles"
              :key="role.name"
              :label="role.displayName || role.name"
              :value="role.name"
              :disabled="!role.enabled"
            >
              <span>{{ role.displayName || role.name }}</span>
              <small class="select-option-note">{{ role.name }}</small>
            </a-select-option>
          </gateway-select>
        </a-form-item>
        <a-form-item :label="tx('启用')"><a-switch v-model:checked="form.enabled" /></a-form-item>
        <a-form-item
          :label="tx(editingUsername ? tx('新密码') : tx('密码'))"
          :required="!editingUsername"
          :help="tx(fieldErrors.password)"
          :validate-status="fieldErrors.password ? 'error' : undefined"
        >
          <gateway-input
            v-model="form.password"
            type="password"
            show-password
            autocomplete="new-password"
            :placeholder="tx(editingUsername ? tx('留空则不修改密码') : tx('请输入登录密码'))"
          />
        </a-form-item>
      </a-form>
      <template #footer>
        <div class="drawer-footer">
          <a-button @click="drawerVisible = false">{{ tx('取消') }}</a-button>
          <a-button v-if="canSaveCurrentUser" type="primary" :loading="saving" @click="save">{{ tx('保存') }}</a-button>
        </div>
      </template>
    </a-modal>
    <ResetUserPasswordDialog
      v-model="passwordResetVisible"
      :username="passwordResetUser?.username ?? ''"
      :display-name="passwordResetUser?.displayName ?? ''"
      @saved="load"
    />
  </section>
</template>

<script setup lang="ts">
import { formatMessage } from '../i18n'
import { notify } from '../utils/ui'
import { tx, localizeColumns } from '../i18n'
import { tagColor, confirmAction } from '../utils/ui'
import { computed, onMounted, reactive, ref } from 'vue'

import {
  DeleteOutlined as Delete,
  EditOutlined as Edit,
  KeyOutlined as Key,
  PlusOutlined as Plus,
  ReloadOutlined as Refresh,
  SearchOutlined as Search
} from '@ant-design/icons-vue'
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

const roleMap = computed(() => new Map(roles.value.map((role) => [role.name.toLowerCase(), role])))
const canCreateUser = computed(() => hasPermission(PERMISSIONS.usersCreate))
const canEditUser = computed(() => hasPermission(PERMISSIONS.usersEdit))
const canResetUserPassword = computed(() => hasPermission(PERMISSIONS.usersPasswordReset))
const canDeleteUser = computed(() => hasPermission(PERMISSIONS.usersDelete))
const canSaveCurrentUser = computed(() => (editingUsername.value ? canEditUser.value : canCreateUser.value))
const selectableRoles = computed(() => {
  const current = form.value?.role.toLowerCase()
  return roles.value.filter((role) => role.enabled || role.name.toLowerCase() === current)
})
const filteredUsers = computed(() => {
  const text = keyword.value.trim().toLowerCase()
  return users.value.filter((user) => {
    const matchesKeyword =
      !text ||
      user.username.toLowerCase().includes(text) ||
      user.displayName.toLowerCase().includes(text) ||
      roleLabel(user.role).toLowerCase().includes(text)
    const matchesRole = !roleFilter.value || user.role.toLowerCase() === roleFilter.value.toLowerCase()
    const matchesEnabled =
      !enabledFilter.value ||
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
    notify.error(error instanceof Error ? error.message : '人员数据加载失败')
  } finally {
    loading.value = false
  }
}

function openCreate() {
  if (!canCreateUser.value) {
    notify.warning('当前用户没有新增人员权限')
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
    notify.warning('当前用户没有编辑人员权限')
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
    notify.warning('当前用户没有重置人员密码权限')
    return
  }
  passwordResetUser.value = user
  passwordResetVisible.value = true
}

async function save() {
  if (!form.value || !validate()) return
  if (!canSaveCurrentUser.value) {
    notify.warning('当前用户没有保存人员权限')
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
    notify.success('人员已保存')
    drawerVisible.value = false
    await load()
  } catch (error) {
    notify.error(error instanceof Error ? error.message : '保存人员失败')
  } finally {
    saving.value = false
  }
}

async function remove(user: GatewayUserInfo) {
  if (!canDeleteUser.value) {
    notify.warning('当前用户没有删除人员权限')
    return
  }
  if (isDefaultAdmin(user)) return
  const confirmed = await confirmAction(
    formatMessage('确认删除人员“{0}”？', [user.displayName || user.username]),
    '删除人员',
    {
      type: 'warning',
      confirmButtonText: '删除',
      cancelButtonText: '取消'
    }
  )
    .then(() => true)
    .catch(() => false)
  if (!confirmed) return

  try {
    await deleteUser(user.username)
    notify.success('人员已删除')
    await load()
  } catch (error) {
    notify.error(error instanceof Error ? error.message : '删除人员失败')
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
  return (
    roles.value.find((role) => role.enabled && role.name === 'Viewer')?.name ||
    roles.value.find((role) => role.enabled)?.name ||
    ''
  )
}

function isDefaultAdmin(user: GatewayUserInfo) {
  return user.username.toLowerCase() === 'admin'
}
</script>

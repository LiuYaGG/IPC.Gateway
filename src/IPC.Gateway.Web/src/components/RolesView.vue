<template>
  <section class="view-stack roles-view">
    <a-card class="panel-card" size="small">
      <template #title>
        <div class="card-header">
          <div class="detail-title">
            <span>{{ tx('角色管理') }}</span>
            <small>{{ tx('维护 Web 端角色基础信息，权限后续在专门页面维护') }}</small>
          </div>
          <div class="card-actions">
            <a-button :loading="loading" @click="load">
              <template #icon>
                <span class="ui-icon"><component :is="Refresh" /></span>
              </template>
              {{ tx('刷新') }}
            </a-button>
            <a-button v-if="canCreateRole" type="primary" @click="openCreate">
              <template #icon>
                <span class="ui-icon"><component :is="Plus" /></span>
              </template>
              {{ tx('新增角色') }}
            </a-button>
          </div>
        </div>
      </template>
      <GatewayTable
        resize-key="components.RolesView.vue.0"
        :loading="loading"
        :data-source="roles"
        row-key="name"
        size="small"
        :pagination="false"
        :columns="
          localizeColumns([
            { key: 'column-0', title: tx('角色编码'), dataIndex: 'name', width: '140', fixed: 'left' },
            { key: 'column-1', title: tx('显示名称'), dataIndex: 'displayName', width: '150' },
            { key: 'column-2', title: tx('说明'), dataIndex: 'description', width: '220', ellipsis: true },
            { key: 'column-3', title: tx('状态'), width: '92' },
            { key: 'column-4', title: tx('类型'), width: '92' },
            { key: 'column-5', title: tx('用户'), dataIndex: 'userCount', width: '80' },
            { key: 'column-6', title: tx('更新时间'), width: '170' },
            { key: 'column-7', title: tx('操作'), width: '178', fixed: 'right' }
          ])
        "
        :scroll="{ x: 1122, y: NaN }"
      >
        <template #bodyCell="tableCell">
          <template v-if="tableCell.column.key === 'column-3'">
            <template v-for="{ row } in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
              <a-tag :color="tagColor(row.enabled ? 'success' : 'info')">
                {{ row.enabled ? tx('启用') : tx('停用') }}
              </a-tag>
            </template>
          </template>
          <template v-if="tableCell.column.key === 'column-4'">
            <template v-for="{ row } in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
              <a-tag :color="tagColor(row.isSystem ? 'warning' : 'primary')">
                {{ row.isSystem ? tx('系统') : tx('自定义') }}
              </a-tag>
            </template>
          </template>
          <template v-if="tableCell.column.key === 'column-6'">
            <template v-for="{ row } in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
              {{ formatDateTime(row.updatedTime) }}
            </template>
          </template>
          <template v-if="tableCell.column.key === 'column-7'">
            <template v-for="{ row } in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
              <div class="table-actions table-actions--compact">
                <a-button v-if="canEditRole" size="small" @click="openEdit(row)" type="link">
                  <template #icon>
                    <span class="ui-icon"><component :is="Edit" /></span>
                  </template>
                  {{ tx('编辑') }}
                </a-button>
                <a-button
                  v-if="canDeleteRole"
                  size="small"
                  :disabled="row.isSystem || row.userCount > 0"
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
          <a-empty :description="tx('暂无角色')">
            <a-button v-if="canCreateRole" type="primary" @click="openCreate">
              <template #icon>
                <span class="ui-icon"><component :is="Plus" /></span>
              </template>
              {{ tx('新增角色') }}
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
      :title="tx(editingName ? tx('编辑角色') : tx('新增角色'))"
      width="620px"
    >
      <a-form
        v-if="form"
        :model="form"
        class="role-form"
        :label-col="{ style: { width: '110px' } }"
        :wrapper-col="{ style: { flex: 1 } }"
        label-align="left"
      >
        <a-form-item
          :label="tx('角色编码')"
          required
          :help="tx(fieldErrors.name)"
          :validate-status="fieldErrors.name ? 'error' : undefined"
        >
          <gateway-input v-model="form.name" :disabled="!!editingName" :placeholder="tx('例如：Maintenance')" />
        </a-form-item>
        <a-form-item
          :label="tx('显示名称')"
          required
          :help="tx(fieldErrors.displayName)"
          :validate-status="fieldErrors.displayName ? 'error' : undefined"
        >
          <gateway-input v-model="form.displayName" :placeholder="tx('例如：维护工程师')" />
        </a-form-item>
        <a-form-item :label="tx('启用')">
          <a-switch v-model:checked="form.enabled" :disabled="editingRole?.isSystem" />
        </a-form-item>
        <a-form-item :label="tx('说明')">
          <gateway-input v-model="form.description" type="textarea" :rows="3" />
        </a-form-item>
      </a-form>
      <template #footer>
        <div class="drawer-footer">
          <a-button @click="drawerVisible = false">{{ tx('取消') }}</a-button>
          <a-button v-if="canSaveCurrentRole" type="primary" :loading="saving" @click="save">{{ tx('保存') }}</a-button>
        </div>
      </template>
    </a-modal>
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
  PlusOutlined as Plus,
  ReloadOutlined as Refresh
} from '@ant-design/icons-vue'
import {
  createRole,
  deleteRole,
  loadRoles,
  updateRole,
  type GatewayRoleInfo,
  type GatewayRoleSaveRequest
} from '../api'
import { formatDateTime } from '../utils/format'
import { PERMISSIONS, usePermissions } from '../utils/permissions'

interface RoleForm {
  name: string
  displayName: string
  description: string
  enabled: boolean
  permissions: string[]
}

const { hasPermission } = usePermissions()
const roles = ref<GatewayRoleInfo[]>([])
const loading = ref(false)
const saving = ref(false)
const drawerVisible = ref(false)
const editingName = ref('')
const form = ref<RoleForm | null>(null)
const fieldErrors = reactive({
  name: '',
  displayName: ''
})

const editingRole = computed(() => roles.value.find((role) => role.name === editingName.value))
const canCreateRole = computed(() => hasPermission(PERMISSIONS.rolesCreate))
const canEditRole = computed(() => hasPermission(PERMISSIONS.rolesEdit))
const canDeleteRole = computed(() => hasPermission(PERMISSIONS.rolesDelete))
const canSaveCurrentRole = computed(() => (editingName.value ? canEditRole.value : canCreateRole.value))

onMounted(() => load())

async function load() {
  loading.value = true
  try {
    roles.value = await loadRoles()
  } catch (error) {
    notify.error(error instanceof Error ? error.message : '角色数据加载失败')
  } finally {
    loading.value = false
  }
}

function openCreate() {
  if (!canCreateRole.value) {
    notify.warning('当前用户没有新增角色权限')
    return
  }
  editingName.value = ''
  form.value = {
    name: '',
    displayName: '',
    description: '',
    enabled: true,
    permissions: []
  }
  clearErrors()
  drawerVisible.value = true
}

function openEdit(role: GatewayRoleInfo) {
  if (!canEditRole.value) {
    notify.warning('当前用户没有编辑角色权限')
    return
  }
  editingName.value = role.name
  form.value = {
    name: role.name,
    displayName: role.displayName,
    description: role.description,
    enabled: role.enabled,
    permissions: [...role.permissions]
  }
  clearErrors()
  drawerVisible.value = true
}

async function save() {
  if (!form.value || !validate()) return
  if (!canSaveCurrentRole.value) {
    notify.warning('当前用户没有保存角色权限')
    return
  }

  const payload: GatewayRoleSaveRequest = {
    name: form.value.name.trim(),
    displayName: form.value.displayName.trim(),
    description: form.value.description.trim(),
    enabled: form.value.enabled,
    permissions: [...form.value.permissions]
  }

  saving.value = true
  try {
    if (editingName.value) await updateRole(editingName.value, payload)
    else await createRole(payload)
    notify.success('角色已保存')
    drawerVisible.value = false
    await load()
  } catch (error) {
    notify.error(error instanceof Error ? error.message : '保存角色失败')
  } finally {
    saving.value = false
  }
}

async function remove(role: GatewayRoleInfo) {
  if (!canDeleteRole.value) {
    notify.warning('当前用户没有删除角色权限')
    return
  }
  if (role.isSystem || role.userCount > 0) return
  const confirmed = await confirmAction(
    formatMessage('确认删除角色“{0}”？', [role.displayName || role.name]),
    '删除角色',
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
    await deleteRole(role.name)
    notify.success('角色已删除')
    await load()
  } catch (error) {
    notify.error(error instanceof Error ? error.message : '删除角色失败')
  }
}

function validate() {
  clearErrors()
  if (!form.value) return false
  if (!/^[A-Za-z][A-Za-z0-9_-]{1,63}$/.test(form.value.name.trim())) {
    fieldErrors.name = '角色编码需以字母开头，仅支持字母、数字、下划线和短横线'
  }
  if (!form.value.displayName.trim()) fieldErrors.displayName = '请输入显示名称'
  return !fieldErrors.name && !fieldErrors.displayName
}

function clearErrors() {
  fieldErrors.name = ''
  fieldErrors.displayName = ''
}
</script>

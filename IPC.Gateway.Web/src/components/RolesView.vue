<template>
  <section class="view-stack roles-view">
    <el-card shadow="never" class="panel-card">
      <template #header>
        <div class="card-header">
          <div class="detail-title">
            <span>角色管理</span>
            <small>维护 Web 端角色基础信息，权限后续在专门页面维护</small>
          </div>
          <div class="card-actions">
            <el-button :icon="Refresh" :loading="loading" @click="load">刷新</el-button>
            <el-button v-if="canCreateRole" type="primary" :icon="Plus" @click="openCreate">新增角色</el-button>
          </div>
        </div>
      </template>

      <el-table v-loading="loading" :data="roles" row-key="name" height="calc(100vh - 260px)">
        <el-table-column prop="name" label="角色编码" min-width="140" fixed />
        <el-table-column prop="displayName" label="显示名称" min-width="150" />
        <el-table-column prop="description" label="说明" min-width="220" show-overflow-tooltip />
        <el-table-column label="状态" width="92">
          <template #default="{ row }">
            <el-tag size="small" :type="row.enabled ? 'success' : 'info'">{{ row.enabled ? '启用' : '停用' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="类型" width="92">
          <template #default="{ row }">
            <el-tag size="small" :type="row.isSystem ? 'warning' : 'primary'">{{ row.isSystem ? '系统' : '自定义' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="userCount" label="用户" width="80" />
        <el-table-column label="更新时间" width="170">
          <template #default="{ row }">{{ formatDateTime(row.updatedTime) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="178" fixed="right">
          <template #default="{ row }">
            <div class="table-actions table-actions--compact">
              <el-button v-if="canEditRole" size="small" text type="primary" :icon="Edit" @click="openEdit(row)">编辑</el-button>
              <el-button v-if="canDeleteRole" size="small" text type="danger" :icon="Delete" :disabled="row.isSystem || row.userCount > 0" @click="remove(row)">删除</el-button>
            </div>
          </template>
        </el-table-column>
        <template #empty>
          <el-empty description="暂无角色">
            <el-button v-if="canCreateRole" type="primary" :icon="Plus" @click="openCreate">新增角色</el-button>
          </el-empty>
        </template>
      </el-table>
    </el-card>

    <el-drawer v-model="drawerVisible" :title="editingName ? '编辑角色' : '新增角色'" size="620px" destroy-on-close>
      <el-form v-if="form" label-width="110px" :model="form" class="role-form">
        <el-form-item label="角色编码" required :error="fieldErrors.name">
          <el-input v-model="form.name" :disabled="!!editingName" placeholder="例如：Maintenance" />
        </el-form-item>
        <el-form-item label="显示名称" required :error="fieldErrors.displayName">
          <el-input v-model="form.displayName" placeholder="例如：维护工程师" />
        </el-form-item>
        <el-form-item label="启用">
          <el-switch v-model="form.enabled" :disabled="editingRole?.isSystem" />
        </el-form-item>
        <el-form-item label="说明">
          <el-input v-model="form.description" type="textarea" :rows="3" />
        </el-form-item>
      </el-form>

      <template #footer>
        <div class="drawer-footer">
          <el-button @click="drawerVisible = false">取消</el-button>
          <el-button v-if="canSaveCurrentRole" type="primary" :loading="saving" @click="save">保存</el-button>
        </div>
      </template>
    </el-drawer>
  </section>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Delete, Edit, Plus, Refresh } from '@element-plus/icons-vue'
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

const editingRole = computed(() => roles.value.find(role => role.name === editingName.value))
const canCreateRole = computed(() => hasPermission(PERMISSIONS.rolesCreate))
const canEditRole = computed(() => hasPermission(PERMISSIONS.rolesEdit))
const canDeleteRole = computed(() => hasPermission(PERMISSIONS.rolesDelete))
const canSaveCurrentRole = computed(() => editingName.value ? canEditRole.value : canCreateRole.value)

onMounted(() => load())

async function load() {
  loading.value = true
  try {
    roles.value = await loadRoles()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '角色数据加载失败')
  } finally {
    loading.value = false
  }
}

function openCreate() {
  if (!canCreateRole.value) {
    ElMessage.warning('当前用户没有新增角色权限')
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
    ElMessage.warning('当前用户没有编辑角色权限')
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
    ElMessage.warning('当前用户没有保存角色权限')
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
    ElMessage.success('角色已保存')
    drawerVisible.value = false
    await load()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存角色失败')
  } finally {
    saving.value = false
  }
}

async function remove(role: GatewayRoleInfo) {
  if (!canDeleteRole.value) {
    ElMessage.warning('当前用户没有删除角色权限')
    return
  }
  if (role.isSystem || role.userCount > 0) return
  const confirmed = await ElMessageBox.confirm(`确认删除角色“${role.displayName || role.name}”？`, '删除角色', {
    type: 'warning',
    confirmButtonText: '删除',
    cancelButtonText: '取消'
  }).then(() => true).catch(() => false)
  if (!confirmed) return

  try {
    await deleteRole(role.name)
    ElMessage.success('角色已删除')
    await load()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '删除角色失败')
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

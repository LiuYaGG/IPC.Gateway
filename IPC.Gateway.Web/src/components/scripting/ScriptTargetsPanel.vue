<template>
  <section>
    <div class="script-toolbar">
      <el-alert title="写入目标决定脚本可访问的表、字段与更新键；未列入白名单的字段会被拒绝。" type="warning" :closable="false" />
      <el-button type="primary" :disabled="!canManage || connections.length === 0" @click="openCreate">新增目标</el-button>
    </div>
    <el-table :data="targets" empty-text="尚未配置写入目标">
      <el-table-column prop="name" label="名称" min-width="150" />
      <el-table-column label="数据库连接" min-width="145"><template #default="scope">{{ connectionName(scope.row.connectionId) }}</template></el-table-column>
      <el-table-column label="数据表" min-width="150"><template #default="scope">{{ [scope.row.schema, scope.row.table].filter(Boolean).join('.') }}</template></el-table-column>
      <el-table-column label="允许操作" width="135"><template #default="scope">{{ [scope.row.allowInsert ? 'INSERT' : '', scope.row.allowUpdate ? 'UPDATE' : ''].filter(Boolean).join(' / ') }}</template></el-table-column>
      <el-table-column label="字段白名单" min-width="210" show-overflow-tooltip><template #default="scope">{{ scope.row.allowedColumns.join(', ') }}</template></el-table-column>
      <el-table-column label="操作" width="155" fixed="right"><template #default="scope"><el-button link type="primary" :disabled="!canManage" @click="openEdit(scope.row)">编辑</el-button><el-button link type="danger" :disabled="!canManage" @click="remove(scope.row)">删除</el-button></template></el-table-column>
    </el-table>

    <el-dialog v-model="dialogVisible" title="数据库写入目标" width="720px" destroy-on-close>
      <el-form :model="form" label-width="120px">
        <el-form-item label="目标名称"><el-input v-model="form.name" /></el-form-item>
        <el-form-item label="数据库连接"><el-select v-model="form.connectionId" style="width: 100%"><el-option v-for="item in connections" :key="item.id" :label="item.name" :value="item.id" /></el-select></el-form-item>
        <el-row :gutter="12"><el-col :span="10"><el-form-item label="架构/Schema"><el-input v-model="form.schema" placeholder="可留空" /></el-form-item></el-col><el-col :span="14"><el-form-item label="表名"><el-input v-model="form.table" /></el-form-item></el-col></el-row>
        <el-form-item label="允许操作"><el-checkbox v-model="form.allowInsert">INSERT</el-checkbox><el-checkbox v-model="form.allowUpdate">UPDATE</el-checkbox></el-form-item>
        <el-form-item label="字段白名单"><el-input v-model="allowedColumnsText" type="textarea" :rows="3" placeholder="逗号或换行分隔，例如 TagName, Value, CollectedAt" /></el-form-item>
        <el-form-item label="更新键字段"><el-input v-model="keyColumnsText" placeholder="仅 UPDATE 使用，例如 DeviceId, TagName" /></el-form-item>
        <el-form-item label="最大影响行数"><el-input-number v-model="form.maxAffectedRows" :min="1" :max="1000" /></el-form-item>
        <el-form-item label="启用"><el-switch v-model="form.enabled" /></el-form-item>
      </el-form>
      <template #footer><el-button @click="dialogVisible = false">取消</el-button><el-button type="primary" :loading="saving" @click="save">保存</el-button></template>
    </el-dialog>
  </section>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { deleteScriptTarget, saveScriptTarget, type ScriptDatabaseConnection, type ScriptDatabaseTarget } from '../../scriptingApi'
import { cloneValue, createTarget, splitColumns } from './scriptingModel'

const props = defineProps<{ targets: ScriptDatabaseTarget[]; connections: ScriptDatabaseConnection[]; canManage: boolean }>()
const emit = defineEmits<{ changed: [] }>()
const dialogVisible = ref(false)
const saving = ref(false)
const allowedColumnsText = ref('')
const keyColumnsText = ref('')
const form = reactive<ScriptDatabaseTarget>(createTarget())

function openCreate() {
  Object.assign(form, createTarget(props.connections[0]?.id ?? ''))
  allowedColumnsText.value = ''
  keyColumnsText.value = ''
  dialogVisible.value = true
}

function openEdit(target: ScriptDatabaseTarget) {
  Object.assign(form, cloneValue(target))
  allowedColumnsText.value = target.allowedColumns.join(', ')
  keyColumnsText.value = target.keyColumns.join(', ')
  dialogVisible.value = true
}

async function save() {
  form.allowedColumns = splitColumns(allowedColumnsText.value)
  form.keyColumns = splitColumns(keyColumnsText.value)
  if (!form.name.trim() || !form.connectionId || !form.table.trim() || form.allowedColumns.length === 0) return ElMessage.warning('请填写名称、连接、表名和字段白名单')
  if (!form.allowInsert && !form.allowUpdate) return ElMessage.warning('至少允许一种写入操作')
  if (form.allowUpdate && form.keyColumns.length === 0) return ElMessage.warning('允许 UPDATE 时必须配置更新键字段')
  saving.value = true
  try {
    await saveScriptTarget(cloneValue(form))
    ElMessage.success('写入目标已保存')
    dialogVisible.value = false
    emit('changed')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存失败')
  } finally {
    saving.value = false
  }
}

async function remove(target: ScriptDatabaseTarget) {
  try {
    await ElMessageBox.confirm(`确定删除目标“${target.name}”吗？`, '删除确认', { type: 'warning' })
    await deleteScriptTarget(target.id)
    ElMessage.success('写入目标已删除')
    emit('changed')
  } catch (error) {
    if (error !== 'cancel' && error !== 'close') ElMessage.error(error instanceof Error ? error.message : '删除失败')
  }
}

function connectionName(id: string) {
  return props.connections.find(item => item.id === id)?.name ?? id
}
</script>

<style scoped>
.script-toolbar { display: flex; align-items: center; gap: 16px; margin-bottom: 16px; }
.script-toolbar .el-alert { flex: 1; }
</style>

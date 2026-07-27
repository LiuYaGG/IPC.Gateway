<template>
  <section>
    <div class="script-toolbar">
      <el-alert title="连接串会由网关密钥加密保存；页面重新加载后只显示掩码。" type="info" :closable="false" />
      <el-button type="primary" :disabled="!canManage" @click="openCreate">新增连接</el-button>
    </div>
    <el-table :data="connections" empty-text="尚未配置数据库连接">
      <el-table-column prop="name" label="名称" min-width="160" />
      <el-table-column prop="provider" label="数据库" width="125" />
      <el-table-column label="状态" width="100"><template #default="scope"><el-tag :type="scope.row.enabled ? 'success' : 'info'">{{ scope.row.enabled ? '启用' : '停用' }}</el-tag></template></el-table-column>
      <el-table-column prop="connectionTimeoutSeconds" label="超时(秒)" width="105" />
      <el-table-column prop="connectionString" label="连接串" min-width="230" show-overflow-tooltip />
      <el-table-column label="操作" width="230" fixed="right">
        <template #default="scope">
          <el-button link type="success" :disabled="!canManage" @click="test(scope.row)">测试</el-button>
          <el-button link type="primary" :disabled="!canManage" @click="openEdit(scope.row)">编辑</el-button>
          <el-button link type="danger" :disabled="!canManage" @click="remove(scope.row)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="dialogVisible" title="数据库连接" width="680px" destroy-on-close>
      <el-form :model="form" label-width="110px">
        <el-form-item label="名称"><el-input v-model="form.name" /></el-form-item>
        <el-form-item label="数据库类型">
          <el-select v-model="form.provider" style="width: 100%">
            <el-option label="SQL Server" value="SqlServer" />
            <el-option label="PostgreSQL" value="PostgreSql" />
            <el-option label="MySQL / MariaDB" value="MySql" />
            <el-option label="SQLite" value="Sqlite" />
            <el-option label="Oracle" value="Oracle" />
            <el-option label="达梦" value="Dameng" />
            <el-option label="人大金仓 KingbaseES" value="KingbaseEs" />
            <el-option label="ClickHouse" value="ClickHouse" />
          </el-select>
        </el-form-item>
        <el-form-item label="连接字符串">
          <el-input v-model="form.connectionString" type="textarea" :rows="5" :placeholder="connectionPlaceholder" />
          <el-text class="connection-hint" type="info">{{ providerHint }}</el-text>
        </el-form-item>
        <el-form-item label="连接超时"><el-input-number v-model="form.connectionTimeoutSeconds" :min="1" :max="120" /> 秒</el-form-item>
        <el-form-item label="启用"><el-switch v-model="form.enabled" /></el-form-item>
      </el-form>
      <template #footer><el-button @click="dialogVisible = false">取消</el-button><el-button type="primary" :loading="saving" @click="save">保存</el-button></template>
    </el-dialog>
  </section>
</template>

<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  deleteScriptConnection,
  saveScriptConnection,
  testScriptConnection,
  type ScriptDatabaseConnection
} from '../../scriptingApi'
import { cloneValue, createConnection } from './scriptingModel'

defineProps<{ connections: ScriptDatabaseConnection[]; canManage: boolean }>()
const emit = defineEmits<{ changed: [] }>()
const dialogVisible = ref(false)
const saving = ref(false)
const form = reactive<ScriptDatabaseConnection>(createConnection())
const connectionExamples: Record<ScriptDatabaseConnection['provider'], string> = {
  SqlServer: 'Server=127.0.0.1;Database=Gateway;User Id=sa;Password=密码;TrustServerCertificate=True',
  PostgreSql: 'Host=127.0.0.1;Port=5432;Database=gateway;Username=postgres;Password=密码',
  MySql: 'Server=127.0.0.1;Port=3306;Database=gateway;User ID=root;Password=密码',
  Sqlite: 'Data Source=D:\\data\\gateway.db',
  Oracle: 'User Id=system;Password=密码;Data Source=127.0.0.1:1521/ORCL',
  Dameng: 'Server=127.0.0.1;Port=5236;User Id=SYSDBA;Password=密码',
  KingbaseEs: 'Server=127.0.0.1;Port=54321;Database=TEST;User Id=SYSTEM;Password=密码',
  ClickHouse: 'Host=127.0.0.1;Port=8123;Database=default;Username=default;Password=密码'
}
const connectionPlaceholder = computed(() => connectionExamples[form.provider])
const providerHint = computed(() => form.provider === 'ClickHouse'
  ? 'ClickHouse 使用 HTTP/HTTPS 端口（通常为 8123/8443）；UPDATE 需要服务器和表引擎支持轻量更新。'
  : '请填写该数据库驱动支持的完整连接字符串。')

function openCreate() {
  Object.assign(form, createConnection())
  dialogVisible.value = true
}

function openEdit(connection: ScriptDatabaseConnection) {
  Object.assign(form, cloneValue(connection))
  dialogVisible.value = true
}

async function save() {
  if (!form.name.trim() || !form.connectionString.trim()) return ElMessage.warning('请填写名称和连接字符串')
  saving.value = true
  try {
    await saveScriptConnection(cloneValue(form))
    ElMessage.success('数据库连接已保存')
    dialogVisible.value = false
    emit('changed')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存失败')
  } finally {
    saving.value = false
  }
}

async function test(connection: ScriptDatabaseConnection) {
  try {
    const response = await testScriptConnection(connection.id)
    ElMessage.success(response.data.message || '连接成功')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '连接失败')
  }
}

async function remove(connection: ScriptDatabaseConnection) {
  try {
    await ElMessageBox.confirm(`确定删除连接“${connection.name}”吗？`, '删除确认', { type: 'warning' })
    await deleteScriptConnection(connection.id)
    ElMessage.success('连接已删除')
    emit('changed')
  } catch (error) {
    if (error !== 'cancel' && error !== 'close') ElMessage.error(error instanceof Error ? error.message : '删除失败')
  }
}
</script>

<style scoped>
.script-toolbar { display: flex; align-items: center; gap: 16px; margin-bottom: 16px; }
.script-toolbar .el-alert { flex: 1; }
.connection-hint { display: block; margin-top: 6px; line-height: 1.5; }
</style>

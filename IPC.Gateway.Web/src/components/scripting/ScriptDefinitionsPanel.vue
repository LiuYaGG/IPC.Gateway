<template>
  <section>
    <div class="script-toolbar">
      <el-input v-model="keyword" clearable placeholder="搜索脚本名称或说明" style="max-width: 320px" />
      <el-button type="primary" :disabled="!canEdit" @click="openCreate">新增脚本</el-button>
    </div>

    <el-table :data="filteredScripts" empty-text="尚未配置脚本">
      <el-table-column prop="name" label="名称" min-width="150" />
      <el-table-column prop="triggerType" label="触发方式" width="110">
        <template #default="scope">{{ triggerLabel(scope.row.triggerType) }}</template>
      </el-table-column>
      <el-table-column label="状态" width="105">
        <template #default="scope">
          <el-tag :type="scope.row.enabled ? 'success' : 'info'">{{ scope.row.enabled ? '启用' : '停用' }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="最近运行" min-width="140">
        <template #default="scope">{{ runtimeState(scope.row.id) }}</template>
      </el-table-column>
      <el-table-column prop="description" label="说明" min-width="190" show-overflow-tooltip />
      <el-table-column label="操作" width="255" fixed="right">
        <template #default="scope">
          <el-button link type="primary" :disabled="!canExecute" @click="execute(scope.row)">运行</el-button>
          <el-button link type="primary" :disabled="!canEdit" @click="openEdit(scope.row)">编辑</el-button>
          <el-button link type="danger" :disabled="!canEdit" @click="remove(scope.row)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="dialogVisible" title="C# 脚本" width="min(980px, 92vw)" destroy-on-close>
      <el-form :model="form" label-width="100px">
        <el-row :gutter="16">
          <el-col :span="12"><el-form-item label="脚本名称"><el-input v-model="form.name" /></el-form-item></el-col>
          <el-col :span="6"><el-form-item label="启用"><el-switch v-model="form.enabled" /></el-form-item></el-col>
          <el-col :span="6"><el-form-item label="超时(秒)"><el-input-number v-model="form.timeoutSeconds" :min="1" :max="300" /></el-form-item></el-col>
        </el-row>
        <el-form-item label="说明"><el-input v-model="form.description" /></el-form-item>
        <el-row :gutter="16">
          <el-col :span="8">
            <el-form-item label="触发方式">
              <el-select v-model="form.triggerType">
                <el-option label="仅手动" value="Manual" />
                <el-option label="固定周期" value="Interval" />
                <el-option label="点位变化" value="TagChanged" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col v-if="form.triggerType === 'Interval'" :span="8">
            <el-form-item label="周期(秒)"><el-input-number v-model="form.intervalSeconds" :min="1" /></el-form-item>
          </el-col>
        </el-row>
        <template v-if="form.triggerType === 'TagChanged'">
          <el-form-item label="筛选点位">
            <el-input v-model="tagFilter" clearable placeholder="输入通道、设备、分组、标签名称或编码" />
          </el-form-item>
          <el-form-item label="触发点位">
            <el-select v-model="form.triggerTagPath" filterable allow-create default-first-option style="width: 100%">
              <el-option v-for="option in filteredTagOptions" :key="option.value" :label="option.label" :value="option.value">
                <div class="tag-option"><span>{{ option.label }}</span><small>{{ option.value }}</small></div>
              </el-option>
            </el-select>
          </el-form-item>
          <el-row :gutter="16">
            <el-col :span="8"><el-form-item label="变化条件"><el-select v-model="form.tagChangeMode"><el-option label="任意变化" value="Any" /><el-option label="上升沿" value="RisingEdge" /><el-option label="下降沿" value="FallingEdge" /></el-select></el-form-item></el-col>
            <el-col :span="8"><el-form-item label="防抖(ms)"><el-input-number v-model="form.debounceMilliseconds" :min="0" /></el-form-item></el-col>
          </el-row>
        </template>
        <el-form-item label="脚本代码">
          <el-input v-model="form.sourceCode" type="textarea" :rows="18" class="code-editor" spellcheck="false" />
        </el-form-item>
        <el-alert title="可使用 Tags、Database、Log、Trigger、UtcNow 和 CancellationToken。数据库只允许调用结构化 InsertAsync/UpdateAsync。" type="info" :closable="false" />
        <el-alert v-if="validationMessage" :title="validationMessage" :type="validationOk ? 'success' : 'error'" show-icon :closable="false" class="script-validation" />
      </el-form>
      <template #footer>
        <el-button :loading="validating" @click="validate">编译检查</el-button>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="save">保存</el-button>
      </template>
    </el-dialog>
  </section>
</template>

<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  deleteGatewayScript,
  executeGatewayScript,
  saveGatewayScript,
  validateGatewayScript,
  type GatewayScriptDefinition,
  type ScriptRuntimeStatus,
  type ScriptTriggerType
} from '../../scriptingApi'
import { cloneValue, createScript } from './scriptingModel'
import type { ScriptTagOption } from './scriptingModel'

const props = defineProps<{
  scripts: GatewayScriptDefinition[]
  statuses: ScriptRuntimeStatus[]
  tagOptions: ScriptTagOption[]
  canEdit: boolean
  canExecute: boolean
}>()
const emit = defineEmits<{ changed: [] }>()
const keyword = ref('')
const dialogVisible = ref(false)
const saving = ref(false)
const validating = ref(false)
const validationMessage = ref('')
const validationOk = ref(false)
const tagFilter = ref('')
const form = reactive<GatewayScriptDefinition>(createScript())

const filteredScripts = computed(() => {
  const term = keyword.value.trim().toLowerCase()
  return term ? props.scripts.filter(item => `${item.name} ${item.description}`.toLowerCase().includes(term)) : props.scripts
})

const filteredTagOptions = computed(() => {
  const term = tagFilter.value.trim().toLocaleLowerCase()
  if (!term) return props.tagOptions
  return props.tagOptions.filter(option => `${option.label} ${option.value}`.toLocaleLowerCase().includes(term))
})

function openCreate() {
  Object.assign(form, createScript())
  tagFilter.value = ''
  validationMessage.value = ''
  dialogVisible.value = true
}

function openEdit(script: GatewayScriptDefinition) {
  Object.assign(form, cloneValue(script))
  tagFilter.value = ''
  validationMessage.value = ''
  dialogVisible.value = true
}

async function validate() {
  validating.value = true
  try {
    const response = await validateGatewayScript(form.sourceCode)
    validationOk.value = response.data.success
    validationMessage.value = response.data.success
      ? `编译检查通过${response.data.warnings.length ? `，${response.data.warnings.length} 条警告` : ''}`
      : response.data.errors.join('；')
  } catch (error) {
    validationOk.value = false
    validationMessage.value = error instanceof Error ? error.message : '编译检查失败'
  } finally {
    validating.value = false
  }
}

async function save() {
  if (!form.name.trim() || !form.sourceCode.trim()) return ElMessage.warning('请填写脚本名称和代码')
  saving.value = true
  try {
    await saveGatewayScript(cloneValue(form))
    ElMessage.success('脚本已保存')
    dialogVisible.value = false
    emit('changed')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存失败')
  } finally {
    saving.value = false
  }
}

async function execute(script: GatewayScriptDefinition) {
  try {
    const response = await executeGatewayScript(script.id)
    const message = response.data.errorMessage || `执行完成：${response.data.state}，耗时 ${response.data.durationMilliseconds} ms`
    response.data.state === 'Succeeded' ? ElMessage.success(message) : ElMessage.warning(message)
    emit('changed')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '执行失败')
  }
}

async function remove(script: GatewayScriptDefinition) {
  try {
    await ElMessageBox.confirm(`确定删除脚本“${script.name}”吗？`, '删除确认', { type: 'warning' })
    await deleteGatewayScript(script.id)
    ElMessage.success('脚本已删除')
    emit('changed')
  } catch (error) {
    if (error !== 'cancel' && error !== 'close') ElMessage.error(error instanceof Error ? error.message : '删除失败')
  }
}

function runtimeState(scriptId: string) {
  return props.statuses.find(item => item.scriptId === scriptId)?.state ?? 'Idle'
}

function triggerLabel(trigger: ScriptTriggerType) {
  return ({ Manual: '手动', Interval: '固定周期', TagChanged: '点位变化' })[trigger]
}
</script>

<style scoped>
.script-toolbar { display: flex; justify-content: space-between; gap: 12px; margin-bottom: 16px; }
.code-editor :deep(textarea) { font-family: Consolas, 'Courier New', monospace; line-height: 1.55; }
.tag-option { display: flex; align-items: center; justify-content: space-between; gap: 18px; }
.tag-option small { color: var(--el-text-color-secondary); font-family: Consolas, monospace; }
.script-validation { margin-top: 12px; }
</style>

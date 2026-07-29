<template>
  <section>
    <div class="script-toolbar">
      <el-input v-model="keyword" clearable placeholder="搜索脚本名称或说明" style="max-width: 320px" />
      <el-button type="primary" :disabled="!canEdit" @click="openCreate">新增脚本</el-button>
    </div>

    <el-table :data="filteredScripts" empty-text="尚未配置脚本">
      <el-table-column prop="name" label="名称" min-width="150" />
      <el-table-column label="脚本类型" width="125">
        <template #default="scope">{{ scriptTypeLabel(scope.row.scriptType) }}</template>
      </el-table-column>
      <el-table-column prop="triggerType" label="触发方式" width="110">
        <template #default="scope">{{ scope.row.scriptType === 'ValueTransform' ? '被调用' : triggerLabel(scope.row.triggerType) }}</template>
      </el-table-column>
      <el-table-column label="发布版本" width="105">
        <template #default="scope">{{ scope.row.scriptType === 'ValueTransform' ? (scope.row.publishedVersion || '未发布') : '-' }}</template>
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
      <el-table-column label="操作" width="320" fixed="right">
        <template #default="scope">
          <el-button v-if="scope.row.scriptType !== 'ValueTransform'" link type="primary" :disabled="!canExecute" @click="execute(scope.row)">运行</el-button>
          <el-button v-else link type="success" :disabled="!canEdit" @click="publish(scope.row)">发布</el-button>
          <el-button link type="primary" :disabled="!canEdit" @click="openEdit(scope.row)">编辑</el-button>
          <el-button link type="danger" :disabled="!canEdit" @click="remove(scope.row)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog
      v-model="dialogVisible"
      title="C# 脚本"
      width="min(1080px, 94vw)"
      class="script-editor-dialog"
      align-center
      append-to-body
      destroy-on-close
      :close-on-click-modal="false"
    >
      <div class="script-dialog-scroll">
        <el-form :model="form" label-position="top">
          <div class="script-basic-grid">
            <el-form-item label="脚本名称" class="script-name-field"><el-input v-model="form.name" /></el-form-item>
            <el-form-item label="脚本类型">
              <el-select v-model="form.scriptType" style="width: 100%" @change="handleScriptTypeChange">
                <el-option label="数据库写入" value="DatabaseWrite" />
                <el-option label="点位联动" value="TagLinkage" />
                <el-option label="值处理" value="ValueTransform" />
              </el-select>
            </el-form-item>
            <el-form-item v-if="form.scriptType !== 'ValueTransform'" label="超时（秒）">
              <el-input-number v-model="form.timeoutSeconds" :min="1" :max="300" controls-position="right" style="width: 100%" />
            </el-form-item>
            <el-form-item v-else label="超时（毫秒）">
              <el-input-number v-model="form.transformTimeoutMilliseconds" :min="10" :max="5000" controls-position="right" style="width: 100%" />
            </el-form-item>
            <el-form-item label="启用" class="script-enabled-field"><el-switch v-model="form.enabled" /></el-form-item>
          </div>
          <el-form-item label="说明"><el-input v-model="form.description" /></el-form-item>
          <div v-if="form.scriptType !== 'ValueTransform'" class="script-trigger-grid">
            <el-form-item label="触发方式">
              <el-select v-model="form.triggerType" style="width: 100%">
                <el-option label="仅手动" value="Manual" />
                <el-option label="固定周期" value="Interval" />
                <el-option label="点位变化" value="TagChanged" />
              </el-select>
            </el-form-item>
            <el-form-item v-if="form.triggerType === 'Interval'" label="周期（秒）">
              <el-input-number v-model="form.intervalSeconds" :min="1" controls-position="right" style="width: 100%" />
            </el-form-item>
          </div>
        <template v-if="form.scriptType !== 'ValueTransform' && form.triggerType === 'TagChanged'">
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
          <div class="script-trigger-grid">
            <el-form-item label="变化条件">
              <el-select v-model="form.tagChangeMode" style="width: 100%"><el-option label="任意变化" value="Any" /><el-option label="上升沿" value="RisingEdge" /><el-option label="下降沿" value="FallingEdge" /></el-select>
            </el-form-item>
            <el-form-item label="防抖（毫秒）">
              <el-input-number v-model="form.debounceMilliseconds" :min="0" controls-position="right" style="width: 100%" />
            </el-form-item>
          </div>
        </template>
        <template v-if="form.scriptType === 'TagLinkage'">
          <el-form-item label="筛选写入点">
            <el-input v-model="writeTagFilter" clearable placeholder="输入通道、设备、分组、标签名称、编码或数据类型" />
          </el-form-item>
          <el-form-item label="允许写入点位">
            <el-select v-model="form.allowedWriteTagPaths" multiple filterable collapse-tags collapse-tags-tooltip style="width: 100%">
              <el-option v-for="option in filteredWritableTagOptions" :key="option.value" :label="`${option.label} (${option.dataType})`" :value="option.value">
                <div class="tag-option"><span>{{ option.label }} · {{ option.dataType }}</span><small>{{ option.value }}</small></div>
              </el-option>
            </el-select>
          </el-form-item>
          <div class="script-trigger-grid">
            <el-form-item label="单次写入上限">
              <el-input-number v-model="form.maxWritesPerExecution" :min="1" :max="100" controls-position="right" style="width: 100%" />
            </el-form-item>
          </div>
          <el-alert title="脚本只能通过 Writes.SetAsync/WriteAsync 写入上述白名单点位；系统会阻止自触发，并将跨脚本联动限制在 8 层以内。" type="warning" :closable="false" class="script-capability" />
        </template>
        <template v-if="form.scriptType === 'ValueTransform'">
          <div class="script-value-grid">
            <el-form-item label="使用范围">
              <el-select v-model="form.valueTransformScope" style="width: 100%">
                <el-option label="规则引擎和标签清洗" value="Both" />
                <el-option label="仅规则引擎" value="RuleEngine" />
                <el-option label="仅标签数据清洗" value="TagCleaning" />
              </el-select>
            </el-form-item>
            <el-form-item v-if="form.valueTransformScope !== 'TagCleaning'" label="规则节点分类">
              <el-select v-model="form.nodeCategory" allow-create filterable style="width: 100%">
                <el-option v-for="category in nodeCategories" :key="category" :label="category" :value="category" />
              </el-select>
            </el-form-item>
            <el-form-item label="输入类型">
              <el-select v-model="form.inputDataType" filterable style="width: 100%">
                <el-option v-for="dataType in valueDataTypes" :key="dataType" :label="dataType" :value="dataType" />
              </el-select>
            </el-form-item>
            <el-form-item label="输出类型">
              <el-select v-model="form.outputDataType" filterable style="width: 100%">
                <el-option v-for="dataType in valueDataTypes" :key="dataType" :label="dataType" :value="dataType" />
              </el-select>
            </el-form-item>
          </div>
        </template>
        <el-form-item label="脚本代码">
          <el-input v-model="form.sourceCode" type="textarea" :rows="15" class="code-editor" spellcheck="false" />
        </el-form-item>
        <el-alert v-if="form.scriptType === 'DatabaseWrite'" title="数据库写入脚本可使用 Tags、Database、Log、Trigger、UtcNow 和 CancellationToken；Database 只允许结构化 InsertAsync/UpdateAsync。" type="info" :closable="false" />
        <el-alert v-else-if="form.scriptType === 'TagLinkage'" title="点位联动脚本可使用 Tags、Writes、Log、Trigger、UtcNow 和 CancellationToken，不能调用 Database。" type="info" :closable="false" />
        <template v-else>
          <el-alert title="值处理脚本只可使用 Input、Log、Now、UtcNow、CancellationToken、Success/Failure 及 .NET 数学和字符串处理能力；不能读写标签、数据库、文件或网络。保存只更新草稿，发布后规则和标签配置才会使用新版本。" type="info" :closable="false" />
          <div class="script-test-row">
            <el-input v-model="testValue" placeholder="输入测试值" />
            <el-button :loading="testing" @click="testTransform">测试当前草稿</el-button>
          </div>
          <el-alert v-if="testMessage" :title="testMessage" :type="testOk ? 'success' : 'error'" show-icon :closable="false" />
        </template>
        <el-alert v-if="validationMessage" :title="validationMessage" :type="validationOk ? 'success' : 'error'" show-icon :closable="false" class="script-validation" />
        </el-form>
      </div>
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
  publishValueTransformScript,
  saveGatewayScript,
  testValueTransformScript,
  validateGatewayScript,
  type GatewayScriptDefinition,
  type GatewayScriptType,
  type ScriptRuntimeStatus,
  type ScriptTriggerType
} from '../../scriptingApi'
import {
  cloneValue,
  createScript,
  databaseScriptExample,
  scriptExampleFor,
  tagLinkageScriptExample,
  valueTransformScriptExample
} from './scriptingModel'
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
const testing = ref(false)
const testValue = ref('')
const testMessage = ref('')
const testOk = ref(false)
const tagFilter = ref('')
const writeTagFilter = ref('')
const form = reactive<GatewayScriptDefinition>(createScript())
const valueDataTypes = [
  'Bool', 'Int8', 'UInt8', 'Int16', 'UInt16', 'Int32', 'UInt32', 'Int64', 'UInt64',
  'Float', 'Double', 'Decimal', 'String', 'DateTime', 'Object',
  'BoolArray', 'Int8Array', 'UInt8Array', 'Int16Array', 'UInt16Array',
  'Int32Array', 'UInt32Array', 'Int64Array', 'UInt64Array', 'FloatArray', 'DoubleArray'
]
const nodeCategories = ['输入', '判断', '处理', '组合', '动作']

const filteredScripts = computed(() => {
  const term = keyword.value.trim().toLowerCase()
  return term ? props.scripts.filter(item => `${item.name} ${item.description}`.toLowerCase().includes(term)) : props.scripts
})

const filteredTagOptions = computed(() => {
  const term = tagFilter.value.trim().toLocaleLowerCase()
  const readable = props.tagOptions.filter(option => option.canRead)
  if (!term) return readable
  return readable.filter(option => `${option.label} ${option.value} ${option.dataType}`.toLocaleLowerCase().includes(term))
})

const filteredWritableTagOptions = computed(() => {
  const term = writeTagFilter.value.trim().toLocaleLowerCase()
  const writable = props.tagOptions.filter(option => option.canWrite)
  if (!term) return writable
  return writable.filter(option => `${option.label} ${option.value} ${option.dataType}`.toLocaleLowerCase().includes(term))
})

function openCreate() {
  Object.assign(form, createScript())
  tagFilter.value = ''
  writeTagFilter.value = ''
  validationMessage.value = ''
  testMessage.value = ''
  dialogVisible.value = true
}

function openEdit(script: GatewayScriptDefinition) {
  const scriptType = script.scriptType ?? 'DatabaseWrite'
  Object.assign(form, createScript(scriptType), cloneValue(script), {
    scriptType,
    allowedWriteTagPaths: script.allowedWriteTagPaths ?? [],
    maxWritesPerExecution: script.maxWritesPerExecution || 20
  })
  tagFilter.value = ''
  writeTagFilter.value = ''
  validationMessage.value = ''
  testMessage.value = ''
  dialogVisible.value = true
}

async function validate() {
  validating.value = true
  try {
    const response = await validateGatewayScript(form.sourceCode, form.scriptType)
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
  if (form.scriptType === 'TagLinkage' && form.allowedWriteTagPaths.length === 0) return ElMessage.warning('请至少选择一个允许写入的目标点位')
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
    if (script.scriptType === 'TagLinkage') {
      await ElMessageBox.confirm(
        `脚本“${script.name}”将向实际设备点位写值，确定立即执行吗？`,
        '点位写入确认',
        { type: 'warning', confirmButtonText: '确认写入', cancelButtonText: '取消' }
      )
    }
    const response = await executeGatewayScript(script.id)
    const message = response.data.errorMessage || `执行完成：${response.data.state}，耗时 ${response.data.durationMilliseconds} ms`
    response.data.state === 'Succeeded' ? ElMessage.success(message) : ElMessage.warning(message)
    emit('changed')
  } catch (error) {
    if (error !== 'cancel' && error !== 'close') ElMessage.error(error instanceof Error ? error.message : '执行失败')
  }
}

async function publish(script: GatewayScriptDefinition) {
  try {
    await ElMessageBox.confirm(`发布脚本“${script.name}”的当前草稿版本 ${script.version ?? 1} 吗？`, '发布确认', { type: 'warning' })
    await publishValueTransformScript(script.id)
    ElMessage.success('值处理脚本已发布')
    emit('changed')
  } catch (error) {
    if (error !== 'cancel' && error !== 'close') ElMessage.error(error instanceof Error ? error.message : '发布失败')
  }
}

async function testTransform() {
  testing.value = true
  try {
    const response = await testValueTransformScript({
      sourceCode: form.sourceCode,
      inputDataType: form.inputDataType,
      outputDataType: form.outputDataType,
      valueText: testValue.value,
      timeoutMilliseconds: form.transformTimeoutMilliseconds
    })
    testOk.value = response.data.success
    testMessage.value = response.data.success
      ? `输出：${response.data.valueText}（${response.data.outputDataType}，${response.data.durationMilliseconds} ms）`
      : response.data.errorMessage
  } catch (error) {
    testOk.value = false
    testMessage.value = error instanceof Error ? error.message : '脚本测试失败'
  } finally {
    testing.value = false
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

function scriptTypeLabel(scriptType: GatewayScriptType | undefined) {
  if (scriptType === 'TagLinkage') return '点位联动'
  if (scriptType === 'ValueTransform') return '值处理'
  return '数据库写入'
}

function handleScriptTypeChange(scriptType: GatewayScriptType) {
  if ([databaseScriptExample, tagLinkageScriptExample, valueTransformScriptExample].includes(form.sourceCode))
    form.sourceCode = scriptExampleFor(scriptType)
  if (scriptType === 'DatabaseWrite') form.allowedWriteTagPaths = []
  if (scriptType === 'ValueTransform') form.triggerType = 'Manual'
  validationMessage.value = ''
  testMessage.value = ''
}
</script>

<style scoped>
.script-toolbar { display: flex; justify-content: space-between; gap: 12px; margin-bottom: 16px; }
.script-dialog-scroll { height: 100%; overflow-x: hidden; overflow-y: auto; padding: 4px 10px 18px 2px; scrollbar-gutter: stable; overscroll-behavior: contain; }
.script-basic-grid { display: grid; grid-template-columns: minmax(260px, 2fr) minmax(180px, 1fr) minmax(170px, .8fr) 90px; gap: 0 16px; }
.script-trigger-grid { display: grid; grid-template-columns: repeat(2, minmax(180px, 260px)); gap: 0 16px; }
.script-value-grid { display: grid; grid-template-columns: repeat(4, minmax(160px, 1fr)); gap: 0 16px; }
.script-test-row { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 12px; margin-top: 14px; }
.code-editor :deep(textarea) { font-family: Consolas, 'Courier New', monospace; line-height: 1.55; }
.tag-option { display: flex; align-items: center; justify-content: space-between; gap: 18px; }
.tag-option small { color: var(--el-text-color-secondary); font-family: Consolas, monospace; }
.script-validation { margin-top: 12px; }
.script-capability { margin-bottom: 16px; }
:deep(.script-dialog-scroll .el-form-item) { margin-bottom: 16px; }
:deep(.script-dialog-scroll .el-form-item__label) { height: auto; margin-bottom: 6px; padding: 0; line-height: 20px; }
:deep(.script-dialog-scroll .el-input-number) { width: 100%; }
:global(.script-editor-dialog) { display: flex; flex-direction: column; height: min(820px, calc(100vh - 32px)); max-height: calc(100vh - 32px); margin: 0; overflow: hidden; }
:global(.script-editor-dialog .el-dialog__header) { flex: 0 0 auto; margin: 0; padding: 18px 22px 14px; border-bottom: 1px solid var(--el-border-color-lighter); }
:global(.script-editor-dialog .el-dialog__body) { flex: 1 1 auto; min-height: 0; padding: 8px 18px 0 20px; overflow: hidden; }
:global(.script-editor-dialog .el-dialog__footer) { flex: 0 0 auto; padding: 12px 22px 16px; border-top: 1px solid var(--el-border-color-lighter); background: var(--el-bg-color); }

@media (max-width: 900px) {
  .script-basic-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .script-trigger-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .script-value-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}

@media (max-width: 600px) {
  .script-basic-grid,
  .script-trigger-grid,
  .script-value-grid { grid-template-columns: minmax(0, 1fr); }
}
</style>

<template>
  <section>
    <div class="script-toolbar">
      <gateway-input v-model="keyword" allow-clear :placeholder="tx('搜索脚本名称或说明')" style="max-width: 320px" />
      <a-button type="primary" :disabled="!canEdit" @click="openCreate">{{ tx('新增脚本') }}</a-button>
    </div>
    <GatewayTable
      resize-key="components.scripting.ScriptDefinitionsPanel.vue.0"
      :data-source="filteredScripts"
      :row-key="(_row: any, index: any) => index"
      size="small"
      :pagination="false"
      :columns="
        localizeColumns([
          { key: 'column-0', title: tx('名称'), dataIndex: 'name', width: '150' },
          { key: 'column-1', title: tx('脚本类型'), width: '125' },
          { key: 'column-2', title: tx('触发方式'), dataIndex: 'triggerType', width: '110' },
          { key: 'column-3', title: tx('发布版本'), width: '105' },
          { key: 'column-4', title: tx('状态'), width: '105' },
          { key: 'column-5', title: tx('最近运行'), width: '140' },
          { key: 'column-6', title: tx('说明'), dataIndex: 'description', width: '190', ellipsis: true },
          { key: 'column-7', title: tx('操作'), width: '320', fixed: 'right' }
        ])
      "
      :scroll="{ x: 1245 }"
    >
      <template #bodyCell="tableCell">
        <template v-if="tableCell.column.key === 'column-1'">
          <template v-for="scope in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
            {{ tx(scriptTypeLabel(scope.row.scriptType)) }}
          </template>
        </template>
        <template v-if="tableCell.column.key === 'column-2'">
          <template v-for="scope in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
            {{ tx(scope.row.scriptType === 'ValueTransform' ? tx('被调用') : triggerLabel(scope.row.triggerType)) }}
          </template>
        </template>
        <template v-if="tableCell.column.key === 'column-3'">
          <template v-for="scope in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
            {{ scope.row.scriptType === 'ValueTransform' ? scope.row.publishedVersion || tx('未发布') : '-' }}
          </template>
        </template>
        <template v-if="tableCell.column.key === 'column-4'">
          <template v-for="scope in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
            <a-tag :color="tagColor(scope.row.enabled ? 'success' : 'info')">
              {{ scope.row.enabled ? tx('启用') : tx('停用') }}
            </a-tag>
          </template>
        </template>
        <template v-if="tableCell.column.key === 'column-5'">
          <template v-for="scope in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
            {{ tx(runtimeState(scope.row.id)) }}
          </template>
        </template>
        <template v-if="tableCell.column.key === 'column-7'">
          <template v-for="scope in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
            <a-button
              v-if="scope.row.scriptType !== 'ValueTransform'"
              :disabled="!canExecute"
              @click="execute(scope.row)"
              type="link"
            >
              {{ tx('运行') }}
            </a-button>
            <a-button v-else :disabled="!canEdit" @click="publish(scope.row)" type="link">{{ tx('发布') }}</a-button>
            <a-button :disabled="!canEdit" @click="openEdit(scope.row)" type="link">{{ tx('编辑') }}</a-button>
            <a-button :disabled="!canEdit" @click="remove(scope.row)" type="link" danger>{{ tx('删除') }}</a-button>
          </template>
        </template>
      </template>
      <template #emptyText><a-empty :description="tx(tx('尚未配置脚本'))" /></template>
    </GatewayTable>
    <a-modal
      v-model:open="dialogVisible"
      :title="tx('C# 脚本')"
      width="min(1080px, 94vw)"
      class="script-editor-dialog"
      destroy-on-close
      :mask-closable="false"
      centered
    >
      <div class="script-dialog-scroll">
        <a-form :model="form" layout="vertical">
          <div class="script-basic-grid">
            <a-form-item :label="tx('脚本名称')" class="script-name-field">
              <gateway-input v-model="form.name" />
            </a-form-item>
            <a-form-item :label="tx('脚本类型')">
              <gateway-select v-model="form.scriptType" style="width: 100%" @change="handleScriptTypeChange">
                <a-select-option :label="tx('数据库写入')" value="DatabaseWrite">
                  {{ tx('数据库写入') }}
                </a-select-option>
                <a-select-option :label="tx('点位联动')" value="TagLinkage">{{ tx('点位联动') }}</a-select-option>
                <a-select-option :label="tx('值处理')" value="ValueTransform">{{ tx('值处理') }}</a-select-option>
              </gateway-select>
            </a-form-item>
            <a-form-item v-if="form.scriptType !== 'ValueTransform'" :label="tx('超时（秒）')">
              <gateway-number-input v-model="form.timeoutSeconds" :min="1" :max="300" style="width: 100%" />
            </a-form-item>
            <a-form-item v-else :label="tx('超时（毫秒）')">
              <gateway-number-input
                v-model="form.transformTimeoutMilliseconds"
                :min="10"
                :max="5000"
                style="width: 100%"
              />
            </a-form-item>
            <a-form-item :label="tx('启用')" class="script-enabled-field">
              <a-switch v-model:checked="form.enabled" />
            </a-form-item>
          </div>
          <a-form-item :label="tx('说明')"><gateway-input v-model="form.description" /></a-form-item>
          <div v-if="form.scriptType !== 'ValueTransform'" class="script-trigger-grid">
            <a-form-item :label="tx('触发方式')">
              <gateway-select v-model="form.triggerType" style="width: 100%">
                <a-select-option :label="tx('仅手动')" value="Manual">{{ tx('仅手动') }}</a-select-option>
                <a-select-option :label="tx('固定周期')" value="Interval">{{ tx('固定周期') }}</a-select-option>
                <a-select-option :label="tx('点位变化')" value="TagChanged">{{ tx('点位变化') }}</a-select-option>
              </gateway-select>
            </a-form-item>
            <a-form-item v-if="form.triggerType === 'Interval'" :label="tx('周期（秒）')">
              <gateway-number-input v-model="form.intervalSeconds" :min="1" style="width: 100%" />
            </a-form-item>
          </div>
          <template v-if="form.scriptType !== 'ValueTransform' && form.triggerType === 'TagChanged'">
            <a-form-item :label="tx('筛选点位')">
              <gateway-input
                v-model="tagFilter"
                allow-clear
                :placeholder="tx('输入通道、设备、分组、标签名称或编码')"
              />
            </a-form-item>
            <a-form-item :label="tx('触发点位')">
              <gateway-select v-model="form.triggerTagPath" searchable style="width: 100%" mode="tags">
                <a-select-option
                  v-for="option in filteredTagOptions"
                  :key="option.value"
                  :label="option.label"
                  :value="option.value"
                >
                  <div class="tag-option">
                    <span>{{ option.label }}</span>
                    <small>{{ option.value }}</small>
                  </div>
                </a-select-option>
              </gateway-select>
            </a-form-item>
            <div class="script-trigger-grid">
              <a-form-item :label="tx('变化条件')">
                <gateway-select v-model="form.tagChangeMode" style="width: 100%">
                  <a-select-option :label="tx('任意变化')" value="Any">{{ tx('任意变化') }}</a-select-option>
                  <a-select-option :label="tx('上升沿')" value="RisingEdge">{{ tx('上升沿') }}</a-select-option>
                  <a-select-option :label="tx('下降沿')" value="FallingEdge">{{ tx('下降沿') }}</a-select-option>
                </gateway-select>
              </a-form-item>
              <a-form-item :label="tx('防抖（毫秒）')">
                <gateway-number-input v-model="form.debounceMilliseconds" :min="0" style="width: 100%" />
              </a-form-item>
            </div>
          </template>
          <template v-if="form.scriptType === 'TagLinkage'">
            <a-form-item :label="tx('筛选写入点')">
              <gateway-input
                v-model="writeTagFilter"
                allow-clear
                :placeholder="tx('输入通道、设备、分组、标签名称、编码或数据类型')"
              />
            </a-form-item>
            <a-form-item :label="tx('允许写入点位')">
              <gateway-select v-model="form.allowedWriteTagPaths" searchable style="width: 100%" mode="multiple">
                <a-select-option
                  v-for="option in filteredWritableTagOptions"
                  :key="option.value"
                  :label="`${option.label} (${option.dataType})`"
                  :value="option.value"
                >
                  <div class="tag-option">
                    <span>{{ option.label }} · {{ option.dataType }}</span>
                    <small>{{ option.value }}</small>
                  </div>
                </a-select-option>
              </gateway-select>
            </a-form-item>
            <div class="script-trigger-grid">
              <a-form-item :label="tx('单次写入上限')">
                <gateway-number-input v-model="form.maxWritesPerExecution" :min="1" :max="100" style="width: 100%" />
              </a-form-item>
            </div>
            <a-alert
              :message="
                tx(
                  '脚本只能通过 Writes.SetAsync/WriteAsync 写入上述白名单点位；系统会阻止自触发，并将跨脚本联动限制在 8 层以内。'
                )
              "
              type="warning"
              :closable="false"
              class="script-capability"
            />
          </template>
          <template v-if="form.scriptType === 'ValueTransform'">
            <div class="script-value-grid">
              <a-form-item :label="tx('使用范围')">
                <gateway-select v-model="form.valueTransformScope" style="width: 100%">
                  <a-select-option :label="tx('规则引擎和标签清洗')" value="Both">
                    {{ tx('规则引擎和标签清洗') }}
                  </a-select-option>
                  <a-select-option :label="tx('仅规则引擎')" value="RuleEngine">{{ tx('仅规则引擎') }}</a-select-option>
                  <a-select-option :label="tx('仅标签数据清洗')" value="TagCleaning">
                    {{ tx('仅标签数据清洗') }}
                  </a-select-option>
                </gateway-select>
              </a-form-item>
              <a-form-item v-if="form.valueTransformScope !== 'TagCleaning'" :label="tx('规则节点分类')">
                <gateway-select v-model="form.nodeCategory" searchable style="width: 100%" mode="tags">
                  <a-select-option
                    v-for="category in nodeCategories"
                    :key="category"
                    :label="tx(category)"
                    :value="category"
                  >
                    {{ tx(category) }}
                  </a-select-option>
                </gateway-select>
              </a-form-item>
              <a-form-item :label="tx('输入类型')">
                <gateway-select v-model="form.inputDataType" searchable style="width: 100%">
                  <a-select-option
                    v-for="dataType in valueDataTypes"
                    :key="dataType"
                    :label="tx(dataType)"
                    :value="dataType"
                  >
                    {{ dataType }}
                  </a-select-option>
                </gateway-select>
              </a-form-item>
              <a-form-item :label="tx('输出类型')">
                <gateway-select v-model="form.outputDataType" searchable style="width: 100%">
                  <a-select-option
                    v-for="dataType in valueDataTypes"
                    :key="dataType"
                    :label="tx(dataType)"
                    :value="dataType"
                  >
                    {{ dataType }}
                  </a-select-option>
                </gateway-select>
              </a-form-item>
            </div>
          </template>
          <a-form-item :label="tx('脚本代码')">
            <gateway-input
              v-model="form.sourceCode"
              type="textarea"
              :rows="15"
              class="code-editor"
              spellcheck="false"
            />
          </a-form-item>
          <a-alert
            v-if="form.scriptType === 'DatabaseWrite'"
            :message="
              tx(
                '数据库写入脚本可使用 Tags、Database、Log、Trigger、UtcNow 和 CancellationToken；Database 只允许结构化 InsertAsync/UpdateAsync。'
              )
            "
            type="info"
            :closable="false"
          />
          <a-alert
            v-else-if="form.scriptType === 'TagLinkage'"
            :message="
              tx('点位联动脚本可使用 Tags、Writes、Log、Trigger、UtcNow 和 CancellationToken，不能调用 Database。')
            "
            type="info"
            :closable="false"
          />
          <template v-else>
            <a-alert
              :message="
                tx(
                  '值处理脚本只可使用 Input、Log、Now、UtcNow、CancellationToken、Success/Failure 及 .NET 数学和字符串处理能力；不能读写标签、数据库、文件或网络。保存只更新草稿，发布后规则和标签配置才会使用新版本。'
                )
              "
              type="info"
              :closable="false"
            />
            <div class="script-test-row">
              <gateway-input v-model="testValue" :placeholder="tx('输入测试值')" />
              <a-button :loading="testing" @click="testTransform">{{ tx('测试当前草稿') }}</a-button>
            </div>
            <a-alert
              v-if="testMessage"
              :message="tx(testMessage)"
              :type="testOk ? 'success' : 'error'"
              show-icon
              :closable="false"
            />
          </template>
          <a-alert
            v-if="validationMessage"
            :message="tx(validationMessage)"
            :type="validationOk ? 'success' : 'error'"
            show-icon
            :closable="false"
            class="script-validation"
          />
        </a-form>
      </div>
      <template #footer>
        <a-button :loading="validating" @click="validate">{{ tx('编译检查') }}</a-button>
        <a-button @click="dialogVisible = false">{{ tx('取消') }}</a-button>
        <a-button type="primary" :loading="saving" @click="save">{{ tx('保存') }}</a-button>
      </template>
    </a-modal>
  </section>
</template>

<script setup lang="ts">
import { formatMessage } from '../../i18n'
import { notify } from '../../utils/ui'
import { tx, localizeColumns } from '../../i18n'
import { tagColor, confirmAction } from '../../utils/ui'
import { statusText } from '../../utils/display'
import { computed, reactive, ref } from 'vue'

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
  'Bool',
  'Byte',
  'UInt8',
  'Int8',
  'Int16',
  'UInt16',
  'Int32',
  'UInt32',
  'Int64',
  'UInt64',
  'Float',
  'Double',
  'Decimal',
  'String',
  'DateTime',
  'Object',
  'BoolArray',
  'ByteArray',
  'UInt8Array',
  'Int8Array',
  'Int16Array',
  'UInt16Array',
  'Int32Array',
  'UInt32Array',
  'Int64Array',
  'UInt64Array',
  'FloatArray',
  'DoubleArray'
]
const nodeCategories = ['输入', '判断', '处理', '组合', '动作']

const filteredScripts = computed(() => {
  const term = keyword.value.trim().toLowerCase()
  return term
    ? props.scripts.filter((item) => `${item.name} ${item.description}`.toLowerCase().includes(term))
    : props.scripts
})

const filteredTagOptions = computed(() => {
  const term = tagFilter.value.trim().toLocaleLowerCase()
  const readable = props.tagOptions.filter((option) => option.canRead)
  if (!term) return readable
  return readable.filter((option) =>
    `${option.label} ${option.value} ${option.dataType}`.toLocaleLowerCase().includes(term)
  )
})

const filteredWritableTagOptions = computed(() => {
  const term = writeTagFilter.value.trim().toLocaleLowerCase()
  const writable = props.tagOptions.filter((option) => option.canWrite)
  if (!term) return writable
  return writable.filter((option) =>
    `${option.label} ${option.value} ${option.dataType}`.toLocaleLowerCase().includes(term)
  )
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
      ? formatMessage('编译检查通过{0}', [
          response.data.warnings.length ? `，${response.data.warnings.length} 条警告` : ''
        ])
      : response.data.errors.join('；')
  } catch (error) {
    validationOk.value = false
    validationMessage.value = error instanceof Error ? error.message : '编译检查失败'
  } finally {
    validating.value = false
  }
}

async function save() {
  if (!form.name.trim() || !form.sourceCode.trim()) return notify.warning('请填写脚本名称和代码')
  if (form.scriptType === 'TagLinkage' && form.allowedWriteTagPaths.length === 0)
    return notify.warning('请至少选择一个允许写入的目标点位')
  saving.value = true
  try {
    await saveGatewayScript(cloneValue(form))
    notify.success('脚本已保存')
    dialogVisible.value = false
    emit('changed')
  } catch (error) {
    notify.error(error instanceof Error ? error.message : '保存失败')
  } finally {
    saving.value = false
  }
}

async function execute(script: GatewayScriptDefinition) {
  try {
    if (script.scriptType === 'TagLinkage') {
      await confirmAction(
        formatMessage('脚本“{0}”将向实际设备点位写值，确定立即执行吗？', [script.name]),
        '点位写入确认',
        {
          type: 'warning',
          confirmButtonText: '确认写入',
          cancelButtonText: '取消'
        }
      )
    }
    const response = await executeGatewayScript(script.id)
    const message =
      response.data.errorMessage ||
      formatMessage('执行完成：{0}，耗时 {1} ms', [tx(statusText(response.data.state)), response.data.durationMilliseconds])
    response.data.state === 'Succeeded' ? notify.success(message) : notify.warning(message)
    emit('changed')
  } catch (error) {
    if (error !== 'cancel' && error !== 'close') notify.error(error instanceof Error ? error.message : '执行失败')
  }
}

async function publish(script: GatewayScriptDefinition) {
  try {
    await confirmAction(
      formatMessage('发布脚本“{0}”的当前草稿版本 {1} 吗？', [script.name, script.version ?? 1]),
      '发布确认',
      {
        type: 'warning'
      }
    )
    await publishValueTransformScript(script.id)
    notify.success('值处理脚本已发布')
    emit('changed')
  } catch (error) {
    if (error !== 'cancel' && error !== 'close') notify.error(error instanceof Error ? error.message : '发布失败')
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
      ? formatMessage('输出：{0}（{1}，{2} ms）', [
          response.data.valueText,
          response.data.outputDataType,
          response.data.durationMilliseconds
        ])
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
    await confirmAction(formatMessage('确定删除脚本“{0}”吗？', [script.name]), '删除确认', { type: 'warning' })
    await deleteGatewayScript(script.id)
    notify.success('脚本已删除')
    emit('changed')
  } catch (error) {
    if (error !== 'cancel' && error !== 'close') notify.error(error instanceof Error ? error.message : '删除失败')
  }
}

function runtimeState(scriptId: string) {
  return statusText(props.statuses.find((item) => item.scriptId === scriptId)?.state ?? 'Idle')
}

function triggerLabel(trigger: ScriptTriggerType) {
  return { Manual: '手动', Interval: '固定周期', TagChanged: '点位变化' }[trigger]
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
.script-toolbar {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 16px;
}
.script-dialog-scroll {
  height: 100%;
  overflow-x: hidden;
  overflow-y: auto;
  padding: 4px 10px 18px 2px;
  scrollbar-gutter: stable;
  overscroll-behavior: contain;
}
.script-basic-grid {
  display: grid;
  grid-template-columns: minmax(260px, 2fr) minmax(180px, 1fr) minmax(170px, 0.8fr) 90px;
  gap: 0 16px;
}
.script-trigger-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(180px, 260px));
  gap: 0 16px;
}
.script-value-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(160px, 1fr));
  gap: 0 16px;
}
.script-test-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 12px;
  margin-top: 14px;
}
.code-editor :deep(textarea) {
  font-family: 'Cascadia Code', 'Microsoft YaHei', sans-serif;
  line-height: 1.55;
}
.tag-option {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 18px;
}
.tag-option small {
  color: var(--ui-text-color-secondary);
  font-family: 'Cascadia Code', 'Microsoft YaHei', sans-serif;
}
.script-validation {
  margin-top: 12px;
}
.script-capability {
  margin-bottom: 16px;
}
:deep(.script-dialog-scroll .ant-form-item) {
  margin-bottom: 16px;
}
:deep(.script-dialog-scroll .ant-form-item-label) {
  height: auto;
  margin-bottom: 6px;
  padding: 0;
  line-height: 20px;
}
:deep(.script-dialog-scroll .ant-input-number) {
  width: 100%;
}
:global(.script-editor-dialog.ant-modal) {
  height: min(820px, calc(100dvh - 32px));
  max-height: calc(100dvh - 32px);
  margin: 0;
}
:global(.script-editor-dialog.ant-modal .ant-modal-content) {
  display: flex;
  flex-direction: column;
  height: 100%;
  max-height: 100%;
  min-height: 0;
  padding: 0;
  overflow: hidden;
}
:global(.script-editor-dialog.ant-modal .ant-modal-header) {
  flex: 0 0 auto;
  margin: 0;
  padding: 18px 22px 14px;
  border-bottom: 1px solid var(--ui-border-color-lighter);
}
:global(.script-editor-dialog.ant-modal .ant-modal-body) {
  flex: 1 1 auto;
  min-height: 0;
  max-height: none;
  padding: 8px 18px 0 20px;
  overflow: hidden;
}
:global(.script-editor-dialog.ant-modal .ant-modal-footer) {
  flex: 0 0 auto;
  padding: 12px 22px 16px;
  border-top: 1px solid var(--ui-border-color-lighter);
  background: var(--ui-bg-color);
}

@media (max-width: 900px) {
  .script-basic-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
  .script-trigger-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
  .script-value-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 600px) {
  .script-basic-grid,
  .script-trigger-grid,
  .script-value-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>

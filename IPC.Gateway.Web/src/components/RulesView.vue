<template>
  <section class="view-stack rules-view">
    <section class="rules-status-bar">
      <div class="rules-status-main">
        <div>
          <span>运行状态</span>
          <el-tag :type="engineStatusType">{{ engineStatusText }}</el-tag>
        </div>
        <strong :class="{ 'error-text': ruleStatus?.lastError }">{{ ruleStatus?.lastError || engineStatusLead }}</strong>
      </div>

      <div class="rules-status-grid">
        <div>
          <span>启用规则</span>
          <strong>{{ ruleStatus?.enabledRuleCount ?? enabledRuleCount }} / {{ ruleStatus?.ruleCount ?? rules.length }}</strong>
        </div>
        <div>
          <span>活跃规则</span>
          <strong>{{ ruleStatus?.activeRuleCount ?? 0 }}</strong>
        </div>
        <div>
          <span>评估次数</span>
          <strong>{{ formatNumber(ruleStatus?.evaluationCount ?? 0) }}</strong>
        </div>
        <div>
          <span>触发 / 清除 / 失败</span>
          <strong>{{ ruleStatus?.triggeredCount ?? 0 }} / {{ ruleStatus?.clearedCount ?? 0 }} / {{ ruleStatus?.failedEvaluationCount ?? 0 }}</strong>
        </div>
      </div>

      <div class="rules-status-actions">
        <span>{{ formatDateTime(ruleStatus?.lastEvaluationTime) }}</span>
        <el-button :icon="Refresh" :loading="loading" @click="fetchRules">刷新</el-button>
        <el-button v-if="canCreateRule" type="primary" :icon="Plus" @click="openCreate">新增规则</el-button>
      </div>
    </section>

    <el-card shadow="never" class="panel-card">
      <template #header>
        <div class="card-header">
          <span>规则列表</span>
          <el-tag type="info">{{ rules.length }} 条</el-tag>
        </div>
      </template>

      <el-table v-loading="loading" :data="rules" row-key="id" height="calc(100vh - 320px)">
        <el-table-column prop="name" label="规则名称" min-width="170" fixed />
        <el-table-column label="状态" width="88">
          <template #default="{ row }">
            <el-tag size="small" :type="row.enabled ? 'success' : 'info'">{{ row.enabled ? '启用' : '停用' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="类型" width="118">
          <template #default="{ row }">{{ conditionTypeLabel(row.conditionType) }}</template>
        </el-table-column>
        <el-table-column label="监测标签" min-width="220" show-overflow-tooltip>
          <template #default="{ row }">
            <div class="rule-source-cell">
              <span>{{ sourceLabel(row) }}</span>
              <small>{{ row.sourcePointCode || '-' }} · {{ row.sourceDataType || '-' }}</small>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="条件" min-width="170">
          <template #default="{ row }">{{ conditionSummary(row) }}</template>
        </el-table-column>
        <el-table-column label="死区 / 变化率" width="138">
          <template #default="{ row }">
            {{ row.conditionType === 'Deadband' ? row.deadband : row.conditionType === 'RateOfChange' ? `${row.rateLimitPerSecond}/s` : '-' }}
          </template>
        </el-table-column>
        <el-table-column label="MQTT" width="94">
          <template #default="{ row }">
            <el-tag size="small" :type="row.publishToMqtt ? 'success' : 'info'">{{ row.publishToMqtt ? '发布' : '关闭' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="最近状态" min-width="170" show-overflow-tooltip>
          <template #default="{ row }">
            <div class="rule-event-cell">
              <el-tag size="small" :type="eventStateType(latestEvent(row.id))">
                {{ latestEvent(row.id)?.state || '-' }}
              </el-tag>
              <small>{{ latestEvent(row.id)?.message || formatDateTime(latestEvent(row.id)?.timestamp) }}</small>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="230" fixed="right">
          <template #default="{ row }">
            <div class="table-actions">
              <el-button size="small" text type="primary" :icon="View" @click="openRuleDetail(row)">详情</el-button>
              <el-button v-if="canEditRule" size="small" text type="primary" :icon="Edit" @click="openEdit(row)">编辑</el-button>
              <el-button v-if="canDeleteRule" size="small" text type="danger" :icon="Delete" @click="removeRule(row)">删除</el-button>
            </div>
          </template>
        </el-table-column>
        <template #empty>
          <el-empty description="暂无规则">
            <el-button v-if="canCreateRule" type="primary" :icon="Plus" @click="openCreate">新增规则</el-button>
          </el-empty>
        </template>
      </el-table>
    </el-card>

    <el-drawer v-model="drawerVisible" :title="editingId ? '编辑规则' : '新增规则'" size="680px" destroy-on-close>
      <el-form v-if="form" :model="form" label-width="130px" class="rule-form">
        <el-divider content-position="left">基础信息</el-divider>
        <el-form-item label="规则名称" required :error="fieldErrors.name">
          <el-input v-model="form.name" placeholder="例如：锅炉压力高限" />
        </el-form-item>
        <div class="rule-form-grid">
          <el-form-item label="启用">
            <el-switch v-model="form.enabled" />
          </el-form-item>
          <el-form-item label="规则类型" required :error="fieldErrors.conditionType">
            <el-select v-model="form.conditionType" @change="changeConditionType">
              <el-option v-for="item in conditionTypeOptions" :key="item.value" :label="item.label" :value="item.value" />
            </el-select>
          </el-form-item>
        </div>
        <el-form-item v-if="form.conditionType !== 'Combination'" label="监测标签" required :error="fieldErrors.source">
          <TagSelector v-model="sourceTagKey" :project="project" @change="selectSourceTag" />
        </el-form-item>

        <el-divider content-position="left">判定条件</el-divider>
        <div v-if="form.conditionType === 'Combination'" class="rule-combination">
          <div class="rule-combination__toolbar">
            <el-form-item label="逻辑关系" required :error="fieldErrors.logicalOperator">
              <el-select v-model="form.logicalOperator">
                <el-option label="AND" value="And" />
                <el-option label="OR" value="Or" />
              </el-select>
            </el-form-item>
            <el-button type="primary" plain :icon="Plus" @click="addCondition">新增条件</el-button>
          </div>

          <div class="rule-condition-list">
            <div v-for="(condition, index) in form.conditions" :key="conditionKey(condition, index)" class="rule-condition-block">
              <div class="rule-condition-block__head">
                <strong>条件 {{ index + 1 }}</strong>
                <el-button size="small" text type="danger" :icon="Delete" @click="removeCondition(index)">删除</el-button>
              </div>

              <el-form-item label="标签" required :error="conditionError(condition, index, 'source')">
                <TagSelector
                  :model-value="conditionTagKey(condition, index)"
                  :project="project"
                  @update:model-value="setConditionTagKey(condition, index, $event)"
                  @change="selectConditionTag(condition, index, $event)"
                />
              </el-form-item>

              <div class="rule-form-grid">
                <el-form-item label="运算符" required :error="conditionError(condition, index, 'operator')">
                  <el-select v-model="condition.operator">
                    <el-option v-for="item in operatorOptions" :key="item.value" :label="item.label" :value="item.value" />
                  </el-select>
                </el-form-item>
                <el-form-item label="比较值" required :error="conditionError(condition, index, 'compareValue')">
                  <el-input-number v-model="condition.compareValue" :step="1" controls-position="right" />
                </el-form-item>
              </div>
            </div>
            <el-empty v-if="form.conditions.length === 0" description="暂无条件">
              <el-button type="primary" :icon="Plus" @click="addCondition">新增条件</el-button>
            </el-empty>
            <p v-if="fieldErrors.conditions" class="form-error-text">{{ fieldErrors.conditions }}</p>
          </div>
        </div>
        <el-form-item v-else-if="form.conditionType === 'Threshold'" label="阈值范围" required :error="fieldErrors.threshold">
          <div class="rule-threshold-row">
            <el-input-number v-model="form.lowLimit" :step="1" controls-position="right" />
            <span>至</span>
            <el-input-number v-model="form.highLimit" :step="1" controls-position="right" />
          </div>
        </el-form-item>
        <div v-else-if="form.conditionType === 'Condition'" class="rule-form-grid">
          <el-form-item label="条件" required :error="fieldErrors.operator">
            <el-select v-model="form.operator">
              <el-option v-for="item in operatorOptions" :key="item.value" :label="item.label" :value="item.value" />
            </el-select>
          </el-form-item>
          <el-form-item label="比较值" required :error="fieldErrors.compareValue">
            <el-input-number v-model="form.compareValue" :step="1" controls-position="right" />
          </el-form-item>
        </div>
        <el-form-item v-else-if="form.conditionType === 'Deadband'" label="死区" required :error="fieldErrors.deadband">
          <el-input-number v-model="form.deadband" :min="0" :step="1" controls-position="right" />
        </el-form-item>
        <el-form-item v-else-if="form.conditionType === 'RateOfChange'" label="变化率" required :error="fieldErrors.rateLimitPerSecond">
          <el-input-number v-model="form.rateLimitPerSecond" :min="0" :step="1" controls-position="right" />
        </el-form-item>
        <el-form-item label="持续时间(s)" :error="fieldErrors.durationSeconds">
          <el-input-number v-model="form.durationSeconds" :min="0" :max="86400" controls-position="right" />
        </el-form-item>

        <el-divider content-position="left">发布</el-divider>
        <div class="rule-form-grid">
          <el-form-item label="发布 MQTT">
            <el-switch v-model="form.publishToMqtt" />
          </el-form-item>
          <el-form-item label="清除时发布">
            <el-switch v-model="form.publishOnClear" />
          </el-form-item>
          <el-form-item label="QoS" :error="fieldErrors.publishQos">
            <el-select v-model="form.publishQos">
              <el-option label="0" :value="0" />
              <el-option label="1" :value="1" />
              <el-option label="2" :value="2" />
            </el-select>
          </el-form-item>
        </div>
        <el-form-item label="主题模板" :required="form.publishToMqtt" :error="fieldErrors.publishTopicTemplate">
          <el-input v-model="form.publishTopicTemplate" />
        </el-form-item>
        <el-form-item label="触发消息">
          <el-input v-model="form.activeMessage" />
        </el-form-item>
        <el-form-item label="清除消息">
          <el-input v-model="form.clearMessage" />
        </el-form-item>
        <el-form-item label="描述">
          <el-input v-model="form.description" type="textarea" :rows="3" />
        </el-form-item>
      </el-form>

      <template #footer>
        <div class="drawer-footer">
          <el-button @click="drawerVisible = false">取消</el-button>
          <el-button v-if="canSaveCurrentRule" type="primary" :loading="saving" @click="saveRule">保存</el-button>
        </div>
      </template>
    </el-drawer>

    <el-drawer v-model="detailDrawerVisible" title="规则详情" size="760px">
      <div v-if="selectedRule" class="rule-detail">
        <div class="stat-pairs">
          <div>
            <span>当前状态</span>
            <el-tag :type="selectedRuleStatus?.isActive ? 'danger' : 'success'">
              {{ selectedRuleStatus?.isActive ? selectedRuleStatus.activeState || 'Active' : 'Clear' }}
            </el-tag>
          </div>
          <div><span>最近触发</span><strong>{{ formatDateTime(selectedRuleStatus?.lastTriggeredTime) }}</strong></div>
          <div><span>最近恢复</span><strong>{{ formatDateTime(selectedRuleStatus?.lastClearedTime) }}</strong></div>
          <div><span>最近评估</span><strong>{{ formatDateTime(selectedRuleStatus?.lastEvaluationTime) }}</strong></div>
          <div><span>评估次数</span><strong>{{ formatNumber(selectedRuleStatus?.evaluationCount ?? 0) }}</strong></div>
          <div><span>触发次数</span><strong>{{ formatNumber(selectedRuleStatus?.triggeredCount ?? 0) }}</strong></div>
          <div><span>恢复次数</span><strong>{{ formatNumber(selectedRuleStatus?.clearedCount ?? 0) }}</strong></div>
          <div><span>失败次数</span><strong>{{ formatNumber(selectedRuleStatus?.failedEvaluationCount ?? 0) }}</strong></div>
        </div>

        <div v-if="selectedRuleStatus?.lastError" class="error-detail__suggestion">
          <span>最近错误</span>
          <p>{{ selectedRuleStatus.lastError }}</p>
          <small>{{ formatDateTime(selectedRuleStatus.lastErrorTime) }}</small>
        </div>

        <section class="rule-test-panel">
          <div class="card-header">
            <span>模拟测试</span>
            <el-button v-if="canDebugRule" type="primary" plain @click="previewRule">预览</el-button>
          </div>
          <div class="rule-test-grid">
            <el-input v-model="testValueText" placeholder="输入测试值" />
            <el-tag :type="testResult?.active ? 'danger' : 'success'">
              {{ testResult ? (testResult.active ? '会触发' : '不会触发') : '待测试' }}
            </el-tag>
          </div>
          <div v-if="testResult" class="rule-message-preview">
            <div><span>状态</span><strong>{{ testResult.state }}</strong></div>
            <div><span>Active 消息</span><strong>{{ testResult.activeMessage }}</strong></div>
            <div><span>Clear 消息</span><strong>{{ testResult.clearMessage }}</strong></div>
          </div>
        </section>

        <section class="rule-event-list">
          <div class="card-header">
            <span>最近事件</span>
            <el-tag type="info">{{ selectedRuleEvents.length }} 条</el-tag>
          </div>
          <el-table :data="selectedRuleEvents" height="260" empty-text="暂无事件">
            <el-table-column label="时间" width="150">
              <template #default="{ row }">{{ formatDateTime(row.timestamp) }}</template>
            </el-table-column>
            <el-table-column label="事件" width="92">
              <template #default="{ row }">
                <el-tag size="small" :type="eventStateType(row)">{{ row.eventType }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="state" label="状态" width="120" />
            <el-table-column label="值" width="100">
              <template #default="{ row }">{{ row.value }}</template>
            </el-table-column>
            <el-table-column prop="message" label="消息" min-width="220" show-overflow-tooltip />
          </el-table>
        </section>
      </div>
      <el-empty v-else description="请选择规则" />
    </el-drawer>
  </section>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Delete, Edit, Plus, Refresh, View } from '@element-plus/icons-vue'
import {
  createRule,
  deleteRule,
  loadRuleEngineStatus,
  loadRules,
  updateRule,
  type EdgeRuleCondition,
  type EdgeRuleConfig,
  type ProjectConfig,
  type RuleEngineRuntimeEvent,
  type RuleEngineRuleRuntimeStatus,
  type RuleEngineRuntimeStatus
} from '../api'
import TagSelector from './TagSelector.vue'
import { formatDateTime, formatNumber } from '../utils/format'
import { findTagSelection, findTagSelectionKey, type TagSelection } from '../utils/tagSelection'
import { PERMISSIONS, usePermissions } from '../utils/permissions'

const props = defineProps<{
  project: ProjectConfig | null
  status: RuleEngineRuntimeStatus | null | undefined
}>()

const emit = defineEmits<{
  changed: []
  'editing-state': [value: boolean]
}>()

const { hasPermission } = usePermissions()
const rules = ref<EdgeRuleConfig[]>([])
const loading = ref(false)
const saving = ref(false)
const runtimeStatus = ref<RuleEngineRuntimeStatus | null>(null)
const drawerVisible = ref(false)
const editingId = ref('')
const form = ref<EdgeRuleConfig | null>(null)
const sourceTagKey = ref('')
const conditionTagKeys = ref<Record<string, string>>({})
const fieldErrors = ref<Record<string, string>>({})
const detailDrawerVisible = ref(false)
const selectedRule = ref<EdgeRuleConfig | null>(null)
const testValueText = ref('')
const testResult = ref<RuleTestResult | null>(null)

interface RuleTestResult {
  active: boolean
  state: string
  value: number
  threshold: number
  activeMessage: string
  clearMessage: string
}

const conditionTypeOptions = [
  { label: '阈值', value: 'Threshold' },
  { label: '条件', value: 'Condition' },
  { label: '死区', value: 'Deadband' },
  { label: '变化率', value: 'RateOfChange' },
  { label: '组合', value: 'Combination' }
]

const operatorOptions = [
  { label: '大于', value: 'GreaterThan' },
  { label: '大于等于', value: 'GreaterThanOrEqual' },
  { label: '小于', value: 'LessThan' },
  { label: '小于等于', value: 'LessThanOrEqual' },
  { label: '等于', value: 'Equal' },
  { label: '不等于', value: 'NotEqual' }
]

const validConditionTypes = new Set(conditionTypeOptions.map(item => item.value))
const validOperators = new Set(operatorOptions.map(item => item.value))
const numericRuleDataTypes = new Set([
  'int16',
  'uint16',
  'int32',
  'uint32',
  'int64',
  'uint64',
  'float',
  'double'
])

const enabledRuleCount = computed(() => rules.value.filter(item => item.enabled).length)
const ruleStatus = computed(() => props.status ?? runtimeStatus.value)
const engineStatusType = computed(() => ruleStatus.value?.isRunning ? 'success' : ruleStatus.value?.enabled ? 'warning' : 'info')
const engineStatusText = computed(() => ruleStatus.value?.isRunning ? '运行中' : ruleStatus.value?.enabled ? '待启动' : '未启用')
const engineStatusLead = computed(() => ruleStatus.value?.isRunning ? '规则引擎正在评估' : ruleStatus.value?.enabled ? '规则引擎已启用' : '规则引擎未启用')
const canCreateRule = computed(() => hasPermission(PERMISSIONS.rulesCreate))
const canEditRule = computed(() => hasPermission(PERMISSIONS.rulesEdit))
const canDeleteRule = computed(() => hasPermission(PERMISSIONS.rulesDelete))
const canDebugRule = computed(() => hasPermission(PERMISSIONS.rulesDebug))
const canSaveCurrentRule = computed(() => editingId.value ? canEditRule.value : canCreateRule.value)

const latestEventsByRuleId = computed(() => {
  const result = new Map<string, RuleEngineRuntimeEvent>()
  for (const item of ruleStatus.value?.recentEvents ?? []) {
    if (!result.has(item.ruleId)) result.set(item.ruleId, item)
  }
  return result
})

const selectedRuleStatus = computed(() => {
  if (!selectedRule.value) return undefined
  return findRuleStatus(selectedRule.value)
})

const selectedRuleEvents = computed(() => {
  if (!selectedRule.value) return []
  const fromStatus = selectedRuleStatus.value?.recentEvents ?? []
  if (fromStatus.length > 0) return fromStatus
  const id = selectedRule.value.id
  return (ruleStatus.value?.recentEvents ?? []).filter(item => item.ruleId === id)
})
const editingActive = computed(() => drawerVisible.value || saving.value)

onMounted(fetchRules)
onBeforeUnmount(() => emit('editing-state', false))
watch(editingActive, value => emit('editing-state', value), { immediate: true })

async function fetchRules() {
  loading.value = true
  try {
    rules.value = await loadRules()
    runtimeStatus.value = await loadRuleEngineStatus().catch(() => props.status ?? null)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '规则加载失败')
  } finally {
    loading.value = false
  }
}

function openCreate() {
  if (!canCreateRule.value) {
    ElMessage.warning('当前用户没有新增规则权限')
    return
  }
  editingId.value = ''
  form.value = createDefaultRule()
  sourceTagKey.value = ''
  conditionTagKeys.value = {}
  fieldErrors.value = {}
  drawerVisible.value = true
}

function openEdit(rule: EdgeRuleConfig) {
  if (!canEditRule.value) {
    ElMessage.warning('当前用户没有编辑规则权限')
    return
  }
  editingId.value = rule.id
  form.value = cloneRule(rule)
  const selection = findTagSelection(props.project, rule)
  sourceTagKey.value = selection?.key ?? findSourceKey(rule)
  if (selection) selectSourceTag(selection)
  initializeConditionTagKeys(form.value)
  fieldErrors.value = {}
  drawerVisible.value = true
}

async function saveRule() {
  if (!form.value || !validateRule(form.value)) return
  if (!canSaveCurrentRule.value) {
    ElMessage.warning('当前用户没有保存规则权限')
    return
  }

  saving.value = true
  try {
    const payload = normalizeRule(form.value)
    if (editingId.value) {
      await updateRule(editingId.value, payload)
      ElMessage.success('规则已更新')
    } else {
      await createRule(payload)
      ElMessage.success('规则已新增')
    }
    drawerVisible.value = false
    await fetchRules()
    emit('changed')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '规则保存失败')
  } finally {
    saving.value = false
  }
}

async function removeRule(rule: EdgeRuleConfig) {
  if (!canDeleteRule.value) {
    ElMessage.warning('当前用户没有删除规则权限')
    return
  }
  try {
    await ElMessageBox.confirm(`确认删除规则“${rule.name}”？`, '删除确认', {
      type: 'warning',
      confirmButtonText: '删除',
      cancelButtonText: '取消'
    })
  } catch {
    return
  }

  loading.value = true
  try {
    await deleteRule(rule.id)
    ElMessage.success('规则已删除')
    await fetchRules()
    emit('changed')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '规则删除失败')
  } finally {
    loading.value = false
  }
}

function openRuleDetail(rule: EdgeRuleConfig) {
  selectedRule.value = cloneRule(rule)
  testValueText.value = ''
  testResult.value = null
  detailDrawerVisible.value = true
}

function previewRule() {
  if (!canDebugRule.value) {
    ElMessage.warning('当前用户没有规则调试权限')
    return
  }
  if (!selectedRule.value) return
  const value = Number(testValueText.value)
  if (!Number.isFinite(value)) {
    ElMessage.warning('请输入合法的测试值')
    return
  }
  testResult.value = evaluateRulePreview(selectedRule.value, value)
}

function selectSourceTag(selection: TagSelection | null) {
  if (!form.value) return
  form.value.sourceDeviceName = selection?.deviceName ?? ''
  form.value.sourceGroupName = selection?.groupName ?? ''
  form.value.sourceTagName = selection?.tagName ?? ''
  form.value.sourcePointCode = selection?.pointCode ?? ''
  form.value.sourceDataType = selection?.dataType ?? ''
  if (selection) fieldErrors.value = { ...fieldErrors.value, source: '' }
}

function changeConditionType(value: string) {
  if (!form.value) return
  if (value === 'Combination') {
    ensureCombinationConditions()
    return
  }
  fieldErrors.value = { ...fieldErrors.value, conditions: '', logicalOperator: '' }
}

function ensureCombinationConditions() {
  if (!form.value) return
  form.value.logicalOperator = form.value.logicalOperator === 'Or' ? 'Or' : 'And'
  if (form.value.conditions.length === 0) {
    form.value.conditions.push(createDefaultCondition(), createDefaultCondition())
  }
  initializeConditionTagKeys(form.value)
}

function addCondition() {
  if (!form.value) return
  const condition = createDefaultCondition()
  form.value.conditions.push(condition)
  conditionTagKeys.value[conditionKey(condition, form.value.conditions.length - 1)] = ''
  fieldErrors.value = { ...fieldErrors.value, conditions: '' }
}

function removeCondition(index: number) {
  if (!form.value) return
  const condition = form.value.conditions[index]
  if (condition) {
    const key = conditionKey(condition, index)
    delete conditionTagKeys.value[key]
    delete fieldErrors.value[`conditionSource:${key}`]
    delete fieldErrors.value[`conditionOperator:${key}`]
    delete fieldErrors.value[`conditionCompare:${key}`]
  }
  form.value.conditions.splice(index, 1)
}

function selectConditionTag(condition: EdgeRuleCondition, index: number, selection: TagSelection | null) {
  condition.sourceDeviceName = selection?.deviceName ?? ''
  condition.sourceGroupName = selection?.groupName ?? ''
  condition.sourceTagName = selection?.tagName ?? ''
  condition.sourcePointCode = selection?.pointCode ?? ''
  condition.sourceDataType = selection?.dataType ?? ''

  const key = conditionKey(condition, index)
  if (selection) {
    fieldErrors.value = {
      ...fieldErrors.value,
      [`conditionSource:${key}`]: ''
    }
  }
}

function initializeConditionTagKeys(rule: EdgeRuleConfig) {
  const keys: Record<string, string> = {}
  for (let index = 0; index < rule.conditions.length; index += 1) {
    const condition = rule.conditions[index]
    keys[conditionKey(condition, index)] = findTagSelectionKey(props.project, condition)
  }
  conditionTagKeys.value = keys
}

function conditionKey(condition: EdgeRuleCondition, index: number) {
  return condition.id || `condition-${index}`
}

function conditionTagKey(condition: EdgeRuleCondition, index: number) {
  return conditionTagKeys.value[conditionKey(condition, index)] ?? ''
}

function setConditionTagKey(condition: EdgeRuleCondition, index: number, value: string) {
  conditionTagKeys.value[conditionKey(condition, index)] = value
}

function conditionError(condition: EdgeRuleCondition, index: number, field: 'source' | 'operator' | 'compareValue') {
  const suffix = field === 'source' ? 'Source' : field === 'operator' ? 'Operator' : 'Compare'
  return fieldErrors.value[`condition${suffix}:${conditionKey(condition, index)}`] ?? ''
}

function validateRule(rule: EdgeRuleConfig) {
  const errors: Record<string, string> = {}
  if (!(rule.name ?? '').trim()) errors.name = '请输入规则名称'
  if (!validConditionTypes.has(rule.conditionType)) errors.conditionType = '请选择有效的规则类型'

  const durationSeconds = Number(rule.durationSeconds)
  if (!Number.isFinite(durationSeconds) || durationSeconds < 0 || durationSeconds > 86400 || !Number.isInteger(durationSeconds)) {
    errors.durationSeconds = '持续时间必须是 0 到 86400 之间的整数秒'
  }

  if (rule.conditionType === 'Combination') {
    if (!['And', 'Or'].includes(rule.logicalOperator)) errors.logicalOperator = '请选择 AND 或 OR'
    if ((rule.conditions ?? []).length < 2) errors.conditions = '组合规则至少需要 2 个条件'

    for (let index = 0; index < (rule.conditions ?? []).length; index += 1) {
      const condition = rule.conditions[index]
      const key = conditionKey(condition, index)
      if (!hasRuleSource(condition)) {
        errors[`conditionSource:${key}`] = '请选择标签'
      } else if (!isNumericRuleDataType(condition.sourceDataType)) {
        errors[`conditionSource:${key}`] = '规则条件只能选择数值类型标签'
      }
      if (!validOperators.has(condition.operator)) {
        errors[`conditionOperator:${key}`] = '请选择有效的运算符'
      }
      if (!Number.isFinite(Number(condition.compareValue))) {
        errors[`conditionCompare:${key}`] = '请输入比较值'
      }
    }
  } else {
    if (!hasRuleSource(rule)) {
      errors.source = '请选择监测标签'
    } else if (!isNumericRuleDataType(rule.sourceDataType)) {
      errors.source = '规则只能选择数值类型标签'
    }

    const lowLimit = Number(rule.lowLimit)
    const highLimit = Number(rule.highLimit)
    if (rule.conditionType === 'Threshold' && (!Number.isFinite(lowLimit) || !Number.isFinite(highLimit) || lowLimit > highLimit)) {
      errors.threshold = '请输入合法的上下限，且低限不能大于高限'
    }

    if (rule.conditionType === 'Condition') {
      if (!validOperators.has(rule.operator)) errors.operator = '请选择有效的运算符'
      if (!Number.isFinite(Number(rule.compareValue))) errors.compareValue = '请输入比较值'
    }

    const deadband = Number(rule.deadband)
    if (rule.conditionType === 'Deadband' && (!Number.isFinite(deadband) || deadband <= 0)) {
      errors.deadband = '请输入大于 0 的死区'
    }

    const rateLimitPerSecond = Number(rule.rateLimitPerSecond)
    if (rule.conditionType === 'RateOfChange' && (!Number.isFinite(rateLimitPerSecond) || rateLimitPerSecond <= 0)) {
      errors.rateLimitPerSecond = '请输入大于 0 的变化率'
    }
  }

  const topicError = validateMqttTopicTemplate(rule.publishToMqtt, rule.publishTopicTemplate)
  if (topicError) errors.publishTopicTemplate = topicError

  if (![0, 1, 2].includes(Number(rule.publishQos)) || !Number.isInteger(Number(rule.publishQos))) {
    errors.publishQos = 'QoS 只能是 0、1、2'
  }

  fieldErrors.value = errors
  if (Object.values(errors).some(Boolean)) {
    ElMessage.warning('请补全规则信息')
    return false
  }
  return true
}

function hasRuleSource(source: { sourcePointCode?: string; sourceDeviceName?: string; sourceTagName?: string }) {
  return !!(source.sourcePointCode ?? '').trim() ||
    (!!(source.sourceDeviceName ?? '').trim() && !!(source.sourceTagName ?? '').trim())
}

function isNumericRuleDataType(dataType: string | undefined) {
  const normalized = (dataType ?? '').trim().toLowerCase()
  return !normalized || numericRuleDataTypes.has(normalized)
}

function validateMqttTopicTemplate(enabled: boolean, template: string | undefined) {
  if (!enabled) return ''
  const topic = (template ?? '').trim()
  if (!topic) return '请输入主题模板'
  if (topic.includes('#') || topic.includes('+')) return '发布主题不能包含 MQTT 通配符 # 或 +'
  if (/\s/.test(topic)) return '发布主题不能包含空白字符'
  return ''
}

function normalizeRule(rule: EdgeRuleConfig): EdgeRuleConfig {
  const selection = findTagSelection(props.project, rule)
  const isCombination = rule.conditionType === 'Combination'
  const conditions = isCombination ? normalizeConditions(rule.conditions ?? []) : []
  const firstCondition = conditions[0]
  return {
    ...createDefaultRule(),
    ...rule,
    id: editingId.value || rule.id || '',
    name: (rule.name ?? '').trim(),
    conditionType: rule.conditionType || 'Threshold',
    sourcePointCode: isCombination
      ? firstCondition?.sourcePointCode ?? ''
      : (selection?.pointCode ?? rule.sourcePointCode ?? '').trim(),
    sourceDeviceName: isCombination
      ? firstCondition?.sourceDeviceName ?? ''
      : (selection?.deviceName ?? rule.sourceDeviceName ?? '').trim(),
    sourceGroupName: isCombination
      ? firstCondition?.sourceGroupName ?? ''
      : (selection?.groupName ?? rule.sourceGroupName ?? '').trim(),
    sourceTagName: isCombination
      ? firstCondition?.sourceTagName ?? ''
      : (selection?.tagName ?? rule.sourceTagName ?? '').trim(),
    sourceDataType: isCombination
      ? firstCondition?.sourceDataType ?? ''
      : (selection?.dataType ?? rule.sourceDataType ?? '').trim(),
    lowLimit: Number(rule.lowLimit) || 0,
    highLimit: Number(rule.highLimit) || 0,
    deadband: Number(rule.deadband) || 0,
    rateLimitPerSecond: Number(rule.rateLimitPerSecond) || 0,
    operator: rule.operator || 'GreaterThan',
    compareValue: Number(rule.compareValue) || 0,
    logicalOperator: isCombination && rule.logicalOperator === 'Or' ? 'Or' : 'And',
    conditions,
    durationSeconds: Number(rule.durationSeconds) || 0,
    publishTopicTemplate: (rule.publishTopicTemplate ?? '').trim(),
    publishQos: Number(rule.publishQos),
    activeMessage: (rule.activeMessage ?? '').trim(),
    clearMessage: (rule.clearMessage ?? '').trim(),
    description: (rule.description ?? '').trim()
  }
}

function createDefaultRule(): EdgeRuleConfig {
  return {
    id: '',
    name: '',
    enabled: true,
    conditionType: 'Threshold',
    sourcePointCode: '',
    sourceDeviceName: '',
    sourceGroupName: '',
    sourceTagName: '',
    sourceDataType: '',
    lowLimit: 0,
    highLimit: 100,
    deadband: 1,
    rateLimitPerSecond: 1,
    operator: 'GreaterThan',
    compareValue: 0,
    logicalOperator: 'And',
    conditions: [],
    durationSeconds: 0,
    publishToMqtt: true,
    publishOnClear: true,
    publishTopicTemplate: 'ipc/rules/{pointCode}/{ruleName}',
    publishQos: 0,
    activeMessage: '',
    clearMessage: '',
    description: ''
  }
}

function createDefaultCondition(): EdgeRuleCondition {
  return {
    id: createClientId(),
    sourcePointCode: '',
    sourceDeviceName: '',
    sourceGroupName: '',
    sourceTagName: '',
    sourceDataType: '',
    operator: 'GreaterThan',
    compareValue: 0
  }
}

function normalizeConditions(conditions: EdgeRuleCondition[]) {
  return conditions.map(condition => {
    const selection = findTagSelection(props.project, condition)
    return {
      ...createDefaultCondition(),
      ...condition,
      id: condition.id || createClientId(),
      sourcePointCode: (selection?.pointCode ?? condition.sourcePointCode ?? '').trim(),
      sourceDeviceName: (selection?.deviceName ?? condition.sourceDeviceName ?? '').trim(),
      sourceGroupName: (selection?.groupName ?? condition.sourceGroupName ?? '').trim(),
      sourceTagName: (selection?.tagName ?? condition.sourceTagName ?? '').trim(),
      sourceDataType: (selection?.dataType ?? condition.sourceDataType ?? '').trim(),
      operator: condition.operator || 'GreaterThan',
      compareValue: Number(condition.compareValue) || 0
    }
  })
}

function cloneRule(rule: EdgeRuleConfig) {
  return {
    ...createDefaultRule(),
    ...rule,
    conditions: (rule.conditions ?? []).map(condition => ({
      ...createDefaultCondition(),
      ...condition,
      id: condition.id || createClientId()
    }))
  }
}

function createClientId() {
  if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) return crypto.randomUUID()
  return `condition-${Date.now()}-${Math.random().toString(16).slice(2)}`
}

function findSourceKey(rule: EdgeRuleConfig) {
  return findTagSelectionKey(props.project, rule)
}

function latestEvent(ruleId: string) {
  return latestEventsByRuleId.value.get(ruleId)
}

function sourceLabel(rule: EdgeRuleConfig) {
  if (rule.conditionType === 'Combination') return `组合条件 ${rule.conditions?.length ?? 0} 条`
  const group = rule.sourceGroupName || '直属标签'
  return [rule.sourceDeviceName, group, rule.sourceTagName].filter(Boolean).join(' / ') || '-'
}

function conditionTypeLabel(value: string) {
  return conditionTypeOptions.find(item => item.value === value)?.label ?? value
}

function operatorLabel(value: string) {
  return operatorOptions.find(item => item.value === value)?.label ?? value
}

function conditionSummary(rule: EdgeRuleConfig) {
  if (rule.conditionType === 'Threshold') return `${rule.lowLimit} 至 ${rule.highLimit}`
  if (rule.conditionType === 'Condition') return `${operatorLabel(rule.operator)} ${rule.compareValue}`
  if (rule.conditionType === 'Deadband') return `死区 ${rule.deadband}`
  if (rule.conditionType === 'RateOfChange') return `变化率 ${rule.rateLimitPerSecond}/s`
  if (rule.conditionType === 'Combination') return `${rule.logicalOperator === 'Or' ? 'OR' : 'AND'} · ${rule.conditions?.length ?? 0} 条`
  return conditionTypeLabel(rule.conditionType)
}

function eventStateType(event?: RuleEngineRuntimeEvent) {
  if (!event) return 'info'
  if (event.state === 'Active' || event.eventType === 'Triggered') return 'danger'
  if (event.eventType === 'Cleared') return 'success'
  if (event.eventType === 'Failed') return 'warning'
  return 'info'
}

function findRuleStatus(rule: EdgeRuleConfig): RuleEngineRuleRuntimeStatus | undefined {
  const id = (rule.id || '').toLowerCase()
  const name = (rule.name || '').toLowerCase()
  return (ruleStatus.value?.rules ?? []).find(item =>
    (item.ruleId || '').toLowerCase() === id ||
    (item.ruleName || '').toLowerCase() === name)
}

function evaluateRulePreview(rule: EdgeRuleConfig, value: number): RuleTestResult {
  let active = false
  let state = 'Normal'
  let threshold = 0

  if (rule.conditionType === 'Threshold') {
    if (value > Number(rule.highLimit)) {
      active = true
      state = 'High'
      threshold = Number(rule.highLimit)
    } else if (value < Number(rule.lowLimit)) {
      active = true
      state = 'Low'
      threshold = Number(rule.lowLimit)
    }
  } else if (rule.conditionType === 'Condition') {
    threshold = Number(rule.compareValue)
    active = compareValue(value, rule.operator, threshold)
    state = `${operatorLabel(rule.operator)} ${threshold}`
  } else if (rule.conditionType === 'Deadband') {
    threshold = Number(rule.deadband)
    active = Math.abs(value) >= threshold
    state = 'Deadband'
  } else if (rule.conditionType === 'RateOfChange') {
    threshold = Number(rule.rateLimitPerSecond)
    active = Math.abs(value) >= threshold
    state = 'RateOfChange'
  } else if (rule.conditionType === 'Combination') {
    const results = (rule.conditions ?? []).map(condition => compareValue(value, condition.operator, Number(condition.compareValue)))
    active = rule.logicalOperator === 'Or' ? results.some(Boolean) : results.length > 0 && results.every(Boolean)
    state = rule.logicalOperator === 'Or' ? 'CombinationOr' : 'CombinationAnd'
    threshold = Number(rule.conditions?.[0]?.compareValue ?? 0)
  }

  return {
    active,
    state,
    value,
    threshold,
    activeMessage: renderRuleMessage(rule.activeMessage || '{ruleName} triggered: {pointCode} = {value}', rule, value, state),
    clearMessage: renderRuleMessage(rule.clearMessage || '{ruleName} cleared: {pointCode} = {value}', rule, value, state)
  }
}

function compareValue(value: number, operator: string, compare: number) {
  if (operator === 'GreaterThan') return value > compare
  if (operator === 'GreaterThanOrEqual') return value >= compare
  if (operator === 'LessThan') return value < compare
  if (operator === 'LessThanOrEqual') return value <= compare
  if (operator === 'Equal') return Math.abs(value - compare) < 0.000001
  if (operator === 'NotEqual') return Math.abs(value - compare) >= 0.000001
  return false
}

function renderRuleMessage(template: string, rule: EdgeRuleConfig, value: number, state: string) {
  const group = rule.sourceGroupName || '_'
  return template
    .replaceAll('{ruleName}', rule.name || '')
    .replaceAll('{pointCode}', rule.sourcePointCode || '')
    .replaceAll('{device}', rule.sourceDeviceName || '')
    .replaceAll('{group}', group)
    .replaceAll('{tag}', rule.sourceTagName || '')
    .replaceAll('{value}', String(value))
    .replaceAll('{state}', state)
}
</script>

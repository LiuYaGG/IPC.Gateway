<template>
  <section class="flow-rules-view">
    <div class="rules-status flow-rules-status">
      <div>
        <p class="eyebrow">Flow Rule Engine</p>
        <h3>{{ status?.isRunning ? '运行中' : '未运行' }}</h3>
      </div>
      <div class="rules-status__metrics">
        <span>流程 {{ rules.length }}</span>
        <span>启用 {{ enabledCount }}</span>
        <span>Active {{ status?.activeRuleCount ?? 0 }}</span>
        <span>评估 {{ status?.evaluationCount ?? 0 }}</span>
      </div>
      <div class="rules-status-actions">
        <el-button :icon="Refresh" :loading="loading" @click="load">刷新</el-button>
        <el-button v-if="canCreateFlowRule" type="primary" :icon="Plus" @click="openCreate">新增流程规则</el-button>
      </div>
    </div>

    <el-table :data="rules" class="flow-rule-table" v-loading="loading">
      <el-table-column prop="name" label="名称" min-width="180" />
      <el-table-column label="状态" width="100">
        <template #default="{ row }">
          <el-tag :type="row.enabled ? 'success' : 'info'">{{ row.enabled ? '启用' : '停用' }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="模式" width="150">
        <template #default="{ row }">
          <el-tag :type="row.mode === 'SimpleCompiled' ? 'primary' : 'warning'">
            {{ row.mode === 'SimpleCompiled' ? '简单编译' : '流程解释' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="节点" width="90">
        <template #default="{ row }">{{ row.nodes?.length ?? 0 }}</template>
      </el-table-column>
      <el-table-column prop="version" label="版本" width="90" />
      <el-table-column prop="publishedVersion" label="发布版本" width="110" />
      <el-table-column prop="lifecycleState" label="生命周期" width="110" />
      <el-table-column prop="compiledRuleId" label="编译规则" min-width="160" show-overflow-tooltip />
      <el-table-column prop="updatedTime" label="更新时间" min-width="170" />
      <el-table-column label="操作" width="170" fixed="right">
        <template #default="{ row }">
          <div class="table-actions">
            <el-button v-if="canEditFlowRule" :icon="Edit" text @click="openEdit(row)">编辑</el-button>
            <el-button v-if="canDeleteFlowRule" :icon="Delete" text type="danger" @click="remove(row)">删除</el-button>
          </div>
        </template>
      </el-table-column>
    </el-table>

    <el-drawer v-model="drawerVisible" class="flow-rule-drawer" size="94%" :title="form?.id ? '编辑流程规则' : '新增流程规则'">
      <template v-if="form">
        <div class="flow-rule-formbar">
          <el-form label-position="top" class="flow-rule-meta">
            <el-form-item label="规则名称" :class="{ 'is-error': showErrors && !form.name?.trim() }">
              <el-input v-model="form.name" />
            </el-form-item>
            <el-form-item label="启用">
              <el-switch v-model="form.enabled" />
            </el-form-item>
            <el-form-item label="说明">
              <el-input v-model="form.description" />
            </el-form-item>
          </el-form>
        </div>

        <div class="flow-editor-toolbar">
          <el-input
            v-model="flowSearch"
            clearable
            placeholder="搜索节点名称、类型或点位"
            @keyup.enter="focusNextSearchResult"
          >
            <template #prefix>
              <el-icon><Search /></el-icon>
            </template>
          </el-input>
          <el-button :icon="Search" :disabled="searchNodeIds.length === 0" @click="focusNextSearchResult">
            {{ searchNodeIds.length || 0 }}
          </el-button>
          <el-button :icon="ZoomOut" @click="setCanvasZoom(canvasZoom - 0.1)" />
          <el-tag type="info">{{ Math.round(canvasZoom * 100) }}%</el-tag>
          <el-button :icon="ZoomIn" @click="setCanvasZoom(canvasZoom + 0.1)" />
          <el-button :icon="FullScreen" @click="fitCanvas">适配</el-button>
          <el-button v-if="canEditCurrentFlowRule" :icon="Rank" @click="autoLayout">自动布局</el-button>
          <el-button v-if="canEditCurrentFlowRule" :icon="CopyDocument" :disabled="!selectedNode" @click="copySelectedNode">复制</el-button>
          <el-button v-if="canEditCurrentFlowRule" :icon="DocumentCopy" :disabled="!copiedNode" @click="pasteNode">粘贴</el-button>
          <el-switch v-if="canDebugFlowRule" v-model="debugHighlight" inline-prompt active-text="调试" inactive-text="调试" />
        </div>

        <div class="flow-editor-shell">
          <div class="flow-palette">
            <div class="flow-palette__header">
              <strong>节点库</strong>
              <el-tag size="small" type="info">{{ FLOW_NODE_GROUPS.length }} 组</el-tag>
            </div>
            <el-collapse v-model="paletteActiveGroups" class="flow-palette-collapse">
              <el-collapse-item
                v-for="group in FLOW_NODE_GROUPS"
                :key="group.title"
                :name="group.title"
              >
                <template #title>
                  <span class="flow-palette__title">
                    {{ group.title }}
                    <small>{{ group.nodes.length }}</small>
                  </span>
                </template>
                <div class="flow-palette__group">
                  <template v-if="canEditCurrentFlowRule">
                    <el-button
                      v-for="item in group.nodes"
                      :key="item.type"
                      class="flow-palette__button"
                      :icon="Plus"
                      @click="addNode(item.type)"
                    >
                      {{ item.label }}
                    </el-button>
                  </template>
                </div>
              </el-collapse-item>
            </el-collapse>
            <el-divider />
            <el-button
              v-if="canEditCurrentFlowRule"
              :icon="Delete"
              :disabled="!selectedEdgeId"
              type="danger"
              plain
              @click="deleteSelectedEdge"
            >
              删除连线
            </el-button>
          </div>

          <FlowRuleCanvas
            ref="canvasComponent"
            :nodes="form.nodes"
            :edges="form.edges"
            :selected-id="selectedNodeId"
            :selected-edge-id="selectedEdgeId"
            :zoom="canvasZoom"
            :highlighted-node-ids="debugNodeIds"
            :search-node-ids="searchNodeIds"
            @select="selectNode"
            @select-edge="selectEdge"
            @clear="clearCanvasSelection"
            @move="(nodeId, x, y) => canEditCurrentFlowRule && moveNode(nodeId, x, y)"
            @connect="(sourceNodeId, targetNodeId) => canEditCurrentFlowRule && connectNodes(sourceNodeId, targetNodeId)"
            @zoom-change="setCanvasZoom"
          />

          <FlowRuleProperties
            :node="selectedNode"
            :project="project"
            @delete="canEditCurrentFlowRule && deleteSelectedNode()"
          />
        </div>

        <el-alert
          v-if="showErrors && errors.length"
          type="error"
          :closable="false"
          class="flow-errors"
          :title="errors.join('；')"
        />
      </template>

      <template #footer>
        <el-button @click="drawerVisible = false">取消</el-button>
        <el-button v-if="canSaveCurrentFlowRule" type="primary" :loading="saving" @click="save">保存</el-button>
      </template>
    </el-drawer>
  </section>
</template>

<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { CopyDocument, Delete, DocumentCopy, Edit, FullScreen, Plus, Rank, Refresh, Search, ZoomIn, ZoomOut } from '@element-plus/icons-vue'
import {
  createFlowRule,
  deleteFlowRule,
  loadFlowRules,
  updateFlowRule,
  type FlowRuleDefinition,
  type FlowRuleNode,
  type ProjectConfig,
  type RuleEngineRuntimeStatus
} from '../api'
import FlowRuleCanvas from './FlowRuleCanvas.vue'
import FlowRuleProperties from './FlowRuleProperties.vue'
import {
  cloneFlowRule,
  createEdge,
  createFlowNode,
  createFlowRuleTemplate,
  FLOW_NODE_GROUPS,
  validateFlowRule
} from '../utils/flowRules'
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
const rules = ref<FlowRuleDefinition[]>([])
const loading = ref(false)
const saving = ref(false)
const drawerVisible = ref(false)
const showErrors = ref(false)
const selectedNodeId = ref('')
const selectedEdgeId = ref('')
const form = ref<FlowRuleDefinition | null>(null)
const canvasComponent = ref<InstanceType<typeof FlowRuleCanvas> | null>(null)
const canvasZoom = ref(1)
const flowSearch = ref('')
const copiedNode = ref<FlowRuleNode | null>(null)
const debugHighlight = ref(true)

const paletteActiveGroups = ref<string[]>(FLOW_NODE_GROUPS[0]?.title ? [FLOW_NODE_GROUPS[0].title] : [])
const enabledCount = computed(() => rules.value.filter(rule => rule.enabled).length)
const canCreateFlowRule = computed(() => hasPermission(PERMISSIONS.flowRulesCreate))
const canEditFlowRule = computed(() => hasPermission(PERMISSIONS.flowRulesEdit))
const canDeleteFlowRule = computed(() => hasPermission(PERMISSIONS.flowRulesDelete))
const canDebugFlowRule = computed(() => hasPermission(PERMISSIONS.flowRulesDebug))
const canEditCurrentFlowRule = computed(() => form.value ? (form.value.id ? canEditFlowRule.value : canCreateFlowRule.value) : false)
const canSaveCurrentFlowRule = computed(() => canEditCurrentFlowRule.value)
const selectedNode = computed(() => form.value?.nodes.find(node => node.id === selectedNodeId.value) ?? null)
const errors = computed(() => form.value ? validateFlowRule(form.value) : [])
const searchNodeIds = computed(() => {
  const keyword = flowSearch.value.trim().toLowerCase()
  if (!keyword || !form.value) return []
  return form.value.nodes
    .filter(node => [
      node.label,
      node.nodeType,
      node.tagName,
      node.pointCode,
      node.deviceName,
      node.groupName
    ].some(value => (value || '').toLowerCase().includes(keyword)))
    .map(node => node.id)
})
const runtimeRuleStatus = computed(() => {
  if (!form.value || !props.status?.rules) return undefined
  const id = form.value.id || ''
  const compiledId = form.value.compiledRuleId || ''
  const name = form.value.name || ''
  return props.status.rules.find(rule =>
    (!!id && rule.ruleId === id) ||
    (!!compiledId && rule.ruleId === compiledId) ||
    (!!name && rule.ruleName === name))
})
const debugNodeIds = computed(() => {
  if (!debugHighlight.value || !form.value) return []
  const status = runtimeRuleStatus.value
  const recent = findLatestRuntimeEvent()
  if (!status?.isActive && !recent) return []

  const ids = new Set<string>()
  if (recent?.pointCode) {
    for (const node of form.value.nodes) {
      if (sameText(node.pointCode, recent.pointCode)) ids.add(node.id)
      if (sameText(node.tagName, recent.tagName) && sameText(node.deviceName, recent.deviceName)) ids.add(node.id)
    }
  }

  const conditionType = (recent?.conditionType || status?.conditionType || '').toLowerCase()
  for (const node of form.value.nodes) {
    if (node.nodeType.toLowerCase() === conditionType) ids.add(node.id)
    if (isConditionLikeNode(node) && status?.isActive) ids.add(node.id)
    if (isActionNode(node) && (status?.isActive || recent?.eventType === 'active')) ids.add(node.id)
  }
  return Array.from(ids)
})

watch(() => props.project, value => {
  if (!drawerVisible.value) rules.value = (value?.flowRules ?? []).map(cloneFlowRule)
}, { immediate: true })

watch([drawerVisible, saving], () => emit('editing-state', drawerVisible.value || saving.value), { immediate: true })

onMounted(() => {
  load()
  window.addEventListener('keydown', handleEditorShortcut)
})
onBeforeUnmount(() => {
  emit('editing-state', false)
  window.removeEventListener('keydown', handleEditorShortcut)
})

async function load() {
  loading.value = true
  try {
    rules.value = (await loadFlowRules()).map(cloneFlowRule)
  } catch (error) {
    rules.value = (props.project?.flowRules ?? []).map(cloneFlowRule)
    ElMessage.warning(error instanceof Error ? error.message : '流程规则加载失败')
  } finally {
    loading.value = false
  }
}

function openCreate() {
  if (!canCreateFlowRule.value) {
    ElMessage.warning('当前用户没有新增流程规则权限')
    return
  }
  form.value = createFlowRuleTemplate()
  selectedNodeId.value = form.value.nodes[0]?.id ?? ''
  selectedEdgeId.value = ''
  showErrors.value = false
  drawerVisible.value = true
}

function openEdit(rule: FlowRuleDefinition) {
  if (!canEditFlowRule.value) {
    ElMessage.warning('当前用户没有编辑流程规则权限')
    return
  }
  form.value = cloneFlowRule(rule)
  selectedNodeId.value = form.value.nodes[0]?.id ?? ''
  selectedEdgeId.value = ''
  showErrors.value = false
  drawerVisible.value = true
}

function resetPaletteGroups() {
  paletteActiveGroups.value = FLOW_NODE_GROUPS[0]?.title ? [FLOW_NODE_GROUPS[0].title] : []
}

function addNode(nodeType: string) {
  if (!canEditCurrentFlowRule.value) return
  if (!form.value) return
  const node = createFlowNode(nodeType, form.value.nodes.length)
  form.value.nodes.push(node)
  selectedNodeId.value = node.id
  selectedEdgeId.value = ''
  nextTick(() => canvasComponent.value?.centerNode(node.id))
}

function deleteSelectedNode() {
  if (!canEditCurrentFlowRule.value) return
  if (!form.value || !selectedNodeId.value) return
  form.value.nodes = form.value.nodes.filter(node => node.id !== selectedNodeId.value)
  form.value.edges = form.value.edges.filter(edge =>
    edge.sourceNodeId !== selectedNodeId.value && edge.targetNodeId !== selectedNodeId.value)
  selectedNodeId.value = form.value.nodes[0]?.id ?? ''
  selectedEdgeId.value = ''
}

function moveNode(nodeId: string, x: number, y: number) {
  if (!canEditCurrentFlowRule.value) return
  const node = form.value?.nodes.find(item => item.id === nodeId)
  if (!node) return
  node.x = Math.round(x)
  node.y = Math.round(y)
}

function connectNodes(sourceNodeId: string, targetNodeId: string) {
  if (!canEditCurrentFlowRule.value) return
  if (!form.value || sourceNodeId === targetNodeId) return
  const validation = validateConnection(sourceNodeId, targetNodeId)
  if (validation) {
    ElMessage.warning(validation)
    return
  }
  const exists = form.value.edges.some(edge =>
    edge.sourceNodeId === sourceNodeId && edge.targetNodeId === targetNodeId)
  if (exists) return
  const edge = createEdge(sourceNodeId, targetNodeId)
  form.value.edges.push(edge)
  selectedNodeId.value = ''
  selectedEdgeId.value = edge.id
}

function selectNode(nodeId: string) {
  selectedNodeId.value = nodeId
  selectedEdgeId.value = ''
}

function selectEdge(edgeId: string) {
  selectedEdgeId.value = edgeId
  selectedNodeId.value = ''
}

function clearCanvasSelection() {
  selectedNodeId.value = ''
  selectedEdgeId.value = ''
}

function deleteSelectedEdge() {
  if (!canEditCurrentFlowRule.value) return
  if (!form.value || !selectedEdgeId.value) return
  form.value.edges = form.value.edges.filter(edge => edge.id !== selectedEdgeId.value)
  selectedEdgeId.value = ''
}

function setCanvasZoom(value: number) {
  canvasZoom.value = Math.min(1.8, Math.max(0.35, Number(value.toFixed(2))))
}

function fitCanvas() {
  canvasComponent.value?.fitView()
}

function focusNextSearchResult() {
  if (!searchNodeIds.value.length) return
  const currentIndex = searchNodeIds.value.indexOf(selectedNodeId.value)
  const nextId = searchNodeIds.value[(currentIndex + 1) % searchNodeIds.value.length]
  selectedNodeId.value = nextId
  selectedEdgeId.value = ''
  nextTick(() => canvasComponent.value?.centerNode(nextId))
}

function copySelectedNode() {
  if (!canEditCurrentFlowRule.value) return
  if (!selectedNode.value) return
  copiedNode.value = cloneNode(selectedNode.value)
  ElMessage.success('节点已复制')
}

function pasteNode() {
  if (!canEditCurrentFlowRule.value) return
  if (!form.value || !copiedNode.value) return
  const node = cloneNode(copiedNode.value)
  node.id = createId()
  node.x = Math.min(1238, node.x + 36)
  node.y = Math.min(724, node.y + 36)
  node.label = node.label || copiedNode.value.label
  form.value.nodes.push(node)
  selectedNodeId.value = node.id
  selectedEdgeId.value = ''
  copiedNode.value = cloneNode(node)
  nextTick(() => canvasComponent.value?.centerNode(node.id))
}

function autoLayout() {
  if (!canEditCurrentFlowRule.value) return
  if (!form.value) return
  const nodes = form.value.nodes
  if (!nodes.length) return

  const levels = calculateNodeLevels(nodes, form.value.edges)
  const buckets = new Map<number, FlowRuleNode[]>()
  for (const node of nodes) {
    const level = levels.get(node.id) ?? 0
    if (!buckets.has(level)) buckets.set(level, [])
    buckets.get(level)!.push(node)
  }

  for (const [level, bucket] of buckets) {
    bucket
      .sort((a, b) => a.y - b.y || a.x - b.x)
      .forEach((node, index) => {
        node.x = 64 + level * 220
        node.y = 70 + index * 128
      })
  }
  nextTick(fitCanvas)
}

function validateConnection(sourceNodeId: string, targetNodeId: string) {
  if (!form.value) return '当前没有可编辑的流程'
  const source = form.value.nodes.find(node => node.id === sourceNodeId)
  const target = form.value.nodes.find(node => node.id === targetNodeId)
  if (!source || !target) return '连线节点不存在'
  if (sourceNodeId === targetNodeId) return '不能连接到自身'
  if (form.value.edges.some(edge => edge.sourceNodeId === sourceNodeId && edge.targetNodeId === targetNodeId)) return '这条连线已经存在'
  if (target.nodeType === 'TagInput') return '标签输入节点只能作为起点'
  if (isTerminalActionNode(source)) return '通知和发布动作不能再连接到下游节点'
  if (source.nodeType === 'Logic' && target.nodeType === 'Logic') return '逻辑节点之间不需要直接相连'
  if (createsCycle(sourceNodeId, targetNodeId)) return '这条连线会形成环路'
  return ''
}

function createsCycle(sourceNodeId: string, targetNodeId: string) {
  if (!form.value) return false
  const graph = new Map<string, string[]>()
  for (const edge of form.value.edges) {
    if (!graph.has(edge.sourceNodeId)) graph.set(edge.sourceNodeId, [])
    graph.get(edge.sourceNodeId)!.push(edge.targetNodeId)
  }
  if (!graph.has(sourceNodeId)) graph.set(sourceNodeId, [])
  graph.get(sourceNodeId)!.push(targetNodeId)

  const stack = [targetNodeId]
  const visited = new Set<string>()
  while (stack.length) {
    const current = stack.pop()!
    if (current === sourceNodeId) return true
    if (visited.has(current)) continue
    visited.add(current)
    for (const next of graph.get(current) ?? []) stack.push(next)
  }
  return false
}

function calculateNodeLevels(nodes: FlowRuleNode[], edges: ReturnType<typeof createEdge>[]) {
  const ids = new Set(nodes.map(node => node.id))
  const incoming = new Map<string, number>()
  const outgoing = new Map<string, string[]>()
  for (const node of nodes) incoming.set(node.id, 0)
  for (const edge of edges) {
    if (!ids.has(edge.sourceNodeId) || !ids.has(edge.targetNodeId)) continue
    incoming.set(edge.targetNodeId, (incoming.get(edge.targetNodeId) ?? 0) + 1)
    if (!outgoing.has(edge.sourceNodeId)) outgoing.set(edge.sourceNodeId, [])
    outgoing.get(edge.sourceNodeId)!.push(edge.targetNodeId)
  }

  const queue = nodes.filter(node => (incoming.get(node.id) ?? 0) === 0)
  const levels = new Map<string, number>()
  for (const node of queue) levels.set(node.id, 0)
  while (queue.length) {
    const current = queue.shift()!
    const level = levels.get(current.id) ?? 0
    for (const nextId of outgoing.get(current.id) ?? []) {
      levels.set(nextId, Math.max(levels.get(nextId) ?? 0, level + 1))
      incoming.set(nextId, (incoming.get(nextId) ?? 1) - 1)
      if ((incoming.get(nextId) ?? 0) <= 0) {
        const next = nodes.find(node => node.id === nextId)
        if (next) queue.push(next)
      }
    }
  }
  nodes.forEach((node, index) => {
    if (!levels.has(node.id)) levels.set(node.id, index)
  })
  return levels
}

function handleEditorShortcut(event: KeyboardEvent) {
  if (!drawerVisible.value || event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement) return
  if (!canEditCurrentFlowRule.value) return
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'c') {
    event.preventDefault()
    copySelectedNode()
  } else if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'v') {
    event.preventDefault()
    pasteNode()
  } else if ((event.ctrlKey || event.metaKey) && event.key === '0') {
    event.preventDefault()
    setCanvasZoom(1)
  } else if (event.key === 'Delete' || event.key === 'Backspace') {
    if (selectedNodeId.value) deleteSelectedNode()
    else if (selectedEdgeId.value) deleteSelectedEdge()
  }
}

async function save() {
  if (!form.value) return
  if (!canSaveCurrentFlowRule.value) {
    ElMessage.warning('当前用户没有保存流程规则权限')
    return
  }
  showErrors.value = true
  if (errors.value.length) return

  saving.value = true
  try {
    const payload = cloneFlowRule(form.value)
    payload.updatedTime = new Date().toISOString()
    if (payload.id) await updateFlowRule(payload.id, payload)
    else await createFlowRule(payload)
    drawerVisible.value = false
    emit('changed')
    await load()
    ElMessage.success('流程规则已保存')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存失败')
  } finally {
    saving.value = false
  }
}

async function remove(rule: FlowRuleDefinition) {
  if (!canDeleteFlowRule.value) {
    ElMessage.warning('当前用户没有删除流程规则权限')
    return
  }
  const confirmed = await ElMessageBox.confirm(`确认删除流程规则「${rule.name}」？`, '删除确认', { type: 'warning' })
    .then(() => true)
    .catch(() => false)
  if (!confirmed) return
  await deleteFlowRule(rule.id)
  emit('changed')
  await load()
  ElMessage.success('流程规则已删除')
}
function findLatestRuntimeEvent() {
  if (!form.value || !props.status) return undefined
  const id = form.value.id || ''
  const compiledId = form.value.compiledRuleId || ''
  const name = form.value.name || ''
  const events = [
    ...(runtimeRuleStatus.value?.recentEvents ?? []),
    ...(props.status.recentEvents ?? [])
  ].filter(event =>
    (!!id && event.ruleId === id) ||
    (!!compiledId && event.ruleId === compiledId) ||
    (!!name && event.ruleName === name))

  return events.sort((left, right) =>
    Date.parse(right.timestamp || '') - Date.parse(left.timestamp || ''))[0]
}

function isConditionLikeNode(node: FlowRuleNode) {
  return [
    'Condition',
    'Threshold',
    'Deadband',
    'RateOfChange',
    'Hysteresis',
    'MultiLevelAlarm',
    'Expression',
    'QualityGate',
    'SlidingWindow',
    'WindowCalculation',
    'Aggregation',
    'Trend',
    'StateMachine',
    'CycleTime',
    'ProcessTakt',
    'AnomalyDetection',
    'TagRelation',
    'ContextGate',
    'Sequence'
  ].includes(node.nodeType)
}

function isActionNode(node: FlowRuleNode) {
  return [
    'MqttPublish',
    'EmailNotify',
    'WebhookCall',
    'DebugProbe'
  ].includes(node.nodeType)
}

function isTerminalActionNode(node: FlowRuleNode) {
  return [
    'MqttPublish',
    'EmailNotify',
    'WebhookCall'
  ].includes(node.nodeType)
}

function cloneNode(node: FlowRuleNode) {
  return JSON.parse(JSON.stringify(node)) as FlowRuleNode
}

function createId() {
  return typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID().replace(/-/g, '')
    : Math.random().toString(16).slice(2) + Date.now().toString(16)
}

function sameText(left: string | null | undefined, right: string | null | undefined) {
  return (left || '').trim().toLowerCase() === (right || '').trim().toLowerCase()
}
</script>

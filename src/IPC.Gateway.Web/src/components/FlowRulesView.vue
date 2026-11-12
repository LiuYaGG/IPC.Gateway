<template>
  <section class="flow-rules-view">
    <div class="rules-status flow-rules-status">
      <div>
        <p class="eyebrow">{{ tx('流程规则引擎') }}</p>
        <h3>{{ status?.isRunning ? tx('运行中') : tx('未运行') }}</h3>
      </div>
      <div class="rules-status__metrics">
        <span>{{ tx('流程') }} {{ rules.length }}</span>
        <span>{{ tx('启用') }} {{ enabledCount }}</span>
        <span>{{ tx('活跃') }} {{ status?.activeRuleCount ?? 0 }}</span>
        <span>{{ tx('评估') }} {{ status?.evaluationCount ?? 0 }}</span>
        <span>{{ tx('待处理') }} {{ status?.pendingInputEventCount ?? 0 }}/{{ status?.pendingActionCount ?? 0 }}</span>
        <span :class="{ 'is-danger': (status?.droppedInputEventCount ?? 0) + (status?.droppedActionCount ?? 0) > 0 }">
          {{ tx('丢弃') }} {{ (status?.droppedInputEventCount ?? 0) + (status?.droppedActionCount ?? 0) }}
        </span>
        <span :class="{ 'is-danger': (status?.actionFailureCount ?? 0) > 0 }">
          {{ tx('动作失败') }} {{ status?.actionFailureCount ?? 0 }}
        </span>
      </div>
      <div class="rules-status-actions">
        <a-button :loading="loading" @click="load">
          <template #icon>
            <span class="ui-icon"><component :is="Refresh" /></span>
          </template>
          {{ tx('刷新') }}
        </a-button>
        <a-button v-if="canCreateFlowRule" type="primary" @click="openCreate">
          <template #icon>
            <span class="ui-icon"><component :is="Plus" /></span>
          </template>
          {{ tx('新增规则引擎') }}
        </a-button>
      </div>
    </div>
    <a-card class="panel-card flow-rule-list" size="small">
    <GatewayTable
      resize-key="components.FlowRulesView.vue.0"
      :data-source="rules"
      :loading="loading"
      :row-key="(_row: any, index: any) => index"
      size="small"
      :pagination="false"
      :columns="
        localizeColumns([
          { key: 'column-0', title: tx('名称'), dataIndex: 'name', width: '180' },
          { key: 'column-1', title: tx('状态'), width: '100' },
          { key: 'column-2', title: tx('模式'), width: '150' },
          { key: 'column-3', title: tx('节点'), width: '90' },
          { key: 'column-4', title: tx('版本'), dataIndex: 'version', width: '90' },
          { key: 'column-5', title: tx('发布版本'), dataIndex: 'publishedVersion', width: '110' },
          {
            key: 'column-6',
            title: tx('生命周期'),
            dataIndex: 'lifecycleState',
            customRender: ({ text }: any) => tx(statusText(text)),
            width: '110'
          },
          { key: 'column-7', title: tx('编译规则'), dataIndex: 'compiledRuleId', width: '160', ellipsis: true },
          { key: 'column-8', title: tx('更新时间'), dataIndex: 'updatedTime', width: '170' },
          { key: 'column-9', title: tx('操作'), width: '170', fixed: 'right' }
        ])
      "
      :scroll="{ x: 1330 }"
    >
      <template #bodyCell="tableCell">
        <template v-if="tableCell.column.key === 'column-1'">
          <template v-for="{ row } in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
            <a-tag :color="tagColor(row.enabled ? 'success' : 'info')">
              {{ row.enabled ? tx('启用') : tx('停用') }}
            </a-tag>
          </template>
        </template>
        <template v-if="tableCell.column.key === 'column-2'">
          <template v-for="{ row } in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
            <a-tag :color="tagColor(row.mode === 'SimpleCompiled' ? 'primary' : 'warning')">
              {{ row.mode === 'SimpleCompiled' ? tx('简单编译') : tx('流程解释') }}
            </a-tag>
          </template>
        </template>
        <template v-if="tableCell.column.key === 'column-3'">
          <template v-for="{ row } in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
            {{ row.nodes?.length ?? 0 }}
          </template>
        </template>
        <template v-if="tableCell.column.key === 'column-9'">
          <template v-for="{ row } in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
            <div class="table-actions">
              <a-button v-if="canEditFlowRule" @click="openEdit(row)" type="link">
                <template #icon>
                  <span class="ui-icon"><component :is="Edit" /></span>
                </template>
                {{ tx('编辑') }}
              </a-button>
              <a-button v-if="canDeleteFlowRule" @click="remove(row)" type="link" danger>
                <template #icon>
                  <span class="ui-icon"><component :is="Delete" /></span>
                </template>
                {{ tx('删除') }}
              </a-button>
            </div>
          </template>
        </template>
      </template>
    </GatewayTable>
    </a-card>
    <a-modal
      centered
      destroy-on-close
      :mask-closable="false"
      v-model:open="drawerVisible"
      class="flow-rule-drawer"
      width="94%"
      :title="tx(form?.id ? tx('编辑规则引擎') : tx('新增规则引擎'))"
    >
      <template v-if="form">
        <div class="flow-rule-formbar">
          <a-form class="flow-rule-meta" layout="vertical">
            <a-form-item :label="tx('规则名称')" :class="{ 'is-error': showErrors && !form.name?.trim() }">
              <gateway-input v-model="form.name" />
            </a-form-item>
            <a-form-item :label="tx('启用')"><a-switch v-model:checked="form.enabled" /></a-form-item>
            <a-form-item :label="tx('说明')"><gateway-input v-model="form.description" /></a-form-item>
          </a-form>
        </div>
        <div class="flow-editor-toolbar">
          <gateway-input
            v-model="flowSearch"
            allow-clear
            :placeholder="tx('搜索节点名称、类型或点位')"
            @keyup.enter="focusNextSearchResult"
          >
            <template #prefix>
              <span class="ui-icon"><Search /></span>
            </template>
          </gateway-input>
          <a-button :disabled="searchNodeIds.length === 0" @click="focusNextSearchResult">
            <template #icon>
              <span class="ui-icon"><component :is="Search" /></span>
            </template>
            {{ searchNodeIds.length || 0 }}
          </a-button>
          <a-button @click="setCanvasZoom(canvasZoom - 0.1)">
            <template #icon>
              <span class="ui-icon"><component :is="ZoomOut" /></span>
            </template>
          </a-button>
          <a-tag :color="tagColor('info')">{{ Math.round(canvasZoom * 100) }}%</a-tag>
          <a-button @click="setCanvasZoom(canvasZoom + 0.1)">
            <template #icon>
              <span class="ui-icon"><component :is="ZoomIn" /></span>
            </template>
          </a-button>
          <a-button @click="fitCanvas">
            <template #icon>
              <span class="ui-icon"><component :is="FullScreen" /></span>
            </template>
            {{ tx('适配') }}
          </a-button>
          <a-button v-if="canEditCurrentFlowRule" @click="autoLayout">
            <template #icon>
              <span class="ui-icon"><component :is="Rank" /></span>
            </template>
            {{ tx('自动布局') }}
          </a-button>
          <a-button v-if="canEditCurrentFlowRule" :disabled="!selectedNode" @click="copySelectedNode">
            <template #icon>
              <span class="ui-icon"><component :is="CopyDocument" /></span>
            </template>
            {{ tx('复制') }}
          </a-button>
          <a-button v-if="canEditCurrentFlowRule" :disabled="!copiedNode" @click="pasteNode">
            <template #icon>
              <span class="ui-icon"><component :is="DocumentCopy" /></span>
            </template>
            {{ tx('粘贴') }}
          </a-button>
          <a-switch
            v-if="canDebugFlowRule"
            v-model:checked="debugHighlight"
            :checked-children="tx('调试')"
            :un-checked-children="tx('调试')"
          />
        </div>
        <div class="flow-editor-shell">
          <div class="flow-palette">
            <div class="flow-palette__header">
              <strong>{{ tx('节点库') }}</strong>
              <a-tag :color="tagColor('info')">{{ paletteGroups.length }} {{ tx('组') }}</a-tag>
            </div>
            <a-collapse v-model:active-key="paletteActiveGroups" class="flow-palette-collapse">
              <a-collapse-panel v-for="group in paletteGroups" :key="group.title">
                <template #header>
                  <span class="flow-palette__title">
                    {{ tx(group.title) }}
                    <small>{{ group.nodes.length }}</small>
                  </span>
                </template>
                <div class="flow-palette__group">
                  <template v-if="canEditCurrentFlowRule">
                    <a-button
                      v-for="item in group.nodes"
                      :key="item.script ? `${item.type}:${item.script.id}` : item.type"
                      class="flow-palette__button"
                      @click="addNode(item)"
                    >
                      <template #icon>
                        <span class="ui-icon"><component :is="Plus" /></span>
                      </template>
                      {{ tx(item.label) }}
                    </a-button>
                  </template>
                </div>
              </a-collapse-panel>
            </a-collapse>
            <a-divider />
            <a-button v-if="canEditCurrentFlowRule" :disabled="!selectedEdgeId" @click="deleteSelectedEdge" danger>
              <template #icon>
                <span class="ui-icon"><component :is="Delete" /></span>
              </template>
              {{ tx('删除连线') }}
            </a-button>
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
            @connect="
              (sourceNodeId, targetNodeId) => canEditCurrentFlowRule && connectNodes(sourceNodeId, targetNodeId)
            "
            @zoom-change="setCanvasZoom"
          />
          <FlowRuleProperties
            :node="selectedNode"
            :project="project"
            :value-scripts="valueScripts"
            @delete="canEditCurrentFlowRule && deleteSelectedNode()"
          />
        </div>
        <a-alert
          v-if="showErrors && errors.length"
          type="error"
          :closable="false"
          class="flow-errors"
          :message="tx(errors.join('；'))"
        />
      </template>
      <template #footer>
        <a-button @click="drawerVisible = false">{{ tx('取消') }}</a-button>
        <a-button v-if="canSaveCurrentFlowRule" type="primary" :loading="saving" @click="save">
          {{ tx('保存') }}
        </a-button>
      </template>
    </a-modal>
  </section>
</template>

<script setup lang="ts">
import { formatMessage } from '../i18n'
import { notify } from '../utils/ui'
import { tx, localizeColumns } from '../i18n'
import { statusText } from '../utils/display'
import { tagColor, confirmAction } from '../utils/ui'
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'

import {
  CopyOutlined as CopyDocument,
  DeleteOutlined as Delete,
  SnippetsOutlined as DocumentCopy,
  EditOutlined as Edit,
  FullscreenOutlined as FullScreen,
  PlusOutlined as Plus,
  PartitionOutlined as Rank,
  ReloadOutlined as Refresh,
  SearchOutlined as Search,
  ZoomInOutlined as ZoomIn,
  ZoomOutOutlined as ZoomOut
} from '@ant-design/icons-vue'
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
import { loadValueTransformCatalog, type ValueTransformCatalogItem } from '../scriptingApi'
import FlowRuleCanvas from './FlowRuleCanvas.vue'
import FlowRuleProperties from './FlowRuleProperties.vue'
import {
  cloneFlowRule,
  createEdge,
  createFlowNode,
  createFlowRuleTemplate,
  createValueScriptFlowNode,
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
const valueScripts = ref<ValueTransformCatalogItem[]>([])

type PaletteNode = { type: string; label: string; script?: ValueTransformCatalogItem }

const paletteActiveGroups = ref<string[]>(FLOW_NODE_GROUPS[0]?.title ? [FLOW_NODE_GROUPS[0].title] : [])
const paletteGroups = computed(() => {
  const groups = FLOW_NODE_GROUPS.map((group) => ({
    title: group.title,
    nodes: group.nodes.map((item) => ({ ...item }) as PaletteNode)
  }))
  for (const script of valueScripts.value.filter((item) => item.scope !== 'TagCleaning')) {
    const title = script.nodeCategory?.trim() || '处理'
    let group = groups.find((item) => item.title === title)
    if (!group) {
      group = { title, nodes: [] }
      groups.push(group)
    }
    group.nodes.push({ type: 'ValueScript', label: script.name, script })
  }
  return groups
})
const enabledCount = computed(() => rules.value.filter((rule) => rule.enabled).length)
const canCreateFlowRule = computed(() => hasPermission(PERMISSIONS.flowRulesCreate))
const canEditFlowRule = computed(() => hasPermission(PERMISSIONS.flowRulesEdit))
const canDeleteFlowRule = computed(() => hasPermission(PERMISSIONS.flowRulesDelete))
const canDebugFlowRule = computed(() => hasPermission(PERMISSIONS.flowRulesDebug))
const canEditCurrentFlowRule = computed(() =>
  form.value ? (form.value.id ? canEditFlowRule.value : canCreateFlowRule.value) : false
)
const canSaveCurrentFlowRule = computed(() => canEditCurrentFlowRule.value)
const selectedNode = computed(() => form.value?.nodes.find((node) => node.id === selectedNodeId.value) ?? null)
const errors = computed(() => (form.value ? validateFlowRule(form.value) : []))
const searchNodeIds = computed(() => {
  const keyword = flowSearch.value.trim().toLowerCase()
  if (!keyword || !form.value) return []
  return form.value.nodes
    .filter((node) =>
      [node.label, node.nodeType, node.tagName, node.pointCode, node.deviceName, node.groupName].some((value) =>
        (value || '').toLowerCase().includes(keyword)
      )
    )
    .map((node) => node.id)
})
const runtimeRuleStatus = computed(() => {
  if (!form.value || !props.status?.rules) return undefined
  const id = form.value.id || ''
  const compiledId = form.value.compiledRuleId || ''
  const name = form.value.name || ''
  return props.status.rules.find(
    (rule) =>
      (!!id && rule.ruleId === id) || (!!compiledId && rule.ruleId === compiledId) || (!!name && rule.ruleName === name)
  )
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
      if (
        sameText(node.channelId, recent.channelId) &&
        sameText(node.deviceId, recent.deviceId) &&
        sameText(node.groupId, recent.groupId) &&
        sameText(node.tagId, recent.tagId)
      )
        ids.add(node.id)
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

watch(
  () => props.project,
  (value) => {
    if (!drawerVisible.value) rules.value = (value?.flowRules ?? []).map(cloneFlowRule)
  },
  { immediate: true }
)

watch([drawerVisible, saving], () => emit('editing-state', drawerVisible.value || saving.value), { immediate: true })

onMounted(async () => {
  try {
    valueScripts.value = (await loadValueTransformCatalog()).data
  } catch {
    valueScripts.value = []
  }
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
    notify.warning(error instanceof Error ? error.message : '规则引擎加载失败')
  } finally {
    loading.value = false
  }
}

function openCreate() {
  if (!canCreateFlowRule.value) {
    notify.warning('当前用户没有新增规则引擎权限')
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
    notify.warning('当前用户没有编辑规则引擎权限')
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

function addNode(item: PaletteNode) {
  if (!canEditCurrentFlowRule.value) return
  if (!form.value) return
  const node = item.script
    ? createValueScriptFlowNode(item.script, form.value.nodes.length)
    : createFlowNode(item.type, form.value.nodes.length)
  form.value.nodes.push(node)
  selectedNodeId.value = node.id
  selectedEdgeId.value = ''
  nextTick(() => canvasComponent.value?.centerNode(node.id))
}

function deleteSelectedNode() {
  if (!canEditCurrentFlowRule.value) return
  if (!form.value || !selectedNodeId.value) return
  form.value.nodes = form.value.nodes.filter((node) => node.id !== selectedNodeId.value)
  form.value.edges = form.value.edges.filter(
    (edge) => edge.sourceNodeId !== selectedNodeId.value && edge.targetNodeId !== selectedNodeId.value
  )
  selectedNodeId.value = form.value.nodes[0]?.id ?? ''
  selectedEdgeId.value = ''
}

function moveNode(nodeId: string, x: number, y: number) {
  if (!canEditCurrentFlowRule.value) return
  const node = form.value?.nodes.find((item) => item.id === nodeId)
  if (!node) return
  node.x = Math.round(x)
  node.y = Math.round(y)
}

function connectNodes(sourceNodeId: string, targetNodeId: string) {
  if (!canEditCurrentFlowRule.value) return
  if (!form.value || sourceNodeId === targetNodeId) return
  const validation = validateConnection(sourceNodeId, targetNodeId)
  if (validation) {
    notify.warning(validation)
    return
  }
  const exists = form.value.edges.some(
    (edge) => edge.sourceNodeId === sourceNodeId && edge.targetNodeId === targetNodeId
  )
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
  form.value.edges = form.value.edges.filter((edge) => edge.id !== selectedEdgeId.value)
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
  notify.success('节点已复制')
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
  const source = form.value.nodes.find((node) => node.id === sourceNodeId)
  const target = form.value.nodes.find((node) => node.id === targetNodeId)
  if (!source || !target) return '连线节点不存在'
  if (sourceNodeId === targetNodeId) return '不能连接到自身'
  if (form.value.edges.some((edge) => edge.sourceNodeId === sourceNodeId && edge.targetNodeId === targetNodeId))
    return '这条连线已经存在'
  if (target.nodeType === 'TagInput') return '标签输入节点只能作为起点'
  if (isTerminalActionNode(source)) return '通知和发布动作不能再连接到下游节点'
  if (['Logic', 'Sequence'].includes(source.nodeType) && ['Logic', 'Sequence'].includes(target.nodeType))
    return '汇合节点之间不能直接相连'
  if (['Logic', 'Sequence'].includes(target.nodeType) && !['Condition', 'Threshold'].includes(source.nodeType)) {
    return 'AND/OR 和顺序/时序节点当前只接受基础条件'
  }
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
  const ids = new Set(nodes.map((node) => node.id))
  const incoming = new Map<string, number>()
  const outgoing = new Map<string, string[]>()
  for (const node of nodes) incoming.set(node.id, 0)
  for (const edge of edges) {
    if (!ids.has(edge.sourceNodeId) || !ids.has(edge.targetNodeId)) continue
    incoming.set(edge.targetNodeId, (incoming.get(edge.targetNodeId) ?? 0) + 1)
    if (!outgoing.has(edge.sourceNodeId)) outgoing.set(edge.sourceNodeId, [])
    outgoing.get(edge.sourceNodeId)!.push(edge.targetNodeId)
  }

  const queue = nodes.filter((node) => (incoming.get(node.id) ?? 0) === 0)
  const levels = new Map<string, number>()
  for (const node of queue) levels.set(node.id, 0)
  while (queue.length) {
    const current = queue.shift()!
    const level = levels.get(current.id) ?? 0
    for (const nextId of outgoing.get(current.id) ?? []) {
      levels.set(nextId, Math.max(levels.get(nextId) ?? 0, level + 1))
      incoming.set(nextId, (incoming.get(nextId) ?? 1) - 1)
      if ((incoming.get(nextId) ?? 0) <= 0) {
        const next = nodes.find((node) => node.id === nextId)
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
  if (!drawerVisible.value || event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement)
    return
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
    notify.warning('当前用户没有保存规则引擎权限')
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
    notify.success('规则引擎已保存')
  } catch (error) {
    notify.error(error instanceof Error ? error.message : '保存失败')
  } finally {
    saving.value = false
  }
}

async function remove(rule: FlowRuleDefinition) {
  if (!canDeleteFlowRule.value) {
    notify.warning('当前用户没有删除规则引擎权限')
    return
  }
  const confirmed = await confirmAction(formatMessage('确认删除规则引擎「{0}」？', [rule.name]), '删除确认', {
    type: 'warning'
  })
    .then(() => true)
    .catch(() => false)
  if (!confirmed) return
  await deleteFlowRule(rule.id)
  emit('changed')
  await load()
  notify.success('规则引擎已删除')
}
function findLatestRuntimeEvent() {
  if (!form.value || !props.status) return undefined
  const id = form.value.id || ''
  const compiledId = form.value.compiledRuleId || ''
  const name = form.value.name || ''
  const events = [...(runtimeRuleStatus.value?.recentEvents ?? []), ...(props.status.recentEvents ?? [])].filter(
    (event) =>
      (!!id && event.ruleId === id) ||
      (!!compiledId && event.ruleId === compiledId) ||
      (!!name && event.ruleName === name)
  )

  return events.sort((left, right) => Date.parse(right.timestamp || '') - Date.parse(left.timestamp || ''))[0]
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
    'NoChangeTimeout',
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
  return ['MqttPublish', 'EmailNotify', 'WebhookCall', 'DebugProbe'].includes(node.nodeType)
}

function isTerminalActionNode(node: FlowRuleNode) {
  return ['MqttPublish', 'EmailNotify', 'WebhookCall'].includes(node.nodeType)
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

<style scoped>
.flow-editor-shell {
  grid-template-columns: clamp(180px, 15vw, 240px) minmax(0, 1fr) clamp(320px, 28vw, 460px);
  height: clamp(360px, calc(100dvh - 330px), 820px);
  min-height: 0;
  min-width: 0;
}

.flow-editor-shell > .flow-palette,
.flow-editor-shell > .flow-canvas,
.flow-editor-shell > .flow-properties {
  width: 100%;
  height: 100%;
  min-width: 0;
  min-height: 0;
  max-height: none;
  overflow: auto;
}

@media (max-width: 900px) {
  .flow-editor-shell {
    grid-template-columns: minmax(0, 1fr);
    height: auto;
  }

  .flow-editor-shell > .flow-palette,
  .flow-editor-shell > .flow-properties {
    height: auto;
    max-height: 60dvh;
  }

  .flow-editor-shell > .flow-canvas {
    height: 50dvh;
    min-height: 280px;
  }
}
</style>

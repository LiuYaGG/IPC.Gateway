<template>
  <div
    ref="canvasRef"
    :class="['flow-canvas', { 'is-panning': panning }]"
    @scroll="updateViewport"
    @wheel="zoomFromWheel"
    @pointerdown.self="startPan"
  >
    <div
      class="flow-canvas__viewport"
      :style="{ width: `${scaledWidth}px`, height: `${scaledHeight}px` }"
    >
      <div
        class="flow-canvas__stage"
        :style="stageStyle"
        @pointerdown.self="startPan"
      >
        <svg
          class="flow-canvas__links"
          :viewBox="`0 0 ${canvasWidth} ${canvasHeight}`"
          @pointerdown.self="startPan"
        >
          <path
            v-for="edge in visibleEdges"
            :key="edge.id"
            :class="['flow-edge', {
              'is-selected': edge.id === selectedEdgeId,
              'is-highlighted': highlightedEdgeIds.includes(edge.id)
            }]"
            :d="edge.path"
            @pointerdown.stop="$emit('select-edge', edge.id)"
          />
          <path
            v-if="draftPath"
            :class="['flow-edge', 'flow-edge--draft', { 'is-invalid': draftInvalid }]"
            :d="draftPath"
          />
        </svg>

        <div
          v-for="node in nodes"
          :key="node.id"
          role="button"
          tabindex="0"
          :class="[
            'flow-node',
            `flow-node--${node.nodeType.toLowerCase()}`,
            {
              'is-selected': node.id === selectedId,
              'is-dragging': draggingNodeId === node.id,
              'is-highlighted': highlightedNodeIds.includes(node.id),
              'is-search-hit': searchNodeIds.includes(node.id)
            }
          ]"
          :style="{ left: `${node.x}px`, top: `${node.y}px` }"
          @pointerdown="startNodeDrag(node, $event)"
          @keydown.enter="$emit('select', node.id)"
        >
          <button
            type="button"
            class="flow-port flow-port--in"
            title="连接到此节点"
            @pointerup.stop="finishConnect(node.id)"
            @pointerdown.stop
          />
          <span>{{ flowNodeTypeLabel(node.nodeType) }}</span>
          <strong>{{ nodeDisplayName(node) }}</strong>
          <small>{{ nodeFooter(node) }}</small>
          <button
            type="button"
            class="flow-port flow-port--out"
            title="从此节点拉线"
            @pointerdown.stop="startConnect(node, $event)"
          />
        </div>

        <el-empty v-if="nodes.length === 0" description="暂无流程节点" />
      </div>
    </div>

    <div class="flow-minimap" @pointerdown="jumpFromMiniMap">
      <svg :viewBox="`0 0 ${canvasWidth} ${canvasHeight}`">
        <path
          v-for="edge in visibleEdges"
          :key="`mini-${edge.id}`"
          class="flow-minimap__edge"
          :d="edge.path"
        />
        <rect
          v-for="node in nodes"
          :key="`mini-node-${node.id}`"
          :class="['flow-minimap__node', { 'is-highlighted': highlightedNodeIds.includes(node.id) }]"
          :x="node.x"
          :y="node.y"
          :width="nodeWidth"
          :height="nodeHeight"
          rx="8"
        />
        <rect
          class="flow-minimap__viewport"
          :x="miniViewport.x"
          :y="miniViewport.y"
          :width="miniViewport.width"
          :height="miniViewport.height"
          rx="10"
        />
      </svg>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { FlowRuleEdge, FlowRuleNode } from '../api'
import { flowNodeTypeLabel, nodeDisplayName } from '../utils/flowRules'

const canvasWidth = 1400
const canvasHeight = 820
const nodeWidth = 150
const nodeHeight = 84

const props = defineProps<{
  nodes: FlowRuleNode[]
  edges: FlowRuleEdge[]
  selectedId: string
  selectedEdgeId: string
  zoom: number
  highlightedNodeIds?: string[]
  searchNodeIds?: string[]
}>()

const emit = defineEmits<{
  select: [nodeId: string]
  'select-edge': [edgeId: string]
  clear: []
  move: [nodeId: string, x: number, y: number]
  connect: [sourceNodeId: string, targetNodeId: string]
  'zoom-change': [zoom: number]
}>()

const canvasRef = ref<HTMLElement | null>(null)
const draggingNodeId = ref('')
const connectingNodeId = ref('')
const draftPoint = ref<{ x: number; y: number } | null>(null)
const panning = ref(false)
const viewport = ref({ x: 0, y: 0, width: canvasWidth, height: canvasHeight })
let dragOffset = { x: 0, y: 0 }
let panStart = { x: 0, y: 0, scrollLeft: 0, scrollTop: 0 }
let panMoved = false

const zoomValue = computed(() => clamp(Number(props.zoom) || 1, 0.35, 1.8))
const scaledWidth = computed(() => canvasWidth * zoomValue.value)
const scaledHeight = computed(() => canvasHeight * zoomValue.value)
const stageStyle = computed(() => ({
  width: `${canvasWidth}px`,
  height: `${canvasHeight}px`,
  transform: `scale(${zoomValue.value})`
}))
const highlightedNodeIds = computed(() => props.highlightedNodeIds ?? [])
const searchNodeIds = computed(() => props.searchNodeIds ?? [])

const visibleEdges = computed(() => (props.edges ?? [])
  .map(edge => {
    const source = findNode(edge.sourceNodeId)
    const target = findNode(edge.targetNodeId)
    if (!source || !target) return null
    return {
      id: edge.id,
      sourceNodeId: edge.sourceNodeId,
      targetNodeId: edge.targetNodeId,
      path: buildPath(source.x + nodeWidth, source.y + nodeHeight / 2, target.x, target.y + nodeHeight / 2)
    }
  })
  .filter(Boolean) as Array<{ id: string; sourceNodeId: string; targetNodeId: string; path: string }>)

const highlightedEdgeIds = computed(() => {
  const ids = new Set(highlightedNodeIds.value)
  if (!ids.size) return []
  return visibleEdges.value
    .filter(edge => ids.has(edge.sourceNodeId) && ids.has(edge.targetNodeId))
    .map(edge => edge.id)
})

const draftPath = computed(() => {
  if (!connectingNodeId.value || !draftPoint.value) return ''
  const source = findNode(connectingNodeId.value)
  if (!source) return ''
  return buildPath(source.x + nodeWidth, source.y + nodeHeight / 2, draftPoint.value.x, draftPoint.value.y)
})

const draftInvalid = computed(() => {
  if (!connectingNodeId.value || !draftPoint.value) return false
  const target = findNodeAtPoint(draftPoint.value.x, draftPoint.value.y)
  return !!target && target.id === connectingNodeId.value
})

const miniViewport = computed(() => ({
  x: viewport.value.x,
  y: viewport.value.y,
  width: Math.min(canvasWidth, viewport.value.width),
  height: Math.min(canvasHeight, viewport.value.height)
}))

watch(() => props.zoom, () => nextTick(updateViewport))
watch(() => props.nodes.length, () => nextTick(updateViewport))

onMounted(() => {
  updateViewport()
})

onBeforeUnmount(() => {
  window.removeEventListener('pointermove', dragNode)
  window.removeEventListener('pointerup', stopNodeDrag)
  window.removeEventListener('pointermove', moveDraftLine)
  window.removeEventListener('pointerup', cancelConnect)
  window.removeEventListener('pointermove', movePan)
  window.removeEventListener('pointerup', stopPan)
})

function startNodeDrag(node: FlowRuleNode, event: PointerEvent) {
  emit('select', node.id)
  draggingNodeId.value = node.id
  const point = toCanvasPoint(event)
  dragOffset = {
    x: point.x - node.x,
    y: point.y - node.y
  }
  window.addEventListener('pointermove', dragNode)
  window.addEventListener('pointerup', stopNodeDrag)
}

function dragNode(event: PointerEvent) {
  if (!draggingNodeId.value) return
  const point = toCanvasPoint(event)
  const x = clamp(point.x - dragOffset.x, 12, canvasWidth - nodeWidth - 12)
  const y = clamp(point.y - dragOffset.y, 12, canvasHeight - nodeHeight - 12)
  emit('move', draggingNodeId.value, x, y)
}

function stopNodeDrag() {
  draggingNodeId.value = ''
  window.removeEventListener('pointermove', dragNode)
  window.removeEventListener('pointerup', stopNodeDrag)
}

function startConnect(node: FlowRuleNode, event: PointerEvent) {
  emit('select', node.id)
  connectingNodeId.value = node.id
  draftPoint.value = toCanvasPoint(event)
  window.addEventListener('pointermove', moveDraftLine)
  window.addEventListener('pointerup', cancelConnect)
}

function moveDraftLine(event: PointerEvent) {
  if (!connectingNodeId.value) return
  draftPoint.value = toCanvasPoint(event)
}

function finishConnect(targetNodeId: string) {
  const sourceNodeId = connectingNodeId.value
  if (sourceNodeId && sourceNodeId !== targetNodeId) {
    emit('connect', sourceNodeId, targetNodeId)
  }
  cancelConnect()
}

function cancelConnect() {
  connectingNodeId.value = ''
  draftPoint.value = null
  window.removeEventListener('pointermove', moveDraftLine)
  window.removeEventListener('pointerup', cancelConnect)
}

function startPan(event: PointerEvent) {
  if (draggingNodeId.value || connectingNodeId.value) return
  const element = canvasRef.value
  if (!element) {
    clearSelection()
    return
  }
  event.preventDefault()
  panning.value = true
  panMoved = false
  panStart = {
    x: event.clientX,
    y: event.clientY,
    scrollLeft: element.scrollLeft,
    scrollTop: element.scrollTop
  }
  window.addEventListener('pointermove', movePan)
  window.addEventListener('pointerup', stopPan)
}

function movePan(event: PointerEvent) {
  if (!panning.value) return
  const element = canvasRef.value
  if (!element) return
  const dx = event.clientX - panStart.x
  const dy = event.clientY - panStart.y
  if (Math.abs(dx) > 2 || Math.abs(dy) > 2) panMoved = true
  element.scrollLeft = panStart.scrollLeft - dx
  element.scrollTop = panStart.scrollTop - dy
  updateViewport()
}

function stopPan() {
  if (!panning.value) return
  panning.value = false
  window.removeEventListener('pointermove', movePan)
  window.removeEventListener('pointerup', stopPan)
  if (!panMoved) clearSelection()
}

function clearSelection() {
  emit('clear')
}

function findNode(nodeId: string) {
  return props.nodes.find(node => node.id === nodeId)
}

function findNodeAtPoint(x: number, y: number) {
  return props.nodes.find(node =>
    x >= node.x &&
    x <= node.x + nodeWidth &&
    y >= node.y &&
    y <= node.y + nodeHeight)
}

function toCanvasPoint(event: PointerEvent) {
  const element = canvasRef.value
  if (!element) return { x: event.clientX, y: event.clientY }
  const rect = element.getBoundingClientRect()
  return {
    x: (event.clientX - rect.left + element.scrollLeft) / zoomValue.value,
    y: (event.clientY - rect.top + element.scrollTop) / zoomValue.value
  }
}

function updateViewport() {
  const element = canvasRef.value
  if (!element) return
  viewport.value = {
    x: element.scrollLeft / zoomValue.value,
    y: element.scrollTop / zoomValue.value,
    width: element.clientWidth / zoomValue.value,
    height: element.clientHeight / zoomValue.value
  }
}

function zoomFromWheel(event: WheelEvent) {
  if (event.ctrlKey || event.metaKey || event.shiftKey) return
  const element = canvasRef.value
  if (!element) return

  event.preventDefault()
  const rect = element.getBoundingClientRect()
  const currentZoom = zoomValue.value
  const delta = event.deltaY > 0 ? -0.08 : 0.08
  const nextZoom = clamp(Number((currentZoom + delta).toFixed(2)), 0.35, 1.8)
  if (nextZoom === currentZoom) return

  const pointerX = event.clientX - rect.left
  const pointerY = event.clientY - rect.top
  const canvasX = (pointerX + element.scrollLeft) / currentZoom
  const canvasY = (pointerY + element.scrollTop) / currentZoom

  emit('zoom-change', nextZoom)
  nextTick(() => {
    element.scrollLeft = Math.max(0, canvasX * nextZoom - pointerX)
    element.scrollTop = Math.max(0, canvasY * nextZoom - pointerY)
    updateViewport()
  })
}

function fitView() {
  const element = canvasRef.value
  if (!element) return
  const bounds = calculateBounds()
  const nextZoom = clamp(Math.min(
    element.clientWidth / Math.max(bounds.width + 96, 320),
    element.clientHeight / Math.max(bounds.height + 96, 220)
  ), 0.35, 1.4)
  emit('zoom-change', Number(nextZoom.toFixed(2)))
  nextTick(() => {
    element.scrollLeft = Math.max(0, (bounds.x - 48) * nextZoom)
    element.scrollTop = Math.max(0, (bounds.y - 48) * nextZoom)
    updateViewport()
  })
}

function centerNode(nodeId: string) {
  const node = findNode(nodeId)
  const element = canvasRef.value
  if (!node || !element) return
  element.scrollLeft = Math.max(0, (node.x + nodeWidth / 2) * zoomValue.value - element.clientWidth / 2)
  element.scrollTop = Math.max(0, (node.y + nodeHeight / 2) * zoomValue.value - element.clientHeight / 2)
  updateViewport()
}

function jumpFromMiniMap(event: PointerEvent) {
  const element = canvasRef.value
  const target = event.currentTarget as HTMLElement
  if (!element || !target) return
  const rect = target.getBoundingClientRect()
  const x = ((event.clientX - rect.left) / rect.width) * canvasWidth
  const y = ((event.clientY - rect.top) / rect.height) * canvasHeight
  element.scrollLeft = Math.max(0, x * zoomValue.value - element.clientWidth / 2)
  element.scrollTop = Math.max(0, y * zoomValue.value - element.clientHeight / 2)
  updateViewport()
}

function calculateBounds() {
  if (!props.nodes.length) return { x: 0, y: 0, width: canvasWidth, height: canvasHeight }
  const left = Math.min(...props.nodes.map(node => node.x))
  const top = Math.min(...props.nodes.map(node => node.y))
  const right = Math.max(...props.nodes.map(node => node.x + nodeWidth))
  const bottom = Math.max(...props.nodes.map(node => node.y + nodeHeight))
  return { x: left, y: top, width: right - left, height: bottom - top }
}

function buildPath(x1: number, y1: number, x2: number, y2: number) {
  const distance = Math.max(80, Math.abs(x2 - x1) * 0.5)
  return `M ${x1} ${y1} C ${x1 + distance} ${y1}, ${x2 - distance} ${y2}, ${x2} ${y2}`
}

function clamp(value: number, min: number, max: number) {
  return Math.min(max, Math.max(min, value))
}

function nodeFooter(node: FlowRuleNode) {
  if (node.nodeType === 'TagInput') return node.pointCode || node.tagName || '未选择标签'
  if (node.nodeType === 'Hysteresis') return `${node.hysteresisMode === 'Low' ? '低限' : '高限'} ${node.hysteresisOnValue}/${node.hysteresisOffValue}`
  if (node.nodeType === 'MultiLevelAlarm') return `${(node.alarmLevels ?? []).length} 个级别`
  if (node.nodeType === 'Expression') return node.expression || '{value} > 0'
  if (node.nodeType === 'WindowCalculation') return `${node.windowStatistic || 'Average'} ${node.operator} ${node.compareValue}`
  if (node.nodeType === 'Aggregation') return `${node.aggregationStatistic || 'Average'} ${node.operator} ${node.compareValue}`
  if (node.nodeType === 'Trend') return `${node.trendMode || 'Slope'} ${node.trendWindowSeconds || 300}s`
  if (node.nodeType === 'CycleTime') return `${node.cycleStartValue || '1'} -> ${node.cycleEndValue || '0'}`
  if (node.nodeType === 'ProcessTakt') return `${node.taktTargetSeconds || 60}s ±${node.taktTolerancePercent ?? 10}%`
  if (node.nodeType === 'AnomalyDetection') return `${node.anomalyMode || 'ZScore'} ${node.anomalyThreshold || 3}`
  if (node.nodeType === 'ModelInference') return `${node.modelPurpose === 'QualityPrediction' ? '质量预测' : '异常预警'} ${node.modelOperator || '>='} ${node.modelThreshold ?? 0.5}`
  if (node.nodeType === 'TagRelation') return node.relatedPointCode || node.relatedTagName || '关联标签'
  if (node.nodeType === 'ContextGate') return `${node.contextName || '上下文'}=${node.contextExpectedValue || ''}`
  if (node.nodeType === 'AlarmLifecycle') return `${node.alarmSeverity || 'Warning'} 生命周期`
  if (node.nodeType === 'ActionPolicy') return `${node.actionCooldownSeconds || 0}s 冷却`
  if (node.nodeType === 'DebugProbe') return node.debugLabel || '调试跟踪'
  if (node.nodeType === 'Transform') return `${node.transformUseAbsolute ? 'abs, ' : ''}x${node.transformMultiplier ?? 1} ${formatOffset(node.transformOffset ?? 0)}`
  if (node.nodeType === 'Function') return node.transformExpression || '{value}'
  if (node.nodeType === 'ValueScript') return `v${node.valueScriptVersion || 0} · ${node.valueScriptInputDataType || '?'} → ${node.valueScriptOutputDataType || '?'}`
  if (node.nodeType === 'Logic') return node.logicalOperator || 'And'
  if (node.nodeType === 'Duration') return `${node.durationSeconds || 0}s`
  if (node.nodeType === 'Sequence') return `${node.sequenceWindowSeconds || 60}s 窗口`
  if (node.nodeType === 'MqttPublish') return node.publishToMqtt ? `QoS ${node.publishQos}` : '未发布'
  if (node.nodeType === 'EmailNotify') return node.emailTo || '未配置收件人'
  if (node.nodeType === 'WebhookCall') return node.webhookUrl || (node.webhookMethod || 'POST')
  return node.pointCode || node.tagName || `${node.operator} ${node.compareValue}`
}

function formatOffset(offset: number) {
  if (!offset) return '+0'
  return offset > 0 ? `+${offset}` : `${offset}`
}

defineExpose({ fitView, centerNode })
</script>


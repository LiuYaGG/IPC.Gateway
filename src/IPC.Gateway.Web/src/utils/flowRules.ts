import type { FlowRuleDefinition, FlowRuleEdge, FlowRuleNode } from '../api'
import type { ValueTransformCatalogItem } from '../scriptingApi'
import type { TagSelection } from './tagSelection'
import { qualityText } from './display'

const FLOW_OPTION_LABELS = {
  qualityOperator: { In: '包含', NotIn: '排除', Include: '包含', Exclude: '排除' },
  statistic: { Average: '平均值', Min: '最小值', Max: '最大值', Sum: '求和', Count: '计数', StdDev: '标准差', First: '首值', Last: '末值', Range: '极差' },
  trend: { Slope: '斜率变化', Rising: '上升', Falling: '下降', Stable: '稳定' },
  anomaly: { ZScore: 'Z 分数', Deviation: '均值偏差', Spike: '突变' },
  severity: { Info: '信息', Warning: '警告', Critical: '严重' },
  logic: { And: '全部满足（AND）', Or: '任一满足（OR）' },
  hysteresis: { Low: '低限', High: '高限' },
  purpose: { QualityPrediction: '质量预测', DeviceAnomaly: '设备异常预警' }
} satisfies Record<string, Record<string, string>>

export function flowOptionLabel(
  kind: keyof typeof FLOW_OPTION_LABELS,
  value: unknown,
  translate: (label: string) => string = (label) => label
) {
  if (value === undefined || value === null || value === '') return ''
  const text = String(value)
  const labels: Record<string, string> = FLOW_OPTION_LABELS[kind]
  return labels[text] ? translate(labels[text]) : text
}

export function flowQualityValuesLabel(value: string | null | undefined, translate: (label: string) => string = (label) => label) {
  return (value ?? '').split(/[,\s;|]+/).filter(Boolean).map((quality) => translate(qualityText(quality))).join(', ')
}

export const FLOW_NODE_GROUPS = [
  {
    title: '输入',
    nodes: [{ type: 'TagInput', label: '标签输入' }]
  },
  {
    title: '判断',
    nodes: [
      { type: 'Condition', label: '基础条件' },
      { type: 'Hysteresis', label: '滞回规则' },
      { type: 'MultiLevelAlarm', label: '多级告警' },
      { type: 'Expression', label: '表达式规则' }
    ]
  },
  {
    title: '处理',
    nodes: [
      { type: 'Transform', label: '数据处理' },
      { type: 'Function', label: '函数节点' }
    ]
  },
  {
    title: '组合',
    nodes: [
      { type: 'Logic', label: 'AND/OR' },
      { type: 'Duration', label: '持续确认' },
      { type: 'Sequence', label: '顺序/时序' }
    ]
  },
  {
    title: '动作',
    nodes: [
      { type: 'MqttPublish', label: 'MQTT 发布' },
      { type: 'EmailNotify', label: '邮件通知' },
      { type: 'WebhookCall', label: 'Webhook' }
    ]
  }
]

FLOW_NODE_GROUPS[0]?.nodes.push({ type: 'QualityGate', label: '质量门控' })
FLOW_NODE_GROUPS[1]?.nodes.push(
  { type: 'SlidingWindow', label: '窗口统计' },
  { type: 'WindowCalculation', label: '窗口计算' },
  { type: 'Aggregation', label: '聚合计算' },
  { type: 'Trend', label: '趋势判断' },
  { type: 'NoChangeTimeout', label: '连续无变化超时' },
  { type: 'StateMachine', label: '状态机' }
)

FLOW_NODE_GROUPS[1]?.nodes.push(
  { type: 'CycleTime', label: '周期时间' },
  { type: 'ProcessTakt', label: '工艺节拍' },
  { type: 'AnomalyDetection', label: '异常检测' },
  { type: 'ModelInference', label: 'ONNX 推理' },
  { type: 'TagRelation', label: '标签关联' },
  { type: 'ContextGate', label: '上下文门控' }
)
FLOW_NODE_GROUPS[3]?.nodes.push(
  { type: 'AlarmLifecycle', label: '告警生命周期' },
  { type: 'ActionPolicy', label: '动作策略' }
)
FLOW_NODE_GROUPS[4]?.nodes.push({ type: 'DebugProbe', label: '调试探针' })
export const FLOW_NODE_TYPES = FLOW_NODE_GROUPS.flatMap((group) => group.nodes)
const FLOW_NODE_TYPE_LABELS: Record<string, string> = {
  ...Object.fromEntries(FLOW_NODE_TYPES.map((node) => [node.type, node.label])),
  ValueScript: '值处理脚本',
  Threshold: '阈值上下限',
  Deadband: '死区变化',
  RateOfChange: '变化率'
}
const LEGACY_DEFAULT_NODE_LABELS: Record<string, string> = {
  Condition: '条件',
  CycleTime: 'Cycle Time',
  TagRelation: 'Tag Relation',
  ContextGate: 'Context Gate',
  AlarmLifecycle: 'Alarm Lifecycle',
  ActionPolicy: 'Action Policy',
  DebugProbe: 'Debug Probe'
}
const CONDITION_NODE_TYPES = new Set([
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
  'ModelInference',
  'TagRelation',
  'ContextGate'
])
const BASIC_CONDITION_NODE_TYPES = new Set(['Condition', 'Threshold'])
const ACTION_NODE_TYPES = new Set(['MqttPublish', 'EmailNotify', 'WebhookCall', 'DebugProbe'])

export const LEGACY_FLOW_NODE_TYPES = [
  { type: 'TagInput', label: '标签输入' },
  { type: 'Condition', label: '条件' },
  { type: 'Logic', label: 'AND/OR' },
  { type: 'Duration', label: '持续确认' },
  { type: 'MqttPublish', label: 'MQTT 发布' }
]

export function createFlowRuleTemplate(): FlowRuleDefinition {
  const tag = createFlowNode('TagInput', 0)
  const condition = createFlowNode('Condition', 1)
  const duration = createFlowNode('Duration', 2)
  const mqtt = createFlowNode('MqttPublish', 3)
  const now = new Date().toISOString()
  return {
    id: '',
    name: '规则引擎',
    description: '',
    enabled: true,
    version: 1,
    lifecycleState: 'Published',
    publishedVersion: 1,
    publishedTime: now,
    publishedBy: '',
    mode: 'Flow',
    compiledRuleId: '',
    nodes: [tag, condition, duration, mqtt],
    edges: [createEdge(tag.id, condition.id), createEdge(condition.id, duration.id), createEdge(duration.id, mqtt.id)],
    createdTime: now,
    updatedTime: now
  }
}

export function createFlowNode(nodeType: string, index = 0): FlowRuleNode {
  const baseX = 72 + index * 220
  const label = flowNodeTypeLabel(nodeType)
  return {
    id: createId(),
    nodeType,
    label,
    x: baseX,
    y: 110,
    channelId: '',
    channelName: '',
    deviceId: '',
    groupId: '',
    tagId: '',
    deviceName: '',
    groupName: '',
    tagName: '',
    pointCode: '',
    dataType: '',
    conditionType: defaultConditionType(nodeType),
    operator: 'GreaterThan',
    compareValue: 0,
    lowLimit: 0,
    highLimit: 100,
    deadband: 1,
    rateLimitPerSecond: 1,
    hysteresisMode: 'High',
    hysteresisOnValue: 10,
    hysteresisOffValue: 8,
    expression: '{value} > 0',
    alarmLevels: nodeType === 'MultiLevelAlarm' ? createDefaultAlarmLevels() : [],
    qualityOperator: nodeType === 'QualityGate' ? 'NotIn' : 'In',
    qualityValues: 'Good',
    windowStatistic: 'Average',
    windowSeconds: 60,
    windowSampleCount: 0,
    aggregationStatistic: 'Average',
    trendMode: 'Slope',
    trendWindowSeconds: 300,
    trendSampleCount: 0,
    trendMinSlopePerSecond: 0,
    trendChangeThreshold: 0,
    trendStableDeadband: 0,
    noChangeTimeoutSeconds: 30,
    noChangeDeadband: 0,
    noChangeMaxAgeSeconds: 10,
    stateName: 'State',
    stateExpectedValue: '1',
    stateClearValue: '',
    stateTimeoutSeconds: 0,
    relatedChannelId: '',
    relatedChannelName: '',
    relatedDeviceId: '',
    relatedGroupId: '',
    relatedTagId: '',
    relatedDeviceName: '',
    relatedGroupName: '',
    relatedTagName: '',
    relatedPointCode: '',
    relatedDataType: '',
    relationOperator: 'GreaterThan',
    relationMultiplier: 1,
    relationOffset: 0,
    contextName: 'Context',
    contextExpectedValue: '',
    contextOperator: 'Equal',
    contextChannelId: '',
    contextChannelName: '',
    contextDeviceId: '',
    contextGroupId: '',
    contextTagId: '',
    contextDeviceName: '',
    contextGroupName: '',
    contextTagName: '',
    contextPointCode: '',
    contextDataType: '',
    cycleStartValue: '1',
    cycleEndValue: '0',
    cycleMinSeconds: 0,
    cycleMaxSeconds: 0,
    taktTargetSeconds: 60,
    taktTolerancePercent: 10,
    anomalyMode: 'ZScore',
    anomalyThreshold: 3,
    anomalyBaselineWindowSeconds: 300,
    anomalyBaselineSampleCount: 0,
    modelPurpose: 'DeviceAnomaly',
    modelPath: '',
    modelInputTags: '',
    modelInputName: '',
    modelInputNames: '',
    modelOutputName: '',
    modelOutputIndex: 0,
    modelOperator: 'GreaterThanOrEqual',
    modelThreshold: 0.5,
    modelTimeoutMilliseconds: 1000,
    alarmSeverity: 'Warning',
    alarmSuppressSeconds: 0,
    alarmReTriggerSeconds: 0,
    alarmEscalateAfterSeconds: 0,
    actionDelaySeconds: 0,
    actionCooldownSeconds: 0,
    actionMaxPerMinute: 0,
    debugEnabled: true,
    debugLabel: '',
    transformMultiplier: 1,
    transformOffset: 0,
    transformUseAbsolute: false,
    transformExpression: nodeType === 'Function' ? '{value}' : '',
    transformTimeoutMilliseconds: 50,
    valueScriptId: '',
    valueScriptVersion: 0,
    valueScriptCategory: '',
    valueScriptInputDataType: '',
    valueScriptOutputDataType: '',
    sequenceWindowSeconds: 60,
    sequenceStepTimeoutSeconds: 0,
    sequenceMinIntervalSeconds: 0,
    sequenceResetOnMismatch: true,
    clearDurationSeconds: 0,
    logicalOperator: 'And',
    durationSeconds: 0,
    publishToMqtt: true,
    publishOnClear: true,
    topicTemplate: 'ipc/rule/{pointCode}/{ruleName}',
    publishQos: 0,
    activeMessage: '',
    clearMessage: '',
    executeOnActive: true,
    executeOnClear: true,
    emailSmtpHost: '',
    emailSmtpPort: 25,
    emailEnableSsl: false,
    emailUsername: '',
    emailPassword: '',
    emailFrom: '',
    emailTo: '',
    emailCc: '',
    emailSubjectTemplate: '{ruleName} {state}',
    emailBodyTemplate: '{message}',
    webhookUrl: '',
    webhookMethod: 'POST',
    webhookHeaders: '',
    webhookBodyTemplate: '{"ruleName":"{ruleName}","state":"{state}","value":"{value}","message":"{message}"}',
    webhookContentType: 'application/json',
    webhookTimeoutSeconds: 5,
    webhookRetryCount: 0
  }
}

/**
 * 根据脚本目录创建固定发布版本的规则节点。
 */
export function createValueScriptFlowNode(script: ValueTransformCatalogItem, index = 0): FlowRuleNode {
  const node = createFlowNode('ValueScript', index)
  node.label = script.name
  node.valueScriptId = script.id
  node.valueScriptVersion = script.version
  node.valueScriptCategory = script.nodeCategory
  node.valueScriptInputDataType = script.inputDataType
  node.valueScriptOutputDataType = script.outputDataType
  node.transformTimeoutMilliseconds = 100
  return node
}

export function createEdge(sourceNodeId: string, targetNodeId: string): FlowRuleEdge {
  return {
    id: createId(),
    sourceNodeId,
    targetNodeId,
    sourcePort: 'out',
    targetPort: 'in'
  }
}

export function cloneFlowRule(rule: FlowRuleDefinition): FlowRuleDefinition {
  return JSON.parse(JSON.stringify(rule)) as FlowRuleDefinition
}

export function applyTagSelectionToNode(node: FlowRuleNode, selection: TagSelection | null) {
  node.channelId = selection?.channelId ?? ''
  node.channelName = selection?.channelName ?? ''
  node.deviceId = selection?.deviceId ?? ''
  node.groupId = selection?.groupId ?? ''
  node.tagId = selection?.tagId ?? ''
  node.deviceName = selection?.deviceName ?? ''
  node.groupName = selection?.groupName ?? ''
  node.tagName = selection?.tagName ?? ''
  node.pointCode = selection?.pointCode ?? ''
  node.dataType = selection?.dataType ?? ''
}

function validateFlowGraph(rule: FlowRuleDefinition) {
  const errors: string[] = []
  const nodes = rule.nodes ?? []
  const edges = rule.edges ?? []
  const nodeById = new Map<string, FlowRuleNode>()
  for (const node of nodes) {
    if (!node.id?.trim()) errors.push('流程节点 ID 不能为空')
    else if (nodeById.has(node.id)) errors.push(`流程节点 ID 重复：${node.id}`)
    else nodeById.set(node.id, node)
  }

  const incoming = new Map<string, FlowRuleEdge[]>()
  const outgoing = new Map<string, FlowRuleEdge[]>()
  const edgePairs = new Set<string>()
  for (const edge of edges) {
    if (!nodeById.has(edge.sourceNodeId) || !nodeById.has(edge.targetNodeId)) {
      errors.push('流程连线引用了不存在的节点')
      continue
    }
    if (edge.sourceNodeId === edge.targetNodeId) errors.push('流程节点不能连接到自身')
    const pair = `${edge.sourceNodeId}\u001f${edge.targetNodeId}`
    if (edgePairs.has(pair)) errors.push('流程中存在重复连线')
    edgePairs.add(pair)
    if (!incoming.has(edge.targetNodeId)) incoming.set(edge.targetNodeId, [])
    if (!outgoing.has(edge.sourceNodeId)) outgoing.set(edge.sourceNodeId, [])
    incoming.get(edge.targetNodeId)!.push(edge)
    outgoing.get(edge.sourceNodeId)!.push(edge)
  }

  const indegree = new Map(nodes.map((node) => [node.id, incoming.get(node.id)?.length ?? 0]))
  const queue = nodes.filter((node) => (indegree.get(node.id) ?? 0) === 0).map((node) => node.id)
  let visited = 0
  while (queue.length) {
    const current = queue.shift()!
    visited++
    for (const edge of outgoing.get(current) ?? []) {
      const count = (indegree.get(edge.targetNodeId) ?? 1) - 1
      indegree.set(edge.targetNodeId, count)
      if (count === 0) queue.push(edge.targetNodeId)
    }
  }
  if (visited !== nodes.length) errors.push('规则引擎不能包含环路')

  let conditions = nodes.filter((node) => CONDITION_NODE_TYPES.has(node.nodeType) && node.nodeType !== 'QualityGate')
  if (!conditions.length) conditions = nodes.filter((node) => node.nodeType === 'QualityGate')
  const logicNodes = nodes.filter((node) => node.nodeType === 'Logic')
  const sequenceNodes = nodes.filter((node) => node.nodeType === 'Sequence')
  if (!conditions.length) errors.push('流程至少需要一个判断节点')
  if (logicNodes.length > 1) errors.push('每条流程只支持一个 AND/OR 节点')
  if (sequenceNodes.length > 1) errors.push('每条流程只支持一个顺序/时序节点')
  if (logicNodes.length && sequenceNodes.length) errors.push('同一流程不能同时使用 AND/OR 和顺序/时序节点')

  let resultNode = sequenceNodes[0] ?? logicNodes[0]
  if (resultNode) {
    const directConditions = (incoming.get(resultNode.id) ?? [])
      .map((edge) => nodeById.get(edge.sourceNodeId))
      .filter((node): node is FlowRuleNode => !!node && conditions.includes(node))
    if (directConditions.length < 2)
      errors.push(`${resultNode.nodeType === 'Sequence' ? '顺序/时序' : 'AND/OR'} 节点至少需要两个直接连接的条件`)
    if (directConditions.some((node) => !BASIC_CONDITION_NODE_TYPES.has(node.nodeType)))
      errors.push('组合与顺序节点当前只接受基础条件')
    if (conditions.some((node) => !directConditions.includes(node)))
      errors.push('组合流程中的条件必须直接连接到汇合节点')
  } else if (conditions.length === 1) {
    resultNode = conditions[0]
  } else if (conditions.length > 1) {
    errors.push('多个条件必须通过一个 AND/OR 或顺序/时序节点汇合')
  }

  const reachable = new Set<string>()
  if (resultNode) {
    const pending = [resultNode.id]
    while (pending.length) {
      const current = pending.pop()!
      for (const edge of outgoing.get(current) ?? []) {
        if (reachable.has(edge.targetNodeId)) continue
        reachable.add(edge.targetNodeId)
        pending.push(edge.targetNodeId)
      }
    }
  }
  for (const node of nodes) {
    if (ACTION_NODE_TYPES.has(node.nodeType) && !reachable.has(node.id))
      errors.push(`动作节点必须连接在判断结果之后：${node.label || node.nodeType}`)
    if (ACTION_NODE_TYPES.has(node.nodeType) && (outgoing.get(node.id)?.length ?? 0) > 0)
      errors.push('动作节点不能再连接下游节点')
    if (node.nodeType !== 'TagInput' && !hasTagSource(node) && (incoming.get(node.id)?.length ?? 0) === 0) {
      errors.push(`流程节点未连接输入：${node.label || node.nodeType}`)
    }
  }
  return errors
}

function isFiniteRuleNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value)
}

export function validateFlowRule(rule: FlowRuleDefinition) {
  const errors: string[] = []
  if (!rule.name?.trim()) errors.push('规则名称不能为空')
  if (!rule.enabled) return errors
  errors.push(...validateFlowGraph(rule))
  if (!rule.nodes?.some((node) => node.nodeType === 'TagInput' || hasTagSource(node))) {
    errors.push('至少需要选择一个标签来源')
  }

  for (const node of rule.nodes ?? []) {
    if (node.nodeType === 'TagInput' && !hasPrimaryTagSource(node)) {
      errors.push(`${node.label || '标签输入'} 必须选择标签`)
    }
    if (node.nodeType === 'Condition') {
      if (node.conditionType === 'Threshold') {
        if (!isFiniteRuleNumber(node.lowLimit) || !isFiniteRuleNumber(node.highLimit) || node.lowLimit > node.highLimit)
          errors.push('阈值上下限不合法')
      } else if (node.conditionType === 'Deadband') {
        if (!isFiniteRuleNumber(node.deadband) || node.deadband < 0) errors.push('死区值必须为非负有限数')
      } else if (node.conditionType === 'RateOfChange') {
        if (!isFiniteRuleNumber(node.rateLimitPerSecond) || node.rateLimitPerSecond < 0)
          errors.push('变化率限值必须为非负有限数')
      } else if (!isFiniteRuleNumber(node.compareValue)) {
        errors.push(`${node.label || '基础条件'} 的比较值不合法`)
      }
    }
    if (node.nodeType === 'Hysteresis') {
      const onValue = Number(node.hysteresisOnValue)
      const offValue = Number(node.hysteresisOffValue)
      if (!isFiniteRuleNumber(node.hysteresisOnValue) || !isFiniteRuleNumber(node.hysteresisOffValue)) {
        errors.push(`${node.label || '滞回规则'} 的动作值和恢复值不合法`)
      } else if (node.hysteresisMode === 'Low' && onValue >= offValue) {
        errors.push(`${node.label || '滞回规则'} 的低限动作值必须小于恢复值`)
      } else if (node.hysteresisMode !== 'Low' && onValue <= offValue) {
        errors.push(`${node.label || '滞回规则'} 的高限动作值必须大于恢复值`)
      }
    }
    if (node.nodeType === 'MultiLevelAlarm') {
      const levels = node.alarmLevels ?? []
      if (!levels.length) errors.push(`${node.label || '多级告警'} 至少需要一个告警级别`)
      for (const level of levels) {
        if (!level.name?.trim()) errors.push('多级告警级别名称不能为空')
        if (!isFiniteRuleNumber(level.compareValue)) errors.push(`${level.name || '告警级别'} 的比较值不合法`)
      }
    }
    if (node.nodeType === 'Expression' && !node.expression?.trim()) {
      errors.push(`${node.label || '表达式规则'} 的表达式不能为空`)
    }
    if (node.nodeType === 'QualityGate') {
      if (!node.qualityValues?.trim()) errors.push('质量门控至少需要一个质量值')
      if (!['In', 'NotIn'].includes(node.qualityOperator || '')) errors.push('质量门控匹配方式只能是 In/NotIn')
    }
    if (node.nodeType === 'SlidingWindow' || node.nodeType === 'WindowCalculation') {
      if (!['Average', 'Min', 'Max', 'Sum', 'Count', 'StdDev'].includes(node.windowStatistic || ''))
        errors.push('窗口统计方法不合法')
      if (!isFiniteRuleNumber(node.windowSeconds) || Number(node.windowSeconds) <= 0)
        errors.push('窗口统计时间窗口必须大于 0 秒')
      if (!isFiniteRuleNumber(node.windowSampleCount) || Number(node.windowSampleCount) < 0)
        errors.push('窗口统计样本数不能小于 0')
      if (!isFiniteRuleNumber(node.compareValue)) errors.push('窗口统计比较值不合法')
    }
    if (node.nodeType === 'Aggregation') {
      if (
        !['Average', 'Min', 'Max', 'Sum', 'Count', 'StdDev', 'First', 'Last', 'Range'].includes(
          node.aggregationStatistic || ''
        )
      )
        errors.push('聚合统计方法不合法')
      if (!isFiniteRuleNumber(node.compareValue)) errors.push('聚合比较值不合法')
    }
    if (node.nodeType === 'NoChangeTimeout') {
      if (!Number.isInteger(node.noChangeTimeoutSeconds) || node.noChangeTimeoutSeconds <= 0)
        errors.push('无变化超时必须为大于 0 的整数秒')
      if (!Number.isFinite(node.noChangeDeadband) || node.noChangeDeadband < 0)
        errors.push('变化死区必须为非负有限数')
      if (!Number.isInteger(node.noChangeMaxAgeSeconds) || node.noChangeMaxAgeSeconds <= 0)
        errors.push('数据最大间隔必须为大于 0 的整数秒')
    }
    if (node.nodeType === 'Trend') {
      if (!['Slope', 'Rising', 'Falling', 'Stable'].includes(node.trendMode || '')) errors.push('趋势判断模式不合法')
      if (!isFiniteRuleNumber(node.trendWindowSeconds) || Number(node.trendWindowSeconds) <= 0)
        errors.push('趋势判断时间窗口必须大于 0 秒')
      if (!isFiniteRuleNumber(node.trendSampleCount) || Number(node.trendSampleCount) < 0)
        errors.push('趋势判断样本数不能小于 0')
      if (!isFiniteRuleNumber(node.trendMinSlopePerSecond) || Number(node.trendMinSlopePerSecond) < 0)
        errors.push('趋势判断最小斜率不能小于 0')
      if (!isFiniteRuleNumber(node.trendChangeThreshold) || Number(node.trendChangeThreshold) < 0)
        errors.push('趋势判断变化阈值不能小于 0')
      if (!isFiniteRuleNumber(node.trendStableDeadband) || Number(node.trendStableDeadband) < 0)
        errors.push('趋势判断稳定死区不能小于 0')
    }
    if (node.nodeType === 'StateMachine') {
      if (!node.stateName?.trim()) errors.push('状态机状态名称不能为空')
      if (!node.stateExpectedValue?.trim()) errors.push('状态机目标状态值不能为空')
      if (!isFiniteRuleNumber(node.stateTimeoutSeconds) || Number(node.stateTimeoutSeconds) < 0)
        errors.push('状态机超时不能小于 0 秒')
      if (node.stateClearValue?.trim() && node.stateClearValue.trim() === node.stateExpectedValue?.trim())
        errors.push('状态机目标值与明确恢复值不能相同')
    }
    if (node.nodeType === 'CycleTime' || node.nodeType === 'ProcessTakt') {
      if (!hasPrimaryTagSource(node)) errors.push('节拍规则需要选择标签来源')
      if (!node.cycleStartValue?.trim()) errors.push('节拍规则开始值不能为空')
      if (!node.cycleEndValue?.trim()) errors.push('节拍规则结束值不能为空')
      if (!isFiniteRuleNumber(node.cycleMinSeconds) || Number(node.cycleMinSeconds) < 0)
        errors.push('节拍规则最短周期不能小于 0')
      if (!isFiniteRuleNumber(node.cycleMaxSeconds) || Number(node.cycleMaxSeconds) < 0)
        errors.push('节拍规则最长周期不能小于 0')
      if (node.cycleStartValue?.trim() === node.cycleEndValue?.trim()) errors.push('周期开始值与结束值不能相同')
      if (
        Number(node.cycleMinSeconds) > 0 &&
        Number(node.cycleMaxSeconds) > 0 &&
        Number(node.cycleMinSeconds) > Number(node.cycleMaxSeconds)
      ) {
        errors.push('周期最小时间不能大于最大时间')
      }
      if (node.nodeType === 'ProcessTakt') {
        if (!isFiniteRuleNumber(node.taktTargetSeconds) || Number(node.taktTargetSeconds) <= 0)
          errors.push('工艺节拍目标秒数必须大于 0')
        if (!isFiniteRuleNumber(node.taktTolerancePercent) || Number(node.taktTolerancePercent) < 0)
          errors.push('工艺节拍容差不能小于 0')
      }
    }
    if (node.nodeType === 'AnomalyDetection') {
      if (!['ZScore', 'Deviation', 'Spike'].includes(node.anomalyMode || '')) errors.push('异常检测模式不合法')
      if (!isFiniteRuleNumber(node.anomalyThreshold) || Number(node.anomalyThreshold) <= 0)
        errors.push('异常检测阈值必须大于 0')
      if (!isFiniteRuleNumber(node.anomalyBaselineWindowSeconds) || Number(node.anomalyBaselineWindowSeconds) <= 0)
        errors.push('异常检测基线窗口必须大于 0 秒')
      if (!isFiniteRuleNumber(node.anomalyBaselineSampleCount) || Number(node.anomalyBaselineSampleCount) < 0)
        errors.push('异常检测样本数不能小于 0')
    }
    if (node.nodeType === 'ModelInference') {
      if (!node.modelPath?.trim()) errors.push('ONNX 推理模型路径不能为空')
      if (!['DeviceAnomaly', 'QualityPrediction'].includes(node.modelPurpose || '')) errors.push('ONNX 推理用途不合法')
      if (
        !['GreaterThan', 'GreaterThanOrEqual', 'LessThan', 'LessThanOrEqual', 'Equal', 'NotEqual'].includes(
          node.modelOperator || ''
        )
      )
        errors.push('ONNX 推理判断符不合法')
      if (!isFiniteRuleNumber(node.modelThreshold)) errors.push('ONNX 推理阈值不合法')
      if (!isFiniteRuleNumber(node.modelOutputIndex) || Number(node.modelOutputIndex) < 0)
        errors.push('ONNX 推理输出序号不能小于 0')
      if (
        !isFiniteRuleNumber(node.modelTimeoutMilliseconds) ||
        Number(node.modelTimeoutMilliseconds) <= 0 ||
        Number(node.modelTimeoutMilliseconds) > 30000
      ) {
        errors.push('ONNX 推理超时必须在 1-30000ms 之间')
      }
    }
    if (node.nodeType === 'TagRelation') {
      if (!hasPrimaryTagSource(node)) errors.push('Tag Relation requires a source tag')
      if (!hasRelatedTagSource(node)) errors.push('Tag Relation requires a related tag')
      if (!isFiniteRuleNumber(node.relationMultiplier)) errors.push('Tag Relation multiplier is invalid')
      if (!isFiniteRuleNumber(node.relationOffset)) errors.push('Tag Relation offset is invalid')
    }
    if (node.nodeType === 'ContextGate') {
      if (!hasPrimaryTagSource(node) && !hasContextTagSource(node)) errors.push('Context Gate requires a context tag')
      if (!node.contextExpectedValue?.trim()) errors.push('Context Gate expected value is required')
    }
    if (node.nodeType === 'AlarmLifecycle') {
      if (!isFiniteRuleNumber(node.alarmSuppressSeconds) || Number(node.alarmSuppressSeconds) < 0)
        errors.push('Alarm Lifecycle suppress seconds must be >= 0')
      if (!isFiniteRuleNumber(node.alarmReTriggerSeconds) || Number(node.alarmReTriggerSeconds) < 0)
        errors.push('Alarm Lifecycle retrigger seconds must be >= 0')
      if (!isFiniteRuleNumber(node.alarmEscalateAfterSeconds) || Number(node.alarmEscalateAfterSeconds) < 0)
        errors.push('Alarm Lifecycle escalation seconds must be >= 0')
    }
    if (node.nodeType === 'ActionPolicy') {
      if (!isFiniteRuleNumber(node.actionDelaySeconds) || Number(node.actionDelaySeconds) < 0)
        errors.push('Action Policy delay seconds must be >= 0')
      if (!isFiniteRuleNumber(node.actionCooldownSeconds) || Number(node.actionCooldownSeconds) < 0)
        errors.push('Action Policy cooldown seconds must be >= 0')
      if (!isFiniteRuleNumber(node.actionMaxPerMinute) || Number(node.actionMaxPerMinute) < 0)
        errors.push('Action Policy max per minute must be >= 0')
    }
    if (node.nodeType === 'Transform' || node.nodeType === 'Function') {
      if (node.nodeType === 'Transform') {
        if (!isFiniteRuleNumber(node.transformMultiplier)) errors.push('数据处理乘数不合法')
        if (!isFiniteRuleNumber(node.transformOffset)) errors.push('数据处理偏移不合法')
      }
      if (node.nodeType === 'Function' && !node.transformExpression?.trim()) errors.push('函数节点表达式不能为空')
      if (
        !isFiniteRuleNumber(node.transformTimeoutMilliseconds) ||
        Number(node.transformTimeoutMilliseconds) <= 0 ||
        Number(node.transformTimeoutMilliseconds) > 5000
      ) {
        errors.push('函数/数据处理超时必须在 1-5000ms 之间')
      }
    }
    if (node.nodeType === 'ValueScript') {
      if (!node.valueScriptId?.trim() || Number(node.valueScriptVersion) <= 0)
        errors.push('值处理脚本节点必须选择一个已发布版本')
      if (
        !isFiniteRuleNumber(node.transformTimeoutMilliseconds) ||
        Number(node.transformTimeoutMilliseconds) < 10 ||
        Number(node.transformTimeoutMilliseconds) > 5000
      ) {
        errors.push('值处理脚本超时必须在 10-5000ms 之间')
      }
    }
    if (node.nodeType === 'Sequence') {
      if (!isFiniteRuleNumber(node.sequenceWindowSeconds) || Number(node.sequenceWindowSeconds) <= 0) {
        errors.push('顺序/时序总窗口必须大于 0 秒')
      }
      if (!isFiniteRuleNumber(node.sequenceStepTimeoutSeconds) || Number(node.sequenceStepTimeoutSeconds) < 0) {
        errors.push('顺序/时序单步超时不能小于 0 秒')
      }
      if (!isFiniteRuleNumber(node.sequenceMinIntervalSeconds) || Number(node.sequenceMinIntervalSeconds) < 0) {
        errors.push('顺序/时序最小间隔不能小于 0 秒')
      }
    }
    if (node.nodeType === 'Duration') {
      if (!isFiniteRuleNumber(node.durationSeconds) || node.durationSeconds < 0)
        errors.push('持续确认时间不能小于 0 秒')
      if (!isFiniteRuleNumber(node.clearDurationSeconds) || node.clearDurationSeconds < 0)
        errors.push('恢复确认时间不能小于 0 秒')
    }
    if (node.nodeType === 'MqttPublish' && node.publishToMqtt && !node.topicTemplate?.trim()) {
      errors.push('MQTT topic 模板不能为空')
    }
    if (node.nodeType === 'MqttPublish' && ![0, 1, 2].includes(node.publishQos)) {
      errors.push('MQTT QoS 只能是 0/1/2')
    }
    if (node.nodeType === 'EmailNotify') {
      if (!node.executeOnActive && !node.executeOnClear) errors.push('邮件通知至少选择触发或恢复一种发送时机')
      if (!node.emailSmtpHost?.trim()) errors.push('邮件通知 SMTP 服务器不能为空')
      if (!node.emailFrom?.trim()) errors.push('邮件通知发件人不能为空')
      if (!node.emailTo?.trim()) errors.push('邮件通知收件人不能为空')
      if (!isFiniteRuleNumber(node.emailSmtpPort) || Number(node.emailSmtpPort) <= 0)
        errors.push('邮件通知 SMTP 端口不合法')
    }
    if (node.nodeType === 'WebhookCall') {
      if (!node.executeOnActive && !node.executeOnClear) errors.push('Webhook 至少选择触发或恢复一种调用时机')
      if (!node.webhookUrl?.trim()) errors.push('Webhook URL 不能为空')
      if (!['GET', 'POST', 'PUT', 'PATCH', 'DELETE'].includes((node.webhookMethod || '').toUpperCase()))
        errors.push('Webhook Method 不合法')
      if (!isFiniteRuleNumber(node.webhookTimeoutSeconds) || Number(node.webhookTimeoutSeconds) <= 0)
        errors.push('Webhook 超时时间不合法')
      if (!isFiniteRuleNumber(node.webhookRetryCount) || Number(node.webhookRetryCount) < 0)
        errors.push('Webhook 重试次数不合法')
    }
  }
  return errors
}

export function legacyValidateFlowRule(rule: FlowRuleDefinition) {
  const errors: string[] = []
  if (!rule.name?.trim()) errors.push('规则名称不能为空')
  if (!rule.nodes?.some((node) => node.nodeType === 'TagInput' || hasTagSource(node))) {
    errors.push('至少需要选择一个标签来源')
  }
  for (const node of rule.nodes ?? []) {
    if (node.nodeType === 'Condition' && !isFiniteRuleNumber(node.compareValue)) {
      errors.push(`${node.label || '条件'} 的比较值不合法`)
    }
    if (node.nodeType === 'MqttPublish' && node.publishToMqtt && !node.topicTemplate?.trim()) {
      errors.push('MQTT topic 模板不能为空')
    }
    if (node.nodeType === 'MqttPublish' && ![0, 1, 2].includes(node.publishQos)) {
      errors.push('MQTT QoS 只能是 0/1/2')
    }
  }
  return errors
}

export function hasTagSource(node: FlowRuleNode) {
  return hasPrimaryTagSource(node) || hasRelatedTagSource(node) || hasContextTagSource(node)
}

function hasPrimaryTagSource(node: FlowRuleNode) {
  return !!node.tagId?.trim()
}

function hasRelatedTagSource(node: FlowRuleNode) {
  return !!node.relatedTagId?.trim()
}

function hasContextTagSource(node: FlowRuleNode) {
  return !!node.contextTagId?.trim()
}

export function nodeDisplayName(node: FlowRuleNode, translate: (label: string) => string = (label) => label) {
  const title = flowNodeLabel(node, translate)
  if (node.nodeType === 'TagInput' && node.tagName) return `${title}: ${node.tagName}`
  if (node.nodeType === 'Condition') {
    if (node.conditionType === 'Threshold') return `${title}: ${node.lowLimit ?? ''} ~ ${node.highLimit ?? ''}`
    if (node.conditionType === 'Deadband') return `${title}: ${node.deadband ?? ''}`
    if (node.conditionType === 'RateOfChange') return `${title}: ${node.rateLimitPerSecond ?? ''}`
    return `${title}: ${operatorSymbol(node.operator)} ${node.compareValue ?? ''}`
  }
  if (node.nodeType === 'Hysteresis')
    return `${title}: ${flowOptionLabel('hysteresis', node.hysteresisMode, translate)} ${node.hysteresisOnValue ?? ''}/${node.hysteresisOffValue ?? ''}`
  if (node.nodeType === 'MultiLevelAlarm') return `${title}: ${(node.alarmLevels ?? []).length} ${translate('级别')}`
  if (node.nodeType === 'Expression') return `${title}: ${node.expression ?? ''}`
  if (node.nodeType === 'QualityGate')
    return `${title}: ${flowOptionLabel('qualityOperator', node.qualityOperator, translate)} ${flowQualityValuesLabel(node.qualityValues, translate)}`.trimEnd()
  if (node.nodeType === 'SlidingWindow')
    return `${title}: ${flowOptionLabel('statistic', node.windowStatistic, translate)} ${operatorSymbol(node.operator)} ${node.compareValue ?? ''}`
  if (node.nodeType === 'WindowCalculation')
    return `${title}: ${flowOptionLabel('statistic', node.windowStatistic, translate)} ${operatorSymbol(node.operator)} ${node.compareValue ?? ''}`
  if (node.nodeType === 'Aggregation')
    return `${title}: ${flowOptionLabel('statistic', node.aggregationStatistic, translate)} ${operatorSymbol(node.operator)} ${node.compareValue ?? ''}`
  if (node.nodeType === 'Trend') return `${title}: ${flowOptionLabel('trend', node.trendMode, translate)}`
  if (node.nodeType === 'NoChangeTimeout') return `${title}: ${formatSeconds(node.noChangeTimeoutSeconds)}`
  if (node.nodeType === 'StateMachine') return `${title}: ${node.stateName ?? ''}=${node.stateExpectedValue ?? ''}`
  if (node.nodeType === 'CycleTime') return `${title}: ${node.cycleStartValue ?? ''} -> ${node.cycleEndValue ?? ''}`
  if (node.nodeType === 'ProcessTakt') return `${title}: ${formatSeconds(node.taktTargetSeconds)}`
  if (node.nodeType === 'AnomalyDetection') return `${title}: ${flowOptionLabel('anomaly', node.anomalyMode, translate)}`
  if (node.nodeType === 'ModelInference')
    return `${title}: ${flowOptionLabel('purpose', node.modelPurpose, translate)} ${operatorSymbol(node.modelOperator ?? '')} ${node.modelThreshold ?? ''}`
  if (node.nodeType === 'TagRelation')
    return `${title}: ${operatorSymbol(node.relationOperator || node.operator)} ${node.relatedPointCode || node.relatedTagName || translate('关联标签')}`
  if (node.nodeType === 'ContextGate')
    return `${title}: ${node.contextName ?? ''}=${node.contextExpectedValue ?? ''}`
  if (node.nodeType === 'AlarmLifecycle') return `${title}: ${flowOptionLabel('severity', node.alarmSeverity, translate)}`
  if (node.nodeType === 'ActionPolicy') return `${title}: ${translate('冷却')} ${formatSeconds(node.actionCooldownSeconds)}`
  if (node.nodeType === 'DebugProbe') return `${title}: ${node.debugLabel || translate('调试跟踪')}`
  if (node.nodeType === 'Transform')
    return `${title}: x${node.transformMultiplier ?? ''} ${node.transformOffset == null ? '' : formatOffset(node.transformOffset)}`
  if (node.nodeType === 'Function') return `${title}: ${node.transformExpression ?? ''}`
  if (node.nodeType === 'ValueScript')
    return `${title}: v${node.valueScriptVersion || 0} ${node.valueScriptInputDataType || '?'} → ${node.valueScriptOutputDataType || '?'}`
  if (node.nodeType === 'Sequence') return `${title}: ${formatSeconds(node.sequenceWindowSeconds)}`
  if (node.nodeType === 'EmailNotify') return `${title}: ${node.emailTo || translate('未配置收件人')}`
  if (node.nodeType === 'WebhookCall') return `${title}: ${node.webhookMethod || 'POST'}`
  if (node.nodeType === 'Logic') return `${title}: ${flowOptionLabel('logic', node.logicalOperator, translate)}`
  return title
}

function formatSeconds(value: number | null | undefined) {
  return value == null ? '' : `${value}s`
}

export function legacyNodeDisplayName(node: FlowRuleNode) {
  const title = flowNodeLabel(node)
  if (node.nodeType === 'TagInput' && node.tagName) return `${title}: ${node.tagName}`
  if (node.nodeType === 'Condition') return `${title}: ${operatorSymbol(node.operator)} ${node.compareValue}`
  if (node.nodeType === 'Logic') return `${title}: ${node.logicalOperator}`
  return title
}

export function flowNodeTypeLabel(nodeType: string) {
  return FLOW_NODE_TYPE_LABELS[nodeType] || nodeType
}

export function flowNodeLabel(node: FlowRuleNode, translate: (label: string) => string = (label) => label) {
  const label = (node.label || '').trim()
  const typeLabel = flowNodeTypeLabel(node.nodeType)
  if (!label || label === typeLabel || label === node.nodeType || label === LEGACY_DEFAULT_NODE_LABELS[node.nodeType])
    return translate(typeLabel)
  return label
}

function formatOffset(offset: number) {
  if (!offset) return '+0'
  return offset > 0 ? `+${offset}` : `${offset}`
}

export function operatorSymbol(operator: string) {
  return (
    (
      {
        GreaterThan: '>',
        GreaterThanOrEqual: '>=',
        LessThan: '<',
        LessThanOrEqual: '<=',
        Equal: '=',
        NotEqual: '!='
      } as Record<string, string>
    )[operator] ?? operator
  )
}

function defaultConditionType(nodeType: string) {
  return CONDITION_NODE_TYPES.has(nodeType) && nodeType !== 'Condition' ? nodeType : 'Condition'
}

function createDefaultAlarmLevels() {
  return [
    {
      id: createId(),
      name: '预警',
      severity: 'Warning',
      operator: 'GreaterThanOrEqual',
      compareValue: 10,
      message: ''
    },
    {
      id: createId(),
      name: '严重',
      severity: 'Critical',
      operator: 'GreaterThanOrEqual',
      compareValue: 20,
      message: ''
    }
  ]
}

function createId() {
  return typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID().replace(/-/g, '')
    : Math.random().toString(16).slice(2) + Date.now().toString(16)
}

import type { FlowRuleDefinition, FlowRuleEdge, FlowRuleNode } from '../api'
import type { TagSelection } from './tagSelection'

export const FLOW_NODE_GROUPS = [
  {
    title: '输入',
    nodes: [
      { type: 'TagInput', label: '标签输入' }
    ]
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
export const FLOW_NODE_TYPES = FLOW_NODE_GROUPS.flatMap(group => group.nodes)
const LEGACY_DEFAULT_NODE_LABELS: Record<string, string> = {
  CycleTime: 'Cycle Time',
  TagRelation: 'Tag Relation',
  ContextGate: 'Context Gate',
  AlarmLifecycle: 'Alarm Lifecycle',
  ActionPolicy: 'Action Policy',
  DebugProbe: 'Debug Probe'
}
const CONDITION_NODE_TYPES = new Set(['Condition', 'Threshold', 'Deadband', 'RateOfChange', 'Hysteresis', 'MultiLevelAlarm', 'Expression', 'QualityGate', 'SlidingWindow', 'WindowCalculation', 'Aggregation', 'Trend', 'StateMachine', 'CycleTime', 'ProcessTakt', 'AnomalyDetection', 'ModelInference', 'TagRelation', 'ContextGate'])

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
    name: '流程规则',
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
    edges: [
      createEdge(tag.id, condition.id),
      createEdge(condition.id, duration.id),
      createEdge(duration.id, mqtt.id)
    ],
    createdTime: now,
    updatedTime: now
  }
}

export function createFlowNode(nodeType: string, index = 0): FlowRuleNode {
  const baseX = 72 + index * 220
  const label = FLOW_NODE_TYPES.find(item => item.type === nodeType)?.label ?? nodeType
  return {
    id: createId(),
    nodeType,
    label,
    x: baseX,
    y: 110,
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
    stateName: 'State',
    stateExpectedValue: '1',
    stateClearValue: '',
    stateTimeoutSeconds: 0,
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
  node.deviceName = selection?.deviceName ?? ''
  node.groupName = selection?.groupName ?? ''
  node.tagName = selection?.tagName ?? ''
  node.pointCode = selection?.pointCode ?? ''
  node.dataType = selection?.dataType ?? ''
}

export function validateFlowRule(rule: FlowRuleDefinition) {
  const errors: string[] = []
  if (!rule.name?.trim()) errors.push('规则名称不能为空')
  if (!rule.nodes?.some(node => node.nodeType === 'TagInput' || hasTagSource(node))) {
    errors.push('至少需要选择一个标签来源')
  }

  for (const node of rule.nodes ?? []) {
    if (node.nodeType === 'Condition' && !Number.isFinite(Number(node.compareValue))) {
      errors.push(`${node.label || '基础条件'} 的比较值不合法`)
    }
    if (node.nodeType === 'Hysteresis') {
      const onValue = Number(node.hysteresisOnValue)
      const offValue = Number(node.hysteresisOffValue)
      if (!Number.isFinite(onValue) || !Number.isFinite(offValue)) {
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
        if (!Number.isFinite(Number(level.compareValue))) errors.push(`${level.name || '告警级别'} 的比较值不合法`)
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
      if (!['Average', 'Min', 'Max', 'Sum', 'Count', 'StdDev'].includes(node.windowStatistic || '')) errors.push('窗口统计方法不合法')
      if (!Number.isFinite(Number(node.windowSeconds)) || Number(node.windowSeconds) <= 0) errors.push('窗口统计时间窗口必须大于 0 秒')
      if (!Number.isFinite(Number(node.windowSampleCount)) || Number(node.windowSampleCount) < 0) errors.push('窗口统计样本数不能小于 0')
      if (!Number.isFinite(Number(node.compareValue))) errors.push('窗口统计比较值不合法')
    }
    if (node.nodeType === 'Aggregation') {
      if (!['Average', 'Min', 'Max', 'Sum', 'Count', 'StdDev', 'First', 'Last', 'Range'].includes(node.aggregationStatistic || '')) errors.push('聚合统计方法不合法')
      if (!Number.isFinite(Number(node.compareValue))) errors.push('聚合比较值不合法')
    }
    if (node.nodeType === 'Trend') {
      if (!['Slope', 'Rising', 'Falling', 'Stable'].includes(node.trendMode || '')) errors.push('趋势判断模式不合法')
      if (!Number.isFinite(Number(node.trendWindowSeconds)) || Number(node.trendWindowSeconds) <= 0) errors.push('趋势判断时间窗口必须大于 0 秒')
      if (!Number.isFinite(Number(node.trendSampleCount)) || Number(node.trendSampleCount) < 0) errors.push('趋势判断样本数不能小于 0')
      if (!Number.isFinite(Number(node.trendMinSlopePerSecond)) || Number(node.trendMinSlopePerSecond) < 0) errors.push('趋势判断最小斜率不能小于 0')
      if (!Number.isFinite(Number(node.trendChangeThreshold)) || Number(node.trendChangeThreshold) < 0) errors.push('趋势判断变化阈值不能小于 0')
      if (!Number.isFinite(Number(node.trendStableDeadband)) || Number(node.trendStableDeadband) < 0) errors.push('趋势判断稳定死区不能小于 0')
    }
    if (node.nodeType === 'StateMachine') {
      if (!node.stateName?.trim()) errors.push('状态机状态名称不能为空')
      if (!node.stateExpectedValue?.trim()) errors.push('状态机目标状态值不能为空')
      if (!Number.isFinite(Number(node.stateTimeoutSeconds)) || Number(node.stateTimeoutSeconds) < 0) errors.push('状态机超时不能小于 0 秒')
    }
    if (node.nodeType === 'CycleTime' || node.nodeType === 'ProcessTakt') {
      if (!hasPrimaryTagSource(node)) errors.push('节拍规则需要选择标签来源')
      if (!node.cycleStartValue?.trim()) errors.push('节拍规则开始值不能为空')
      if (!node.cycleEndValue?.trim()) errors.push('节拍规则结束值不能为空')
      if (!Number.isFinite(Number(node.cycleMinSeconds)) || Number(node.cycleMinSeconds) < 0) errors.push('节拍规则最短周期不能小于 0')
      if (!Number.isFinite(Number(node.cycleMaxSeconds)) || Number(node.cycleMaxSeconds) < 0) errors.push('节拍规则最长周期不能小于 0')
      if (node.nodeType === 'ProcessTakt') {
        if (!Number.isFinite(Number(node.taktTargetSeconds)) || Number(node.taktTargetSeconds) <= 0) errors.push('工艺节拍目标秒数必须大于 0')
        if (!Number.isFinite(Number(node.taktTolerancePercent)) || Number(node.taktTolerancePercent) < 0) errors.push('工艺节拍容差不能小于 0')
      }
    }
    if (node.nodeType === 'AnomalyDetection') {
      if (!['ZScore', 'Deviation', 'Spike'].includes(node.anomalyMode || '')) errors.push('异常检测模式不合法')
      if (!Number.isFinite(Number(node.anomalyThreshold)) || Number(node.anomalyThreshold) <= 0) errors.push('异常检测阈值必须大于 0')
      if (!Number.isFinite(Number(node.anomalyBaselineWindowSeconds)) || Number(node.anomalyBaselineWindowSeconds) <= 0) errors.push('异常检测基线窗口必须大于 0 秒')
      if (!Number.isFinite(Number(node.anomalyBaselineSampleCount)) || Number(node.anomalyBaselineSampleCount) < 0) errors.push('异常检测样本数不能小于 0')
    }
    if (node.nodeType === 'ModelInference') {
      if (!node.modelPath?.trim()) errors.push('ONNX 推理模型路径不能为空')
      if (!['DeviceAnomaly', 'QualityPrediction'].includes(node.modelPurpose || '')) errors.push('ONNX 推理用途不合法')
      if (!['GreaterThan', 'GreaterThanOrEqual', 'LessThan', 'LessThanOrEqual', 'Equal', 'NotEqual'].includes(node.modelOperator || '')) errors.push('ONNX 推理判断符不合法')
      if (!Number.isFinite(Number(node.modelThreshold))) errors.push('ONNX 推理阈值不合法')
      if (!Number.isFinite(Number(node.modelOutputIndex)) || Number(node.modelOutputIndex) < 0) errors.push('ONNX 推理输出序号不能小于 0')
      if (!Number.isFinite(Number(node.modelTimeoutMilliseconds)) || Number(node.modelTimeoutMilliseconds) <= 0 || Number(node.modelTimeoutMilliseconds) > 30000) {
        errors.push('ONNX 推理超时必须在 1-30000ms 之间')
      }
    }
    if (node.nodeType === 'TagRelation') {
      if (!hasPrimaryTagSource(node)) errors.push('Tag Relation requires a source tag')
      if (!hasRelatedTagSource(node)) errors.push('Tag Relation requires a related tag')
      if (!Number.isFinite(Number(node.relationMultiplier))) errors.push('Tag Relation multiplier is invalid')
      if (!Number.isFinite(Number(node.relationOffset))) errors.push('Tag Relation offset is invalid')
    }
    if (node.nodeType === 'ContextGate') {
      if (!hasPrimaryTagSource(node) && !hasContextTagSource(node)) errors.push('Context Gate requires a context tag')
      if (!node.contextExpectedValue?.trim()) errors.push('Context Gate expected value is required')
    }
    if (node.nodeType === 'AlarmLifecycle') {
      if (!Number.isFinite(Number(node.alarmSuppressSeconds)) || Number(node.alarmSuppressSeconds) < 0) errors.push('Alarm Lifecycle suppress seconds must be >= 0')
      if (!Number.isFinite(Number(node.alarmReTriggerSeconds)) || Number(node.alarmReTriggerSeconds) < 0) errors.push('Alarm Lifecycle retrigger seconds must be >= 0')
      if (!Number.isFinite(Number(node.alarmEscalateAfterSeconds)) || Number(node.alarmEscalateAfterSeconds) < 0) errors.push('Alarm Lifecycle escalation seconds must be >= 0')
    }
    if (node.nodeType === 'ActionPolicy') {
      if (!Number.isFinite(Number(node.actionDelaySeconds)) || Number(node.actionDelaySeconds) < 0) errors.push('Action Policy delay seconds must be >= 0')
      if (!Number.isFinite(Number(node.actionCooldownSeconds)) || Number(node.actionCooldownSeconds) < 0) errors.push('Action Policy cooldown seconds must be >= 0')
      if (!Number.isFinite(Number(node.actionMaxPerMinute)) || Number(node.actionMaxPerMinute) < 0) errors.push('Action Policy max per minute must be >= 0')
    }
    if (node.nodeType === 'Transform' || node.nodeType === 'Function') {
      if (!Number.isFinite(Number(node.transformMultiplier))) errors.push('数据处理乘数不合法')
      if (!Number.isFinite(Number(node.transformOffset))) errors.push('数据处理偏移不合法')
      if (node.nodeType === 'Function' && !node.transformExpression?.trim()) errors.push('函数节点表达式不能为空')
      if (!Number.isFinite(Number(node.transformTimeoutMilliseconds)) || Number(node.transformTimeoutMilliseconds) <= 0 || Number(node.transformTimeoutMilliseconds) > 5000) {
        errors.push('函数/数据处理超时必须在 1-5000ms 之间')
      }
    }
    if (node.nodeType === 'Sequence') {
      if (!Number.isFinite(Number(node.sequenceWindowSeconds)) || Number(node.sequenceWindowSeconds) <= 0) {
        errors.push('顺序/时序总窗口必须大于 0 秒')
      }
      if (!Number.isFinite(Number(node.sequenceStepTimeoutSeconds)) || Number(node.sequenceStepTimeoutSeconds) < 0) {
        errors.push('顺序/时序单步超时不能小于 0 秒')
      }
      if (!Number.isFinite(Number(node.sequenceMinIntervalSeconds)) || Number(node.sequenceMinIntervalSeconds) < 0) {
        errors.push('顺序/时序最小间隔不能小于 0 秒')
      }
    }
    if (node.nodeType === 'Duration' && (!Number.isFinite(Number(node.clearDurationSeconds)) || Number(node.clearDurationSeconds) < 0)) {
      errors.push('恢复确认时间不能小于 0 秒')
    }
    if (node.nodeType === 'MqttPublish' && node.publishToMqtt && !node.topicTemplate?.trim()) {
      errors.push('MQTT topic 模板不能为空')
    }
    if (node.nodeType === 'MqttPublish' && ![0, 1, 2].includes(Number(node.publishQos))) {
      errors.push('MQTT QoS 只能是 0/1/2')
    }
    if (node.nodeType === 'EmailNotify') {
      if (!node.executeOnActive && !node.executeOnClear) errors.push('邮件通知至少选择触发或恢复一种发送时机')
      if (!node.emailSmtpHost?.trim()) errors.push('邮件通知 SMTP 服务器不能为空')
      if (!node.emailFrom?.trim()) errors.push('邮件通知发件人不能为空')
      if (!node.emailTo?.trim()) errors.push('邮件通知收件人不能为空')
      if (!Number.isFinite(Number(node.emailSmtpPort)) || Number(node.emailSmtpPort) <= 0) errors.push('邮件通知 SMTP 端口不合法')
    }
    if (node.nodeType === 'WebhookCall') {
      if (!node.executeOnActive && !node.executeOnClear) errors.push('Webhook 至少选择触发或恢复一种调用时机')
      if (!node.webhookUrl?.trim()) errors.push('Webhook URL 不能为空')
      if (!['GET', 'POST', 'PUT', 'PATCH', 'DELETE'].includes((node.webhookMethod || '').toUpperCase())) errors.push('Webhook Method 不合法')
      if (!Number.isFinite(Number(node.webhookTimeoutSeconds)) || Number(node.webhookTimeoutSeconds) <= 0) errors.push('Webhook 超时时间不合法')
      if (!Number.isFinite(Number(node.webhookRetryCount)) || Number(node.webhookRetryCount) < 0) errors.push('Webhook 重试次数不合法')
    }
  }
  return errors
}

export function legacyValidateFlowRule(rule: FlowRuleDefinition) {
  const errors: string[] = []
  if (!rule.name?.trim()) errors.push('规则名称不能为空')
  if (!rule.nodes?.some(node => node.nodeType === 'TagInput' || hasTagSource(node))) {
    errors.push('至少需要选择一个标签来源')
  }
  for (const node of rule.nodes ?? []) {
    if (node.nodeType === 'Condition' && !Number.isFinite(Number(node.compareValue))) {
      errors.push(`${node.label || '条件'} 的比较值不合法`)
    }
    if (node.nodeType === 'MqttPublish' && node.publishToMqtt && !node.topicTemplate?.trim()) {
      errors.push('MQTT topic 模板不能为空')
    }
    if (node.nodeType === 'MqttPublish' && ![0, 1, 2].includes(Number(node.publishQos))) {
      errors.push('MQTT QoS 只能是 0/1/2')
    }
  }
  return errors
}

export function hasTagSource(node: FlowRuleNode) {
  return hasPrimaryTagSource(node) || hasRelatedTagSource(node) || hasContextTagSource(node)
}

function hasPrimaryTagSource(node: FlowRuleNode) {
  return !!(node.pointCode?.trim() || (node.deviceName?.trim() && node.tagName?.trim()))
}

function hasRelatedTagSource(node: FlowRuleNode) {
  return !!(node.relatedPointCode?.trim() || (node.relatedDeviceName?.trim() && node.relatedTagName?.trim()))
}

function hasContextTagSource(node: FlowRuleNode) {
  return !!(node.contextPointCode?.trim() || (node.contextDeviceName?.trim() && node.contextTagName?.trim()))
}

export function nodeDisplayName(node: FlowRuleNode) {
  const title = flowNodeLabel(node)
  if (node.nodeType === 'TagInput' && node.tagName) return `${title}: ${node.tagName}`
  if (node.nodeType === 'Condition') return `${title}: ${operatorSymbol(node.operator)} ${node.compareValue}`
  if (node.nodeType === 'Hysteresis') return `${title}: ${node.hysteresisMode === 'Low' ? '低限' : '高限'} ${node.hysteresisOnValue}/${node.hysteresisOffValue}`
  if (node.nodeType === 'MultiLevelAlarm') return `${title}: ${(node.alarmLevels ?? []).length} 级`
  if (node.nodeType === 'Expression') return `${title}: ${node.expression || '{value} > 0'}`
  if (node.nodeType === 'QualityGate') return `${title}: ${node.qualityOperator || 'In'} ${node.qualityValues || 'Good'}`
  if (node.nodeType === 'SlidingWindow') return `${title}: ${node.windowStatistic || 'Average'} ${operatorSymbol(node.operator)} ${node.compareValue}`
  if (node.nodeType === 'WindowCalculation') return `${title}: ${node.windowStatistic || 'Average'} ${operatorSymbol(node.operator)} ${node.compareValue}`
  if (node.nodeType === 'Aggregation') return `${title}: ${node.aggregationStatistic || 'Average'} ${operatorSymbol(node.operator)} ${node.compareValue}`
  if (node.nodeType === 'Trend') return `${title}: ${node.trendMode || 'Slope'}`
  if (node.nodeType === 'StateMachine') return `${title}: ${node.stateName || 'State'}=${node.stateExpectedValue || ''}`
  if (node.nodeType === 'CycleTime') return `${title}: ${node.cycleStartValue || '1'} -> ${node.cycleEndValue || '0'}`
  if (node.nodeType === 'ProcessTakt') return `${title}: ${node.taktTargetSeconds || 60}s`
  if (node.nodeType === 'AnomalyDetection') return `${title}: ${node.anomalyMode || 'ZScore'}`
  if (node.nodeType === 'ModelInference') return `${title}: ${node.modelPurpose === 'QualityPrediction' ? '质量预测' : '异常预警'} ${operatorSymbol(node.modelOperator || 'GreaterThanOrEqual')} ${node.modelThreshold ?? 0.5}`
  if (node.nodeType === 'TagRelation') return `${title}: ${operatorSymbol(node.relationOperator || node.operator)} ${node.relatedPointCode || node.relatedTagName || 'related'}`
  if (node.nodeType === 'ContextGate') return `${title}: ${node.contextName || 'Context'}=${node.contextExpectedValue || ''}`
  if (node.nodeType === 'AlarmLifecycle') return `${title}: ${node.alarmSeverity || 'Warning'}`
  if (node.nodeType === 'ActionPolicy') return `${title}: cd ${node.actionCooldownSeconds || 0}s`
  if (node.nodeType === 'DebugProbe') return `${title}: ${node.debugLabel || 'trace'}`
  if (node.nodeType === 'Transform') return `${title}: x${node.transformMultiplier ?? 1} ${formatOffset(node.transformOffset ?? 0)}`
  if (node.nodeType === 'Function') return `${title}: ${node.transformExpression || '{value}'}`
  if (node.nodeType === 'Sequence') return `${title}: ${node.sequenceWindowSeconds || 60}s`
  if (node.nodeType === 'EmailNotify') return `${title}: ${node.emailTo || '未配置收件人'}`
  if (node.nodeType === 'WebhookCall') return `${title}: ${node.webhookMethod || 'POST'}`
  if (node.nodeType === 'Logic') return `${title}: ${node.logicalOperator}`
  return title
}

export function legacyNodeDisplayName(node: FlowRuleNode) {
  const title = flowNodeLabel(node)
  if (node.nodeType === 'TagInput' && node.tagName) return `${title}: ${node.tagName}`
  if (node.nodeType === 'Condition') return `${title}: ${operatorSymbol(node.operator)} ${node.compareValue}`
  if (node.nodeType === 'Logic') return `${title}: ${node.logicalOperator}`
  return title
}

export function flowNodeTypeLabel(nodeType: string) {
  return FLOW_NODE_TYPES.find(item => item.type === nodeType)?.label || nodeType
}

export function flowNodeLabel(node: FlowRuleNode) {
  const label = (node.label || '').trim()
  const typeLabel = flowNodeTypeLabel(node.nodeType)
  if (!label || label === node.nodeType || label === LEGACY_DEFAULT_NODE_LABELS[node.nodeType]) return typeLabel
  return label
}

function formatOffset(offset: number) {
  if (!offset) return '+0'
  return offset > 0 ? `+${offset}` : `${offset}`
}

function operatorSymbol(operator: string) {
  return ({
    GreaterThan: '>',
    GreaterThanOrEqual: '>=',
    LessThan: '<',
    LessThanOrEqual: '<=',
    Equal: '=',
    NotEqual: '!='
  } as Record<string, string>)[operator] ?? operator
}

function defaultConditionType(nodeType: string) {
  return CONDITION_NODE_TYPES.has(nodeType) && nodeType !== 'Condition'
    ? nodeType
    : 'Condition'
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

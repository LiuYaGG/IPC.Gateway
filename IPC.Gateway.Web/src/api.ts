export interface ApiResult<T> {
  success: boolean
  errorMessage?: string
  data: T
}

export interface GatewayStatus {
  isRunning: boolean
  projectName: string
  configurationStore: string
  deviceCount: number
  groupCount: number
  tagCount: number
  enabledDeviceCount: number
  onlineDeviceCount: number
  goodTagCount: number
  badTagCount: number
  noDataTagCount: number
  startedTime: string
  lastReloadTime: string
  devices: DeviceRuntimeStatus[]
  tags: TagValueSnapshot[]
  recentErrors: RuntimeErrorDetail[]
  mqtt: MqttRuntimeStatus
  opcUa: OpcUaServerRuntimeStatus
  history: HistoryRuntimeStatus
  ruleEngine: RuleEngineRuntimeStatus
  flowRuleEngine: RuleEngineRuntimeStatus
  scheduler: RuntimeSchedulerStatus
  system: SystemResourceStatus
}

export interface GatewayHealthResponse {
  success: boolean
  status: string
  service: string
  version: string
  timestamp: string
  startedTime: string
  uptimeSeconds: number
  isRunning: boolean
  projectId: string
  projectName: string
  errorMessage: string
  runtime: GatewayHealthRuntimeSummary
  components: GatewayHealthComponent[]
}

export interface GatewayHealthRuntimeSummary {
  deviceCount: number
  onlineDeviceCount: number
  tagCount: number
  goodTagCount: number
  badTagCount: number
  noDataTagCount: number
  recentErrorCount: number
  mqttConnected: boolean
  historyRunning: boolean
  ruleEngineRunning: boolean
}

export interface GatewayHealthComponent {
  name: string
  status: string
  message: string
  data: Record<string, unknown>
}

export interface StorageHealthConfig {
  degradedAvailableMegabytes: number
  unhealthyAvailableMegabytes: number
  degradedAvailablePercent: number
  unhealthyAvailablePercent: number
}

export interface GatewayAuditLogQuery {
  limit?: number
  offset?: number
  target?: string
  outcome?: string
  username?: string
  from?: string
  to?: string
}

export interface GatewayAuditLogEntry {
  timestamp: string
  level: string
  action: string
  target: string
  outcome: string
  userName: string
  role: string
  remoteIpAddress: string
  method: string
  path: string
  traceId: string
  errorMessage: string
  rawDetail: string
  source: string
}

export interface GatewayAuditLogQueryResult {
  limit: number
  offset: number
  returned: number
  hasMore: boolean
  items: GatewayAuditLogEntry[]
}

export interface GatewaySecuritySummary {
  passwordPolicy: GatewayPasswordPolicy
  accountLockout: GatewayAccountLockout
  tls: GatewayTlsSummary
  api: GatewayApiSecuritySummary
  apiTokens: GatewayApiTokenSummary
  secretStorage: GatewaySecretStorageSummary
  certificates: GatewayCertificateManagementSummary
}

export interface GatewayPasswordPolicy {
  enabled: boolean
  minLength: number
  maxLength: number
  requireUppercase: boolean
  requireLowercase: boolean
  requireDigit: boolean
  requireSymbol: boolean
  rejectUsernameInPassword: boolean
}

export interface GatewayAccountLockout {
  enabled: boolean
  maxFailedAttempts: number
  lockoutMinutes: number
  resetFailedCountOnSuccess: boolean
}

export interface GatewayTlsSummary {
  requireHttps: boolean
  enableHttpsRedirection: boolean
  enableHsts: boolean
  hstsMaxAgeDays: number
  httpsPort: number
  minimumProtocol: string
  certificateConfigured: boolean
}

export interface GatewayApiSecuritySummary {
  requireAuthenticationForHealth: boolean
  auditUnauthorizedRequests: boolean
  auditForbiddenRequests: boolean
  auditConfigurationRequestHash: boolean
  maxAuditedBodyBytes: number
}

export interface GatewayApiTokenSummary {
  enabled: boolean
  headerName: string
  requireHttps: boolean
  configuredTokenCount: number
  enabledTokenCount: number
}

export interface GatewaySecretStorageSummary {
  enabled: boolean
  environmentVariableName: string
  masterKeyConfigured: boolean
}

export interface GatewayCertificateManagementSummary {
  includeTlsCertificate: boolean
  includeOpcUaCertificateStore: boolean
  expiringSoonDays: number
}

export interface GatewayCertificateInventory {
  expiringSoonDays: number
  totalCount: number
  healthyCount: number
  expiringSoonCount: number
  expiredCount: number
  certificates: GatewayCertificateInfo[]
}

export interface GatewayCertificateInfo {
  source: string
  path: string
  subject: string
  issuer: string
  thumbprint: string
  serialNumber: string
  notBefore: string
  notAfter: string
  daysRemaining: number
  hasPrivateKey: boolean
  state: string
  errorMessage: string
}

export interface GatewayPermissionInfo {
  key: string
  name: string
  group: string
  page: string
  action: string
  description: string
}

export interface GatewayRoleInfo {
  id: string
  name: string
  displayName: string
  description: string
  enabled: boolean
  isSystem: boolean
  permissions: string[]
  userCount: number
  createdTime: string
  updatedTime: string
}

export interface GatewayRoleSaveRequest {
  name: string
  displayName: string
  description: string
  enabled: boolean
  permissions: string[]
}

export interface GatewayUserInfo {
  id: string
  username: string
  displayName: string
  role: string
  enabled: boolean
  createdTime: string
}

export interface GatewayCurrentUserResponse {
  success: boolean
  user: GatewayUserInfo | null
  permissions: string[]
}

export interface GatewayUserSaveRequest {
  username: string
  displayName: string
  role: string
  enabled: boolean
  password: string
}

export interface GatewayUserPasswordResetRequest {
  newPassword: string
}

export interface CircuitBreakerStatus {
  name: string
  enabled: boolean
  state: string
  isOpen: boolean
  isHalfOpen: boolean
  consecutiveFailures: number
  consecutiveSuccesses: number
  totalFailures: number
  totalSuccesses: number
  totalTrips: number
  totalRejected: number
  openedTime: string
  nextRetryTime: string
  lastFailureTime: string
  lastFailureMessage: string
  degradedMode: string
}

export interface DeviceRuntimeStatus {
  deviceId: string
  deviceName: string
  protocol: string
  enabled: boolean
  isConnected: boolean
  isPolling: boolean
  status: string
  consecutiveFailures: number
  totalReads: number
  successfulReads: number
  failedReads: number
  successRate: number
  lastPollTime: string
  lastSuccessTime: string
  lastFailureTime: string
  nextReconnectTime: string
  lastReconnectDelayMs: number
  nextPollTime: string
  currentTaskId: number
  lastTaskStatus: string
  lastTaskDurationMs: number
  slowPollCount: number
  timeoutCount: number
  lastError: string
  protocolCircuitBreaker: CircuitBreakerStatus
}

export interface TagValueSnapshot {
  deviceId: string
  deviceProtocol: string
  groupId: string
  tagId: string
  deviceName: string
  groupName: string
  tagName: string
  rawValueText: string
  valueText: string
  unit: string
  pointCode: string
  assetPath: string
  businessType: string
  source: string
  precision: number
  dataType: string
  mqttPublishEnabled: boolean
  cleaningApplied: boolean
  cleaningAction: string
  cleaningMessage: string
  quality: string
  timestamp: string
  errorMessage: string
}

export interface RuntimeErrorDetail {
  category: string
  deviceName: string
  groupName: string
  tagName: string
  message: string
  suggestion: string
  source: string
  timestamp: string
}

export interface RuntimeEventEnvelope<T> {
  sequence: number
  type: string
  timestamp: string
  data: T
}

export interface RuntimeTagsChangedEvent {
  tags: TagValueSnapshot[]
  pendingCount: number
}

export interface RuntimeDevicesChangedEvent {
  devices: DeviceRuntimeStatus[]
  removedDeviceKeys: string[]
}

export interface RuntimeStatusPatchEvent {
  status: Partial<GatewayStatus>
}

export interface SystemResourceStatus {
  cpuUsagePercent: number
  memoryUsagePercent: number
  totalMemoryBytes: number
  availableMemoryBytes: number
  usedMemoryBytes: number
  processWorkingSetBytes: number
  threadPoolAvailableWorkerThreads: number
  threadPoolMaxWorkerThreads: number
  threadPoolAvailableCompletionPortThreads: number
  threadPoolMaxCompletionPortThreads: number
  threadPoolWorkerUtilizationPercent: number
  sampleTime: string
  source: string
}

export interface RuntimeSchedulerStatus {
  isolationStrategy: string
  healthStatus: string
  healthMessage: string
  maxConcurrentDevicePolls: number
  schedulerIntervalMs: number
  backpressureEnabled: boolean
  backpressureActive: boolean
  queueHighWatermark: number
  queueLowWatermark: number
  backpressureDelayMs: number
  maxDevicePollsQueuedPerSchedulerTick: number
  slowPollThresholdMs: number
  pollTimeoutMs: number
  totalQueued: number
  totalStarted: number
  totalCompleted: number
  totalFailed: number
  totalSlow: number
  totalBackpressureThrottled: number
  totalRateLimited: number
  tagValueChangedPendingCount: number
  tagValueChangedQueueLimit: number
  tagValueChangedMaxObservedPendingCount: number
  totalTagValueChangedQueued: number
  totalTagValueChangedDispatched: number
  totalTagValueChangedDropped: number
  queue: RuntimePollingQueueStatus
  timeout: RuntimeTimeoutStats
  tasks: RuntimePollingTaskStatus[]
}

export interface RuntimePollingQueueStatus {
  pendingCount: number
  runningCount: number
  queueLimit: number
  highWatermark: number
  lowWatermark: number
  utilizationPercent: number
  backpressureActive: boolean
  availableWorkers: number
  rejectedCount: number
  backpressureThrottledCount: number
  rateLimitedCount: number
  maxObservedPendingCount: number
  lastBackpressureTime: string
  lastBackpressureMessage: string
}

export interface RuntimeTimeoutStats {
  pollTimeoutCount: number
  readTimeoutCount: number
  lastTimeoutTime: string
  lastTimeoutDeviceName: string
  lastTimeoutMessage: string
}

export interface RuntimePollingTaskStatus {
  deviceId: string
  deviceName: string
  taskId: number
  status: string
  isQueued: boolean
  isRunning: boolean
  queuedTime: string
  startedTime: string
  finishedTime: string
  lastDurationMs: number
  slowPollCount: number
  timeoutCount: number
  lastError: string
}

export interface ProjectConfig {
  projectId: string
  name: string
  devices: DeviceConfig[]
  rules: EdgeRuleConfig[]
  flowRules: FlowRuleDefinition[]
}

export interface EdgeRuleConfig {
  id: string
  name: string
  enabled: boolean
  conditionType: string
  sourcePointCode: string
  sourceDeviceName: string
  sourceGroupName: string
  sourceTagName: string
  sourceDataType: string
  lowLimit: number
  highLimit: number
  deadband: number
  rateLimitPerSecond: number
  operator: string
  compareValue: number
  logicalOperator: string
  conditions: EdgeRuleCondition[]
  durationSeconds: number
  publishToMqtt: boolean
  publishOnClear: boolean
  publishTopicTemplate: string
  publishQos: number
  activeMessage: string
  clearMessage: string
  transformMultiplier?: number
  transformOffset?: number
  transformUseAbsolute?: boolean
  transformExpression?: string
  transformTimeoutMilliseconds?: number
  qualityOperator?: string
  qualityValues?: string
  windowStatistic?: string
  windowSeconds?: number
  windowSampleCount?: number
  aggregationStatistic?: string
  trendMode?: string
  trendWindowSeconds?: number
  trendSampleCount?: number
  trendMinSlopePerSecond?: number
  trendChangeThreshold?: number
  trendStableDeadband?: number
  stateName?: string
  stateExpectedValue?: string
  stateClearValue?: string
  stateTimeoutSeconds?: number
  relatedDeviceName?: string
  relatedGroupName?: string
  relatedTagName?: string
  relatedPointCode?: string
  relatedDataType?: string
  relationOperator?: string
  relationMultiplier?: number
  relationOffset?: number
  contextName?: string
  contextExpectedValue?: string
  contextOperator?: string
  contextDeviceName?: string
  contextGroupName?: string
  contextTagName?: string
  contextPointCode?: string
  contextDataType?: string
  cycleStartValue?: string
  cycleEndValue?: string
  cycleMinSeconds?: number
  cycleMaxSeconds?: number
  taktTargetSeconds?: number
  taktTolerancePercent?: number
  anomalyMode?: string
  anomalyThreshold?: number
  anomalyBaselineWindowSeconds?: number
  anomalyBaselineSampleCount?: number
  modelPurpose?: string
  modelPath?: string
  modelInputTags?: string
  modelInputName?: string
  modelInputNames?: string
  modelOutputName?: string
  modelOutputIndex?: number
  modelOperator?: string
  modelThreshold?: number
  modelTimeoutMilliseconds?: number
  alarmSeverity?: string
  alarmSuppressSeconds?: number
  alarmReTriggerSeconds?: number
  alarmEscalateAfterSeconds?: number
  actionDelaySeconds?: number
  actionCooldownSeconds?: number
  actionMaxPerMinute?: number
  sequenceWindowSeconds?: number
  sequenceStepTimeoutSeconds?: number
  sequenceMinIntervalSeconds?: number
  sequenceResetOnMismatch?: boolean
  clearDurationSeconds?: number
  description: string
  actions?: EdgeRuleActionConfig[]
}

export interface EdgeRuleActionConfig {
  id: string
  actionType: string
  enabled: boolean
  executeOnActive: boolean
  executeOnClear: boolean
  topicTemplate: string
  qos: number
  activeMessage: string
  clearMessage: string
  emailSmtpHost: string
  emailSmtpPort: number
  emailEnableSsl: boolean
  emailUsername: string
  emailPassword: string
  emailFrom: string
  emailTo: string
  emailCc: string
  emailSubjectTemplate: string
  emailBodyTemplate: string
  webhookUrl: string
  webhookMethod: string
  webhookHeaders: string
  webhookBodyTemplate: string
  webhookContentType: string
  webhookTimeoutSeconds: number
  webhookRetryCount: number
  debugLabel: string
}

export interface EdgeRuleCondition {
  id: string
  sourcePointCode: string
  sourceDeviceName: string
  sourceGroupName: string
  sourceTagName: string
  sourceDataType: string
  operator: string
  compareValue: number
  transformMultiplier?: number
  transformOffset?: number
  transformUseAbsolute?: boolean
  transformExpression?: string
}

export interface FlowRuleDefinition {
  id: string
  name: string
  description: string
  enabled: boolean
  version: number
  lifecycleState: string
  publishedVersion: number
  publishedTime: string
  publishedBy: string
  mode: string
  compiledRuleId: string
  nodes: FlowRuleNode[]
  edges: FlowRuleEdge[]
  createdTime: string
  updatedTime: string
}

export interface FlowRuleNode {
  id: string
  nodeType: string
  label: string
  x: number
  y: number
  deviceName: string
  groupName: string
  tagName: string
  pointCode: string
  dataType: string
  conditionType: string
  operator: string
  compareValue: number
  lowLimit: number
  highLimit: number
  deadband: number
  rateLimitPerSecond: number
  hysteresisMode: string
  hysteresisOnValue: number
  hysteresisOffValue: number
  expression: string
  alarmLevels: FlowRuleAlarmLevel[]
  qualityOperator: string
  qualityValues: string
  windowStatistic: string
  windowSeconds: number
  windowSampleCount: number
  aggregationStatistic: string
  trendMode: string
  trendWindowSeconds: number
  trendSampleCount: number
  trendMinSlopePerSecond: number
  trendChangeThreshold: number
  trendStableDeadband: number
  stateName: string
  stateExpectedValue: string
  stateClearValue: string
  stateTimeoutSeconds: number
  relatedDeviceName: string
  relatedGroupName: string
  relatedTagName: string
  relatedPointCode: string
  relatedDataType: string
  relationOperator: string
  relationMultiplier: number
  relationOffset: number
  contextName: string
  contextExpectedValue: string
  contextOperator: string
  contextDeviceName: string
  contextGroupName: string
  contextTagName: string
  contextPointCode: string
  contextDataType: string
  cycleStartValue: string
  cycleEndValue: string
  cycleMinSeconds: number
  cycleMaxSeconds: number
  taktTargetSeconds: number
  taktTolerancePercent: number
  anomalyMode: string
  anomalyThreshold: number
  anomalyBaselineWindowSeconds: number
  anomalyBaselineSampleCount: number
  modelPurpose: string
  modelPath: string
  modelInputTags: string
  modelInputName: string
  modelInputNames: string
  modelOutputName: string
  modelOutputIndex: number
  modelOperator: string
  modelThreshold: number
  modelTimeoutMilliseconds: number
  alarmSeverity: string
  alarmSuppressSeconds: number
  alarmReTriggerSeconds: number
  alarmEscalateAfterSeconds: number
  actionDelaySeconds: number
  actionCooldownSeconds: number
  actionMaxPerMinute: number
  debugEnabled: boolean
  debugLabel: string
  transformMultiplier: number
  transformOffset: number
  transformUseAbsolute: boolean
  transformExpression: string
  transformTimeoutMilliseconds: number
  sequenceWindowSeconds: number
  sequenceStepTimeoutSeconds: number
  sequenceMinIntervalSeconds: number
  sequenceResetOnMismatch: boolean
  clearDurationSeconds: number
  logicalOperator: string
  durationSeconds: number
  publishToMqtt: boolean
  publishOnClear: boolean
  topicTemplate: string
  publishQos: number
  activeMessage: string
  clearMessage: string
  executeOnActive: boolean
  executeOnClear: boolean
  emailSmtpHost: string
  emailSmtpPort: number
  emailEnableSsl: boolean
  emailUsername: string
  emailPassword: string
  emailFrom: string
  emailTo: string
  emailCc: string
  emailSubjectTemplate: string
  emailBodyTemplate: string
  webhookUrl: string
  webhookMethod: string
  webhookHeaders: string
  webhookBodyTemplate: string
  webhookContentType: string
  webhookTimeoutSeconds: number
  webhookRetryCount: number
}

export interface FlowRuleAlarmLevel {
  id: string
  name: string
  severity: string
  operator: string
  compareValue: number
  message: string
}

export interface FlowRuleEdge {
  id: string
  sourceNodeId: string
  targetNodeId: string
  sourcePort: string
  targetPort: string
}

export interface DeviceConfig {
  id: string
  name: string
  enabled: boolean
  protocol: string
  connection: PlcConnection
  defaultScanRateMs: number
  failureRetryDelayMs: number
  maxFailureRetryDelayMs: number
  tags: TagConfig[]
  groups: GroupConfig[]
}

export interface PlcConnection {
  protocol: string
  host: string
  port: number
  rack: number
  slot: number
  timeoutMilliseconds: number
  wordOrder: string
  transport: string
  dataBits: number
  serialParity: string
  serialStopBits: string
  username: string
  password: string
  certificatePath: string
  certificatePassword: string
  certificateThumbprint: string
  trustStorePath: string
  validateServerCertificate: boolean
  opcDaServerProgId: string
  opcDaGroupName: string
  driverId: string
  driverOptionsJson: string
}

export interface GatewayConnectionParameterDefinition {
  key: string
  label: string
  parameterType: string
  group: string
  defaultValue: string
  placeholder: string
  helpText: string
  unit: string
  required: boolean
  secret: boolean
  advanced: boolean
  readOnly: boolean
  min?: number | null
  max?: number | null
  options: string[]
}

export interface GatewayProtocolCatalogItem {
  driverId: string
  displayName: string
  protocol: string
  category: string
  builtIn: boolean
  signatureStatus: string
  signatureError: string
  parameters: GatewayConnectionParameterDefinition[]
}

export interface GroupConfig {
  id: string
  deviceId: string
  name: string
  enabled: boolean
  scanRateMs: number
  tags: TagConfig[]
}

export interface TagConfig {
  id: string
  deviceId: string
  groupId: string
  name: string
  protocol: string
  address: string
  meterAddress: string
  meterDataIdentifier: string
  meterType: string
  dataType: string
  elementCount: number
  elementOffset: number
  enabled: boolean
  mqttPublishEnabled: boolean
  accessMode: string
  scanRateMs: number
  failureRetryDelayMs: number
  unit: string
  pointCode: string
  assetPath: string
  businessType: string
  source: string
  precision: number
  scaling: ScalingConfig
  cleaning: DataCleaningConfig
  alarm: TagAlarmConfig
  description: string
}

export interface ScalingConfig {
  enabled: boolean
  multiplier: number
  offset: number
  clampEnabled: boolean
  minValue: number
  maxValue: number
  decimalPlaces: number
}

export interface DataCleaningConfig {
  enabled: boolean
  outOfRangeEnabled: boolean
  minValue: number
  maxValue: number
  deadbandEnabled: boolean
  deadband: number
  duplicateFilterEnabled: boolean
  spikeFilterEnabled: boolean
  spikeThreshold: number
  spikeWindowSeconds: number
  enumMappingEnabled: boolean
  enumMappings: DataCleaningEnumMapping[]
  unitConversionEnabled: boolean
  sourceUnit: string
  targetUnit: string
  unitMultiplier: number
  unitOffset: number
  preserveLastGoodOnFilter: boolean
}

export interface DataCleaningEnumMapping {
  rawValue: string
  cleanValue: string
  description: string
}

export interface TagAlarmConfig {
  enabled: boolean
  lowLimit: number
  highLimit: number
  lowAlarmMessage: string
  highAlarmMessage: string
  warningDeviation: number
  lowWarningMessage: string
  highWarningMessage: string
}

export interface MqttStatus {
  enabled: boolean
  connected: boolean
  state: string
  host: string
  port: number
  lastError: string
}

export interface MqttRuntimeStatus {
  enabled: boolean
  gatewayId: string
  gatewayName: string
  siteName: string
  cloudProtocolVersion: string
  configVersion: number
  publishMode: string
  isRunning: boolean
  isConnected: boolean
  broker: string
  subscribeTopic: string
  publishEnabled: boolean
  publishTopicTemplate: string
  publishQos: number
  heartbeatTopic: string
  statusTopic: string
  commandReplyTopicTemplate: string
  sparkplugEnabled: boolean
  sparkplugNamespace: string
  sparkplugGroupId: string
  sparkplugEdgeNodeId: string
  sparkplugNodeBirthTopic: string
  sparkplugNodeDeathTopic: string
  outboxDirectory: string
  outboxQuarantineDirectory: string
  lastError: string
  lastMessage: string
  lastWriteResult: string
  lastPublishResult: string
  lastConnectedTime: string
  lastMessageTime: string
  lastPublishTime: string
  lastPublishFailureTime: string
  lastSparkplugBirthTime: string
  lastSparkplugDeathTime: string
  nextPublishRetryTime: string
  circuitBreaker: CircuitBreakerStatus
  reconnectCount: number
  receivedCount: number
  successfulWrites: number
  failedWrites: number
  publishedCount: number
  failedPublishes: number
  sparkplugBirthCount: number
  sparkplugDeathCount: number
  sparkplugDataCount: number
  outboxPendingCount: number
  outboxEnqueuedCount: number
  outboxBytes: number
  outboxExpiredDeletedCount: number
  outboxOverflowDeletedCount: number
  outboxInvalidMessageCount: number
  outboxQuarantinedMessageCount: number
  outboxQuarantineCount: number
  outboxQuarantineBytes: number
  outboxQuarantineExpiredDeletedCount: number
  outboxOldestPendingTime: string
  outboxNewestPendingTime: string
  outboxOldestQuarantineTime: string
  outboxNewestQuarantineTime: string
  outboxOldestPendingAgeSeconds: number
  publishRetryBackoffSeconds: number
  publishConsecutiveFailureCount: number
}

export interface OpcUaServerRuntimeStatus {
  enabled: boolean
  isRunning: boolean
  applicationName: string
  endpointUrl: string
  namespaceUri: string
  deviceNodeCount: number
  groupNodeCount: number
  tagNodeCount: number
  valueUpdateCount: number
  startedTime: string
  lastReloadTime: string
  lastValueUpdateTime: string
  lastError: string
  lastMessage: string
}

export interface OpcUaServerConfig {
  enabled: boolean
  applicationName: string
  applicationUri: string
  productUri: string
  host: string
  port: number
  endpointPath: string
  endpointUrl: string
  namespaceUri: string
  certificateStorePath: string
  autoAcceptUntrustedCertificates: boolean
  minimumSamplingIntervalMs: number
  publishDiagnostics: boolean
}

export interface HistoryRuntimeStatus {
  enabled: boolean
  isRunning: boolean
  directory: string
  retentionDays: number
  valueFiles: number
  alarmFiles: number
  publishFiles: number
  totalBytes: number
  coldDirectory: string
  tieringEnabled: boolean
  retentionPolicy: string
  hotRetentionDays: number
  coldRetentionDays: number
  storageCompressionEnabled: boolean
  autoCleanupEnabled: boolean
  cleanupIntervalHours: number
  lastCleanupTime: string
  nextCleanupTime: string
  hotFileCount: number
  coldFileCount: number
  compressedFileCount: number
  hotBytes: number
  coldBytes: number
  compressedBytes: number
  dataProcessingEnabled: boolean
  compressionEnabled: boolean
  downsamplingEnabled: boolean
  alignmentEnabled: boolean
  fillEnabled: boolean
  aggregationEnabled: boolean
  receivedValueCount: number
  writtenValueCount: number
  skippedValueCount: number
  compressedValueCount: number
  downsampledValueCount: number
  filledValueCount: number
  aggregatedValueCount: number
  isDegraded: boolean
  lastErrorTime: string
  lastError: string
  circuitBreaker: CircuitBreakerStatus
}

export interface HistoryConfig {
  enabled: boolean
  directory: string
  retentionDays: number
  maxViewRecords: number
  dataProcessing: HistoryDataProcessingConfig
  storage: HistoryStorageConfig
}

export interface HistoryStorageConfig {
  tieringEnabled: boolean
  coldDirectory: string
  retentionPolicy: string
  hotRetentionDays: number
  coldRetentionDays: number
  compressionEnabled: boolean
  compressHotFiles: boolean
  compressColdFiles: boolean
  compressAfterDays: number
  autoCleanupEnabled: boolean
  cleanupIntervalHours: number
  maxStorageMegabytes: number
}

export interface HistoryDataProcessingConfig {
  enabled: boolean
  compressionEnabled: boolean
  compressionTolerance: number
  compressDuplicateText: boolean
  downsamplingEnabled: boolean
  downsamplingIntervalMs: number
  alignmentEnabled: boolean
  alignmentIntervalMs: number
  fillEnabled: boolean
  fillIntervalMs: number
  fillMaxGapSeconds: number
  fillMode: string
  aggregationEnabled: boolean
  aggregationIntervalSeconds: number
  aggregationMethods: string
  maxSyntheticPointsPerInput: number
}

export interface RuleEngineRuntimeStatus {
  isRunning: boolean
  enabled: boolean
  ruleCount: number
  enabledRuleCount: number
  activeRuleCount: number
  cachedSnapshotCount: number
  recentEventCount: number
  evaluationCount: number
  triggeredCount: number
  clearedCount: number
  failedEvaluationCount: number
  lastEvaluationTime: string
  lastEventTime: string
  lastErrorTime: string
  lastError: string
  circuitBreaker: CircuitBreakerStatus
  recentEvents: RuleEngineRuntimeEvent[]
  rules: RuleEngineRuleRuntimeStatus[]
}

export interface RuleEngineRuleRuntimeStatus {
  ruleId: string
  ruleName: string
  conditionType: string
  isActive: boolean
  activeState: string
  lastEvaluationTime: string
  lastTriggeredTime: string
  lastClearedTime: string
  lastErrorTime: string
  lastError: string
  evaluationCount: number
  triggeredCount: number
  clearedCount: number
  failedEvaluationCount: number
  recentEvents: RuleEngineRuntimeEvent[]
}

export interface RuleEngineRuntimeEvent {
  ruleId: string
  ruleName: string
  conditionType: string
  eventType: string
  state: string
  message: string
  topic: string
  pointCode: string
  deviceName: string
  groupName: string
  tagName: string
  value: number
  threshold: number
  timestamp: string
}

export interface SyncPayload {
  status: GatewayStatus
  project: ProjectConfig
  mqtt: Record<string, unknown>
  opcUa: OpcUaServerConfig
  history: HistoryConfig
  storageHealth: StorageHealthConfig
}

export interface WriteTagCommand {
  deviceName: string
  groupName: string
  tagName: string
  dataType: string
  valueText: string
  timeoutMilliseconds?: number
}

export interface WriteTagResult {
  success: boolean
  deviceName: string
  groupName: string
  tagName: string
  dataType: string
  quality: string
  timestamp: string
  errorMessage: string
  currentValueText: string
}

export interface GatewayUpdatePackageManifest {
  manifestVersion: number
  packageId: string
  product: string
  packageType: string
  version: string
  minVersion: string
  createdTime: string
  buildId: string
  entryDirectory: string
  requiresRestart: boolean
  description: string
  hashAlgorithm: string
  files: GatewayUpdatePackageFileDigest[]
  signatureAlgorithm: string
  signature: string
  signer: string
  signedTime?: string | null
}

export interface GatewayUpdatePackageFileDigest {
  path: string
  sha256: string
  sizeBytes: number
}

export interface GatewayUpdatePackageRecord {
  packageId: string
  packageType: string
  version: string
  fileName: string
  storedPath: string
  sha256: string
  sizeBytes: number
  uploadedTime: string
  status: string
  errorMessage: string
  manifestVersion: number
  fileCount: number
  contentHashValid: boolean
  signatureValid: boolean
  trustStatus: string
  trustMessage: string
  signer: string
  signedTime?: string | null
  manifest: GatewayUpdatePackageManifest
}

export interface GatewayRollbackPoint {
  rollbackId: string
  version: string
  sourcePackageId: string
  createdTime: string
  directory: string
  sizeBytes: number
  fileCount: number
}

export interface GatewayPendingUpdateAction {
  actionId: string
  actionType: string
  packageId: string
  rollbackId: string
  version: string
  packagePath: string
  sourceDirectory: string
  targetDirectory: string
  rollbackDirectory: string
  scriptPath: string
  requiresServiceRestart: boolean
  createdTime: string
  status: string
}

export interface GatewayUpdateStatus {
  enabled: boolean
  productId: string
  currentVersion: string
  installDirectory: string
  updateDirectory: string
  offlineScriptPath: string
  requirePackageFileDigests: boolean
  requirePackageSignature: boolean
  trustedPackagePublicKeyConfigured: boolean
  pendingAction?: GatewayPendingUpdateAction | null
  packages: GatewayUpdatePackageRecord[]
  rollbackPoints: GatewayRollbackPoint[]
}

export interface GatewayPrepareUpdateResult {
  prepared: boolean
  message: string
  pendingAction: GatewayPendingUpdateAction
}

export interface GatewaySupportRuntimeSummary {
  isRunning: boolean
  projectId: string
  projectName: string
  configurationStore: string
  deviceCount: number
  onlineDeviceCount: number
  tagCount: number
  goodTagCount: number
  badTagCount: number
  noDataTagCount: number
  startedTime: string
  lastReloadTime: string
}

export interface GatewaySupportComponent {
  name: string
  status: string
  message: string
  data: Record<string, unknown>
}

export interface GatewaySupportAuditEntry {
  timestamp: string
  action: string
  outcome: string
  target: string
  userName: string
  path: string
  traceId: string
  errorMessage: string
}

export interface GatewaySupportSnapshot {
  snapshotId: string
  traceId: string
  capturedTimeUtc: string
  capturedBy: string
  productId: string
  version: string
  environmentName: string
  machineName: string
  processId: number
  runtime: GatewaySupportRuntimeSummary
  components: GatewaySupportComponent[]
  recentErrors: RuntimeErrorDetail[]
  recentAudit: GatewaySupportAuditEntry[]
  auditDetailsIncluded: boolean
  recommendedActions: string[]
}

export interface GatewayWatchdogCheckResult {
  name: string
  state: string
  message: string
  observedTime: string
  recoveryRecommended: boolean
}

export interface GatewayWatchdogRecoveryEvent {
  timestamp: string
  action: string
  outcome: string
  reason: string
  errorMessage: string
}

export interface GatewayRestartProtectionStatus {
  recentRecoveryCount: number
  recentHostRestartRequestCount: number
  recoveryBlocked: boolean
  hostRestartBlocked: boolean
  windowStartTime: string
  nextAllowedRecoveryTime: string
}

export interface GatewayWatchdogStatus {
  enabled: boolean
  state: string
  startedTime: string
  lastCheckTime: string
  lastHealthyTime: string
  lastRecoveryTime: string
  lastIssue: string
  checkCount: number
  recoveryAttemptCount: number
  recoverySuccessCount: number
  recoveryFailureCount: number
  blockedRecoveryCount: number
  hostRestartRequestCount: number
  checks: GatewayWatchdogCheckResult[]
  recentEvents: GatewayWatchdogRecoveryEvent[]
  restartProtection: GatewayRestartProtectionStatus
}

export interface GatewayWatchdogConfig {
  enabled: boolean
  checkIntervalSeconds: number
  startupGraceSeconds: number
  runtimeNoProgressSeconds: number
  recoveryCooldownSeconds: number
  recoveryTimeoutSeconds: number
  maxRecoveriesPerWindow: number
  recoveryWindowMinutes: number
  maxHostRestartRequestsPerWindow: number
  hostRestartProtectionWindowMinutes: number
  requestHostStopOnUnrecoverable: boolean
  stateDirectory: string
  monitorMqtt: boolean
  mqttDisconnectedSeconds: number
  monitorHistory: boolean
  monitorRuleEngine: boolean
  monitorOpcUa: boolean
  monitorScheduler: boolean
}

export interface GatewayDeviceTemplateSummary {
  templateId: string
  name: string
  protocol: string
  description: string
  groupCount: number
  tagCount: number
}

export interface GatewayDeviceTemplateApplyRequest {
  deviceName: string
  host: string
  port: number
  groupName: string
  defaultScanRateMs: number
}

export interface GatewayDeviceTemplateApplyResult {
  device: DeviceConfig
  addedGroupCount: number
  addedTagCount: number
}

export interface GatewayTagImportResult {
  totalRows: number
  addedCount: number
  updatedCount: number
  warnings: string[]
}

export interface GatewayLicenseStatus {
  configured: boolean
  valid: boolean
  operational: boolean
  expired: boolean
  signatureVerified: boolean
  requireValidLicense: boolean
  productId: string
  customerName: string
  edition: string
  serialNumber: string
  expiresUtc: string
  maxDevices: number
  maxTags: number
  features: string[]
  status: string
  message: string
}

export interface GatewayCompatibilityMatrix {
  gatewayVersion: string
  configurationSchemaVersion: string
  backupSchemaVersion: string
  minimumSupportedPluginManifestVersion: string
  items: GatewayCompatibilityItem[]
}

export interface GatewayCompatibilityItem {
  capability: string
  currentVersion: string
  compatibleRange: string
  status: string
  notes: string
}

export interface GatewayProtocolDriverInfo {
  driverId: string
  displayName: string
  protocol: string
  version: string
  minGatewayVersion: string
  maxGatewayVersion: string
  assemblyPath: string
  assemblySha256: string
  signatureStatus: string
  signatureError: string
  signer: string
  loadContextId: string
  builtIn: boolean
}

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  let response: Response
  try {
    response = await fetch(url, {
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
        ...(init?.headers ?? {})
      },
      ...init
    })
  } catch (error) {
    if (isAbortError(error)) throw error
    throw new Error(localizeErrorMessage(error instanceof Error ? error.message : undefined) || '网络请求失败，请检查后端服务。')
  }

  if (!response.ok) {
    const payload = await response.json().catch(() => undefined) as { errorMessage?: string } | undefined
    const message = localizeErrorMessage(payload?.errorMessage)
    throw new Error(response.status === 401 ? message || '登录已过期，请重新登录' : message || `请求失败（HTTP ${response.status}）`)
  }

  return (await response.json()) as T
}

async function requestHealth<T>(url: string, signal?: AbortSignal): Promise<T> {
  let response: Response
  try {
    response = await fetch(url, {
      credentials: 'include',
      signal,
      headers: {
        Accept: 'application/json'
      }
    })
  } catch (error) {
    if (isAbortError(error)) throw error
    throw new Error(localizeErrorMessage(error instanceof Error ? error.message : undefined) || '网络请求失败，请检查后端服务。')
  }

  if (response.status === 401) {
    throw new Error('登录已过期，请重新登录')
  }

  const payload = await response.json().catch(() => undefined)
  if (!response.ok && response.status !== 503) {
    throw new Error(`请求失败（HTTP ${response.status}）`)
  }

  return payload as T
}

export async function login(username: string, password: string) {
  return request<{ success: boolean; token: string; errorMessage?: string }>('/api/auth/login', {
    method: 'POST',
    body: JSON.stringify({ username, password })
  })
}

export async function logout() {
  return request<{ success: boolean }>('/api/auth/logout', { method: 'POST' })
}

export async function loadCurrentUser() {
  return request<GatewayCurrentUserResponse>('/api/auth/me')
}

export async function changeCurrentPassword(currentPassword: string, newPassword: string) {
  const result = await request<ApiResult<{ changed: boolean }>>('/api/auth/password', {
    method: 'PUT',
    body: JSON.stringify({ currentPassword, newPassword })
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '密码修改失败')
  return result.data
}

export async function loadUsers() {
  const result = await request<ApiResult<GatewayUserInfo[]>>('/api/auth/users')
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '人员加载失败')
  return result.data
}

export async function createUser(user: GatewayUserSaveRequest) {
  const result = await request<ApiResult<GatewayUserInfo>>('/api/auth/users', {
    method: 'POST',
    body: JSON.stringify(user)
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '人员新增失败')
  return result.data
}

export async function updateUser(username: string, user: GatewayUserSaveRequest) {
  const result = await request<ApiResult<GatewayUserInfo>>(`/api/auth/users/${encodeURIComponent(username)}`, {
    method: 'PUT',
    body: JSON.stringify(user)
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '人员更新失败')
  return result.data
}

export async function resetUserPassword(username: string, payload: GatewayUserPasswordResetRequest) {
  const result = await request<ApiResult<GatewayUserInfo>>(`/api/auth/users/${encodeURIComponent(username)}/password`, {
    method: 'PUT',
    body: JSON.stringify(payload)
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '密码重置失败')
  return result.data
}

export async function deleteUser(username: string) {
  const result = await request<ApiResult<{ deleted: boolean }>>(`/api/auth/users/${encodeURIComponent(username)}`, {
    method: 'DELETE'
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '人员删除失败')
  return result.data
}

export async function loadRolePermissions() {
  const result = await request<ApiResult<GatewayPermissionInfo[]>>('/api/auth/permissions')
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '角色权限加载失败')
  return result.data
}

export async function loadRoles() {
  const result = await request<ApiResult<GatewayRoleInfo[]>>('/api/auth/roles')
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '角色加载失败')
  return result.data
}

export async function createRole(role: GatewayRoleSaveRequest) {
  const result = await request<ApiResult<GatewayRoleInfo>>('/api/auth/roles', {
    method: 'POST',
    body: JSON.stringify(role)
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '角色新增失败')
  return result.data
}

export async function updateRole(roleName: string, role: GatewayRoleSaveRequest) {
  const result = await request<ApiResult<GatewayRoleInfo>>(`/api/auth/roles/${encodeURIComponent(roleName)}`, {
    method: 'PUT',
    body: JSON.stringify(role)
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '角色更新失败')
  return result.data
}

export async function updateRolePermissions(roleName: string, permissions: string[]) {
  const result = await request<ApiResult<GatewayRoleInfo>>(`/api/auth/roles/${encodeURIComponent(roleName)}/permissions`, {
    method: 'PUT',
    body: JSON.stringify({ permissions })
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '权限分配保存失败')
  return result.data
}

export async function deleteRole(roleName: string) {
  const result = await request<ApiResult<{ deleted: boolean }>>(`/api/auth/roles/${encodeURIComponent(roleName)}`, {
    method: 'DELETE'
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '角色删除失败')
  return result.data
}

export async function loadSync(signal?: AbortSignal) {
  const result = await request<ApiResult<SyncPayload>>('/api/config/sync', { signal })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '同步运行数据失败')
  return result.data
}

export async function loadStatus(signal?: AbortSignal) {
  const result = await request<ApiResult<GatewayStatus>>('/api/config/status', { signal })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || 'Runtime status failed to load')
  return result.data
}

export async function loadProtocolCatalog() {
  const result = await request<ApiResult<GatewayProtocolCatalogItem[]>>('/api/config/protocols')
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || 'Protocol catalog failed to load')
  return result.data
}

export async function loadReadyHealth(signal?: AbortSignal) {
  return requestHealth<GatewayHealthResponse>('/api/health/ready', signal)
}

export async function loadStorageHealth() {
  const result = await request<ApiResult<StorageHealthConfig>>('/api/config/storage-health')
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '历史库健康配置加载失败')
  return result.data
}

export async function loadAuditLogs(query: GatewayAuditLogQuery = {}) {
  const params = buildAuditLogParams(query)
  const suffix = params.toString() ? `?${params.toString()}` : ''
  const result = await request<ApiResult<GatewayAuditLogQueryResult>>(`/api/config/audit${suffix}`)
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '审计日志加载失败')
  return result.data
}

export async function exportAuditLogs(query: GatewayAuditLogQuery = {}) {
  const params = buildAuditLogParams(query)
  const suffix = params.toString() ? `?${params.toString()}` : ''
  let response: Response
  try {
    response = await fetch(`/api/config/audit/export${suffix}`, {
      credentials: 'include',
      headers: {
        Accept: 'text/csv'
      }
    })
  } catch (error) {
    if (isAbortError(error)) throw error
    throw new Error(localizeErrorMessage(error instanceof Error ? error.message : undefined) || '网络请求失败，请检查后端服务。')
  }

  if (!response.ok) {
    throw new Error(response.status === 401 ? '登录已过期，请重新登录' : `导出失败（HTTP ${response.status}）`)
  }

  return await response.blob()
}

export async function loadSecuritySummary() {
  const result = await request<ApiResult<GatewaySecuritySummary>>('/api/security/summary')
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '工业安全配置加载失败')
  return result.data
}

export async function loadSecurityCertificates() {
  const result = await request<ApiResult<GatewayCertificateInventory>>('/api/security/certificates')
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '证书状态加载失败')
  return result.data
}

export async function loadUpdateStatus() {
  const result = await request<ApiResult<GatewayUpdateStatus>>('/api/maintenance/updates/status')
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '安装升级状态加载失败')
  return result.data
}

export async function loadSupportSnapshot() {
  const result = await request<ApiResult<GatewaySupportSnapshot>>('/api/maintenance/support/snapshot')
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '售后快照生成失败')
  return result.data
}

export async function uploadUpdatePackage(file: File) {
  const form = new FormData()
  form.append('file', file)
  let response: Response
  try {
    response = await fetch('/api/maintenance/updates/packages', {
      method: 'POST',
      credentials: 'include',
      body: form
    })
  } catch (error) {
    if (isAbortError(error)) throw error
    throw new Error(localizeErrorMessage(error instanceof Error ? error.message : undefined) || '升级包上传失败')
  }

  const result = await response.json().catch(() => undefined) as ApiResult<GatewayUpdatePackageRecord> | undefined
  if (!response.ok || !result?.success) {
    throw new Error(localizeErrorMessage(result?.errorMessage) || `升级包上传失败（HTTP ${response.status}）`)
  }
  return result.data
}

export async function prepareUpdatePackage(packageId: string) {
  const result = await request<ApiResult<GatewayPrepareUpdateResult>>(`/api/maintenance/updates/packages/${encodeURIComponent(packageId)}/prepare`, {
    method: 'POST'
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '离线升级准备失败')
  return result.data
}

export async function prepareUpdateRollback(rollbackId: string) {
  const result = await request<ApiResult<GatewayPrepareUpdateResult>>(`/api/maintenance/updates/rollback/${encodeURIComponent(rollbackId)}/prepare`, {
    method: 'POST'
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '版本回滚准备失败')
  return result.data
}

export async function loadWatchdogStatus() {
  const result = await request<ApiResult<GatewayWatchdogStatus>>('/api/maintenance/watchdog/status')
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '看门狗状态加载失败')
  return result.data
}

export async function loadWatchdogConfig() {
  const result = await request<ApiResult<GatewayWatchdogConfig>>('/api/maintenance/watchdog/config')
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '看门狗配置加载失败')
  return result.data
}

export async function saveWatchdogConfig(options: GatewayWatchdogConfig) {
  const result = await request<ApiResult<GatewayWatchdogConfig>>('/api/maintenance/watchdog/config', {
    method: 'PUT',
    body: JSON.stringify(options)
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '看门狗配置保存失败')
  return result.data
}

export async function loadDeviceTemplates() {
  const result = await request<ApiResult<GatewayDeviceTemplateSummary[]>>('/api/commercial/device-templates')
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || 'Device templates failed to load')
  return result.data
}

export async function applyDeviceTemplate(templateId: string, payload: GatewayDeviceTemplateApplyRequest) {
  const result = await request<ApiResult<GatewayDeviceTemplateApplyResult>>(`/api/commercial/device-templates/${encodeURIComponent(templateId)}/apply`, {
    method: 'POST',
    body: JSON.stringify(payload)
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || 'Device template apply failed')
  return result.data
}

export async function exportTagsCsv(deviceId = '') {
  const suffix = deviceId ? `?deviceId=${encodeURIComponent(deviceId)}` : ''
  let response: Response
  try {
    response = await fetch(`/api/commercial/tags/export${suffix}`, {
      credentials: 'include',
      headers: { Accept: 'text/csv' }
    })
  } catch (error) {
    if (isAbortError(error)) throw error
    throw new Error(localizeErrorMessage(error instanceof Error ? error.message : undefined) || 'Tag export failed')
  }
  if (!response.ok) throw new Error(`Tag export failed (HTTP ${response.status})`)
  return await response.blob()
}

export async function importTagsCsv(csv: string, deviceId = '') {
  const suffix = deviceId ? `?deviceId=${encodeURIComponent(deviceId)}` : ''
  const result = await request<ApiResult<GatewayTagImportResult>>(`/api/commercial/tags/import${suffix}`, {
    method: 'POST',
    headers: { 'Content-Type': 'text/csv' },
    body: csv
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || 'Tag import failed')
  return result.data
}

export async function exportProjectBackup() {
  let response: Response
  try {
    response = await fetch('/api/commercial/project/backup', {
      credentials: 'include',
      headers: { Accept: 'application/json' }
    })
  } catch (error) {
    if (isAbortError(error)) throw error
    throw new Error(localizeErrorMessage(error instanceof Error ? error.message : undefined) || 'Project backup failed')
  }
  if (!response.ok) throw new Error(`Project backup failed (HTTP ${response.status})`)
  return await response.blob()
}

export async function restoreProjectBackup(json: string) {
  const result = await request<ApiResult<{ backupId: string; projectId: string; projectName: string; deviceCount: number; tagCount: number }>>('/api/commercial/project/restore', {
    method: 'POST',
    body: json
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || 'Project restore failed')
  return result.data
}

export async function loadLicenseStatus() {
  const result = await request<ApiResult<GatewayLicenseStatus>>('/api/commercial/license')
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || 'License status failed to load')
  return result.data
}

export async function loadCompatibilityMatrix() {
  const result = await request<ApiResult<GatewayCompatibilityMatrix>>('/api/commercial/compatibility')
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || 'Compatibility matrix failed to load')
  return result.data
}

export async function loadProtocolDrivers() {
  const result = await request<ApiResult<GatewayProtocolDriverInfo[]>>('/api/commercial/drivers')
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || 'Protocol driver status failed to load')
  return result.data
}

function buildAuditLogParams(query: GatewayAuditLogQuery = {}) {
  const params = new URLSearchParams()
  if (query.limit) params.set('limit', String(query.limit))
  if (query.offset) params.set('offset', String(query.offset))
  if (query.target) params.set('target', query.target)
  if (query.outcome) params.set('outcome', query.outcome)
  if (query.username) params.set('username', query.username)
  if (query.from) params.set('from', query.from)
  if (query.to) params.set('to', query.to)
  return params
}

export async function loadTagSnapshots(query: Partial<Pick<TagValueSnapshot, 'deviceId' | 'deviceName' | 'groupId' | 'groupName' | 'tagId' | 'tagName'>> = {}) {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) {
    if (value) params.set(key, value)
  }

  const suffix = params.toString() ? `?${params.toString()}` : ''
  const result = await request<ApiResult<TagValueSnapshot[]>>(`/api/config/status/tags${suffix}`)
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '标签实时值加载失败')
  return result.data
}

export async function saveProject(project: ProjectConfig) {
  const result = await request<ApiResult<ProjectConfig>>('/api/config/project', {
    method: 'PUT',
    body: JSON.stringify(project)
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '保存失败')
  return result.data
}

export async function loadDevices() {
  const result = await request<ApiResult<DeviceConfig[]>>('/api/config/devices')
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '设备加载失败')
  return result.data
}

export async function createDevice(device: DeviceConfig) {
  const result = await request<ApiResult<DeviceConfig>>('/api/config/devices', {
    method: 'POST',
    body: JSON.stringify(device)
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '设备新增失败')
  return result.data
}

export async function updateDevice(deviceId: string, device: DeviceConfig) {
  const result = await request<ApiResult<DeviceConfig>>(`/api/config/devices/${encodeURIComponent(deviceId)}`, {
    method: 'PUT',
    body: JSON.stringify(device)
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '设备更新失败')
  return result.data
}

export async function deleteDevice(deviceId: string) {
  const result = await request<ApiResult<DeviceConfig>>(`/api/config/devices/${encodeURIComponent(deviceId)}`, {
    method: 'DELETE'
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '设备删除失败')
  return result.data
}

export async function createGroup(deviceId: string, group: GroupConfig) {
  const result = await request<ApiResult<GroupConfig>>(`/api/config/devices/${encodeURIComponent(deviceId)}/groups`, {
    method: 'POST',
    body: JSON.stringify(group)
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '分组新增失败')
  return result.data
}

export async function updateGroup(groupId: string, group: GroupConfig) {
  const result = await request<ApiResult<GroupConfig>>(`/api/config/groups/${encodeURIComponent(groupId)}`, {
    method: 'PUT',
    body: JSON.stringify(group)
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '分组更新失败')
  return result.data
}

export async function deleteGroup(groupId: string) {
  const result = await request<ApiResult<GroupConfig>>(`/api/config/groups/${encodeURIComponent(groupId)}`, {
    method: 'DELETE'
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '分组删除失败')
  return result.data
}

export async function createDeviceTag(deviceId: string, tag: TagConfig) {
  const result = await request<ApiResult<TagConfig>>(`/api/config/devices/${encodeURIComponent(deviceId)}/tags`, {
    method: 'POST',
    body: JSON.stringify(tag)
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '标签新增失败')
  return result.data
}

export async function createGroupTag(groupId: string, tag: TagConfig) {
  const result = await request<ApiResult<TagConfig>>(`/api/config/groups/${encodeURIComponent(groupId)}/tags`, {
    method: 'POST',
    body: JSON.stringify(tag)
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '标签新增失败')
  return result.data
}

export async function updateTag(tagId: string, tag: TagConfig) {
  const result = await request<ApiResult<TagConfig>>(`/api/config/tags/${encodeURIComponent(tagId)}`, {
    method: 'PUT',
    body: JSON.stringify(tag)
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '标签更新失败')
  return result.data
}

export async function deleteTag(tagId: string) {
  const result = await request<ApiResult<TagConfig>>(`/api/config/tags/${encodeURIComponent(tagId)}`, {
    method: 'DELETE'
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '标签删除失败')
  return result.data
}

export async function writeTag(command: WriteTagCommand) {
  const result = await request<ApiResult<WriteTagResult>>('/api/config/tags/write', {
    method: 'POST',
    body: JSON.stringify(command)
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '标签写入失败')
  return result.data
}

export async function saveMqtt(options: Record<string, unknown>) {
  const result = await request<ApiResult<Record<string, unknown>>>('/api/config/mqtt', {
    method: 'PUT',
    body: JSON.stringify(options)
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '保存失败')
  return result.data
}

export async function saveOpcUa(options: OpcUaServerConfig) {
  const result = await request<ApiResult<OpcUaServerConfig>>('/api/config/opcua', {
    method: 'PUT',
    body: JSON.stringify(options)
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '保存 OPC UA Server 配置失败')
  return result.data
}

export async function loadHistoryConfig() {
  const result = await request<ApiResult<HistoryConfig>>('/api/config/history')
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '历史库配置加载失败')
  return result.data
}

export async function saveHistory(options: HistoryConfig) {
  const result = await request<ApiResult<HistoryConfig>>('/api/config/history', {
    method: 'PUT',
    body: JSON.stringify(options)
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '历史库配置保存失败')
  return result.data
}

export async function loadMqttStatus() {
  const result = await request<ApiResult<MqttRuntimeStatus>>('/api/config/status/mqtt')
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || 'MQTT 状态加载失败')
  return result.data
}

export async function loadOpcUaStatus() {
  const result = await request<ApiResult<OpcUaServerRuntimeStatus>>('/api/config/status/opcua')
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || 'OPC UA Server 状态加载失败')
  return result.data
}

export async function loadRuleEngineStatus() {
  const result = await request<ApiResult<RuleEngineRuntimeStatus>>('/api/config/rules/status')
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '规则引擎状态加载失败')
  return result.data
}

export async function loadRules() {
  const result = await request<ApiResult<EdgeRuleConfig[]>>('/api/config/rules')
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '规则加载失败')
  return result.data
}

export async function createRule(rule: EdgeRuleConfig) {
  const result = await request<ApiResult<EdgeRuleConfig>>('/api/config/rules', {
    method: 'POST',
    body: JSON.stringify(rule)
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '规则新增失败')
  return result.data
}

export async function updateRule(ruleId: string, rule: EdgeRuleConfig) {
  const result = await request<ApiResult<EdgeRuleConfig>>(`/api/config/rules/${encodeURIComponent(ruleId)}`, {
    method: 'PUT',
    body: JSON.stringify(rule)
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '规则更新失败')
  return result.data
}

export async function deleteRule(ruleId: string) {
  const result = await request<ApiResult<EdgeRuleConfig>>(`/api/config/rules/${encodeURIComponent(ruleId)}`, {
    method: 'DELETE'
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '规则删除失败')
  return result.data
}

export async function loadFlowRuleEngineStatus() {
  const result = await request<ApiResult<RuleEngineRuntimeStatus>>('/api/config/flow-rules/status')
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '流程规则引擎状态加载失败')
  return result.data
}

export async function loadFlowRules() {
  const result = await request<ApiResult<FlowRuleDefinition[]>>('/api/config/flow-rules')
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '流程规则加载失败')
  return result.data
}

export async function createFlowRule(rule: FlowRuleDefinition) {
  const result = await request<ApiResult<FlowRuleDefinition>>('/api/config/flow-rules', {
    method: 'POST',
    body: JSON.stringify(rule)
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '流程规则新增失败')
  return result.data
}

export async function updateFlowRule(ruleId: string, rule: FlowRuleDefinition) {
  const result = await request<ApiResult<FlowRuleDefinition>>(`/api/config/flow-rules/${encodeURIComponent(ruleId)}`, {
    method: 'PUT',
    body: JSON.stringify(rule)
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '流程规则更新失败')
  return result.data
}

export async function deleteFlowRule(ruleId: string) {
  const result = await request<ApiResult<FlowRuleDefinition>>(`/api/config/flow-rules/${encodeURIComponent(ruleId)}`, {
    method: 'DELETE'
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '流程规则删除失败')
  return result.data
}

export async function loadHistoryStatus() {
  const result = await request<ApiResult<HistoryRuntimeStatus>>('/api/config/status/history')
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '历史库状态加载失败')
  return result.data
}

export async function saveStorageHealth(options: StorageHealthConfig) {
  const result = await request<ApiResult<StorageHealthConfig>>('/api/config/storage-health', {
    method: 'PUT',
    body: JSON.stringify(options)
  })
  if (!result.success) throw new Error(localizeErrorMessage(result.errorMessage) || '历史库健康配置保存失败')
  return result.data
}

function localizeErrorMessage(message: string | undefined): string {
  const text = (message ?? '').trim()
  if (!text) return ''
  const known: Record<string, string> = {
    'Current user is not allowed to manage users.': '当前用户没有人员管理权限。',
    'Current user is not allowed to view role permissions.': '当前用户没有查看角色权限的权限。',
    'Current user is not allowed to manage roles.': '当前用户没有角色管理权限。',
    'Username is required.': '请输入账号。',
    'Password is required when creating a new user.': '新增人员时必须填写密码。',
    'Current password is incorrect.': '当前密码不正确。',
    'Current login account is invalid.': '当前登录账号无效。',
    'Please enter current password.': '请输入当前密码。',
    'Please enter new password.': '请输入新密码。',
    '当前用户没有重置人员密码权限。': '当前用户没有重置人员密码权限。',
    'Selected role does not exist.': '选择的角色不存在。',
    'Selected role is disabled.': '选择的角色已停用。',
    'System roles cannot be disabled.': '系统角色不能停用。',
    'System roles cannot be deleted.': '系统角色不能删除。',
    'This role is still assigned to users and cannot be deleted.': '该角色仍有关联人员，不能删除。',
    'Role name must start with a letter and contain only letters, numbers, underscores or hyphens.': '角色编码需以字母开头，仅支持字母、数字、下划线和短横线。',
    '未登录或会话已过期。': '登录已过期，请重新登录',
    '当前接口要求通过 HTTPS/TLS 访问。': '当前接口要求通过 HTTPS/TLS 访问。',
    '当前用户没有查看工业安全配置的权限。': '当前用户没有查看工业安全配置的权限。',
    '当前用户没有查看证书状态的权限。': '当前用户没有查看证书状态的权限。',
    'Not signed in or session expired.': '登录已过期，请重新登录。',
    'Current user is not allowed to write gateway configuration.': '当前用户没有修改网关配置权限。',
    'Tag was not found.': '标签不存在。',
    'Write timed out.': '标签写入超时。',
    'Write value is required.': '请输入写入值。',
    'Value is empty.': '值不能为空。',
    'Value must be true/false or 1/0.': '布尔值只能填写 true/false 或 1/0。',
    'Value must be an Int16 number.': '写入值必须是 Int16 数字。',
    'Value must be a UInt16 number.': '写入值必须是 UInt16 数字。',
    'Value must be an Int32 number.': '写入值必须是 Int32 数字。',
    'Value must be a UInt32 number.': '写入值必须是 UInt32 数字。',
    'Value must be an Int64 number.': '写入值必须是 Int64 数字。',
    'Value must be a UInt64 number.': '写入值必须是 UInt64 数字。',
    'Value must be a finite Float number.': '写入值必须是有效的 Float 数字。',
    'Value must be a finite Double number.': '写入值必须是有效的 Double 数字。',
    'Runtime service is not available.': '运行服务不可用。',
    'Email SMTP host is empty.': '邮件 SMTP 主机不能为空。',
    'Email sender is empty.': '邮件发件人不能为空。',
    'Email recipient is empty.': '邮件收件人不能为空。',
    'Webhook URL is empty.': 'Webhook 地址不能为空。',
    'Transform expression did not return a numeric value.': '转换表达式没有返回数值。',
    'Failed to fetch': '网络请求失败，请检查后端服务。',
    'NetworkError when attempting to fetch resource.': '网络请求失败，请检查后端服务。'
  }
  if (known[text]) return known[text]

  const writeCount = /^Write value count exceeds tag ElementCount\. Count: (\d+), ElementCount: (\d+)\.$/.exec(text)
  if (writeCount) return `写入值数量超过标签元素数量（当前 ${writeCount[1]}，最大 ${writeCount[2]}）。`

  const invalidArrayItem = /^Invalid array item at index (\d+): (.+)$/.exec(text)
  if (invalidArrayItem) return `第 ${invalidArrayItem[1]} 项数组值无效：${localizeErrorMessage(invalidArrayItem[2]) || invalidArrayItem[2]}`

  const unsupportedWriteType = /^Unsupported write data type: (.+)\.$/.exec(text)
  if (unsupportedWriteType) return `不支持写入的数据类型：${unsupportedWriteType[1]}。`

  const flowRuleMissing = /^Flow rule was not found: (.+)$/.exec(text)
  if (flowRuleMissing) return `流程规则不存在：${flowRuleMissing[1]}`

  const emptyConfiguration = /^(.+) configuration cannot be empty\.$/.exec(text)
  if (emptyConfiguration) return `${emptyConfiguration[1]} 配置不能为空。`

  const webhookHttp = /^Webhook returned HTTP (.+)$/.exec(text)
  if (webhookHttp) return `Webhook 返回 HTTP ${webhookHttp[1]}。`

  const invalidWebhookHeader = /^Invalid webhook header: (.+)$/.exec(text)
  if (invalidWebhookHeader) return `Webhook 请求头无效：${invalidWebhookHeader[1]}。`

  return text
}

function isAbortError(error: unknown) {
  return error instanceof DOMException && error.name === 'AbortError'
}

/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Application.Gateway.Contracts
* 项目描述 ：
* 类 名 称 ：GatewaySyncDto
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Core.Application.Gateway.Contracts
* 机器名称 ：UNKNOWN 
* CLR 版本 ：10.0.0
* 作    者 ：ipc
* 创建时间 ：2026-06-23 17:52:06
* 更新时间 ：2026-06-23 17:52:06
* 版 本 号 ：v1.0.0.0
*******************************************************************
* Copyright @ ipc 2026. All rights reserved.
*******************************************************************
//----------------------------------------------------------------*/
namespace IPC.Gateway.Core.Application.Gateway.Contracts;

public sealed class GatewaySyncDto
{
    public GatewayRuntimeStatusDto Status { get; set; } = new GatewayRuntimeStatusDto();
    public ProjectConfigurationDto Project { get; set; } = new ProjectConfigurationDto();
    public MqttConfigurationDto Mqtt { get; set; } = new MqttConfigurationDto();
    public OpcUaServerConfigurationDto OpcUa { get; set; } = new OpcUaServerConfigurationDto();
    public HistoryConfigurationDto History { get; set; } = new HistoryConfigurationDto();
    public StorageHealthConfigurationDto StorageHealth { get; set; } = new StorageHealthConfigurationDto();
}

public sealed class GatewayRuntimeStatusDto
{
    public bool IsRunning { get; set; }
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public string ConfigurationStore { get; set; } = string.Empty;
    public int DeviceCount { get; set; }
    public int GroupCount { get; set; }
    public int TagCount { get; set; }
    public int EnabledDeviceCount { get; set; }
    public int OnlineDeviceCount { get; set; }
    public int GoodTagCount { get; set; }
    public int BadTagCount { get; set; }
    public int NoDataTagCount { get; set; }
    public DateTime StartedTime { get; set; }
    public DateTime LastReloadTime { get; set; }
    public ProjectValidationResultDto ConfigValidation { get; set; } = new ProjectValidationResultDto();
    public IList<DeviceRuntimeStatusDto> Devices { get; set; } = new List<DeviceRuntimeStatusDto>();
    public IList<TagValueSnapshotDto> Tags { get; set; } = new List<TagValueSnapshotDto>();
    public IList<RuntimeErrorDto> RecentErrors { get; set; } = new List<RuntimeErrorDto>();
    public MqttRuntimeStatusDto Mqtt { get; set; } = new MqttRuntimeStatusDto();
    public OpcUaServerRuntimeStatusDto OpcUa { get; set; } = new OpcUaServerRuntimeStatusDto();
    public HistoryStatsDto History { get; set; } = new HistoryStatsDto();
    public RuleEngineRuntimeStatusDto FlowRuleEngine { get; set; } = new RuleEngineRuntimeStatusDto();
    public RuntimeSchedulerStatusDto Scheduler { get; set; } = new RuntimeSchedulerStatusDto();
    public SystemResourceStatusDto System { get; set; } = new SystemResourceStatusDto();
}

public class ProjectConfigurationDto
{
    public string ProjectId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public IList<ChannelConfigurationDto> Channels { get; set; } = new List<ChannelConfigurationDto>();
    public IList<DeviceConfigurationDto> Devices { get; set; } = new List<DeviceConfigurationDto>();
    public IList<EdgeRuleConfigurationDto> Rules { get; set; } = new List<EdgeRuleConfigurationDto>();
    public IList<FlowRuleDefinitionDto> FlowRules { get; set; } = new List<FlowRuleDefinitionDto>();
}

public class ChannelConfigurationDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string Protocol { get; set; } = "ModbusTcp";
    public string DriverId { get; set; } = string.Empty;
    public int MaxConcurrentDevicePolls { get; set; } = 4;
    public int SchedulingWeight { get; set; } = 1;
}

public class DeviceConfigurationDto
{
    public string Id { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string Protocol { get; set; } = "ModbusTcp";
    public PlcConnectionDto Connection { get; set; } = new PlcConnectionDto();
    public int DefaultScanRateMs { get; set; } = 1000;
    public int FailureRetryDelayMs { get; set; } = 1000;
    public int MaxFailureRetryDelayMs { get; set; } = 30000;
    public IList<TagConfigurationDto> Tags { get; set; } = new List<TagConfigurationDto>();
    public IList<GroupConfigurationDto> Groups { get; set; } = new List<GroupConfigurationDto>();
}

public class GroupConfigurationDto
{
    public string Id { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int ScanRateMs { get; set; } = 1000;
    public IList<TagConfigurationDto> Tags { get; set; } = new List<TagConfigurationDto>();
}

public class TagConfigurationDto
{
    public string Id { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string MeterAddress { get; set; } = string.Empty;
    public string MeterDataIdentifier { get; set; } = string.Empty;
    public string MeterType { get; set; } = string.Empty;
    public string DataType { get; set; } = "Int16";
    public int ElementCount { get; set; } = 1;
    public int ElementOffset { get; set; }
    public bool Enabled { get; set; } = true;
    public bool MqttPublishEnabled { get; set; }
    public string AccessMode { get; set; } = "ReadWrite";
    public int ScanRateMs { get; set; }
    public int FailureRetryDelayMs { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string PointCode { get; set; } = string.Empty;
    public string AssetPath { get; set; } = string.Empty;
    public string BusinessType { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public int Precision { get; set; } = -1;
    public ScalingConfigurationDto Scaling { get; set; } = new ScalingConfigurationDto();
    public DataCleaningConfigurationDto Cleaning { get; set; } = new DataCleaningConfigurationDto();
    public TagAlarmConfigurationDto Alarm { get; set; } = new TagAlarmConfigurationDto();
    public string Description { get; set; } = string.Empty;
}

public sealed class PlcConnectionDto
{
    public string Protocol { get; set; } = "ModbusTcp";
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public int Rack { get; set; }
    public int Slot { get; set; }
    public int TimeoutMilliseconds { get; set; } = 3000;
    public string WordOrder { get; set; } = "HighWordFirst";
    public string Transport { get; set; } = "Tcp";
    public int DataBits { get; set; } = 8;
    public string SerialParity { get; set; } = "None";
    public string SerialStopBits { get; set; } = "One";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string OpcUaSecurityPolicy { get; set; } = "None";
    public string OpcUaMessageSecurityMode { get; set; } = "None";
    public bool OpcUaAutoTrustServerCertificate { get; set; }
    public string OpcDaServerProgId { get; set; } = string.Empty;
    public string OpcDaGroupName { get; set; } = string.Empty;
    public string DriverId { get; set; } = string.Empty;
    public string DriverOptionsJson { get; set; } = string.Empty;
}

public sealed class ScalingConfigurationDto
{
    public bool Enabled { get; set; }
    public double Multiplier { get; set; } = 1D;
    public double Offset { get; set; }
    public bool ClampEnabled { get; set; }
    public double MinValue { get; set; }
    public double MaxValue { get; set; }
    public int DecimalPlaces { get; set; } = 2;
}

public sealed class DataCleaningConfigurationDto
{
    public bool Enabled { get; set; }
    public bool OutOfRangeEnabled { get; set; }
    public double MinValue { get; set; }
    public double MaxValue { get; set; }
    public bool DeadbandEnabled { get; set; }
    public double Deadband { get; set; }
    public bool DuplicateFilterEnabled { get; set; }
    public bool SpikeFilterEnabled { get; set; }
    public double SpikeThreshold { get; set; }
    public int SpikeWindowSeconds { get; set; }
    public bool EnumMappingEnabled { get; set; }
    public IList<DataCleaningEnumMappingDto> EnumMappings { get; set; } = new List<DataCleaningEnumMappingDto>();
    public bool UnitConversionEnabled { get; set; }
    public string SourceUnit { get; set; } = string.Empty;
    public string TargetUnit { get; set; } = string.Empty;
    public double UnitMultiplier { get; set; } = 1D;
    public double UnitOffset { get; set; }
    public bool PreserveLastGoodOnFilter { get; set; } = true;
}

public sealed class DataCleaningEnumMappingDto
{
    public string RawValue { get; set; } = string.Empty;
    public string CleanValue { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class TagAlarmConfigurationDto
{
    public bool Enabled { get; set; }
    public double LowLimit { get; set; }
    public double HighLimit { get; set; }
    public string LowAlarmMessage { get; set; } = string.Empty;
    public string HighAlarmMessage { get; set; } = string.Empty;
    public double WarningDeviation { get; set; }
    public string LowWarningMessage { get; set; } = string.Empty;
    public string HighWarningMessage { get; set; } = string.Empty;
}

public class EdgeRuleConfigurationDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string ConditionType { get; set; } = "Threshold";
    public string SourcePointCode { get; set; } = string.Empty;
    public string SourceDeviceName { get; set; } = string.Empty;
    public string SourceGroupName { get; set; } = string.Empty;
    public string SourceTagName { get; set; } = string.Empty;
    public string SourceDataType { get; set; } = string.Empty;
    public double LowLimit { get; set; }
    public double HighLimit { get; set; } = 100D;
    public double Deadband { get; set; } = 1D;
    public double RateLimitPerSecond { get; set; } = 1D;
    public string Operator { get; set; } = "GreaterThan";
    public double CompareValue { get; set; }
    public string LogicalOperator { get; set; } = "And";
    public IList<EdgeRuleConditionDto> Conditions { get; set; } = new List<EdgeRuleConditionDto>();
    public int DurationSeconds { get; set; }
    public bool PublishToMqtt { get; set; } = true;
    public bool PublishOnClear { get; set; } = true;
    public string PublishTopicTemplate { get; set; } = "ipc/rule/{pointCode}/{ruleName}";
    public int PublishQos { get; set; }
    public string ActiveMessage { get; set; } = string.Empty;
    public string ClearMessage { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string QualityOperator { get; set; } = "In";
    public string QualityValues { get; set; } = "Good";
    public string WindowStatistic { get; set; } = "Average";
    public int WindowSeconds { get; set; } = 60;
    public int WindowSampleCount { get; set; }
    public string AggregationStatistic { get; set; } = "Average";
    public string TrendMode { get; set; } = "Slope";
    public int TrendWindowSeconds { get; set; } = 300;
    public int TrendSampleCount { get; set; }
    public double TrendMinSlopePerSecond { get; set; }
    public double TrendChangeThreshold { get; set; }
    public double TrendStableDeadband { get; set; }
    public string StateName { get; set; } = "State";
    public string StateExpectedValue { get; set; } = "1";
    public string StateClearValue { get; set; } = string.Empty;
    public int StateTimeoutSeconds { get; set; }
    public string RelatedDeviceName { get; set; } = string.Empty;
    public string RelatedGroupName { get; set; } = string.Empty;
    public string RelatedTagName { get; set; } = string.Empty;
    public string RelatedPointCode { get; set; } = string.Empty;
    public string RelatedDataType { get; set; } = string.Empty;
    public string RelationOperator { get; set; } = "GreaterThan";
    public double RelationMultiplier { get; set; } = 1D;
    public double RelationOffset { get; set; }
    public string ContextName { get; set; } = "Context";
    public string ContextExpectedValue { get; set; } = string.Empty;
    public string ContextOperator { get; set; } = "Equal";
    public string ContextDeviceName { get; set; } = string.Empty;
    public string ContextGroupName { get; set; } = string.Empty;
    public string ContextTagName { get; set; } = string.Empty;
    public string ContextPointCode { get; set; } = string.Empty;
    public string ContextDataType { get; set; } = string.Empty;
    public string CycleStartValue { get; set; } = "1";
    public string CycleEndValue { get; set; } = "0";
    public int CycleMinSeconds { get; set; }
    public int CycleMaxSeconds { get; set; }
    public double TaktTargetSeconds { get; set; } = 60D;
    public double TaktTolerancePercent { get; set; } = 10D;
    public string AnomalyMode { get; set; } = "ZScore";
    public double AnomalyThreshold { get; set; } = 3D;
    public int AnomalyBaselineWindowSeconds { get; set; } = 300;
    public int AnomalyBaselineSampleCount { get; set; }
    public string ModelPurpose { get; set; } = "DeviceAnomaly";
    public string ModelPath { get; set; } = string.Empty;
    public string ModelInputTags { get; set; } = string.Empty;
    public string ModelInputName { get; set; } = string.Empty;
    public string ModelInputNames { get; set; } = string.Empty;
    public string ModelOutputName { get; set; } = string.Empty;
    public int ModelOutputIndex { get; set; }
    public string ModelOperator { get; set; } = "GreaterThanOrEqual";
    public double ModelThreshold { get; set; } = 0.5D;
    public int ModelTimeoutMilliseconds { get; set; } = 1000;
    public string AlarmSeverity { get; set; } = "Warning";
    public int AlarmSuppressSeconds { get; set; }
    public int AlarmReTriggerSeconds { get; set; }
    public int AlarmEscalateAfterSeconds { get; set; }
    public int ActionDelaySeconds { get; set; }
    public int ActionCooldownSeconds { get; set; }
    public int ActionMaxPerMinute { get; set; }
    public double TransformMultiplier { get; set; } = 1D;
    public double TransformOffset { get; set; }
    public bool TransformUseAbsolute { get; set; }
    public string TransformExpression { get; set; } = string.Empty;
    public int TransformTimeoutMilliseconds { get; set; } = 50;
    public int SequenceWindowSeconds { get; set; } = 60;
    public int SequenceStepTimeoutSeconds { get; set; }
    public int SequenceMinIntervalSeconds { get; set; }
    public bool SequenceResetOnMismatch { get; set; } = true;
    public int ClearDurationSeconds { get; set; }
    public IList<EdgeRuleActionDto> Actions { get; set; } = new List<EdgeRuleActionDto>();
}

public sealed class EdgeRuleActionDto
{
    public string Id { get; set; } = string.Empty;
    public string ActionType { get; set; } = "MqttPublish";
    public bool Enabled { get; set; } = true;
    public bool ExecuteOnActive { get; set; } = true;
    public bool ExecuteOnClear { get; set; } = true;
    public string TopicTemplate { get; set; } = "ipc/rule/{pointCode}/{ruleName}";
    public int Qos { get; set; }
    public string ActiveMessage { get; set; } = string.Empty;
    public string ClearMessage { get; set; } = string.Empty;
    public string EmailSmtpHost { get; set; } = string.Empty;
    public int EmailSmtpPort { get; set; } = 25;
    public bool EmailEnableSsl { get; set; }
    public string EmailUsername { get; set; } = string.Empty;
    public string EmailPassword { get; set; } = string.Empty;
    public string EmailFrom { get; set; } = string.Empty;
    public string EmailTo { get; set; } = string.Empty;
    public string EmailCc { get; set; } = string.Empty;
    public string EmailSubjectTemplate { get; set; } = "{ruleName} {state}";
    public string EmailBodyTemplate { get; set; } = "{message}";
    public string WebhookUrl { get; set; } = string.Empty;
    public string WebhookMethod { get; set; } = "POST";
    public string WebhookHeaders { get; set; } = string.Empty;
    public string WebhookBodyTemplate { get; set; } = "{\"ruleName\":\"{ruleName}\",\"state\":\"{state}\",\"value\":\"{value}\",\"message\":\"{message}\"}";
    public string WebhookContentType { get; set; } = "application/json";
    public int WebhookTimeoutSeconds { get; set; } = 5;
    public int WebhookRetryCount { get; set; }
    public string DebugLabel { get; set; } = string.Empty;
}

public sealed class EdgeRuleConditionDto
{
    public string Id { get; set; } = string.Empty;
    public string SourcePointCode { get; set; } = string.Empty;
    public string SourceDeviceName { get; set; } = string.Empty;
    public string SourceGroupName { get; set; } = string.Empty;
    public string SourceTagName { get; set; } = string.Empty;
    public string SourceDataType { get; set; } = string.Empty;
    public string Operator { get; set; } = "GreaterThan";
    public double CompareValue { get; set; }
    public double TransformMultiplier { get; set; } = 1D;
    public double TransformOffset { get; set; }
    public bool TransformUseAbsolute { get; set; }
    public string TransformExpression { get; set; } = string.Empty;
}

public class FlowRuleDefinitionDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int Version { get; set; } = 1;
    public string LifecycleState { get; set; } = "Draft";
    public int PublishedVersion { get; set; }
    public DateTime PublishedTime { get; set; }
    public string PublishedBy { get; set; } = string.Empty;
    public string Mode { get; set; } = "Flow";
    public string CompiledRuleId { get; set; } = string.Empty;
    public IList<FlowRuleNodeDto> Nodes { get; set; } = new List<FlowRuleNodeDto>();
    public IList<FlowRuleEdgeDto> Edges { get; set; } = new List<FlowRuleEdgeDto>();
    public DateTime CreatedTime { get; set; }
    public DateTime UpdatedTime { get; set; }
}

public sealed class FlowRuleNodeDto
{
    public string Id { get; set; } = string.Empty;
    public string NodeType { get; set; } = "Condition";
    public string Label { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string TagName { get; set; } = string.Empty;
    public string PointCode { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string ConditionType { get; set; } = "Condition";
    public string Operator { get; set; } = "GreaterThan";
    public double CompareValue { get; set; }
    public double LowLimit { get; set; }
    public double HighLimit { get; set; } = 100D;
    public double Deadband { get; set; } = 1D;
    public double RateLimitPerSecond { get; set; } = 1D;
    public string LogicalOperator { get; set; } = "And";
    public int DurationSeconds { get; set; }
    public bool PublishToMqtt { get; set; } = true;
    public bool PublishOnClear { get; set; } = true;
    public string TopicTemplate { get; set; } = "ipc/rule/{pointCode}/{ruleName}";
    public int PublishQos { get; set; }
    public string ActiveMessage { get; set; } = string.Empty;
    public string ClearMessage { get; set; } = string.Empty;
    public string HysteresisMode { get; set; } = "High";
    public double HysteresisOnValue { get; set; }
    public double HysteresisOffValue { get; set; }
    public string Expression { get; set; } = "{value} > 0";
    public IList<FlowRuleAlarmLevelDto> AlarmLevels { get; set; } = new List<FlowRuleAlarmLevelDto>();
    public string QualityOperator { get; set; } = "In";
    public string QualityValues { get; set; } = "Good";
    public string WindowStatistic { get; set; } = "Average";
    public int WindowSeconds { get; set; } = 60;
    public int WindowSampleCount { get; set; }
    public string AggregationStatistic { get; set; } = "Average";
    public string TrendMode { get; set; } = "Slope";
    public int TrendWindowSeconds { get; set; } = 300;
    public int TrendSampleCount { get; set; }
    public double TrendMinSlopePerSecond { get; set; }
    public double TrendChangeThreshold { get; set; }
    public double TrendStableDeadband { get; set; }
    public string StateName { get; set; } = "State";
    public string StateExpectedValue { get; set; } = "1";
    public string StateClearValue { get; set; } = string.Empty;
    public int StateTimeoutSeconds { get; set; }
    public string RelatedDeviceName { get; set; } = string.Empty;
    public string RelatedGroupName { get; set; } = string.Empty;
    public string RelatedTagName { get; set; } = string.Empty;
    public string RelatedPointCode { get; set; } = string.Empty;
    public string RelatedDataType { get; set; } = string.Empty;
    public string RelationOperator { get; set; } = "GreaterThan";
    public double RelationMultiplier { get; set; } = 1D;
    public double RelationOffset { get; set; }
    public string ContextName { get; set; } = "Context";
    public string ContextExpectedValue { get; set; } = string.Empty;
    public string ContextOperator { get; set; } = "Equal";
    public string ContextDeviceName { get; set; } = string.Empty;
    public string ContextGroupName { get; set; } = string.Empty;
    public string ContextTagName { get; set; } = string.Empty;
    public string ContextPointCode { get; set; } = string.Empty;
    public string ContextDataType { get; set; } = string.Empty;
    public string CycleStartValue { get; set; } = "1";
    public string CycleEndValue { get; set; } = "0";
    public int CycleMinSeconds { get; set; }
    public int CycleMaxSeconds { get; set; }
    public double TaktTargetSeconds { get; set; } = 60D;
    public double TaktTolerancePercent { get; set; } = 10D;
    public string AnomalyMode { get; set; } = "ZScore";
    public double AnomalyThreshold { get; set; } = 3D;
    public int AnomalyBaselineWindowSeconds { get; set; } = 300;
    public int AnomalyBaselineSampleCount { get; set; }
    public string ModelPurpose { get; set; } = "DeviceAnomaly";
    public string ModelPath { get; set; } = string.Empty;
    public string ModelInputTags { get; set; } = string.Empty;
    public string ModelInputName { get; set; } = string.Empty;
    public string ModelInputNames { get; set; } = string.Empty;
    public string ModelOutputName { get; set; } = string.Empty;
    public int ModelOutputIndex { get; set; }
    public string ModelOperator { get; set; } = "GreaterThanOrEqual";
    public double ModelThreshold { get; set; } = 0.5D;
    public int ModelTimeoutMilliseconds { get; set; } = 1000;
    public string AlarmSeverity { get; set; } = "Warning";
    public int AlarmSuppressSeconds { get; set; }
    public int AlarmReTriggerSeconds { get; set; }
    public int AlarmEscalateAfterSeconds { get; set; }
    public int ActionDelaySeconds { get; set; }
    public int ActionCooldownSeconds { get; set; }
    public int ActionMaxPerMinute { get; set; }
    public bool DebugEnabled { get; set; } = true;
    public string DebugLabel { get; set; } = string.Empty;
    public double TransformMultiplier { get; set; } = 1D;
    public double TransformOffset { get; set; }
    public bool TransformUseAbsolute { get; set; }
    public string TransformExpression { get; set; } = string.Empty;
    public int TransformTimeoutMilliseconds { get; set; } = 50;
    public int SequenceWindowSeconds { get; set; } = 60;
    public int SequenceStepTimeoutSeconds { get; set; }
    public int SequenceMinIntervalSeconds { get; set; }
    public bool SequenceResetOnMismatch { get; set; } = true;
    public int ClearDurationSeconds { get; set; }
    public bool ExecuteOnActive { get; set; } = true;
    public bool ExecuteOnClear { get; set; } = true;
    public string EmailSmtpHost { get; set; } = string.Empty;
    public int EmailSmtpPort { get; set; } = 25;
    public bool EmailEnableSsl { get; set; }
    public string EmailUsername { get; set; } = string.Empty;
    public string EmailPassword { get; set; } = string.Empty;
    public string EmailFrom { get; set; } = string.Empty;
    public string EmailTo { get; set; } = string.Empty;
    public string EmailCc { get; set; } = string.Empty;
    public string EmailSubjectTemplate { get; set; } = "{ruleName} {state}";
    public string EmailBodyTemplate { get; set; } = "{message}";
    public string WebhookUrl { get; set; } = string.Empty;
    public string WebhookMethod { get; set; } = "POST";
    public string WebhookHeaders { get; set; } = string.Empty;
    public string WebhookBodyTemplate { get; set; } = "{\"ruleName\":\"{ruleName}\",\"state\":\"{state}\",\"value\":\"{value}\",\"message\":\"{message}\"}";
    public string WebhookContentType { get; set; } = "application/json";
    public int WebhookTimeoutSeconds { get; set; } = 5;
    public int WebhookRetryCount { get; set; }
}

public sealed class FlowRuleEdgeDto
{
    public string Id { get; set; } = string.Empty;
    public string SourceNodeId { get; set; } = string.Empty;
    public string TargetNodeId { get; set; } = string.Empty;
    public string SourcePort { get; set; } = string.Empty;
    public string TargetPort { get; set; } = string.Empty;
}

public sealed class FlowRuleAlarmLevelDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Severity { get; set; } = "Warning";
    public string Operator { get; set; } = "GreaterThanOrEqual";
    public double CompareValue { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class MqttConfigurationDto
{
    public bool Enabled { get; set; }
    public string GatewayId { get; set; } = string.Empty;
    public string GatewayName { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;
    public string CloudProtocolVersion { get; set; } = string.Empty;
    public int ConfigVersion { get; set; } = 1;
    public string PublishMode { get; set; } = "Classic";
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 1883;
    public string ClientId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool UseTls { get; set; }
    public bool AllowUntrustedCertificates { get; set; }
    public string ClientCertificatePath { get; set; } = string.Empty;
    public string ClientCertificatePassword { get; set; } = string.Empty;
    public string ClientCertificateThumbprint { get; set; } = string.Empty;
    public string ServerCertificateThumbprint { get; set; } = string.Empty;
    public string CaCertificatePath { get; set; } = string.Empty;
    public string SubscribeTopic { get; set; } = string.Empty;
    public bool PublishEnabled { get; set; } = true;
    public bool PublishSelectedTagsOnly { get; set; }
    public bool PublishChangedOnly { get; set; } = true;
    public int PublishUnchangedHeartbeatSeconds { get; set; }
    public string PublishTopicTemplate { get; set; } = string.Empty;
    public int PublishQos { get; set; }
    public bool HeartbeatEnabled { get; set; } = true;
    public int HeartbeatIntervalSeconds { get; set; } = 60;
    public string HeartbeatTopic { get; set; } = string.Empty;
    public int HeartbeatQos { get; set; }
    public string StatusTopic { get; set; } = string.Empty;
    public string CommandReplyTopicTemplate { get; set; } = string.Empty;
    public string OutboxDirectory { get; set; } = string.Empty;
    public int PublishAckTimeoutMilliseconds { get; set; } = 5000;
    public int OutboxMaxMessages { get; set; } = 10000;
    public int OutboxMaxMegabytes { get; set; } = 100;
    public int OutboxRetentionHours { get; set; } = 168;
    public int OutboxQuarantineRetentionHours { get; set; } = 720;
    public int PublishFlushBatchSize { get; set; } = 100;
    public int PublishRetryMinSeconds { get; set; } = 1;
    public int PublishRetryMaxSeconds { get; set; } = 60;
    public int ReconnectSeconds { get; set; } = 5;
    public int KeepAliveSeconds { get; set; } = 30;
    public string SparkplugNamespace { get; set; } = "spBv1.0";
    public string SparkplugGroupId { get; set; } = "IPC-Gateway";
    public string SparkplugEdgeNodeId { get; set; } = "EdgeNode";
    public string SparkplugDeviceIdSource { get; set; } = "DeviceName";
    public string SparkplugMetricNameTemplate { get; set; } = "{group}/{tag}";
    public bool SparkplugPublishNodeBirth { get; set; } = true;
    public bool SparkplugPublishDeviceBirth { get; set; } = true;
    public bool SparkplugPublishDeviceDeath { get; set; } = true;
    public bool SparkplugIncludeProperties { get; set; } = true;
    public bool SparkplugUseAliases { get; set; }
    public int SparkplugDeathQos { get; set; }
    public int SparkplugBirthQos { get; set; }
}

public class OpcUaServerConfigurationDto
{
    public bool Enabled { get; set; }
    public string ApplicationName { get; set; } = string.Empty;
    public string ApplicationUri { get; set; } = string.Empty;
    public string ProductUri { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 4840;
    public string EndpointPath { get; set; } = string.Empty;
    public string EndpointUrl { get; set; } = string.Empty;
    public string NamespaceUri { get; set; } = string.Empty;
    public string CertificateStorePath { get; set; } = string.Empty;
    public bool AutoAcceptUntrustedCertificates { get; set; } = true;
    public bool AllowAnonymous { get; set; } = true;
    public bool UsernamePasswordEnabled { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool PasswordConfigured { get; set; }
    public string SecurityPolicy { get; set; } = string.Empty;
    public bool AllowSecurityPolicyNone { get; set; } = true;
    public bool EnableBasic256SignAndEncrypt { get; set; }
    public bool EnableBasic256Sha256SignAndEncrypt { get; set; }
    public int MinimumSamplingIntervalMs { get; set; } = 250;
    public bool PublishDiagnostics { get; set; } = true;
}

public class HistoryConfigurationDto
{
    public bool Enabled { get; set; } = true;
    public string Directory { get; set; } = string.Empty;
    public int RetentionDays { get; set; } = 7;
    public int MaxViewRecords { get; set; } = 500;
    public HistoryDataProcessingConfigurationDto DataProcessing { get; set; } = new HistoryDataProcessingConfigurationDto();
    public HistoryStorageConfigurationDto Storage { get; set; } = new HistoryStorageConfigurationDto();
}

public class HistoryDataProcessingConfigurationDto
{
    public bool Enabled { get; set; }
    public bool CompressionEnabled { get; set; }
    public double CompressionTolerance { get; set; }
    public bool CompressDuplicateText { get; set; } = true;
    public bool DownsamplingEnabled { get; set; }
    public int DownsamplingIntervalMs { get; set; }
    public bool AlignmentEnabled { get; set; }
    public int AlignmentIntervalMs { get; set; }
    public bool FillEnabled { get; set; }
    public int FillIntervalMs { get; set; }
    public int FillMaxGapSeconds { get; set; }
    public string FillMode { get; set; } = "Previous";
    public bool AggregationEnabled { get; set; }
    public int AggregationIntervalSeconds { get; set; }
    public string AggregationMethods { get; set; } = "Average,Min,Max,Count";
    public int MaxSyntheticPointsPerInput { get; set; } = 1000;
}

public class HistoryStorageConfigurationDto
{
    public bool TieringEnabled { get; set; }
    public string ColdDirectory { get; set; } = "Data\\HistoryCold";
    public string RetentionPolicy { get; set; } = "DeleteOnly";
    public int HotRetentionDays { get; set; } = 7;
    public int ColdRetentionDays { get; set; } = 90;
    public bool CompressionEnabled { get; set; }
    public bool CompressHotFiles { get; set; }
    public bool CompressColdFiles { get; set; } = true;
    public int CompressAfterDays { get; set; } = 3;
    public bool AutoCleanupEnabled { get; set; } = true;
    public int CleanupIntervalHours { get; set; } = 24;
    public int MaxStorageMegabytes { get; set; }
}

public class StorageHealthConfigurationDto
{
    public double DegradedAvailableMegabytes { get; set; } = 1024D;
    public double UnhealthyAvailableMegabytes { get; set; } = 256D;
    public double DegradedAvailablePercent { get; set; } = 10D;
    public double UnhealthyAvailablePercent { get; set; } = 2D;
}

public sealed class GatewayConfigurationVersionDto
{
    public string Id { get; set; } = string.Empty;
    public string ConfigType { get; set; } = string.Empty;
    public int Version { get; set; }
    public bool Active { get; set; }
    public DateTime CreatedTime { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class ProjectValidationResultDto
{
    public bool IsValid { get; set; } = true;
    public IList<string> Errors { get; set; } = new List<string>();
    public IList<string> Warnings { get; set; } = new List<string>();
}

public sealed class DeviceRuntimeStatusDto
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool IsConnected { get; set; }
    public bool IsPolling { get; set; }
    public bool IsQueued { get; set; }
    public string Status { get; set; } = string.Empty;
    public int ConsecutiveFailures { get; set; }
    public long TotalReads { get; set; }
    public long SuccessfulReads { get; set; }
    public long FailedReads { get; set; }
    public double SuccessRate { get; set; }
    public DateTime LastPollTime { get; set; }
    public DateTime LastSuccessTime { get; set; }
    public DateTime LastFailureTime { get; set; }
    public DateTime NextReconnectTime { get; set; }
    public int LastReconnectDelayMs { get; set; }
    public DateTime NextPollTime { get; set; }
    public long CurrentTaskId { get; set; }
    public string LastTaskStatus { get; set; } = string.Empty;
    public long LastTaskDurationMs { get; set; }
    public long SlowPollCount { get; set; }
    public long TimeoutCount { get; set; }
    public string LastError { get; set; } = string.Empty;
    public CircuitBreakerStatusDto ProtocolCircuitBreaker { get; set; } = new CircuitBreakerStatusDto();
    public string DeviceState { get; set; } = string.Empty;
    public bool TransportConnected { get; set; }
    public bool IsIsolated { get; set; }
    public string RecoveryState { get; set; } = string.Empty;
    public DateTime IsolatedSinceTime { get; set; }
    public DateTime NextRecoveryProbeTime { get; set; }
    public string ChannelKey { get; set; } = string.Empty;
    public string ChannelStatus { get; set; } = string.Empty;
    public int ChannelConsecutiveFailures { get; set; }
    public DateTime ChannelLastSuccessTime { get; set; }
    public DateTime ChannelLastFailureTime { get; set; }
    public string ChannelLastError { get; set; } = string.Empty;
}

public sealed class RuntimeSchedulerStatusDto
{
    public string IsolationStrategy { get; set; } = string.Empty;
    public string HealthStatus { get; set; } = string.Empty;
    public string HealthMessage { get; set; } = string.Empty;
    public int MaxConcurrentDevicePolls { get; set; }
    public int SchedulerIntervalMs { get; set; }
    public bool BackpressureEnabled { get; set; }
    public bool BackpressureActive { get; set; }
    public int QueueHighWatermark { get; set; }
    public int QueueLowWatermark { get; set; }
    public int BackpressureDelayMs { get; set; }
    public int MaxDevicePollsQueuedPerSchedulerTick { get; set; }
    public int SlowPollThresholdMs { get; set; }
    public int PollTimeoutMs { get; set; }
    public long TotalQueued { get; set; }
    public long TotalStarted { get; set; }
    public long TotalCompleted { get; set; }
    public long TotalFailed { get; set; }
    public long TotalSlow { get; set; }
    public long TotalBackpressureThrottled { get; set; }
    public long TotalRateLimited { get; set; }
    public RuntimePollingQueueStatusDto Queue { get; set; } = new RuntimePollingQueueStatusDto();
    public RuntimeTimeoutStatsDto Timeout { get; set; } = new RuntimeTimeoutStatsDto();
    public IList<RuntimePollingTaskStatusDto> Tasks { get; set; } = new List<RuntimePollingTaskStatusDto>();
}

public sealed class RuntimePollingQueueStatusDto
{
    public int PendingCount { get; set; }
    public int RecoveryPendingCount { get; set; }
    public int RunningCount { get; set; }
    public int QueueLimit { get; set; }
    public int HighWatermark { get; set; }
    public int LowWatermark { get; set; }
    public double UtilizationPercent { get; set; }
    public bool BackpressureActive { get; set; }
    public int AvailableWorkers { get; set; }
    public long RejectedCount { get; set; }
    public long BackpressureThrottledCount { get; set; }
    public long RateLimitedCount { get; set; }
    public int MaxObservedPendingCount { get; set; }
    public DateTime LastBackpressureTime { get; set; }
    public string LastBackpressureMessage { get; set; } = string.Empty;
}

public sealed class RuntimeTimeoutStatsDto
{
    public long PollTimeoutCount { get; set; }
    public long ReadTimeoutCount { get; set; }
    public long RecentPollTimeoutCount { get; set; }
    public long RecentReadTimeoutCount { get; set; }
    public int TimeoutWindowSeconds { get; set; }
    public DateTime LastTimeoutTime { get; set; }
    public string LastTimeoutDeviceName { get; set; } = string.Empty;
    public string LastTimeoutMessage { get; set; } = string.Empty;
}

public sealed class RuntimePollingTaskStatusDto
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public long TaskId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsQueued { get; set; }
    public bool IsRunning { get; set; }
    public DateTime QueuedTime { get; set; }
    public DateTime StartedTime { get; set; }
    public DateTime FinishedTime { get; set; }
    public long LastDurationMs { get; set; }
    public long SlowPollCount { get; set; }
    public long TimeoutCount { get; set; }
    public string LastError { get; set; } = string.Empty;
}

public sealed class SystemResourceStatusDto
{
    public double CpuUsagePercent { get; set; }
    public double MemoryUsagePercent { get; set; }
    public long TotalMemoryBytes { get; set; }
    public long AvailableMemoryBytes { get; set; }
    public long UsedMemoryBytes { get; set; }
    public long ProcessWorkingSetBytes { get; set; }
    public int ThreadPoolAvailableWorkerThreads { get; set; }
    public int ThreadPoolMaxWorkerThreads { get; set; }
    public int ThreadPoolAvailableCompletionPortThreads { get; set; }
    public int ThreadPoolMaxCompletionPortThreads { get; set; }
    public double ThreadPoolWorkerUtilizationPercent { get; set; }
    public DateTime SampleTime { get; set; }
    public string Source { get; set; } = string.Empty;
}

public sealed class RuntimeErrorDto
{
    public string Category { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string TagName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Suggestion { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public sealed class TagValueSnapshotDto
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceProtocol { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public string TagId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string TagName { get; set; } = string.Empty;
    public string RawValueText { get; set; } = string.Empty;
    public string ValueText { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string PointCode { get; set; } = string.Empty;
    public string AssetPath { get; set; } = string.Empty;
    public string BusinessType { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public int Precision { get; set; } = -1;
    public string DataType { get; set; } = string.Empty;
    public bool MqttPublishEnabled { get; set; }
    public bool CleaningApplied { get; set; }
    public string CleaningAction { get; set; } = string.Empty;
    public string CleaningMessage { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string TagState { get; set; } = string.Empty;
    public bool IsTagIsolated { get; set; }
    public bool IsStaticValidationError { get; set; }
    public int TagConsecutiveFailures { get; set; }
    public DateTime NextTagRecoveryProbeTime { get; set; }
}

public sealed class MqttRuntimeStatusDto
{
    public bool Enabled { get; set; }
    public string GatewayId { get; set; } = string.Empty;
    public string GatewayName { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;
    public string CloudProtocolVersion { get; set; } = string.Empty;
    public int ConfigVersion { get; set; }
    public string PublishMode { get; set; } = string.Empty;
    public bool IsRunning { get; set; }
    public bool IsConnected { get; set; }
    public string Broker { get; set; } = string.Empty;
    public string SubscribeTopic { get; set; } = string.Empty;
    public bool PublishEnabled { get; set; }
    public string PublishTopicTemplate { get; set; } = string.Empty;
    public int PublishQos { get; set; }
    public string HeartbeatTopic { get; set; } = string.Empty;
    public string StatusTopic { get; set; } = string.Empty;
    public string CommandReplyTopicTemplate { get; set; } = string.Empty;
    public bool SparkplugEnabled { get; set; }
    public string SparkplugNamespace { get; set; } = string.Empty;
    public string SparkplugGroupId { get; set; } = string.Empty;
    public string SparkplugEdgeNodeId { get; set; } = string.Empty;
    public string SparkplugNodeBirthTopic { get; set; } = string.Empty;
    public string SparkplugNodeDeathTopic { get; set; } = string.Empty;
    public string OutboxDirectory { get; set; } = string.Empty;
    public string OutboxQuarantineDirectory { get; set; } = string.Empty;
    public string LastError { get; set; } = string.Empty;
    public string LastMessage { get; set; } = string.Empty;
    public string LastWriteResult { get; set; } = string.Empty;
    public string LastPublishResult { get; set; } = string.Empty;
    public DateTime LastConnectedTime { get; set; }
    public DateTime LastMessageTime { get; set; }
    public DateTime LastPublishTime { get; set; }
    public DateTime LastPublishFailureTime { get; set; }
    public DateTime LastSparkplugBirthTime { get; set; }
    public DateTime LastSparkplugDeathTime { get; set; }
    public DateTime NextPublishRetryTime { get; set; }
    public CircuitBreakerStatusDto CircuitBreaker { get; set; } = new CircuitBreakerStatusDto();
    public int ReconnectCount { get; set; }
    public int ReceivedCount { get; set; }
    public int SuccessfulWrites { get; set; }
    public int FailedWrites { get; set; }
    public int PublishedCount { get; set; }
    public int FailedPublishes { get; set; }
    public int SparkplugBirthCount { get; set; }
    public int SparkplugDeathCount { get; set; }
    public int SparkplugDataCount { get; set; }
    public int OutboxPendingCount { get; set; }
    public int OutboxEnqueuedCount { get; set; }
    public long OutboxBytes { get; set; }
    public int OutboxExpiredDeletedCount { get; set; }
    public int OutboxOverflowDeletedCount { get; set; }
    public int OutboxInvalidMessageCount { get; set; }
    public int OutboxQuarantinedMessageCount { get; set; }
    public int OutboxQuarantineCount { get; set; }
    public long OutboxQuarantineBytes { get; set; }
    public int OutboxQuarantineExpiredDeletedCount { get; set; }
    public DateTime OutboxOldestPendingTime { get; set; }
    public DateTime OutboxNewestPendingTime { get; set; }
    public DateTime OutboxOldestQuarantineTime { get; set; }
    public DateTime OutboxNewestQuarantineTime { get; set; }
    public long OutboxOldestPendingAgeSeconds { get; set; }
    public int PublishRetryBackoffSeconds { get; set; }
    public int PublishConsecutiveFailureCount { get; set; }
}

public sealed class OpcUaServerRuntimeStatusDto
{
    public bool Enabled { get; set; }
    public bool IsRunning { get; set; }
    public string ApplicationName { get; set; } = string.Empty;
    public string EndpointUrl { get; set; } = string.Empty;
    public string NamespaceUri { get; set; } = string.Empty;
    public int DeviceNodeCount { get; set; }
    public int GroupNodeCount { get; set; }
    public int TagNodeCount { get; set; }
    public long ValueUpdateCount { get; set; }
    public DateTime StartedTime { get; set; }
    public DateTime LastReloadTime { get; set; }
    public DateTime LastValueUpdateTime { get; set; }
    public string LastError { get; set; } = string.Empty;
    public string LastMessage { get; set; } = string.Empty;
}

public sealed class HistoryStatsDto
{
    public bool Enabled { get; set; }
    public bool IsRunning { get; set; }
    public string Directory { get; set; } = string.Empty;
    public int RetentionDays { get; set; }
    public int ValueFiles { get; set; }
    public int AlarmFiles { get; set; }
    public int PublishFiles { get; set; }
    public long TotalBytes { get; set; }
    public string ColdDirectory { get; set; } = string.Empty;
    public bool TieringEnabled { get; set; }
    public string RetentionPolicy { get; set; } = string.Empty;
    public int HotRetentionDays { get; set; }
    public int ColdRetentionDays { get; set; }
    public bool StorageCompressionEnabled { get; set; }
    public bool AutoCleanupEnabled { get; set; }
    public int CleanupIntervalHours { get; set; }
    public DateTime LastCleanupTime { get; set; }
    public DateTime NextCleanupTime { get; set; }
    public int HotFileCount { get; set; }
    public int ColdFileCount { get; set; }
    public int CompressedFileCount { get; set; }
    public long HotBytes { get; set; }
    public long ColdBytes { get; set; }
    public long CompressedBytes { get; set; }
    public bool DataProcessingEnabled { get; set; }
    public bool CompressionEnabled { get; set; }
    public bool DownsamplingEnabled { get; set; }
    public bool AlignmentEnabled { get; set; }
    public bool FillEnabled { get; set; }
    public bool AggregationEnabled { get; set; }
    public long ReceivedValueCount { get; set; }
    public long WrittenValueCount { get; set; }
    public long SkippedValueCount { get; set; }
    public long CompressedValueCount { get; set; }
    public long DownsampledValueCount { get; set; }
    public long FilledValueCount { get; set; }
    public long AggregatedValueCount { get; set; }
    public bool IsDegraded { get; set; }
    public DateTime LastErrorTime { get; set; }
    public string LastError { get; set; } = string.Empty;
    public CircuitBreakerStatusDto CircuitBreaker { get; set; } = new CircuitBreakerStatusDto();
}

public sealed class RuleEngineRuntimeStatusDto
{
    public bool IsRunning { get; set; }
    public bool Enabled { get; set; }
    public int RuleCount { get; set; }
    public int EnabledRuleCount { get; set; }
    public int ActiveRuleCount { get; set; }
    public int CachedSnapshotCount { get; set; }
    public int RecentEventCount { get; set; }
    public long EvaluationCount { get; set; }
    public long TriggeredCount { get; set; }
    public long ClearedCount { get; set; }
    public long FailedEvaluationCount { get; set; }
    public DateTime LastEvaluationTime { get; set; }
    public DateTime LastEventTime { get; set; }
    public DateTime LastErrorTime { get; set; }
    public string LastError { get; set; } = string.Empty;
    public CircuitBreakerStatusDto CircuitBreaker { get; set; } = new CircuitBreakerStatusDto();
    public IList<RuleEngineRuntimeEventDto> RecentEvents { get; set; } = new List<RuleEngineRuntimeEventDto>();
    public IList<RuleEngineRuleRuntimeStatusDto> Rules { get; set; } = new List<RuleEngineRuleRuntimeStatusDto>();
}

public sealed class RuleEngineRuleRuntimeStatusDto
{
    public string RuleId { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string ConditionType { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string ActiveState { get; set; } = string.Empty;
    public DateTime LastEvaluationTime { get; set; }
    public DateTime LastTriggeredTime { get; set; }
    public DateTime LastClearedTime { get; set; }
    public DateTime LastErrorTime { get; set; }
    public string LastError { get; set; } = string.Empty;
    public long EvaluationCount { get; set; }
    public long TriggeredCount { get; set; }
    public long ClearedCount { get; set; }
    public long FailedEvaluationCount { get; set; }
    public IList<RuleEngineRuntimeEventDto> RecentEvents { get; set; } = new List<RuleEngineRuntimeEventDto>();
}

public sealed class RuleEngineRuntimeEventDto
{
    public string RuleId { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string ConditionType { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string PointCode { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string TagName { get; set; } = string.Empty;
    public double Value { get; set; }
    public double Threshold { get; set; }
    public DateTime Timestamp { get; set; }
}

public sealed class ConfigurationVersionsQuery
{
    public string ConfigType { get; set; } = string.Empty;
    public int Limit { get; set; } = 50;
}

public sealed class CircuitBreakerStatusDto
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string State { get; set; } = string.Empty;
    public bool IsOpen { get; set; }
    public bool IsHalfOpen { get; set; }
    public int ConsecutiveFailures { get; set; }
    public int ConsecutiveSuccesses { get; set; }
    public long TotalFailures { get; set; }
    public long TotalSuccesses { get; set; }
    public long TotalTrips { get; set; }
    public long TotalRejected { get; set; }
    public DateTime OpenedTime { get; set; }
    public DateTime NextRetryTime { get; set; }
    public DateTime LastFailureTime { get; set; }
    public string LastFailureMessage { get; set; } = string.Empty;
    public string DegradedMode { get; set; } = string.Empty;
}

public sealed class RuntimeTagSnapshotQuery
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string TagId { get; set; } = string.Empty;
    public string TagName { get; set; } = string.Empty;
}

public sealed class WriteTagCommand
{
    public string DeviceName { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string TagName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string ValueText { get; set; } = string.Empty;
    public int TimeoutMilliseconds { get; set; } = 10000;
}

public sealed class WriteTagResultDto
{
    public bool Success { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string TagName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string CurrentValueText { get; set; } = string.Empty;
}

public sealed class RollbackConfigurationCommand
{
    public string ConfigType { get; set; } = "project";
    public int Version { get; set; }
}

public sealed class RawConfigurationCommand
{
    public string Source { get; set; } = "WebApi";
    public string Payload { get; set; } = string.Empty;
}

public sealed class SaveProjectConfigurationCommand : ProjectConfigurationDto
{
}

public sealed class ValidateProjectConfigurationCommand : ProjectConfigurationDto
{
}

public sealed class SaveDeviceConfigurationCommand : DeviceConfigurationDto
{
}

public sealed class SaveChannelConfigurationCommand : ChannelConfigurationDto
{
}

public sealed class SaveGroupConfigurationCommand : GroupConfigurationDto
{
}

public sealed class SaveTagConfigurationCommand : TagConfigurationDto
{
}

public sealed class SaveRuleConfigurationCommand : EdgeRuleConfigurationDto
{
}

public sealed class SaveFlowRuleDefinitionCommand : FlowRuleDefinitionDto
{
}

public sealed class SaveMqttConfigurationCommand : MqttConfigurationDto
{
}

public sealed class SaveOpcUaServerConfigurationCommand : OpcUaServerConfigurationDto
{
}

public sealed class SaveHistoryConfigurationCommand : HistoryConfigurationDto
{
}

public sealed class SaveStorageHealthConfigurationCommand : StorageHealthConfigurationDto
{
}

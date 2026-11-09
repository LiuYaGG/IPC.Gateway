/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Configuration
* 项目描述 ：
* 类 名 称 ：FlowRuleDefinition
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Runtime.Configuration
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
using System;
using System.Collections.Generic;

namespace IPC.Runtime.Configuration
{
    public sealed class FlowRuleDefinition
    {
        public FlowRuleDefinition()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = "Flow Rule";
            Description = string.Empty;
            Enabled = true;
            Version = 1;
            LifecycleState = FlowRuleLifecycleStates.Draft;
            PublishedBy = string.Empty;
            Mode = FlowRuleModes.Flow;
            CompiledRuleId = string.Empty;
            Nodes = new List<FlowRuleNode>();
            Edges = new List<FlowRuleEdge>();
            CreatedTime = DateTime.Now;
            UpdatedTime = DateTime.Now;
        }

        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool Enabled { get; set; }
        public int Version { get; set; }
        public string LifecycleState { get; set; }
        public int PublishedVersion { get; set; }
        public DateTime PublishedTime { get; set; }
        public string PublishedBy { get; set; }
        public string Mode { get; set; }
        public string CompiledRuleId { get; set; }
        public List<FlowRuleNode> Nodes { get; set; }
        public List<FlowRuleEdge> Edges { get; set; }
        public DateTime CreatedTime { get; set; }
        public DateTime UpdatedTime { get; set; }
    }

    public sealed class FlowRuleNode
    {
        public FlowRuleNode()
        {
            Id = Guid.NewGuid().ToString("N");
            NodeType = FlowRuleNodeTypes.Condition;
            Label = string.Empty;
            ChannelId = string.Empty;
            ChannelName = string.Empty;
            DeviceId = string.Empty;
            GroupId = string.Empty;
            TagId = string.Empty;
            DeviceName = string.Empty;
            GroupName = string.Empty;
            TagName = string.Empty;
            PointCode = string.Empty;
            DataType = string.Empty;
            ConditionType = EdgeRuleConditionType.Condition.ToString();
            Operator = EdgeRuleComparisonOperator.GreaterThan.ToString();
            LogicalOperator = EdgeRuleLogicalOperator.And.ToString();
            TopicTemplate = "ipc/rule/{pointCode}/{ruleName}";
            ActiveMessage = string.Empty;
            ClearMessage = string.Empty;
            PublishToMqtt = true;
            PublishOnClear = true;
            HysteresisMode = "High";
            Expression = "{value} > 0";
            AlarmLevels = new List<FlowRuleAlarmLevel>();
            QualityOperator = "In";
            QualityValues = "Good";
            WindowStatistic = "Average";
            WindowSeconds = 60;
            WindowSampleCount = 0;
            AggregationStatistic = "Average";
            TrendMode = "Slope";
            TrendWindowSeconds = 300;
            TrendSampleCount = 0;
            TrendMinSlopePerSecond = 0D;
            TrendChangeThreshold = 0D;
            TrendStableDeadband = 0D;
            StateName = "State";
            StateExpectedValue = "1";
            StateClearValue = string.Empty;
            StateTimeoutSeconds = 0;
            RelatedChannelId = string.Empty;
            RelatedChannelName = string.Empty;
            RelatedDeviceId = string.Empty;
            RelatedGroupId = string.Empty;
            RelatedTagId = string.Empty;
            RelatedDeviceName = string.Empty;
            RelatedGroupName = string.Empty;
            RelatedTagName = string.Empty;
            RelatedPointCode = string.Empty;
            RelatedDataType = string.Empty;
            RelationOperator = EdgeRuleComparisonOperator.GreaterThan.ToString();
            RelationMultiplier = 1D;
            RelationOffset = 0D;
            ContextName = "Context";
            ContextExpectedValue = string.Empty;
            ContextOperator = EdgeRuleComparisonOperator.Equal.ToString();
            ContextChannelId = string.Empty;
            ContextChannelName = string.Empty;
            ContextDeviceId = string.Empty;
            ContextGroupId = string.Empty;
            ContextTagId = string.Empty;
            ContextDeviceName = string.Empty;
            ContextGroupName = string.Empty;
            ContextTagName = string.Empty;
            ContextPointCode = string.Empty;
            ContextDataType = string.Empty;
            CycleStartValue = "1";
            CycleEndValue = "0";
            CycleMinSeconds = 0;
            CycleMaxSeconds = 0;
            TaktTargetSeconds = 60D;
            TaktTolerancePercent = 10D;
            AnomalyMode = "ZScore";
            AnomalyThreshold = 3D;
            AnomalyBaselineWindowSeconds = 300;
            AnomalyBaselineSampleCount = 0;
            ModelPurpose = "DeviceAnomaly";
            ModelPath = string.Empty;
            ModelInputTags = string.Empty;
            ModelInputName = string.Empty;
            ModelInputNames = string.Empty;
            ModelOutputName = string.Empty;
            ModelOutputIndex = 0;
            ModelOperator = EdgeRuleComparisonOperator.GreaterThanOrEqual.ToString();
            ModelThreshold = 0.5D;
            ModelTimeoutMilliseconds = 1000;
            AlarmSeverity = "Warning";
            AlarmSuppressSeconds = 0;
            AlarmReTriggerSeconds = 0;
            AlarmEscalateAfterSeconds = 0;
            ActionDelaySeconds = 0;
            ActionCooldownSeconds = 0;
            ActionMaxPerMinute = 0;
            DebugEnabled = true;
            DebugLabel = string.Empty;
            TransformMultiplier = 1D;
            TransformOffset = 0D;
            TransformUseAbsolute = false;
            TransformExpression = string.Empty;
            TransformTimeoutMilliseconds = 50;
            ValueScriptId = string.Empty;
            ValueScriptVersion = 0;
            ValueScriptCategory = string.Empty;
            ValueScriptInputDataType = string.Empty;
            ValueScriptOutputDataType = string.Empty;
            SequenceWindowSeconds = 60;
            SequenceStepTimeoutSeconds = 0;
            SequenceMinIntervalSeconds = 0;
            SequenceResetOnMismatch = true;
            ClearDurationSeconds = 0;
            ExecuteOnActive = true;
            ExecuteOnClear = true;
            EmailSmtpHost = string.Empty;
            EmailSmtpPort = 25;
            EmailEnableSsl = false;
            EmailUsername = string.Empty;
            EmailPassword = string.Empty;
            EmailFrom = string.Empty;
            EmailTo = string.Empty;
            EmailCc = string.Empty;
            EmailSubjectTemplate = "{ruleName} {state}";
            EmailBodyTemplate = "{message}";
            WebhookUrl = string.Empty;
            WebhookMethod = "POST";
            WebhookHeaders = string.Empty;
            WebhookBodyTemplate = "{\"ruleName\":\"{ruleName}\",\"state\":\"{state}\",\"value\":\"{value}\",\"message\":\"{message}\"}";
            WebhookContentType = "application/json";
            WebhookTimeoutSeconds = 5;
            WebhookRetryCount = 0;
        }

        public string Id { get; set; }
        public string NodeType { get; set; }
        public string Label { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public string ChannelId { get; set; }
        public string ChannelName { get; set; }
        public string DeviceId { get; set; }
        public string GroupId { get; set; }
        public string TagId { get; set; }
        public string DeviceName { get; set; }
        public string GroupName { get; set; }
        public string TagName { get; set; }
        public string PointCode { get; set; }
        public string DataType { get; set; }
        public string ConditionType { get; set; }
        public string Operator { get; set; }
        public double CompareValue { get; set; }
        public double LowLimit { get; set; }
        public double HighLimit { get; set; } = 100D;
        public double Deadband { get; set; } = 1D;
        public double RateLimitPerSecond { get; set; } = 1D;
        public string LogicalOperator { get; set; }
        public int DurationSeconds { get; set; }
        public bool PublishToMqtt { get; set; }
        public bool PublishOnClear { get; set; }
        public string TopicTemplate { get; set; }
        public int PublishQos { get; set; }
        public string ActiveMessage { get; set; }
        public string ClearMessage { get; set; }
        public string HysteresisMode { get; set; }
        public double HysteresisOnValue { get; set; }
        public double HysteresisOffValue { get; set; }
        public string Expression { get; set; }
        public List<FlowRuleAlarmLevel> AlarmLevels { get; set; }
        public string QualityOperator { get; set; }
        public string QualityValues { get; set; }
        public string WindowStatistic { get; set; }
        public int WindowSeconds { get; set; }
        public int WindowSampleCount { get; set; }
        public string AggregationStatistic { get; set; }
        public string TrendMode { get; set; }
        public int TrendWindowSeconds { get; set; }
        public int TrendSampleCount { get; set; }
        public double TrendMinSlopePerSecond { get; set; }
        public double TrendChangeThreshold { get; set; }
        public double TrendStableDeadband { get; set; }
        public string StateName { get; set; }
        public string StateExpectedValue { get; set; }
        public string StateClearValue { get; set; }
        public int StateTimeoutSeconds { get; set; }
        public string RelatedChannelId { get; set; }
        public string RelatedChannelName { get; set; }
        public string RelatedDeviceId { get; set; }
        public string RelatedGroupId { get; set; }
        public string RelatedTagId { get; set; }
        public string RelatedDeviceName { get; set; }
        public string RelatedGroupName { get; set; }
        public string RelatedTagName { get; set; }
        public string RelatedPointCode { get; set; }
        public string RelatedDataType { get; set; }
        public string RelationOperator { get; set; }
        public double RelationMultiplier { get; set; }
        public double RelationOffset { get; set; }
        public string ContextName { get; set; }
        public string ContextExpectedValue { get; set; }
        public string ContextOperator { get; set; }
        public string ContextChannelId { get; set; }
        public string ContextChannelName { get; set; }
        public string ContextDeviceId { get; set; }
        public string ContextGroupId { get; set; }
        public string ContextTagId { get; set; }
        public string ContextDeviceName { get; set; }
        public string ContextGroupName { get; set; }
        public string ContextTagName { get; set; }
        public string ContextPointCode { get; set; }
        public string ContextDataType { get; set; }
        public string CycleStartValue { get; set; }
        public string CycleEndValue { get; set; }
        public int CycleMinSeconds { get; set; }
        public int CycleMaxSeconds { get; set; }
        public double TaktTargetSeconds { get; set; }
        public double TaktTolerancePercent { get; set; }
        public string AnomalyMode { get; set; }
        public double AnomalyThreshold { get; set; }
        public int AnomalyBaselineWindowSeconds { get; set; }
        public int AnomalyBaselineSampleCount { get; set; }
        public string ModelPurpose { get; set; }
        public string ModelPath { get; set; }
        public string ModelInputTags { get; set; }
        public string ModelInputName { get; set; }
        public string ModelInputNames { get; set; }
        public string ModelOutputName { get; set; }
        public int ModelOutputIndex { get; set; }
        public string ModelOperator { get; set; }
        public double ModelThreshold { get; set; }
        public int ModelTimeoutMilliseconds { get; set; }
        public string AlarmSeverity { get; set; }
        public int AlarmSuppressSeconds { get; set; }
        public int AlarmReTriggerSeconds { get; set; }
        public int AlarmEscalateAfterSeconds { get; set; }
        public int ActionDelaySeconds { get; set; }
        public int ActionCooldownSeconds { get; set; }
        public int ActionMaxPerMinute { get; set; }
        public bool DebugEnabled { get; set; }
        public string DebugLabel { get; set; }
        public double TransformMultiplier { get; set; }
        public double TransformOffset { get; set; }
        public bool TransformUseAbsolute { get; set; }
        public string TransformExpression { get; set; }
        public int TransformTimeoutMilliseconds { get; set; }
        public string ValueScriptId { get; set; }
        public int ValueScriptVersion { get; set; }
        public string ValueScriptCategory { get; set; }
        public string ValueScriptInputDataType { get; set; }
        public string ValueScriptOutputDataType { get; set; }
        public int SequenceWindowSeconds { get; set; }
        public int SequenceStepTimeoutSeconds { get; set; }
        public int SequenceMinIntervalSeconds { get; set; }
        public bool SequenceResetOnMismatch { get; set; }
        public int ClearDurationSeconds { get; set; }
        public bool ExecuteOnActive { get; set; }
        public bool ExecuteOnClear { get; set; }
        public string EmailSmtpHost { get; set; }
        public int EmailSmtpPort { get; set; }
        public bool EmailEnableSsl { get; set; }
        public string EmailUsername { get; set; }
        public string EmailPassword { get; set; }
        public string EmailFrom { get; set; }
        public string EmailTo { get; set; }
        public string EmailCc { get; set; }
        public string EmailSubjectTemplate { get; set; }
        public string EmailBodyTemplate { get; set; }
        public string WebhookUrl { get; set; }
        public string WebhookMethod { get; set; }
        public string WebhookHeaders { get; set; }
        public string WebhookBodyTemplate { get; set; }
        public string WebhookContentType { get; set; }
        public int WebhookTimeoutSeconds { get; set; }
        public int WebhookRetryCount { get; set; }
    }

    public sealed class FlowRuleAlarmLevel
    {
        public FlowRuleAlarmLevel()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = "Level";
            Severity = "Warning";
            Operator = EdgeRuleComparisonOperator.GreaterThanOrEqual.ToString();
            Message = string.Empty;
        }

        public string Id { get; set; }
        public string Name { get; set; }
        public string Severity { get; set; }
        public string Operator { get; set; }
        public double CompareValue { get; set; }
        public string Message { get; set; }
    }

    public sealed class FlowRuleEdge
    {
        public FlowRuleEdge()
        {
            Id = Guid.NewGuid().ToString("N");
            SourceNodeId = string.Empty;
            TargetNodeId = string.Empty;
            SourcePort = string.Empty;
            TargetPort = string.Empty;
        }

        public string Id { get; set; }
        public string SourceNodeId { get; set; }
        public string TargetNodeId { get; set; }
        public string SourcePort { get; set; }
        public string TargetPort { get; set; }
    }

    public static class FlowRuleModes
    {
        public const string SimpleCompiled = "SimpleCompiled";
        public const string Flow = "Flow";
    }

    public static class FlowRuleLifecycleStates
    {
        public const string Draft = "Draft";
        public const string Published = "Published";
        public const string Archived = "Archived";
    }

    public static class FlowRuleNodeTypes
    {
        public const string TagInput = "TagInput";
        public const string QualityGate = "QualityGate";
        public const string Condition = "Condition";
        public const string Threshold = "Threshold";
        public const string Deadband = "Deadband";
        public const string RateOfChange = "RateOfChange";
        public const string Hysteresis = "Hysteresis";
        public const string MultiLevelAlarm = "MultiLevelAlarm";
        public const string Expression = "Expression";
        public const string SlidingWindow = "SlidingWindow";
        public const string Aggregation = "Aggregation";
        public const string WindowCalculation = "WindowCalculation";
        public const string Trend = "Trend";
        public const string StateMachine = "StateMachine";
        public const string CycleTime = "CycleTime";
        public const string ProcessTakt = "ProcessTakt";
        public const string AnomalyDetection = "AnomalyDetection";
        public const string ModelInference = "ModelInference";
        public const string TagRelation = "TagRelation";
        public const string ContextGate = "ContextGate";
        public const string Transform = "Transform";
        public const string Function = "Function";
        public const string ValueScript = "ValueScript";
        public const string Sequence = "Sequence";
        public const string Logic = "Logic";
        public const string Duration = "Duration";
        public const string AlarmLifecycle = "AlarmLifecycle";
        public const string ActionPolicy = "ActionPolicy";
        public const string DebugProbe = "DebugProbe";
        public const string MqttPublish = "MqttPublish";
        public const string EmailNotify = "EmailNotify";
        public const string WebhookCall = "WebhookCall";
    }
}

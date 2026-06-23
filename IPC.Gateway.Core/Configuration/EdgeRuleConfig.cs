/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Configuration
* 项目描述 ：
* 类 名 称 ：EdgeRuleConfig
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
    
    
    
    
    
    
    
    
    
    public sealed class EdgeRuleConfig
    {
        public EdgeRuleConfig()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = "Rule";
            Enabled = true;
            ConditionType = EdgeRuleConditionType.Threshold;
            SourcePointCode = string.Empty;
            SourceDeviceName = string.Empty;
            SourceGroupName = string.Empty;
            SourceTagName = string.Empty;
            SourceDataType = string.Empty;
            LowLimit = 0D;
            HighLimit = 100D;
            Deadband = 1D;
            RateLimitPerSecond = 1D;
            Operator = EdgeRuleComparisonOperator.GreaterThan;
            CompareValue = 0D;
            LogicalOperator = EdgeRuleLogicalOperator.And;
            Conditions = new List<EdgeRuleConditionConfig>();
            DurationSeconds = 0;
            PublishToMqtt = true;
            PublishOnClear = true;
            PublishTopicTemplate = "ipc/rule/{pointCode}/{ruleName}";
            PublishQos = 0;
            ActiveMessage = string.Empty;
            ClearMessage = string.Empty;
            Description = string.Empty;
            HysteresisMode = "High";
            Expression = "{value} > 0";
            AlarmLevels = new List<EdgeRuleAlarmLevelConfig>();
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
            RelatedDeviceName = string.Empty;
            RelatedGroupName = string.Empty;
            RelatedTagName = string.Empty;
            RelatedPointCode = string.Empty;
            RelatedDataType = string.Empty;
            RelationOperator = EdgeRuleComparisonOperator.GreaterThan;
            RelationMultiplier = 1D;
            RelationOffset = 0D;
            ContextName = "Context";
            ContextExpectedValue = string.Empty;
            ContextOperator = EdgeRuleComparisonOperator.Equal;
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
            ModelOperator = EdgeRuleComparisonOperator.GreaterThanOrEqual;
            ModelThreshold = 0.5D;
            ModelTimeoutMilliseconds = 1000;
            AlarmSeverity = "Warning";
            AlarmSuppressSeconds = 0;
            AlarmReTriggerSeconds = 0;
            AlarmEscalateAfterSeconds = 0;
            ActionDelaySeconds = 0;
            ActionCooldownSeconds = 0;
            ActionMaxPerMinute = 0;
            TransformMultiplier = 1D;
            TransformOffset = 0D;
            TransformUseAbsolute = false;
            TransformExpression = string.Empty;
            TransformTimeoutMilliseconds = 50;
            SequenceWindowSeconds = 60;
            SequenceStepTimeoutSeconds = 0;
            SequenceMinIntervalSeconds = 0;
            SequenceResetOnMismatch = true;
            ClearDurationSeconds = 0;
            Actions = new List<EdgeRuleActionConfig>();
        }

        public string Id { get; set; }
        public string Name { get; set; }
        public bool Enabled { get; set; }
        public EdgeRuleConditionType ConditionType { get; set; }
        public string SourcePointCode { get; set; }
        public string SourceDeviceName { get; set; }
        public string SourceGroupName { get; set; }
        public string SourceTagName { get; set; }
        public string SourceDataType { get; set; }
        public double LowLimit { get; set; }
        public double HighLimit { get; set; }
        public double Deadband { get; set; }
        public double RateLimitPerSecond { get; set; }
        public EdgeRuleComparisonOperator Operator { get; set; }
        public double CompareValue { get; set; }
        public EdgeRuleLogicalOperator LogicalOperator { get; set; }
        public List<EdgeRuleConditionConfig> Conditions { get; set; }
        public int DurationSeconds { get; set; }
        public bool PublishToMqtt { get; set; }
        public bool PublishOnClear { get; set; }
        public string PublishTopicTemplate { get; set; }
        public int PublishQos { get; set; }
        public string ActiveMessage { get; set; }
        public string ClearMessage { get; set; }
        public string Description { get; set; }
        public string HysteresisMode { get; set; }
        public double HysteresisOnValue { get; set; }
        public double HysteresisOffValue { get; set; }
        public string Expression { get; set; }
        public List<EdgeRuleAlarmLevelConfig> AlarmLevels { get; set; }
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
        public string RelatedDeviceName { get; set; }
        public string RelatedGroupName { get; set; }
        public string RelatedTagName { get; set; }
        public string RelatedPointCode { get; set; }
        public string RelatedDataType { get; set; }
        public EdgeRuleComparisonOperator RelationOperator { get; set; }
        public double RelationMultiplier { get; set; }
        public double RelationOffset { get; set; }
        public string ContextName { get; set; }
        public string ContextExpectedValue { get; set; }
        public EdgeRuleComparisonOperator ContextOperator { get; set; }
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
        public EdgeRuleComparisonOperator ModelOperator { get; set; }
        public double ModelThreshold { get; set; }
        public int ModelTimeoutMilliseconds { get; set; }
        public string AlarmSeverity { get; set; }
        public int AlarmSuppressSeconds { get; set; }
        public int AlarmReTriggerSeconds { get; set; }
        public int AlarmEscalateAfterSeconds { get; set; }
        public int ActionDelaySeconds { get; set; }
        public int ActionCooldownSeconds { get; set; }
        public int ActionMaxPerMinute { get; set; }
        public double TransformMultiplier { get; set; }
        public double TransformOffset { get; set; }
        public bool TransformUseAbsolute { get; set; }
        public string TransformExpression { get; set; }
        public int TransformTimeoutMilliseconds { get; set; }
        public int SequenceWindowSeconds { get; set; }
        public int SequenceStepTimeoutSeconds { get; set; }
        public int SequenceMinIntervalSeconds { get; set; }
        public bool SequenceResetOnMismatch { get; set; }
        public int ClearDurationSeconds { get; set; }
        public List<EdgeRuleActionConfig> Actions { get; set; }
    }

    public sealed class EdgeRuleActionConfig
    {
        public EdgeRuleActionConfig()
        {
            Id = Guid.NewGuid().ToString("N");
            ActionType = FlowRuleNodeTypes.MqttPublish;
            Enabled = true;
            ExecuteOnActive = true;
            ExecuteOnClear = true;
            TopicTemplate = "ipc/rule/{pointCode}/{ruleName}";
            Qos = 0;
            ActiveMessage = string.Empty;
            ClearMessage = string.Empty;
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
            DebugLabel = string.Empty;
        }

        public string Id { get; set; }
        public string ActionType { get; set; }
        public bool Enabled { get; set; }
        public bool ExecuteOnActive { get; set; }
        public bool ExecuteOnClear { get; set; }
        public string TopicTemplate { get; set; }
        public int Qos { get; set; }
        public string ActiveMessage { get; set; }
        public string ClearMessage { get; set; }
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
        public string DebugLabel { get; set; }
    }

    public sealed class EdgeRuleAlarmLevelConfig
    {
        public EdgeRuleAlarmLevelConfig()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = "Level";
            Severity = "Warning";
            Operator = EdgeRuleComparisonOperator.GreaterThanOrEqual;
            Message = string.Empty;
        }

        public string Id { get; set; }
        public string Name { get; set; }
        public string Severity { get; set; }
        public EdgeRuleComparisonOperator Operator { get; set; }
        public double CompareValue { get; set; }
        public string Message { get; set; }
    }
}

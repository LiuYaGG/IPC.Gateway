/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Application.Gateway.Contracts
* 项目描述 ：
* 类 名 称 ：GatewayConfigurationContractMapper
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
using System.Collections.Generic;
using IPC.EdgeGateway;
using IPC.Gateway.DataProcessing;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Core.Resilience;
using IPC.Plc.Communication.Core;
using IPC.Runtime.Api;
using IPC.Runtime.Configuration;
using IPC.Runtime.Values;

namespace IPC.Gateway.Core.Application.Gateway.Contracts;

public static class GatewayConfigurationContractMapper
{
    public static GatewaySyncDto ToDto(GatewaySyncResult result)
    {
        return new GatewaySyncDto
        {
            Status = ToDto(result.Status),
            Project = ToDto(result.Project),
            Mqtt = ToDto(result.Mqtt),
            OpcUa = ToDto(result.OpcUa),
            History = ToDto(result.History),
            StorageHealth = ToDto(result.StorageHealth)
        };
    }

    public static GatewayRuntimeStatusDto ToDto(GatewayRuntimeStatus status)
    {
        return new GatewayRuntimeStatusDto
        {
            IsRunning = status.IsRunning,
            ProjectId = status.ProjectId,
            ProjectName = status.ProjectName,
            ProjectPath = status.ProjectPath,
            ConfigurationStore = status.ConfigurationStore,
            DeviceCount = status.DeviceCount,
            GroupCount = status.GroupCount,
            TagCount = status.TagCount,
            EnabledDeviceCount = status.EnabledDeviceCount,
            OnlineDeviceCount = status.OnlineDeviceCount,
            GoodTagCount = status.GoodTagCount,
            BadTagCount = status.BadTagCount,
            NoDataTagCount = status.NoDataTagCount,
            StartedTime = status.StartedTime,
            LastReloadTime = status.LastReloadTime,
            ConfigValidation = ToDto(status.ConfigValidation),
            Devices = status.Devices.Select(ToDto).ToList(),
            Tags = status.Tags.Select(ToDto).ToList(),
            RecentErrors = status.RecentErrors.Select(ToDto).ToList(),
            Mqtt = ToDto(status.Mqtt),
            OpcUa = ToDto(status.OpcUa),
            History = ToDto(status.History),
            FlowRuleEngine = ToDto(status.FlowRuleEngine),
            Scheduler = ToDto(status.Scheduler),
            System = ToDto(status.System)
        };
    }

    public static ProjectConfigurationDto ToDto(ProjectConfig project)
    {
        return new ProjectConfigurationDto
        {
            ProjectId = project.ProjectId,
            Name = project.Name,
            Channels = project.Channels.Select(ToDto).ToList(),
            Devices = project.Devices.Select(ToDto).ToList(),
            Rules = project.Rules.Select(ToDto).ToList(),
            FlowRules = project.FlowRules.Select(ToDto).ToList()
        };
    }

    public static ChannelConfigurationDto ToDto(ChannelConfig channel)
    {
        return new ChannelConfigurationDto
        {
            Id = channel.Id,
            Name = channel.Name,
            Enabled = channel.Enabled,
            Protocol = channel.Protocol.ToString(),
            DriverId = channel.DriverId,
            MaxConcurrentDevicePolls = channel.MaxConcurrentDevicePolls,
            SchedulingWeight = channel.SchedulingWeight
        };
    }

    public static DeviceConfigurationDto ToDto(DeviceConfig device)
    {
        return new DeviceConfigurationDto
        {
            Id = device.Id,
            ChannelId = device.ChannelId,
            Name = device.Name,
            Enabled = device.Enabled,
            Protocol = device.Protocol.ToString(),
            Connection = ToDto(device.Connection),
            DefaultScanRateMs = device.DefaultScanRateMs,
            FailureRetryDelayMs = device.FailureRetryDelayMs,
            MaxFailureRetryDelayMs = device.MaxFailureRetryDelayMs,
            Tags = device.Tags.Select(tag => ToDto(tag, device.Protocol.ToString())).ToList(),
            Groups = device.Groups.Select(group => ToDto(group, device.Protocol.ToString())).ToList()
        };
    }

    public static GroupConfigurationDto ToDto(GroupConfig group)
    {
        return ToDto(group, string.Empty);
    }

    public static GroupConfigurationDto ToDto(GroupConfig group, string protocol)
    {
        return new GroupConfigurationDto
        {
            Id = group.Id,
            DeviceId = group.DeviceId,
            Name = group.Name,
            Enabled = group.Enabled,
            ScanRateMs = group.ScanRateMs,
            Tags = group.Tags.Select(tag => ToDto(tag, protocol)).ToList()
        };
    }

    public static TagConfigurationDto ToDto(TagConfig tag)
    {
        return ToDto(tag, string.Empty);
    }

    public static TagConfigurationDto ToDto(TagConfig tag, string protocol)
    {
        return new TagConfigurationDto
        {
            Id = tag.Id,
            DeviceId = tag.DeviceId,
            GroupId = tag.GroupId,
            Name = tag.Name,
            Protocol = protocol ?? string.Empty,
            Address = tag.Address,
            MeterAddress = tag.MeterAddress,
            MeterDataIdentifier = tag.MeterDataIdentifier,
            MeterType = tag.MeterType,
            DataType = tag.DataType.ToString(),
            ElementCount = tag.ElementCount,
            ElementOffset = tag.ElementOffset,
            Enabled = tag.Enabled,
            MqttPublishEnabled = tag.MqttPublishEnabled,
            AccessMode = tag.AccessMode.ToString(),
            ScanRateMs = tag.ScanRateMs,
            FailureRetryDelayMs = tag.FailureRetryDelayMs,
            Unit = tag.Unit,
            PointCode = tag.PointCode,
            AssetPath = tag.AssetPath,
            BusinessType = tag.BusinessType,
            Source = tag.Source,
            Precision = tag.Precision,
            Scaling = ToDto(tag.Scaling),
            Cleaning = ToDto(tag.Cleaning),
            Alarm = ToDto(tag.Alarm),
            Description = tag.Description
        };
    }

    public static PlcConnectionDto ToDto(PlcConnectionOptions options)
    {
        options ??= new PlcConnectionOptions();
        return new PlcConnectionDto
        {
            Protocol = options.Protocol.ToString(),
            Host = options.Host ?? string.Empty,
            Port = options.Port,
            Rack = options.Rack,
            Slot = options.Slot,
            TimeoutMilliseconds = options.TimeoutMilliseconds,
            WordOrder = options.WordOrder.ToString(),
            Transport = options.Transport.ToString(),
            DataBits = options.DataBits,
            SerialParity = options.SerialParity.ToString(),
            SerialStopBits = options.SerialStopBits.ToString(),
            Username = options.Username ?? string.Empty,
            Password = options.Password ?? string.Empty,
            OpcUaSecurityPolicy = options.OpcUaSecurityPolicy ?? "None",
            OpcUaMessageSecurityMode = options.OpcUaMessageSecurityMode ?? "None",
            OpcUaAutoTrustServerCertificate = options.OpcUaAutoTrustServerCertificate,
            OpcDaServerProgId = options.OpcDaServerProgId,
            OpcDaGroupName = options.OpcDaGroupName,
            DriverId = options.DriverId,
            DriverOptionsJson = options.DriverOptionsJson
        };
    }

    public static ScalingConfigurationDto ToDto(ScalingConfig scaling)
    {
        scaling ??= ScalingConfig.Default();
        return new ScalingConfigurationDto
        {
            Enabled = scaling.Enabled,
            Multiplier = scaling.Multiplier,
            Offset = scaling.Offset,
            ClampEnabled = scaling.ClampEnabled,
            MinValue = scaling.MinValue,
            MaxValue = scaling.MaxValue,
            DecimalPlaces = scaling.DecimalPlaces
        };
    }

    public static DataCleaningConfigurationDto ToDto(DataCleaningConfig cleaning)
    {
        cleaning ??= DataCleaningConfig.Default();
        DataCleaningConfigurationDto dto = new DataCleaningConfigurationDto
        {
            Enabled = cleaning.Enabled,
            OutOfRangeEnabled = cleaning.OutOfRangeEnabled,
            MinValue = cleaning.MinValue,
            MaxValue = cleaning.MaxValue,
            DeadbandEnabled = cleaning.DeadbandEnabled,
            Deadband = cleaning.Deadband,
            DuplicateFilterEnabled = cleaning.DuplicateFilterEnabled,
            SpikeFilterEnabled = cleaning.SpikeFilterEnabled,
            SpikeThreshold = cleaning.SpikeThreshold,
            SpikeWindowSeconds = cleaning.SpikeWindowSeconds,
            EnumMappingEnabled = cleaning.EnumMappingEnabled,
            UnitConversionEnabled = cleaning.UnitConversionEnabled,
            SourceUnit = cleaning.SourceUnit,
            TargetUnit = cleaning.TargetUnit,
            UnitMultiplier = cleaning.UnitMultiplier,
            UnitOffset = cleaning.UnitOffset,
            PreserveLastGoodOnFilter = cleaning.PreserveLastGoodOnFilter
        };

        if (cleaning.EnumMappings != null)
        {
            for (int i = 0; i < cleaning.EnumMappings.Count; i++)
            {
                DataCleaningEnumMappingConfig item = cleaning.EnumMappings[i];
                if (item == null)
                    continue;
                dto.EnumMappings.Add(new DataCleaningEnumMappingDto
                {
                    RawValue = item.RawValue,
                    CleanValue = item.CleanValue,
                    Description = item.Description
                });
            }
        }

        return dto;
    }

    public static TagAlarmConfigurationDto ToDto(TagAlarmConfig alarm)
    {
        alarm ??= TagAlarmConfig.Default();
        return new TagAlarmConfigurationDto
        {
            Enabled = alarm.Enabled,
            LowLimit = alarm.LowLimit,
            HighLimit = alarm.HighLimit,
            LowAlarmMessage = alarm.LowAlarmMessage,
            HighAlarmMessage = alarm.HighAlarmMessage,
            WarningDeviation = alarm.WarningDeviation,
            LowWarningMessage = alarm.LowWarningMessage,
            HighWarningMessage = alarm.HighWarningMessage
        };
    }

    public static EdgeRuleConfigurationDto ToDto(EdgeRuleConfig rule)
    {
        return new EdgeRuleConfigurationDto
        {
            Id = rule.Id,
            Name = rule.Name,
            Enabled = rule.Enabled,
            ConditionType = rule.ConditionType.ToString(),
            SourceChannelId = rule.SourceChannelId,
            SourceChannelName = rule.SourceChannelName,
            SourceDeviceId = rule.SourceDeviceId,
            SourceGroupId = rule.SourceGroupId,
            SourceTagId = rule.SourceTagId,
            SourcePointCode = rule.SourcePointCode,
            SourceDeviceName = rule.SourceDeviceName,
            SourceGroupName = rule.SourceGroupName,
            SourceTagName = rule.SourceTagName,
            SourceDataType = rule.SourceDataType,
            LowLimit = rule.LowLimit,
            HighLimit = rule.HighLimit,
            Deadband = rule.Deadband,
            RateLimitPerSecond = rule.RateLimitPerSecond,
            Operator = rule.Operator.ToString(),
            CompareValue = rule.CompareValue,
            LogicalOperator = rule.LogicalOperator.ToString(),
            Conditions = rule.Conditions.Select(ToDto).ToList(),
            DurationSeconds = rule.DurationSeconds,
            PublishToMqtt = rule.PublishToMqtt,
            PublishOnClear = rule.PublishOnClear,
            PublishTopicTemplate = rule.PublishTopicTemplate,
            PublishQos = rule.PublishQos,
            ActiveMessage = rule.ActiveMessage,
            ClearMessage = rule.ClearMessage,
            Description = rule.Description,
            QualityOperator = rule.QualityOperator,
            QualityValues = rule.QualityValues,
            WindowStatistic = rule.WindowStatistic,
            WindowSeconds = rule.WindowSeconds,
            WindowSampleCount = rule.WindowSampleCount,
            AggregationStatistic = rule.AggregationStatistic,
            TrendMode = rule.TrendMode,
            TrendWindowSeconds = rule.TrendWindowSeconds,
            TrendSampleCount = rule.TrendSampleCount,
            TrendMinSlopePerSecond = rule.TrendMinSlopePerSecond,
            TrendChangeThreshold = rule.TrendChangeThreshold,
            TrendStableDeadband = rule.TrendStableDeadband,
            StateName = rule.StateName,
            StateExpectedValue = rule.StateExpectedValue,
            StateClearValue = rule.StateClearValue,
            StateTimeoutSeconds = rule.StateTimeoutSeconds,
            RelatedChannelId = rule.RelatedChannelId,
            RelatedChannelName = rule.RelatedChannelName,
            RelatedDeviceId = rule.RelatedDeviceId,
            RelatedGroupId = rule.RelatedGroupId,
            RelatedTagId = rule.RelatedTagId,
            RelatedDeviceName = rule.RelatedDeviceName,
            RelatedGroupName = rule.RelatedGroupName,
            RelatedTagName = rule.RelatedTagName,
            RelatedPointCode = rule.RelatedPointCode,
            RelatedDataType = rule.RelatedDataType,
            RelationOperator = rule.RelationOperator.ToString(),
            RelationMultiplier = rule.RelationMultiplier,
            RelationOffset = rule.RelationOffset,
            ContextName = rule.ContextName,
            ContextExpectedValue = rule.ContextExpectedValue,
            ContextOperator = rule.ContextOperator.ToString(),
            ContextChannelId = rule.ContextChannelId,
            ContextChannelName = rule.ContextChannelName,
            ContextDeviceId = rule.ContextDeviceId,
            ContextGroupId = rule.ContextGroupId,
            ContextTagId = rule.ContextTagId,
            ContextDeviceName = rule.ContextDeviceName,
            ContextGroupName = rule.ContextGroupName,
            ContextTagName = rule.ContextTagName,
            ContextPointCode = rule.ContextPointCode,
            ContextDataType = rule.ContextDataType,
            CycleStartValue = rule.CycleStartValue,
            CycleEndValue = rule.CycleEndValue,
            CycleMinSeconds = rule.CycleMinSeconds,
            CycleMaxSeconds = rule.CycleMaxSeconds,
            TaktTargetSeconds = rule.TaktTargetSeconds,
            TaktTolerancePercent = rule.TaktTolerancePercent,
            AnomalyMode = rule.AnomalyMode,
            AnomalyThreshold = rule.AnomalyThreshold,
            AnomalyBaselineWindowSeconds = rule.AnomalyBaselineWindowSeconds,
            AnomalyBaselineSampleCount = rule.AnomalyBaselineSampleCount,
            ModelPurpose = rule.ModelPurpose,
            ModelPath = rule.ModelPath,
            ModelInputTags = rule.ModelInputTags,
            ModelInputName = rule.ModelInputName,
            ModelInputNames = rule.ModelInputNames,
            ModelOutputName = rule.ModelOutputName,
            ModelOutputIndex = Math.Max(0, rule.ModelOutputIndex),
            ModelOperator = rule.ModelOperator.ToString(),
            ModelThreshold = rule.ModelThreshold,
            ModelTimeoutMilliseconds = NormalizeModelTimeout(rule.ModelTimeoutMilliseconds),
            AlarmSeverity = rule.AlarmSeverity,
            AlarmSuppressSeconds = rule.AlarmSuppressSeconds,
            AlarmReTriggerSeconds = rule.AlarmReTriggerSeconds,
            AlarmEscalateAfterSeconds = rule.AlarmEscalateAfterSeconds,
            ActionDelaySeconds = rule.ActionDelaySeconds,
            ActionCooldownSeconds = rule.ActionCooldownSeconds,
            ActionMaxPerMinute = rule.ActionMaxPerMinute,
            TransformMultiplier = rule.TransformMultiplier,
            TransformOffset = rule.TransformOffset,
            TransformUseAbsolute = rule.TransformUseAbsolute,
            TransformExpression = rule.TransformExpression,
            TransformTimeoutMilliseconds = NormalizeTransformTimeout(rule.TransformTimeoutMilliseconds),
            SequenceWindowSeconds = rule.SequenceWindowSeconds,
            SequenceStepTimeoutSeconds = rule.SequenceStepTimeoutSeconds,
            SequenceMinIntervalSeconds = rule.SequenceMinIntervalSeconds,
            SequenceResetOnMismatch = rule.SequenceResetOnMismatch,
            ClearDurationSeconds = rule.ClearDurationSeconds,
            Actions = (rule.Actions ?? new List<EdgeRuleActionConfig>()).Select(ToDto).ToList()
        };
    }

    public static EdgeRuleActionDto ToDto(EdgeRuleActionConfig action)
    {
        if (action == null)
            return new EdgeRuleActionDto();

        return new EdgeRuleActionDto
        {
            Id = action.Id,
            ActionType = action.ActionType,
            Enabled = action.Enabled,
            ExecuteOnActive = action.ExecuteOnActive,
            ExecuteOnClear = action.ExecuteOnClear,
            TopicTemplate = action.TopicTemplate,
            Qos = action.Qos,
            ActiveMessage = action.ActiveMessage,
            ClearMessage = action.ClearMessage,
            EmailSmtpHost = action.EmailSmtpHost,
            EmailSmtpPort = action.EmailSmtpPort,
            EmailEnableSsl = action.EmailEnableSsl,
            EmailUsername = action.EmailUsername,
            EmailPassword = action.EmailPassword,
            EmailFrom = action.EmailFrom,
            EmailTo = action.EmailTo,
            EmailCc = action.EmailCc,
            EmailSubjectTemplate = action.EmailSubjectTemplate,
            EmailBodyTemplate = action.EmailBodyTemplate,
            WebhookUrl = action.WebhookUrl,
            WebhookMethod = action.WebhookMethod,
            WebhookHeaders = action.WebhookHeaders,
            WebhookBodyTemplate = action.WebhookBodyTemplate,
            WebhookContentType = action.WebhookContentType,
            WebhookTimeoutSeconds = action.WebhookTimeoutSeconds,
            WebhookRetryCount = action.WebhookRetryCount,
            DebugLabel = action.DebugLabel
        };
    }

    public static EdgeRuleConditionDto ToDto(EdgeRuleConditionConfig condition)
    {
        return new EdgeRuleConditionDto
        {
            Id = condition.Id,
            SourceChannelId = condition.SourceChannelId,
            SourceChannelName = condition.SourceChannelName,
            SourceDeviceId = condition.SourceDeviceId,
            SourceGroupId = condition.SourceGroupId,
            SourceTagId = condition.SourceTagId,
            SourcePointCode = condition.SourcePointCode,
            SourceDeviceName = condition.SourceDeviceName,
            SourceGroupName = condition.SourceGroupName,
            SourceTagName = condition.SourceTagName,
            SourceDataType = condition.SourceDataType,
            Operator = condition.Operator.ToString(),
            CompareValue = condition.CompareValue,
            TransformMultiplier = condition.TransformMultiplier,
            TransformOffset = condition.TransformOffset,
            TransformUseAbsolute = condition.TransformUseAbsolute,
            TransformExpression = condition.TransformExpression
        };
    }

    public static FlowRuleDefinitionDto ToDto(FlowRuleDefinition rule)
    {
        if (rule == null)
            return new FlowRuleDefinitionDto();

        return new FlowRuleDefinitionDto
        {
            Id = rule.Id,
            Name = rule.Name,
            Description = rule.Description,
            Enabled = rule.Enabled,
            Version = rule.Version,
            LifecycleState = rule.LifecycleState,
            PublishedVersion = rule.PublishedVersion,
            PublishedTime = rule.PublishedTime,
            PublishedBy = rule.PublishedBy,
            Mode = rule.Mode,
            CompiledRuleId = rule.CompiledRuleId,
            Nodes = rule.Nodes.Select(ToDto).ToList(),
            Edges = rule.Edges.Select(ToDto).ToList(),
            CreatedTime = rule.CreatedTime,
            UpdatedTime = rule.UpdatedTime
        };
    }

    public static FlowRuleNodeDto ToDto(FlowRuleNode node)
    {
        if (node == null)
            return new FlowRuleNodeDto();

        return new FlowRuleNodeDto
        {
            Id = node.Id,
            NodeType = node.NodeType,
            Label = node.Label,
            X = node.X,
            Y = node.Y,
            ChannelId = node.ChannelId,
            ChannelName = node.ChannelName,
            DeviceId = node.DeviceId,
            GroupId = node.GroupId,
            TagId = node.TagId,
            DeviceName = node.DeviceName,
            GroupName = node.GroupName,
            TagName = node.TagName,
            PointCode = node.PointCode,
            DataType = node.DataType,
            ConditionType = node.ConditionType,
            Operator = node.Operator,
            CompareValue = node.CompareValue,
            LowLimit = node.LowLimit,
            HighLimit = node.HighLimit,
            Deadband = node.Deadband,
            RateLimitPerSecond = node.RateLimitPerSecond,
            LogicalOperator = node.LogicalOperator,
            DurationSeconds = node.DurationSeconds,
            PublishToMqtt = node.PublishToMqtt,
            PublishOnClear = node.PublishOnClear,
            TopicTemplate = node.TopicTemplate,
            PublishQos = node.PublishQos,
            ActiveMessage = node.ActiveMessage,
            ClearMessage = node.ClearMessage,
            HysteresisMode = node.HysteresisMode,
            HysteresisOnValue = node.HysteresisOnValue,
            HysteresisOffValue = node.HysteresisOffValue,
            Expression = node.Expression,
            AlarmLevels = node.AlarmLevels.Select(ToDto).ToList(),
            QualityOperator = node.QualityOperator,
            QualityValues = node.QualityValues,
            WindowStatistic = node.WindowStatistic,
            WindowSeconds = node.WindowSeconds,
            WindowSampleCount = node.WindowSampleCount,
            AggregationStatistic = node.AggregationStatistic,
            TrendMode = node.TrendMode,
            TrendWindowSeconds = node.TrendWindowSeconds,
            TrendSampleCount = node.TrendSampleCount,
            TrendMinSlopePerSecond = node.TrendMinSlopePerSecond,
            TrendChangeThreshold = node.TrendChangeThreshold,
            TrendStableDeadband = node.TrendStableDeadband,
            StateName = node.StateName,
            StateExpectedValue = node.StateExpectedValue,
            StateClearValue = node.StateClearValue,
            StateTimeoutSeconds = node.StateTimeoutSeconds,
            RelatedChannelId = node.RelatedChannelId,
            RelatedChannelName = node.RelatedChannelName,
            RelatedDeviceId = node.RelatedDeviceId,
            RelatedGroupId = node.RelatedGroupId,
            RelatedTagId = node.RelatedTagId,
            RelatedDeviceName = node.RelatedDeviceName,
            RelatedGroupName = node.RelatedGroupName,
            RelatedTagName = node.RelatedTagName,
            RelatedPointCode = node.RelatedPointCode,
            RelatedDataType = node.RelatedDataType,
            RelationOperator = node.RelationOperator,
            RelationMultiplier = node.RelationMultiplier,
            RelationOffset = node.RelationOffset,
            ContextName = node.ContextName,
            ContextExpectedValue = node.ContextExpectedValue,
            ContextOperator = node.ContextOperator,
            ContextChannelId = node.ContextChannelId,
            ContextChannelName = node.ContextChannelName,
            ContextDeviceId = node.ContextDeviceId,
            ContextGroupId = node.ContextGroupId,
            ContextTagId = node.ContextTagId,
            ContextDeviceName = node.ContextDeviceName,
            ContextGroupName = node.ContextGroupName,
            ContextTagName = node.ContextTagName,
            ContextPointCode = node.ContextPointCode,
            ContextDataType = node.ContextDataType,
            CycleStartValue = node.CycleStartValue,
            CycleEndValue = node.CycleEndValue,
            CycleMinSeconds = node.CycleMinSeconds,
            CycleMaxSeconds = node.CycleMaxSeconds,
            TaktTargetSeconds = node.TaktTargetSeconds,
            TaktTolerancePercent = node.TaktTolerancePercent,
            AnomalyMode = node.AnomalyMode,
            AnomalyThreshold = node.AnomalyThreshold,
            AnomalyBaselineWindowSeconds = node.AnomalyBaselineWindowSeconds,
            AnomalyBaselineSampleCount = node.AnomalyBaselineSampleCount,
            ModelPurpose = node.ModelPurpose,
            ModelPath = node.ModelPath,
            ModelInputTags = node.ModelInputTags,
            ModelInputName = node.ModelInputName,
            ModelInputNames = node.ModelInputNames,
            ModelOutputName = node.ModelOutputName,
            ModelOutputIndex = Math.Max(0, node.ModelOutputIndex),
            ModelOperator = node.ModelOperator,
            ModelThreshold = node.ModelThreshold,
            ModelTimeoutMilliseconds = NormalizeModelTimeout(node.ModelTimeoutMilliseconds),
            AlarmSeverity = node.AlarmSeverity,
            AlarmSuppressSeconds = node.AlarmSuppressSeconds,
            AlarmReTriggerSeconds = node.AlarmReTriggerSeconds,
            AlarmEscalateAfterSeconds = node.AlarmEscalateAfterSeconds,
            ActionDelaySeconds = node.ActionDelaySeconds,
            ActionCooldownSeconds = node.ActionCooldownSeconds,
            ActionMaxPerMinute = node.ActionMaxPerMinute,
            DebugEnabled = node.DebugEnabled,
            DebugLabel = node.DebugLabel,
            TransformMultiplier = node.TransformMultiplier,
            TransformOffset = node.TransformOffset,
            TransformUseAbsolute = node.TransformUseAbsolute,
            TransformExpression = node.TransformExpression,
            TransformTimeoutMilliseconds = NormalizeTransformTimeout(node.TransformTimeoutMilliseconds),
            SequenceWindowSeconds = node.SequenceWindowSeconds,
            SequenceStepTimeoutSeconds = node.SequenceStepTimeoutSeconds,
            SequenceMinIntervalSeconds = node.SequenceMinIntervalSeconds,
            SequenceResetOnMismatch = node.SequenceResetOnMismatch,
            ClearDurationSeconds = node.ClearDurationSeconds,
            ExecuteOnActive = node.ExecuteOnActive,
            ExecuteOnClear = node.ExecuteOnClear,
            EmailSmtpHost = node.EmailSmtpHost,
            EmailSmtpPort = node.EmailSmtpPort,
            EmailEnableSsl = node.EmailEnableSsl,
            EmailUsername = node.EmailUsername,
            EmailPassword = node.EmailPassword,
            EmailFrom = node.EmailFrom,
            EmailTo = node.EmailTo,
            EmailCc = node.EmailCc,
            EmailSubjectTemplate = node.EmailSubjectTemplate,
            EmailBodyTemplate = node.EmailBodyTemplate,
            WebhookUrl = node.WebhookUrl,
            WebhookMethod = node.WebhookMethod,
            WebhookHeaders = node.WebhookHeaders,
            WebhookBodyTemplate = node.WebhookBodyTemplate,
            WebhookContentType = node.WebhookContentType,
            WebhookTimeoutSeconds = node.WebhookTimeoutSeconds,
            WebhookRetryCount = node.WebhookRetryCount
        };
    }

    public static FlowRuleAlarmLevelDto ToDto(FlowRuleAlarmLevel level)
    {
        if (level == null)
            return new FlowRuleAlarmLevelDto();

        return new FlowRuleAlarmLevelDto
        {
            Id = level.Id,
            Name = level.Name,
            Severity = level.Severity,
            Operator = level.Operator,
            CompareValue = level.CompareValue,
            Message = level.Message
        };
    }

    public static FlowRuleEdgeDto ToDto(FlowRuleEdge edge)
    {
        if (edge == null)
            return new FlowRuleEdgeDto();

        return new FlowRuleEdgeDto
        {
            Id = edge.Id,
            SourceNodeId = edge.SourceNodeId,
            TargetNodeId = edge.TargetNodeId,
            SourcePort = edge.SourcePort,
            TargetPort = edge.TargetPort
        };
    }

    public static MqttConfigurationDto ToDto(MqttGatewayOptions options)
    {
        return new MqttConfigurationDto
        {
            Enabled = options.Enabled,
            GatewayId = options.GatewayId,
            GatewayName = options.GatewayName,
            SiteName = options.SiteName,
            CloudProtocolVersion = options.CloudProtocolVersion,
            ConfigVersion = options.ConfigVersion,
            PublishMode = options.PublishMode,
            Host = options.Host,
            Port = options.Port,
            ClientId = options.ClientId,
            Username = options.Username,
            Password = options.Password,
            UseTls = options.UseTls,
            AllowUntrustedCertificates = options.AllowUntrustedCertificates,
            ClientCertificatePath = options.ClientCertificatePath,
            ClientCertificatePassword = options.ClientCertificatePassword,
            ClientCertificateThumbprint = options.ClientCertificateThumbprint,
            ServerCertificateThumbprint = options.ServerCertificateThumbprint,
            CaCertificatePath = options.CaCertificatePath,
            SubscribeTopic = options.SubscribeTopic,
            PublishEnabled = options.PublishEnabled,
            PublishSelectedTagsOnly = options.PublishSelectedTagsOnly,
            PublishChangedOnly = options.PublishChangedOnly,
            PublishUnchangedHeartbeatSeconds = options.PublishUnchangedHeartbeatSeconds,
            PublishTopicTemplate = options.PublishTopicTemplate,
            PublishQos = options.PublishQos,
            HeartbeatEnabled = options.HeartbeatEnabled,
            HeartbeatIntervalSeconds = options.HeartbeatIntervalSeconds,
            HeartbeatTopic = options.HeartbeatTopic,
            HeartbeatQos = options.HeartbeatQos,
            StatusTopic = options.StatusTopic,
            CommandReplyTopicTemplate = options.CommandReplyTopicTemplate,
            OutboxDirectory = options.OutboxDirectory,
            PublishAckTimeoutMilliseconds = options.PublishAckTimeoutMilliseconds,
            OutboxMaxMessages = options.OutboxMaxMessages,
            OutboxMaxMegabytes = options.OutboxMaxMegabytes,
            OutboxRetentionHours = options.OutboxRetentionHours,
            OutboxQuarantineRetentionHours = options.OutboxQuarantineRetentionHours,
            PublishFlushBatchSize = options.PublishFlushBatchSize,
            PublishRetryMinSeconds = options.PublishRetryMinSeconds,
            PublishRetryMaxSeconds = options.PublishRetryMaxSeconds,
            ReconnectSeconds = options.ReconnectSeconds,
            KeepAliveSeconds = options.KeepAliveSeconds,
            SparkplugNamespace = options.SparkplugNamespace,
            SparkplugGroupId = options.SparkplugGroupId,
            SparkplugEdgeNodeId = options.SparkplugEdgeNodeId,
            SparkplugDeviceIdSource = options.SparkplugDeviceIdSource,
            SparkplugMetricNameTemplate = options.SparkplugMetricNameTemplate,
            SparkplugPublishNodeBirth = options.SparkplugPublishNodeBirth,
            SparkplugPublishDeviceBirth = options.SparkplugPublishDeviceBirth,
            SparkplugPublishDeviceDeath = options.SparkplugPublishDeviceDeath,
            SparkplugIncludeProperties = options.SparkplugIncludeProperties,
            SparkplugUseAliases = options.SparkplugUseAliases,
            SparkplugDeathQos = options.SparkplugDeathQos,
            SparkplugBirthQos = options.SparkplugBirthQos,
            SparkplugEnableCommands = options.SparkplugEnableCommands,
            SparkplugPrimaryHostId = options.SparkplugPrimaryHostId
        };
    }

    public static OpcUaServerConfigurationDto ToDto(OpcUaServerOptions options)
    {
        OpcUaServerOptions normalized = OpcUaServerOptions.Normalize(options);
        return new OpcUaServerConfigurationDto
        {
            Enabled = normalized.Enabled,
            ApplicationName = normalized.ApplicationName,
            ApplicationUri = normalized.ApplicationUri,
            ProductUri = normalized.ProductUri,
            Host = normalized.Host,
            Port = normalized.Port,
            EndpointPath = normalized.EndpointPath,
            EndpointUrl = normalized.EndpointUrl,
            NamespaceUri = normalized.NamespaceUri,
            CertificateStorePath = normalized.CertificateStorePath,
            AutoAcceptUntrustedCertificates = normalized.AutoAcceptUntrustedCertificates,
            AllowAnonymous = normalized.AllowAnonymous,
            UsernamePasswordEnabled = normalized.UsernamePasswordEnabled,
            Username = normalized.Username,
            Password = string.Empty,
            PasswordConfigured = OpcUaPasswordHasher.IsPasswordConfigured(normalized),
            SecurityPolicy = normalized.SecurityPolicy,
            AllowSecurityPolicyNone = normalized.AllowSecurityPolicyNone,
            EnableBasic256SignAndEncrypt = normalized.EnableBasic256SignAndEncrypt,
            EnableBasic256Sha256SignAndEncrypt = normalized.EnableBasic256Sha256SignAndEncrypt,
            MinimumSamplingIntervalMs = normalized.MinimumSamplingIntervalMs,
            PublishDiagnostics = normalized.PublishDiagnostics
        };
    }

    public static HistoryConfigurationDto ToDto(LocalHistoryOptions options)
    {
        return new HistoryConfigurationDto
        {
            Enabled = options.Enabled,
            Directory = options.Directory,
            RetentionDays = options.RetentionDays,
            MaxViewRecords = options.MaxViewRecords,
            DataProcessing = ToDto(options.DataProcessing),
            Storage = ToDto(options.Storage, options.RetentionDays)
        };
    }

    public static HistoryDataProcessingConfigurationDto ToDto(EdgeDataProcessingOptions options)
    {
        EdgeDataProcessingOptions normalized = EdgeDataProcessingOptions.Normalize(options);
        return new HistoryDataProcessingConfigurationDto
        {
            Enabled = normalized.Enabled,
            CompressionEnabled = normalized.CompressionEnabled,
            CompressionTolerance = normalized.CompressionTolerance,
            CompressDuplicateText = normalized.CompressDuplicateText,
            DownsamplingEnabled = normalized.DownsamplingEnabled,
            DownsamplingIntervalMs = normalized.DownsamplingIntervalMs,
            AlignmentEnabled = normalized.AlignmentEnabled,
            AlignmentIntervalMs = normalized.AlignmentIntervalMs,
            FillEnabled = normalized.FillEnabled,
            FillIntervalMs = normalized.FillIntervalMs,
            FillMaxGapSeconds = normalized.FillMaxGapSeconds,
            FillMode = normalized.FillMode,
            AggregationEnabled = normalized.AggregationEnabled,
            AggregationIntervalSeconds = normalized.AggregationIntervalSeconds,
            AggregationMethods = normalized.AggregationMethods,
            MaxSyntheticPointsPerInput = normalized.MaxSyntheticPointsPerInput
        };
    }

    public static HistoryStorageConfigurationDto ToDto(LocalHistoryStorageOptions options, int retentionDays)
    {
        LocalHistoryStorageOptions normalized = LocalHistoryStorageOptions.Normalize(options, LocalHistoryOptions.ClampRetentionDays(retentionDays));
        return new HistoryStorageConfigurationDto
        {
            TieringEnabled = normalized.TieringEnabled,
            ColdDirectory = normalized.ColdDirectory,
            RetentionPolicy = normalized.RetentionPolicy,
            HotRetentionDays = normalized.HotRetentionDays,
            ColdRetentionDays = normalized.ColdRetentionDays,
            CompressionEnabled = normalized.CompressionEnabled,
            CompressHotFiles = normalized.CompressHotFiles,
            CompressColdFiles = normalized.CompressColdFiles,
            CompressAfterDays = normalized.CompressAfterDays,
            AutoCleanupEnabled = normalized.AutoCleanupEnabled,
            CleanupIntervalHours = normalized.CleanupIntervalHours,
            MaxStorageMegabytes = normalized.MaxStorageMegabytes
        };
    }

    public static StorageHealthConfigurationDto ToDto(StorageHealthThresholds thresholds)
    {
        StorageHealthThresholds normalized = StorageHealthEvaluator.NormalizeThresholds(thresholds);
        return new StorageHealthConfigurationDto
        {
            DegradedAvailableMegabytes = Math.Round(normalized.DegradedAvailableBytes / 1024D / 1024D, 2),
            UnhealthyAvailableMegabytes = Math.Round(normalized.UnhealthyAvailableBytes / 1024D / 1024D, 2),
            DegradedAvailablePercent = normalized.DegradedAvailablePercent,
            UnhealthyAvailablePercent = normalized.UnhealthyAvailablePercent
        };
    }

    public static GatewayConfigurationVersionDto ToDto(GatewayConfigurationVersionInfo version)
    {
        return new GatewayConfigurationVersionDto
        {
            Id = version.Id,
            ConfigType = version.ConfigType,
            Version = version.Version,
            Active = version.Active,
            CreatedTime = version.CreatedTime,
            Source = version.Source,
            Description = version.Description
        };
    }

    public static ProjectValidationResultDto ToDto(ProjectConfigValidationResult validation)
    {
        return new ProjectValidationResultDto
        {
            IsValid = validation.IsValid,
            Errors = validation.Errors.ToList(),
            Warnings = validation.Warnings.ToList()
        };
    }

    public static DeviceRuntimeStatusDto ToDto(DeviceRuntimeStatus status)
    {
        return new DeviceRuntimeStatusDto
        {
            ChannelId = status.ChannelId,
            ChannelName = status.ChannelName,
            DeviceId = status.DeviceId,
            DeviceName = status.DeviceName,
            Protocol = status.Protocol,
            Enabled = status.Enabled,
            IsConnected = status.IsConnected,
            IsPolling = status.IsPolling,
            IsQueued = status.IsQueued,
            Status = status.Status,
            ConsecutiveFailures = status.ConsecutiveFailures,
            TotalReads = status.TotalReads,
            SuccessfulReads = status.SuccessfulReads,
            FailedReads = status.FailedReads,
            SuccessRate = status.SuccessRate,
            LastPollTime = status.LastPollTime,
            LastSuccessTime = status.LastSuccessTime,
            LastFailureTime = status.LastFailureTime,
            NextReconnectTime = status.NextReconnectTime,
            LastReconnectDelayMs = status.LastReconnectDelayMs,
            NextPollTime = status.NextPollTime,
            CurrentTaskId = status.CurrentTaskId,
            LastTaskStatus = status.LastTaskStatus,
            LastTaskDurationMs = status.LastTaskDurationMs,
            SlowPollCount = status.SlowPollCount,
            TimeoutCount = status.TimeoutCount,
            LastError = status.LastError,
            ProtocolCircuitBreaker = ToDto(status.ProtocolCircuitBreaker),
            DeviceState = status.DeviceState,
            TransportConnected = status.TransportConnected,
            IsIsolated = status.IsIsolated,
            RecoveryState = status.RecoveryState,
            IsolatedSinceTime = status.IsolatedSinceTime,
            NextRecoveryProbeTime = status.NextRecoveryProbeTime,
            ChannelKey = status.ChannelKey,
            ChannelStatus = status.ChannelStatus,
            ChannelConsecutiveFailures = status.ChannelConsecutiveFailures,
            ChannelLastSuccessTime = status.ChannelLastSuccessTime,
            ChannelLastFailureTime = status.ChannelLastFailureTime,
            ChannelLastError = status.ChannelLastError
        };
    }

    public static RuntimeSchedulerStatusDto ToDto(RuntimeSchedulerStatus status)
    {
        if (status == null)
            return new RuntimeSchedulerStatusDto();

        return new RuntimeSchedulerStatusDto
        {
            IsolationStrategy = status.IsolationStrategy,
            HealthStatus = status.HealthStatus,
            HealthMessage = status.HealthMessage,
            MaxConcurrentDevicePolls = status.MaxConcurrentDevicePolls,
            SchedulerIntervalMs = status.SchedulerIntervalMs,
            BackpressureEnabled = status.BackpressureEnabled,
            BackpressureActive = status.BackpressureActive,
            QueueHighWatermark = status.QueueHighWatermark,
            QueueLowWatermark = status.QueueLowWatermark,
            BackpressureDelayMs = status.BackpressureDelayMs,
            MaxDevicePollsQueuedPerSchedulerTick = status.MaxDevicePollsQueuedPerSchedulerTick,
            SlowPollThresholdMs = status.SlowPollThresholdMs,
            PollTimeoutMs = status.PollTimeoutMs,
            TotalQueued = status.TotalQueued,
            TotalStarted = status.TotalStarted,
            TotalCompleted = status.TotalCompleted,
            TotalFailed = status.TotalFailed,
            TotalSlow = status.TotalSlow,
            TotalBackpressureThrottled = status.TotalBackpressureThrottled,
            TotalRateLimited = status.TotalRateLimited,
            Queue = ToDto(status.Queue),
            Timeout = ToDto(status.Timeout),
            Tasks = status.Tasks.Select(ToDto).ToList()
        };
    }

    public static RuntimePollingQueueStatusDto ToDto(RuntimePollingQueueStatus status)
    {
        if (status == null)
            return new RuntimePollingQueueStatusDto();

        return new RuntimePollingQueueStatusDto
        {
            PendingCount = status.PendingCount,
            RecoveryPendingCount = status.RecoveryPendingCount,
            RunningCount = status.RunningCount,
            QueueLimit = status.QueueLimit,
            HighWatermark = status.HighWatermark,
            LowWatermark = status.LowWatermark,
            UtilizationPercent = status.UtilizationPercent,
            BackpressureActive = status.BackpressureActive,
            AvailableWorkers = status.AvailableWorkers,
            RejectedCount = status.RejectedCount,
            BackpressureThrottledCount = status.BackpressureThrottledCount,
            RateLimitedCount = status.RateLimitedCount,
            MaxObservedPendingCount = status.MaxObservedPendingCount,
            LastBackpressureTime = status.LastBackpressureTime,
            LastBackpressureMessage = status.LastBackpressureMessage
        };
    }

    public static RuntimeTimeoutStatsDto ToDto(RuntimeTimeoutStats status)
    {
        if (status == null)
            return new RuntimeTimeoutStatsDto();

        return new RuntimeTimeoutStatsDto
        {
            PollTimeoutCount = status.PollTimeoutCount,
            ReadTimeoutCount = status.ReadTimeoutCount,
            RecentPollTimeoutCount = status.RecentPollTimeoutCount,
            RecentReadTimeoutCount = status.RecentReadTimeoutCount,
            TimeoutWindowSeconds = status.TimeoutWindowSeconds,
            LastTimeoutTime = status.LastTimeoutTime,
            LastTimeoutDeviceName = status.LastTimeoutDeviceName,
            LastTimeoutMessage = status.LastTimeoutMessage
        };
    }

    public static RuntimePollingTaskStatusDto ToDto(RuntimePollingTaskStatus status)
    {
        if (status == null)
            return new RuntimePollingTaskStatusDto();

        return new RuntimePollingTaskStatusDto
        {
            DeviceId = status.DeviceId,
            DeviceName = status.DeviceName,
            TaskId = status.TaskId,
            Status = status.Status,
            IsQueued = status.IsQueued,
            IsRunning = status.IsRunning,
            QueuedTime = status.QueuedTime,
            StartedTime = status.StartedTime,
            FinishedTime = status.FinishedTime,
            LastDurationMs = status.LastDurationMs,
            SlowPollCount = status.SlowPollCount,
            TimeoutCount = status.TimeoutCount,
            LastError = status.LastError
        };
    }

    public static SystemResourceStatusDto ToDto(SystemResourceStatus status)
    {
        if (status == null)
            return new SystemResourceStatusDto();

        return new SystemResourceStatusDto
        {
            CpuUsagePercent = status.CpuUsagePercent,
            MemoryUsagePercent = status.MemoryUsagePercent,
            TotalMemoryBytes = status.TotalMemoryBytes,
            AvailableMemoryBytes = status.AvailableMemoryBytes,
            UsedMemoryBytes = status.UsedMemoryBytes,
            ProcessWorkingSetBytes = status.ProcessWorkingSetBytes,
            ThreadPoolAvailableWorkerThreads = status.ThreadPoolAvailableWorkerThreads,
            ThreadPoolMaxWorkerThreads = status.ThreadPoolMaxWorkerThreads,
            ThreadPoolAvailableCompletionPortThreads = status.ThreadPoolAvailableCompletionPortThreads,
            ThreadPoolMaxCompletionPortThreads = status.ThreadPoolMaxCompletionPortThreads,
            ThreadPoolWorkerUtilizationPercent = status.ThreadPoolWorkerUtilizationPercent,
            SampleTime = status.SampleTime,
            Source = status.Source
        };
    }

    public static RuntimeErrorDto ToDto(RuntimeErrorDetail error)
    {
        return new RuntimeErrorDto
        {
            Category = error.Category,
            ChannelId = error.ChannelId,
            ChannelName = error.ChannelName,
            DeviceId = error.DeviceId,
            DeviceName = error.DeviceName,
            GroupId = error.GroupId,
            GroupName = error.GroupName,
            TagId = error.TagId,
            TagName = error.TagName,
            Message = error.Message,
            Suggestion = error.Suggestion,
            Source = error.Source,
            Timestamp = error.Timestamp
        };
    }

    public static TagValueSnapshotDto ToDto(TagValueSnapshot snapshot)
    {
        if (snapshot == null)
            return new TagValueSnapshotDto();

        return new TagValueSnapshotDto
        {
            ChannelId = snapshot.ChannelId ?? string.Empty,
            ChannelName = snapshot.ChannelName ?? string.Empty,
            DeviceId = snapshot.DeviceId ?? string.Empty,
            DeviceProtocol = snapshot.DeviceProtocol ?? string.Empty,
            GroupId = snapshot.GroupId ?? string.Empty,
            TagId = snapshot.TagId ?? string.Empty,
            DeviceName = snapshot.DeviceName ?? string.Empty,
            GroupName = snapshot.GroupName ?? string.Empty,
            TagName = snapshot.TagName ?? string.Empty,
            RawValueText = snapshot.RawValueText ?? string.Empty,
            ValueText = snapshot.ValueText ?? string.Empty,
            Unit = snapshot.Unit ?? string.Empty,
            PointCode = snapshot.PointCode ?? string.Empty,
            AssetPath = snapshot.AssetPath ?? string.Empty,
            BusinessType = snapshot.BusinessType ?? string.Empty,
            Source = snapshot.Source ?? string.Empty,
            Precision = snapshot.Precision,
            DataType = snapshot.DataType ?? string.Empty,
            MqttPublishEnabled = snapshot.MqttPublishEnabled,
            CleaningApplied = snapshot.CleaningApplied,
            CleaningAction = snapshot.CleaningAction ?? string.Empty,
            CleaningMessage = snapshot.CleaningMessage ?? string.Empty,
            Quality = snapshot.Quality.ToString(),
            Timestamp = snapshot.Timestamp,
            ErrorMessage = snapshot.ErrorMessage ?? string.Empty,
            TagState = snapshot.TagState ?? string.Empty,
            IsTagIsolated = snapshot.IsTagIsolated,
            IsStaticValidationError = snapshot.IsStaticValidationError,
            TagConsecutiveFailures = snapshot.TagConsecutiveFailures,
            NextTagRecoveryProbeTime = snapshot.NextTagRecoveryProbeTime
        };
    }

    public static MqttRuntimeStatusDto ToDto(MqttGatewayStatus status)
    {
        return new MqttRuntimeStatusDto
        {
            Enabled = status.Enabled,
            GatewayId = status.GatewayId,
            GatewayName = status.GatewayName,
            SiteName = status.SiteName,
            CloudProtocolVersion = status.CloudProtocolVersion,
            ConfigVersion = status.ConfigVersion,
            PublishMode = status.PublishMode,
            IsRunning = status.IsRunning,
            IsConnected = status.IsConnected,
            Broker = status.Broker,
            SubscribeTopic = status.SubscribeTopic,
            PublishEnabled = status.PublishEnabled,
            PublishTopicTemplate = status.PublishTopicTemplate,
            PublishQos = status.PublishQos,
            HeartbeatTopic = status.HeartbeatTopic,
            StatusTopic = status.StatusTopic,
            CommandReplyTopicTemplate = status.CommandReplyTopicTemplate,
            SparkplugEnabled = status.SparkplugEnabled,
            SparkplugNamespace = status.SparkplugNamespace,
            SparkplugGroupId = status.SparkplugGroupId,
            SparkplugEdgeNodeId = status.SparkplugEdgeNodeId,
            SparkplugNodeBirthTopic = status.SparkplugNodeBirthTopic,
            SparkplugNodeDeathTopic = status.SparkplugNodeDeathTopic,
            OutboxDirectory = status.OutboxDirectory,
            OutboxQuarantineDirectory = status.OutboxQuarantineDirectory,
            LastError = status.LastError,
            LastMessage = status.LastMessage,
            LastWriteResult = status.LastWriteResult,
            LastPublishResult = status.LastPublishResult,
            LastConnectedTime = status.LastConnectedTime,
            LastMessageTime = status.LastMessageTime,
            LastPublishTime = status.LastPublishTime,
            LastPublishFailureTime = status.LastPublishFailureTime,
            LastSparkplugBirthTime = status.LastSparkplugBirthTime,
            LastSparkplugDeathTime = status.LastSparkplugDeathTime,
            NextPublishRetryTime = status.NextPublishRetryTime,
            ReconnectCount = status.ReconnectCount,
            ReceivedCount = status.ReceivedCount,
            SuccessfulWrites = status.SuccessfulWrites,
            FailedWrites = status.FailedWrites,
            PublishedCount = status.PublishedCount,
            FailedPublishes = status.FailedPublishes,
            SparkplugBirthCount = status.SparkplugBirthCount,
            SparkplugDeathCount = status.SparkplugDeathCount,
            SparkplugDataCount = status.SparkplugDataCount,
            OutboxPendingCount = status.OutboxPendingCount,
            OutboxEnqueuedCount = status.OutboxEnqueuedCount,
            OutboxBytes = status.OutboxBytes,
            OutboxExpiredDeletedCount = status.OutboxExpiredDeletedCount,
            OutboxOverflowDeletedCount = status.OutboxOverflowDeletedCount,
            OutboxInvalidMessageCount = status.OutboxInvalidMessageCount,
            OutboxQuarantinedMessageCount = status.OutboxQuarantinedMessageCount,
            OutboxQuarantineCount = status.OutboxQuarantineCount,
            OutboxQuarantineBytes = status.OutboxQuarantineBytes,
            OutboxQuarantineExpiredDeletedCount = status.OutboxQuarantineExpiredDeletedCount,
            OutboxOldestPendingTime = status.OutboxOldestPendingTime,
            OutboxNewestPendingTime = status.OutboxNewestPendingTime,
            OutboxOldestQuarantineTime = status.OutboxOldestQuarantineTime,
            OutboxNewestQuarantineTime = status.OutboxNewestQuarantineTime,
            OutboxOldestPendingAgeSeconds = status.OutboxOldestPendingAgeSeconds,
            PublishRetryBackoffSeconds = status.PublishRetryBackoffSeconds,
            PublishConsecutiveFailureCount = status.PublishConsecutiveFailureCount,
            CircuitBreaker = ToDto(status.CircuitBreaker)
        };
    }

    public static HistoryStatsDto ToDto(LocalHistoryStats stats)
    {
        return new HistoryStatsDto
        {
            Enabled = stats.Enabled,
            IsRunning = stats.IsRunning,
            Directory = stats.Directory,
            RetentionDays = stats.RetentionDays,
            ValueFiles = stats.ValueFiles,
            AlarmFiles = stats.AlarmFiles,
            PublishFiles = stats.PublishFiles,
            TotalBytes = stats.TotalBytes,
            ColdDirectory = stats.ColdDirectory,
            TieringEnabled = stats.TieringEnabled,
            RetentionPolicy = stats.RetentionPolicy,
            HotRetentionDays = stats.HotRetentionDays,
            ColdRetentionDays = stats.ColdRetentionDays,
            StorageCompressionEnabled = stats.StorageCompressionEnabled,
            AutoCleanupEnabled = stats.AutoCleanupEnabled,
            CleanupIntervalHours = stats.CleanupIntervalHours,
            LastCleanupTime = stats.LastCleanupTime,
            NextCleanupTime = stats.NextCleanupTime,
            HotFileCount = stats.HotFileCount,
            ColdFileCount = stats.ColdFileCount,
            CompressedFileCount = stats.CompressedFileCount,
            HotBytes = stats.HotBytes,
            ColdBytes = stats.ColdBytes,
            CompressedBytes = stats.CompressedBytes,
            DataProcessingEnabled = stats.DataProcessingEnabled,
            CompressionEnabled = stats.CompressionEnabled,
            DownsamplingEnabled = stats.DownsamplingEnabled,
            AlignmentEnabled = stats.AlignmentEnabled,
            FillEnabled = stats.FillEnabled,
            AggregationEnabled = stats.AggregationEnabled,
            ReceivedValueCount = stats.ReceivedValueCount,
            WrittenValueCount = stats.WrittenValueCount,
            SkippedValueCount = stats.SkippedValueCount,
            CompressedValueCount = stats.CompressedValueCount,
            DownsampledValueCount = stats.DownsampledValueCount,
            FilledValueCount = stats.FilledValueCount,
            AggregatedValueCount = stats.AggregatedValueCount,
            IsDegraded = stats.IsDegraded,
            LastErrorTime = stats.LastErrorTime,
            LastError = stats.LastError,
            CircuitBreaker = ToDto(stats.CircuitBreaker)
        };
    }

    public static OpcUaServerRuntimeStatusDto ToDto(OpcUaServerStatus status)
    {
        if (status == null)
            return new OpcUaServerRuntimeStatusDto();

        return new OpcUaServerRuntimeStatusDto
        {
            Enabled = status.Enabled,
            IsRunning = status.IsRunning,
            ApplicationName = status.ApplicationName,
            EndpointUrl = status.EndpointUrl,
            NamespaceUri = status.NamespaceUri,
            ChannelNodeCount = status.ChannelNodeCount,
            DeviceNodeCount = status.DeviceNodeCount,
            GroupNodeCount = status.GroupNodeCount,
            TagNodeCount = status.TagNodeCount,
            ValueUpdateCount = status.ValueUpdateCount,
            StartedTime = status.StartedTime,
            LastReloadTime = status.LastReloadTime,
            LastValueUpdateTime = status.LastValueUpdateTime,
            LastError = status.LastError,
            LastMessage = status.LastMessage
        };
    }

    public static RuleEngineRuntimeStatusDto ToDto(EdgeRuleEngineStatus status)
    {
        if (status == null)
            return new RuleEngineRuntimeStatusDto();

        return new RuleEngineRuntimeStatusDto
        {
            IsRunning = status.IsRunning,
            Enabled = status.Enabled,
            RuleCount = status.RuleCount,
            EnabledRuleCount = status.EnabledRuleCount,
            ActiveRuleCount = status.ActiveRuleCount,
            CachedSnapshotCount = status.CachedSnapshotCount,
            RecentEventCount = status.RecentEventCount,
            EvaluationCount = status.EvaluationCount,
            TriggeredCount = status.TriggeredCount,
            ClearedCount = status.ClearedCount,
            FailedEvaluationCount = status.FailedEvaluationCount,
            ActionFailureCount = status.ActionFailureCount,
            PendingActionCount = status.PendingActionCount,
            DroppedActionCount = status.DroppedActionCount,
            PendingInputEventCount = status.PendingInputEventCount,
            MaxObservedPendingInputEventCount = status.MaxObservedPendingInputEventCount,
            DroppedInputEventCount = status.DroppedInputEventCount,
            LastEvaluationTime = status.LastEvaluationTime,
            LastEventTime = status.LastEventTime,
            LastErrorTime = status.LastErrorTime,
            LastError = status.LastError,
            CircuitBreaker = ToDto(status.CircuitBreaker),
            RecentEvents = status.RecentEvents.Select(ToDto).ToList(),
            Rules = status.Rules.Select(ToDto).ToList()
        };
    }

    public static CircuitBreakerStatusDto ToDto(CircuitBreakerStatus status)
    {
        if (status == null)
            return new CircuitBreakerStatusDto();

        return new CircuitBreakerStatusDto
        {
            Name = status.Name,
            Enabled = status.Enabled,
            State = status.State,
            IsOpen = status.IsOpen,
            IsHalfOpen = status.IsHalfOpen,
            ConsecutiveFailures = status.ConsecutiveFailures,
            ConsecutiveSuccesses = status.ConsecutiveSuccesses,
            TotalFailures = status.TotalFailures,
            TotalSuccesses = status.TotalSuccesses,
            TotalTrips = status.TotalTrips,
            TotalRejected = status.TotalRejected,
            OpenedTime = status.OpenedTime,
            NextRetryTime = status.NextRetryTime,
            LastFailureTime = status.LastFailureTime,
            LastFailureMessage = status.LastFailureMessage,
            DegradedMode = status.DegradedMode
        };
    }

    public static RuleEngineRuleRuntimeStatusDto ToDto(EdgeRuleRuntimeRuleStatus status)
    {
        if (status == null)
            return new RuleEngineRuleRuntimeStatusDto();

        return new RuleEngineRuleRuntimeStatusDto
        {
            RuleId = status.RuleId,
            RuleName = status.RuleName,
            ConditionType = status.ConditionType,
            IsActive = status.IsActive,
            ActiveState = status.ActiveState,
            LastEvaluationTime = status.LastEvaluationTime,
            LastTriggeredTime = status.LastTriggeredTime,
            LastClearedTime = status.LastClearedTime,
            LastErrorTime = status.LastErrorTime,
            LastError = status.LastError,
            EvaluationCount = status.EvaluationCount,
            TriggeredCount = status.TriggeredCount,
            ClearedCount = status.ClearedCount,
            FailedEvaluationCount = status.FailedEvaluationCount,
            ActionFailureCount = status.ActionFailureCount,
            RecentEvents = status.RecentEvents.Select(ToDto).ToList()
        };
    }

    public static RuleEngineRuntimeEventDto ToDto(EdgeRuleRuntimeEvent ruleEvent)
    {
        if (ruleEvent == null)
            return new RuleEngineRuntimeEventDto();

        TagValueSnapshot snapshot = ruleEvent.Snapshot;
        return new RuleEngineRuntimeEventDto
        {
            EventId = ruleEvent.EventId,
            RuleId = ruleEvent.RuleId,
            RuleName = ruleEvent.RuleName,
            ConditionType = ruleEvent.ConditionType.ToString(),
            EventType = ruleEvent.EventType,
            State = ruleEvent.State,
            Message = ruleEvent.Message,
            Severity = ruleEvent.Severity,
            Topic = ruleEvent.Topic,
            PointCode = BuildPointCode(snapshot),
            ChannelId = snapshot == null ? string.Empty : snapshot.ChannelId,
            ChannelName = snapshot == null ? string.Empty : snapshot.ChannelName,
            DeviceId = snapshot == null ? string.Empty : snapshot.DeviceId,
            DeviceName = snapshot == null ? string.Empty : snapshot.DeviceName,
            GroupId = snapshot == null ? string.Empty : snapshot.GroupId,
            GroupName = snapshot == null ? string.Empty : snapshot.GroupName,
            TagId = snapshot == null ? string.Empty : snapshot.TagId,
            TagName = snapshot == null ? string.Empty : snapshot.TagName,
            Value = ruleEvent.Value,
            Threshold = ruleEvent.Threshold,
            Timestamp = ruleEvent.Timestamp
        };
    }

    public static WriteTagResultDto ToDto(WriteTagResponse response)
    {
        if (response == null)
            return new WriteTagResultDto();

        return new WriteTagResultDto
        {
            Success = response.Success,
            ChannelId = response.ChannelId,
            ChannelName = response.ChannelName,
            DeviceId = response.DeviceId,
            GroupId = response.GroupId,
            TagId = response.TagId,
            DeviceName = response.DeviceName,
            GroupName = response.GroupName,
            TagName = response.TagName,
            DataType = response.DataType,
            Quality = response.Quality,
            Timestamp = response.Timestamp,
            ErrorMessage = response.ErrorMessage,
            CurrentValueText = response.CurrentValue == null ? string.Empty : response.CurrentValue.ValueText
        };
    }

    public static ProjectConfig ToConfig(ProjectConfigurationDto dto)
    {
        return new ProjectConfig
        {
            ProjectId = EmptyToNewId(dto.ProjectId),
            Name = dto.Name,
            Channels = dto.Channels.Select(ToConfig).ToList(),
            Devices = dto.Devices.Select(ToConfig).ToList(),
            Rules = dto.Rules.Select(ToConfig).ToList(),
            FlowRules = dto.FlowRules.Select(ToConfig).ToList()
        };
    }

    public static ChannelConfig ToConfig(ChannelConfigurationDto dto)
    {
        return new ChannelConfig
        {
            Id = EmptyToNewId(dto.Id),
            Name = dto.Name,
            Enabled = dto.Enabled,
            Protocol = ParseEnum(dto.Protocol, PlcProtocol.ModbusTcp),
            DriverId = dto.DriverId,
            MaxConcurrentDevicePolls = dto.MaxConcurrentDevicePolls,
            SchedulingWeight = dto.SchedulingWeight
        };
    }

    public static DeviceConfig ToConfig(DeviceConfigurationDto dto)
    {
        return new DeviceConfig
        {
            Id = EmptyToNewId(dto.Id),
            ChannelId = dto.ChannelId,
            Name = dto.Name,
            Enabled = dto.Enabled,
            Protocol = ParseEnum(dto.Protocol, PlcProtocol.ModbusTcp),
            Connection = ToConfig(dto.Connection),
            DefaultScanRateMs = dto.DefaultScanRateMs,
            FailureRetryDelayMs = dto.FailureRetryDelayMs,
            MaxFailureRetryDelayMs = dto.MaxFailureRetryDelayMs,
            Tags = dto.Tags.Select(ToConfig).ToList(),
            Groups = dto.Groups.Select(ToConfig).ToList()
        };
    }

    public static GroupConfig ToConfig(GroupConfigurationDto dto)
    {
        return new GroupConfig
        {
            Id = EmptyToNewId(dto.Id),
            DeviceId = dto.DeviceId,
            Name = dto.Name,
            Enabled = dto.Enabled,
            ScanRateMs = dto.ScanRateMs,
            Tags = dto.Tags.Select(ToConfig).ToList()
        };
    }

    public static TagConfig ToConfig(TagConfigurationDto dto)
    {
        return new TagConfig
        {
            Id = EmptyToNewId(dto.Id),
            DeviceId = dto.DeviceId,
            GroupId = dto.GroupId,
            Name = dto.Name,
            Address = dto.Address,
            MeterAddress = dto.MeterAddress,
            MeterDataIdentifier = dto.MeterDataIdentifier,
            MeterType = dto.MeterType,
            DataType = ParseEnum(dto.DataType, PlcDataType.Int16),
            ElementCount = dto.ElementCount,
            ElementOffset = dto.ElementOffset,
            Enabled = dto.Enabled,
            MqttPublishEnabled = dto.MqttPublishEnabled,
            AccessMode = ParseEnum(dto.AccessMode, TagAccessMode.ReadWrite),
            ScanRateMs = dto.ScanRateMs,
            FailureRetryDelayMs = dto.FailureRetryDelayMs,
            Unit = dto.Unit,
            PointCode = dto.PointCode,
            AssetPath = dto.AssetPath,
            BusinessType = dto.BusinessType,
            Source = dto.Source,
            Precision = dto.Precision,
            Scaling = ToConfig(dto.Scaling),
            Cleaning = ToConfig(dto.Cleaning),
            Alarm = ToConfig(dto.Alarm),
            Description = dto.Description
        };
    }

    public static PlcConnectionOptions ToConfig(PlcConnectionDto dto)
    {
        return new PlcConnectionOptions
        {
            Protocol = ParseEnum(dto.Protocol, PlcProtocol.ModbusTcp),
            Host = dto.Host,
            Port = dto.Port,
            Rack = dto.Rack,
            Slot = dto.Slot,
            TimeoutMilliseconds = dto.TimeoutMilliseconds,
            WordOrder = ParseEnum(dto.WordOrder, PlcWordOrder.HighWordFirst),
            Transport = ParseEnum(dto.Transport, NetworkTransport.Tcp),
            DataBits = dto.DataBits,
            SerialParity = ParseEnum(dto.SerialParity, Parity.None),
            SerialStopBits = ParseEnum(dto.SerialStopBits, StopBits.One),
            Username = dto.Username,
            Password = dto.Password,
            OpcUaSecurityPolicy = dto.OpcUaSecurityPolicy,
            OpcUaMessageSecurityMode = dto.OpcUaMessageSecurityMode,
            OpcUaAutoTrustServerCertificate = dto.OpcUaAutoTrustServerCertificate,
            OpcDaServerProgId = dto.OpcDaServerProgId,
            OpcDaGroupName = dto.OpcDaGroupName,
            DriverId = dto.DriverId,
            DriverOptionsJson = dto.DriverOptionsJson
        };
    }

    public static ScalingConfig ToConfig(ScalingConfigurationDto dto)
    {
        dto ??= new ScalingConfigurationDto();
        return new ScalingConfig
        {
            Enabled = dto.Enabled,
            Multiplier = dto.Multiplier,
            Offset = dto.Offset,
            ClampEnabled = dto.ClampEnabled,
            MinValue = dto.MinValue,
            MaxValue = dto.MaxValue,
            DecimalPlaces = dto.DecimalPlaces
        };
    }

    public static DataCleaningConfig ToConfig(DataCleaningConfigurationDto dto)
    {
        dto ??= new DataCleaningConfigurationDto();
        DataCleaningConfig config = new DataCleaningConfig
        {
            Enabled = dto.Enabled,
            OutOfRangeEnabled = dto.OutOfRangeEnabled,
            MinValue = dto.MinValue,
            MaxValue = dto.MaxValue,
            DeadbandEnabled = dto.DeadbandEnabled,
            Deadband = dto.Deadband,
            DuplicateFilterEnabled = dto.DuplicateFilterEnabled,
            SpikeFilterEnabled = dto.SpikeFilterEnabled,
            SpikeThreshold = dto.SpikeThreshold,
            SpikeWindowSeconds = dto.SpikeWindowSeconds,
            EnumMappingEnabled = dto.EnumMappingEnabled,
            UnitConversionEnabled = dto.UnitConversionEnabled,
            SourceUnit = dto.SourceUnit,
            TargetUnit = dto.TargetUnit,
            UnitMultiplier = dto.UnitMultiplier,
            UnitOffset = dto.UnitOffset,
            PreserveLastGoodOnFilter = dto.PreserveLastGoodOnFilter
        };

        if (dto.EnumMappings != null)
        {
            for (int i = 0; i < dto.EnumMappings.Count; i++)
            {
                DataCleaningEnumMappingDto item = dto.EnumMappings[i];
                if (item == null)
                    continue;
                config.EnumMappings.Add(new DataCleaningEnumMappingConfig
                {
                    RawValue = item.RawValue,
                    CleanValue = item.CleanValue,
                    Description = item.Description
                });
            }
        }

        return config;
    }

    public static TagAlarmConfig ToConfig(TagAlarmConfigurationDto dto)
    {
        dto ??= new TagAlarmConfigurationDto();
        return new TagAlarmConfig
        {
            Enabled = dto.Enabled,
            LowLimit = dto.LowLimit,
            HighLimit = dto.HighLimit,
            LowAlarmMessage = dto.LowAlarmMessage,
            HighAlarmMessage = dto.HighAlarmMessage,
            WarningDeviation = dto.WarningDeviation,
            LowWarningMessage = dto.LowWarningMessage,
            HighWarningMessage = dto.HighWarningMessage
        };
    }

    public static EdgeRuleConfig ToConfig(EdgeRuleConfigurationDto dto)
    {
        return new EdgeRuleConfig
        {
            Id = EmptyToNewId(dto.Id),
            Name = dto.Name,
            Enabled = dto.Enabled,
            ConditionType = ParseEnum(dto.ConditionType, EdgeRuleConditionType.Threshold),
            SourceChannelId = dto.SourceChannelId,
            SourceChannelName = dto.SourceChannelName,
            SourceDeviceId = dto.SourceDeviceId,
            SourceGroupId = dto.SourceGroupId,
            SourceTagId = dto.SourceTagId,
            SourcePointCode = dto.SourcePointCode,
            SourceDeviceName = dto.SourceDeviceName,
            SourceGroupName = dto.SourceGroupName,
            SourceTagName = dto.SourceTagName,
            SourceDataType = dto.SourceDataType,
            LowLimit = dto.LowLimit,
            HighLimit = dto.HighLimit,
            Deadband = dto.Deadband,
            RateLimitPerSecond = dto.RateLimitPerSecond,
            Operator = ParseEnum(dto.Operator, EdgeRuleComparisonOperator.GreaterThan),
            CompareValue = dto.CompareValue,
            LogicalOperator = ParseEnum(dto.LogicalOperator, EdgeRuleLogicalOperator.And),
            Conditions = dto.Conditions.Select(ToConfig).ToList(),
            DurationSeconds = dto.DurationSeconds,
            PublishToMqtt = dto.PublishToMqtt,
            PublishOnClear = dto.PublishOnClear,
            PublishTopicTemplate = dto.PublishTopicTemplate,
            PublishQos = dto.PublishQos,
            ActiveMessage = dto.ActiveMessage,
            ClearMessage = dto.ClearMessage,
            Description = dto.Description,
            QualityOperator = dto.QualityOperator,
            QualityValues = dto.QualityValues,
            WindowStatistic = dto.WindowStatistic,
            WindowSeconds = dto.WindowSeconds,
            WindowSampleCount = dto.WindowSampleCount,
            AggregationStatistic = dto.AggregationStatistic,
            TrendMode = dto.TrendMode,
            TrendWindowSeconds = dto.TrendWindowSeconds,
            TrendSampleCount = dto.TrendSampleCount,
            TrendMinSlopePerSecond = dto.TrendMinSlopePerSecond,
            TrendChangeThreshold = dto.TrendChangeThreshold,
            TrendStableDeadband = dto.TrendStableDeadband,
            StateName = dto.StateName,
            StateExpectedValue = dto.StateExpectedValue,
            StateClearValue = dto.StateClearValue,
            StateTimeoutSeconds = dto.StateTimeoutSeconds,
            RelatedChannelId = dto.RelatedChannelId,
            RelatedChannelName = dto.RelatedChannelName,
            RelatedDeviceId = dto.RelatedDeviceId,
            RelatedGroupId = dto.RelatedGroupId,
            RelatedTagId = dto.RelatedTagId,
            RelatedDeviceName = dto.RelatedDeviceName,
            RelatedGroupName = dto.RelatedGroupName,
            RelatedTagName = dto.RelatedTagName,
            RelatedPointCode = dto.RelatedPointCode,
            RelatedDataType = dto.RelatedDataType,
            RelationOperator = ParseEnum(dto.RelationOperator, EdgeRuleComparisonOperator.GreaterThan),
            RelationMultiplier = dto.RelationMultiplier,
            RelationOffset = dto.RelationOffset,
            ContextName = dto.ContextName,
            ContextExpectedValue = dto.ContextExpectedValue,
            ContextOperator = ParseEnum(dto.ContextOperator, EdgeRuleComparisonOperator.Equal),
            ContextChannelId = dto.ContextChannelId,
            ContextChannelName = dto.ContextChannelName,
            ContextDeviceId = dto.ContextDeviceId,
            ContextGroupId = dto.ContextGroupId,
            ContextTagId = dto.ContextTagId,
            ContextDeviceName = dto.ContextDeviceName,
            ContextGroupName = dto.ContextGroupName,
            ContextTagName = dto.ContextTagName,
            ContextPointCode = dto.ContextPointCode,
            ContextDataType = dto.ContextDataType,
            CycleStartValue = dto.CycleStartValue,
            CycleEndValue = dto.CycleEndValue,
            CycleMinSeconds = dto.CycleMinSeconds,
            CycleMaxSeconds = dto.CycleMaxSeconds,
            TaktTargetSeconds = dto.TaktTargetSeconds,
            TaktTolerancePercent = dto.TaktTolerancePercent,
            AnomalyMode = dto.AnomalyMode,
            AnomalyThreshold = dto.AnomalyThreshold,
            AnomalyBaselineWindowSeconds = dto.AnomalyBaselineWindowSeconds,
            AnomalyBaselineSampleCount = dto.AnomalyBaselineSampleCount,
            ModelPurpose = dto.ModelPurpose,
            ModelPath = dto.ModelPath,
            ModelInputTags = dto.ModelInputTags,
            ModelInputName = dto.ModelInputName,
            ModelInputNames = dto.ModelInputNames,
            ModelOutputName = dto.ModelOutputName,
            ModelOutputIndex = Math.Max(0, dto.ModelOutputIndex),
            ModelOperator = ParseEnum(dto.ModelOperator, EdgeRuleComparisonOperator.GreaterThanOrEqual),
            ModelThreshold = dto.ModelThreshold,
            ModelTimeoutMilliseconds = NormalizeModelTimeout(dto.ModelTimeoutMilliseconds),
            AlarmSeverity = dto.AlarmSeverity,
            AlarmSuppressSeconds = dto.AlarmSuppressSeconds,
            AlarmReTriggerSeconds = dto.AlarmReTriggerSeconds,
            AlarmEscalateAfterSeconds = dto.AlarmEscalateAfterSeconds,
            ActionDelaySeconds = dto.ActionDelaySeconds,
            ActionCooldownSeconds = dto.ActionCooldownSeconds,
            ActionMaxPerMinute = dto.ActionMaxPerMinute,
            TransformMultiplier = dto.TransformMultiplier,
            TransformOffset = dto.TransformOffset,
            TransformUseAbsolute = dto.TransformUseAbsolute,
            TransformExpression = dto.TransformExpression,
            TransformTimeoutMilliseconds = NormalizeTransformTimeout(dto.TransformTimeoutMilliseconds),
            SequenceWindowSeconds = dto.SequenceWindowSeconds,
            SequenceStepTimeoutSeconds = dto.SequenceStepTimeoutSeconds,
            SequenceMinIntervalSeconds = dto.SequenceMinIntervalSeconds,
            SequenceResetOnMismatch = dto.SequenceResetOnMismatch,
            ClearDurationSeconds = dto.ClearDurationSeconds,
            Actions = (dto.Actions ?? new List<EdgeRuleActionDto>()).Select(ToConfig).ToList()
        };
    }

    public static EdgeRuleActionConfig ToConfig(EdgeRuleActionDto dto)
    {
        return new EdgeRuleActionConfig
        {
            Id = EmptyToNewId(dto.Id),
            ActionType = dto.ActionType,
            Enabled = dto.Enabled,
            ExecuteOnActive = dto.ExecuteOnActive,
            ExecuteOnClear = dto.ExecuteOnClear,
            TopicTemplate = dto.TopicTemplate,
            Qos = dto.Qos,
            ActiveMessage = dto.ActiveMessage,
            ClearMessage = dto.ClearMessage,
            EmailSmtpHost = dto.EmailSmtpHost,
            EmailSmtpPort = dto.EmailSmtpPort,
            EmailEnableSsl = dto.EmailEnableSsl,
            EmailUsername = dto.EmailUsername,
            EmailPassword = dto.EmailPassword,
            EmailFrom = dto.EmailFrom,
            EmailTo = dto.EmailTo,
            EmailCc = dto.EmailCc,
            EmailSubjectTemplate = dto.EmailSubjectTemplate,
            EmailBodyTemplate = dto.EmailBodyTemplate,
            WebhookUrl = dto.WebhookUrl,
            WebhookMethod = dto.WebhookMethod,
            WebhookHeaders = dto.WebhookHeaders,
            WebhookBodyTemplate = dto.WebhookBodyTemplate,
            WebhookContentType = dto.WebhookContentType,
            WebhookTimeoutSeconds = dto.WebhookTimeoutSeconds,
            WebhookRetryCount = dto.WebhookRetryCount,
            DebugLabel = dto.DebugLabel
        };
    }

    public static EdgeRuleConditionConfig ToConfig(EdgeRuleConditionDto dto)
    {
        return new EdgeRuleConditionConfig
        {
            Id = EmptyToNewId(dto.Id),
            SourceChannelId = dto.SourceChannelId,
            SourceChannelName = dto.SourceChannelName,
            SourceDeviceId = dto.SourceDeviceId,
            SourceGroupId = dto.SourceGroupId,
            SourceTagId = dto.SourceTagId,
            SourcePointCode = dto.SourcePointCode,
            SourceDeviceName = dto.SourceDeviceName,
            SourceGroupName = dto.SourceGroupName,
            SourceTagName = dto.SourceTagName,
            SourceDataType = dto.SourceDataType,
            Operator = ParseEnum(dto.Operator, EdgeRuleComparisonOperator.GreaterThan),
            CompareValue = dto.CompareValue,
            TransformMultiplier = dto.TransformMultiplier,
            TransformOffset = dto.TransformOffset,
            TransformUseAbsolute = dto.TransformUseAbsolute,
            TransformExpression = dto.TransformExpression
        };
    }

    public static FlowRuleDefinition ToConfig(FlowRuleDefinitionDto dto)
    {
        return new FlowRuleDefinition
        {
            Id = EmptyToNewId(dto.Id),
            Name = dto.Name,
            Description = dto.Description,
            Enabled = dto.Enabled,
            Version = dto.Version,
            LifecycleState = dto.LifecycleState,
            PublishedVersion = dto.PublishedVersion,
            PublishedTime = dto.PublishedTime,
            PublishedBy = dto.PublishedBy,
            Mode = dto.Mode,
            CompiledRuleId = dto.CompiledRuleId,
            Nodes = dto.Nodes.Select(ToConfig).ToList(),
            Edges = dto.Edges.Select(ToConfig).ToList(),
            CreatedTime = dto.CreatedTime,
            UpdatedTime = dto.UpdatedTime
        };
    }

    public static FlowRuleNode ToConfig(FlowRuleNodeDto dto)
    {
        return new FlowRuleNode
        {
            Id = EmptyToNewId(dto.Id),
            NodeType = dto.NodeType,
            Label = dto.Label,
            X = dto.X,
            Y = dto.Y,
            ChannelId = dto.ChannelId,
            ChannelName = dto.ChannelName,
            DeviceId = dto.DeviceId,
            GroupId = dto.GroupId,
            TagId = dto.TagId,
            DeviceName = dto.DeviceName,
            GroupName = dto.GroupName,
            TagName = dto.TagName,
            PointCode = dto.PointCode,
            DataType = dto.DataType,
            ConditionType = dto.ConditionType,
            Operator = dto.Operator,
            CompareValue = dto.CompareValue,
            LowLimit = dto.LowLimit,
            HighLimit = dto.HighLimit,
            Deadband = dto.Deadband,
            RateLimitPerSecond = dto.RateLimitPerSecond,
            LogicalOperator = dto.LogicalOperator,
            DurationSeconds = dto.DurationSeconds,
            PublishToMqtt = dto.PublishToMqtt,
            PublishOnClear = dto.PublishOnClear,
            TopicTemplate = dto.TopicTemplate,
            PublishQos = dto.PublishQos,
            ActiveMessage = dto.ActiveMessage,
            ClearMessage = dto.ClearMessage,
            HysteresisMode = dto.HysteresisMode,
            HysteresisOnValue = dto.HysteresisOnValue,
            HysteresisOffValue = dto.HysteresisOffValue,
            Expression = dto.Expression,
            AlarmLevels = dto.AlarmLevels.Select(ToConfig).ToList(),
            QualityOperator = dto.QualityOperator,
            QualityValues = dto.QualityValues,
            WindowStatistic = dto.WindowStatistic,
            WindowSeconds = dto.WindowSeconds,
            WindowSampleCount = dto.WindowSampleCount,
            AggregationStatistic = dto.AggregationStatistic,
            TrendMode = dto.TrendMode,
            TrendWindowSeconds = dto.TrendWindowSeconds,
            TrendSampleCount = dto.TrendSampleCount,
            TrendMinSlopePerSecond = dto.TrendMinSlopePerSecond,
            TrendChangeThreshold = dto.TrendChangeThreshold,
            TrendStableDeadband = dto.TrendStableDeadband,
            StateName = dto.StateName,
            StateExpectedValue = dto.StateExpectedValue,
            StateClearValue = dto.StateClearValue,
            StateTimeoutSeconds = dto.StateTimeoutSeconds,
            RelatedChannelId = dto.RelatedChannelId,
            RelatedChannelName = dto.RelatedChannelName,
            RelatedDeviceId = dto.RelatedDeviceId,
            RelatedGroupId = dto.RelatedGroupId,
            RelatedTagId = dto.RelatedTagId,
            RelatedDeviceName = dto.RelatedDeviceName,
            RelatedGroupName = dto.RelatedGroupName,
            RelatedTagName = dto.RelatedTagName,
            RelatedPointCode = dto.RelatedPointCode,
            RelatedDataType = dto.RelatedDataType,
            RelationOperator = dto.RelationOperator,
            RelationMultiplier = dto.RelationMultiplier,
            RelationOffset = dto.RelationOffset,
            ContextName = dto.ContextName,
            ContextExpectedValue = dto.ContextExpectedValue,
            ContextOperator = dto.ContextOperator,
            ContextChannelId = dto.ContextChannelId,
            ContextChannelName = dto.ContextChannelName,
            ContextDeviceId = dto.ContextDeviceId,
            ContextGroupId = dto.ContextGroupId,
            ContextTagId = dto.ContextTagId,
            ContextDeviceName = dto.ContextDeviceName,
            ContextGroupName = dto.ContextGroupName,
            ContextTagName = dto.ContextTagName,
            ContextPointCode = dto.ContextPointCode,
            ContextDataType = dto.ContextDataType,
            CycleStartValue = dto.CycleStartValue,
            CycleEndValue = dto.CycleEndValue,
            CycleMinSeconds = dto.CycleMinSeconds,
            CycleMaxSeconds = dto.CycleMaxSeconds,
            TaktTargetSeconds = dto.TaktTargetSeconds,
            TaktTolerancePercent = dto.TaktTolerancePercent,
            AnomalyMode = dto.AnomalyMode,
            AnomalyThreshold = dto.AnomalyThreshold,
            AnomalyBaselineWindowSeconds = dto.AnomalyBaselineWindowSeconds,
            AnomalyBaselineSampleCount = dto.AnomalyBaselineSampleCount,
            ModelPurpose = dto.ModelPurpose,
            ModelPath = dto.ModelPath,
            ModelInputTags = dto.ModelInputTags,
            ModelInputName = dto.ModelInputName,
            ModelInputNames = dto.ModelInputNames,
            ModelOutputName = dto.ModelOutputName,
            ModelOutputIndex = Math.Max(0, dto.ModelOutputIndex),
            ModelOperator = dto.ModelOperator,
            ModelThreshold = dto.ModelThreshold,
            ModelTimeoutMilliseconds = NormalizeModelTimeout(dto.ModelTimeoutMilliseconds),
            AlarmSeverity = dto.AlarmSeverity,
            AlarmSuppressSeconds = dto.AlarmSuppressSeconds,
            AlarmReTriggerSeconds = dto.AlarmReTriggerSeconds,
            AlarmEscalateAfterSeconds = dto.AlarmEscalateAfterSeconds,
            ActionDelaySeconds = dto.ActionDelaySeconds,
            ActionCooldownSeconds = dto.ActionCooldownSeconds,
            ActionMaxPerMinute = dto.ActionMaxPerMinute,
            DebugEnabled = dto.DebugEnabled,
            DebugLabel = dto.DebugLabel,
            TransformMultiplier = dto.TransformMultiplier,
            TransformOffset = dto.TransformOffset,
            TransformUseAbsolute = dto.TransformUseAbsolute,
            TransformExpression = dto.TransformExpression,
            TransformTimeoutMilliseconds = NormalizeTransformTimeout(dto.TransformTimeoutMilliseconds),
            SequenceWindowSeconds = dto.SequenceWindowSeconds,
            SequenceStepTimeoutSeconds = dto.SequenceStepTimeoutSeconds,
            SequenceMinIntervalSeconds = dto.SequenceMinIntervalSeconds,
            SequenceResetOnMismatch = dto.SequenceResetOnMismatch,
            ClearDurationSeconds = dto.ClearDurationSeconds,
            ExecuteOnActive = dto.ExecuteOnActive,
            ExecuteOnClear = dto.ExecuteOnClear,
            EmailSmtpHost = dto.EmailSmtpHost,
            EmailSmtpPort = dto.EmailSmtpPort,
            EmailEnableSsl = dto.EmailEnableSsl,
            EmailUsername = dto.EmailUsername,
            EmailPassword = dto.EmailPassword,
            EmailFrom = dto.EmailFrom,
            EmailTo = dto.EmailTo,
            EmailCc = dto.EmailCc,
            EmailSubjectTemplate = dto.EmailSubjectTemplate,
            EmailBodyTemplate = dto.EmailBodyTemplate,
            WebhookUrl = dto.WebhookUrl,
            WebhookMethod = dto.WebhookMethod,
            WebhookHeaders = dto.WebhookHeaders,
            WebhookBodyTemplate = dto.WebhookBodyTemplate,
            WebhookContentType = dto.WebhookContentType,
            WebhookTimeoutSeconds = dto.WebhookTimeoutSeconds,
            WebhookRetryCount = dto.WebhookRetryCount
        };
    }

    public static FlowRuleAlarmLevel ToConfig(FlowRuleAlarmLevelDto dto)
    {
        return new FlowRuleAlarmLevel
        {
            Id = EmptyToNewId(dto.Id),
            Name = dto.Name,
            Severity = dto.Severity,
            Operator = dto.Operator,
            CompareValue = dto.CompareValue,
            Message = dto.Message
        };
    }

    public static FlowRuleEdge ToConfig(FlowRuleEdgeDto dto)
    {
        return new FlowRuleEdge
        {
            Id = EmptyToNewId(dto.Id),
            SourceNodeId = dto.SourceNodeId,
            TargetNodeId = dto.TargetNodeId,
            SourcePort = dto.SourcePort,
            TargetPort = dto.TargetPort
        };
    }

    public static MqttGatewayOptions ToConfig(MqttConfigurationDto dto)
    {
        return new MqttGatewayOptions
        {
            Enabled = dto.Enabled,
            GatewayId = dto.GatewayId,
            GatewayName = dto.GatewayName,
            SiteName = dto.SiteName,
            CloudProtocolVersion = dto.CloudProtocolVersion,
            ConfigVersion = dto.ConfigVersion,
            PublishMode = dto.PublishMode,
            Host = dto.Host,
            Port = dto.Port,
            ClientId = dto.ClientId,
            Username = dto.Username,
            Password = dto.Password,
            UseTls = dto.UseTls,
            AllowUntrustedCertificates = dto.AllowUntrustedCertificates,
            ClientCertificatePath = dto.ClientCertificatePath,
            ClientCertificatePassword = dto.ClientCertificatePassword,
            ClientCertificateThumbprint = dto.ClientCertificateThumbprint,
            ServerCertificateThumbprint = dto.ServerCertificateThumbprint,
            CaCertificatePath = dto.CaCertificatePath,
            SubscribeTopic = dto.SubscribeTopic,
            PublishEnabled = dto.PublishEnabled,
            PublishSelectedTagsOnly = dto.PublishSelectedTagsOnly,
            PublishChangedOnly = dto.PublishChangedOnly,
            PublishUnchangedHeartbeatSeconds = dto.PublishUnchangedHeartbeatSeconds,
            PublishTopicTemplate = dto.PublishTopicTemplate,
            PublishQos = dto.PublishQos,
            HeartbeatEnabled = dto.HeartbeatEnabled,
            HeartbeatIntervalSeconds = dto.HeartbeatIntervalSeconds,
            HeartbeatTopic = dto.HeartbeatTopic,
            HeartbeatQos = dto.HeartbeatQos,
            StatusTopic = dto.StatusTopic,
            CommandReplyTopicTemplate = dto.CommandReplyTopicTemplate,
            OutboxDirectory = dto.OutboxDirectory,
            PublishAckTimeoutMilliseconds = dto.PublishAckTimeoutMilliseconds,
            OutboxMaxMessages = dto.OutboxMaxMessages,
            OutboxMaxMegabytes = dto.OutboxMaxMegabytes,
            OutboxRetentionHours = dto.OutboxRetentionHours,
            OutboxQuarantineRetentionHours = dto.OutboxQuarantineRetentionHours,
            PublishFlushBatchSize = dto.PublishFlushBatchSize,
            PublishRetryMinSeconds = dto.PublishRetryMinSeconds,
            PublishRetryMaxSeconds = dto.PublishRetryMaxSeconds,
            ReconnectSeconds = dto.ReconnectSeconds,
            KeepAliveSeconds = dto.KeepAliveSeconds,
            SparkplugNamespace = dto.SparkplugNamespace,
            SparkplugGroupId = dto.SparkplugGroupId,
            SparkplugEdgeNodeId = dto.SparkplugEdgeNodeId,
            SparkplugDeviceIdSource = dto.SparkplugDeviceIdSource,
            SparkplugMetricNameTemplate = dto.SparkplugMetricNameTemplate,
            SparkplugPublishNodeBirth = dto.SparkplugPublishNodeBirth,
            SparkplugPublishDeviceBirth = dto.SparkplugPublishDeviceBirth,
            SparkplugPublishDeviceDeath = dto.SparkplugPublishDeviceDeath,
            SparkplugIncludeProperties = dto.SparkplugIncludeProperties,
            SparkplugUseAliases = dto.SparkplugUseAliases,
            SparkplugDeathQos = dto.SparkplugDeathQos,
            SparkplugBirthQos = dto.SparkplugBirthQos,
            SparkplugEnableCommands = dto.SparkplugEnableCommands,
            SparkplugPrimaryHostId = dto.SparkplugPrimaryHostId
        };
    }

    public static OpcUaServerOptions ToConfig(OpcUaServerConfigurationDto dto)
    {
        dto ??= new OpcUaServerConfigurationDto();
        OpcUaServerOptions options = new OpcUaServerOptions
        {
            Enabled = dto.Enabled,
            ApplicationName = dto.ApplicationName,
            ApplicationUri = dto.ApplicationUri,
            ProductUri = dto.ProductUri,
            Host = dto.Host,
            Port = dto.Port,
            EndpointPath = dto.EndpointPath,
            NamespaceUri = dto.NamespaceUri,
            CertificateStorePath = dto.CertificateStorePath,
            AutoAcceptUntrustedCertificates = dto.AutoAcceptUntrustedCertificates,
            AllowAnonymous = dto.AllowAnonymous,
            UsernamePasswordEnabled = dto.UsernamePasswordEnabled,
            Username = dto.Username,
            SecurityPolicy = dto.SecurityPolicy,
            AllowSecurityPolicyNone = dto.AllowSecurityPolicyNone,
            EnableBasic256SignAndEncrypt = dto.EnableBasic256SignAndEncrypt,
            EnableBasic256Sha256SignAndEncrypt = dto.EnableBasic256Sha256SignAndEncrypt,
            MinimumSamplingIntervalMs = dto.MinimumSamplingIntervalMs,
            PublishDiagnostics = dto.PublishDiagnostics
        };

        if (!string.IsNullOrEmpty(dto.Password))
            OpcUaPasswordHasher.SetPassword(options, dto.Password);

        return OpcUaServerOptions.Normalize(options);
    }

    public static LocalHistoryOptions ToConfig(HistoryConfigurationDto dto)
    {
        return new LocalHistoryOptions
        {
            Enabled = dto.Enabled,
            Directory = dto.Directory,
            RetentionDays = dto.RetentionDays,
            MaxViewRecords = dto.MaxViewRecords,
            DataProcessing = ToConfig(dto.DataProcessing),
            Storage = ToConfig(dto.Storage, dto.RetentionDays)
        };
    }

    public static EdgeDataProcessingOptions ToConfig(HistoryDataProcessingConfigurationDto dto)
    {
        dto ??= new HistoryDataProcessingConfigurationDto();
        return EdgeDataProcessingOptions.Normalize(new EdgeDataProcessingOptions
        {
            Enabled = dto.Enabled,
            CompressionEnabled = dto.CompressionEnabled,
            CompressionTolerance = dto.CompressionTolerance,
            CompressDuplicateText = dto.CompressDuplicateText,
            DownsamplingEnabled = dto.DownsamplingEnabled,
            DownsamplingIntervalMs = dto.DownsamplingIntervalMs,
            AlignmentEnabled = dto.AlignmentEnabled,
            AlignmentIntervalMs = dto.AlignmentIntervalMs,
            FillEnabled = dto.FillEnabled,
            FillIntervalMs = dto.FillIntervalMs,
            FillMaxGapSeconds = dto.FillMaxGapSeconds,
            FillMode = dto.FillMode,
            AggregationEnabled = dto.AggregationEnabled,
            AggregationIntervalSeconds = dto.AggregationIntervalSeconds,
            AggregationMethods = dto.AggregationMethods,
            MaxSyntheticPointsPerInput = dto.MaxSyntheticPointsPerInput
        });
    }

    public static LocalHistoryStorageOptions ToConfig(HistoryStorageConfigurationDto dto, int retentionDays)
    {
        dto ??= new HistoryStorageConfigurationDto();
        return LocalHistoryStorageOptions.Normalize(new LocalHistoryStorageOptions
        {
            TieringEnabled = dto.TieringEnabled,
            ColdDirectory = dto.ColdDirectory,
            RetentionPolicy = dto.RetentionPolicy,
            HotRetentionDays = dto.HotRetentionDays,
            ColdRetentionDays = dto.ColdRetentionDays,
            CompressionEnabled = dto.CompressionEnabled,
            CompressHotFiles = dto.CompressHotFiles,
            CompressColdFiles = dto.CompressColdFiles,
            CompressAfterDays = dto.CompressAfterDays,
            AutoCleanupEnabled = dto.AutoCleanupEnabled,
            CleanupIntervalHours = dto.CleanupIntervalHours,
            MaxStorageMegabytes = dto.MaxStorageMegabytes
        }, LocalHistoryOptions.ClampRetentionDays(retentionDays));
    }

    public static StorageHealthThresholds ToConfig(StorageHealthConfigurationDto dto)
    {
        dto ??= new StorageHealthConfigurationDto();
        return StorageHealthEvaluator.NormalizeThresholds(new StorageHealthThresholds
        {
            DegradedAvailableBytes = MegabytesToBytes(dto.DegradedAvailableMegabytes),
            UnhealthyAvailableBytes = MegabytesToBytes(dto.UnhealthyAvailableMegabytes),
            DegradedAvailablePercent = dto.DegradedAvailablePercent,
            UnhealthyAvailablePercent = dto.UnhealthyAvailablePercent
        });
    }

    private static TEnum ParseEnum<TEnum>(string value, TEnum defaultValue) where TEnum : struct
    {
        return Enum.TryParse(value, true, out TEnum parsed) ? parsed : defaultValue;
    }

    private static int NormalizeTransformTimeout(int timeoutMilliseconds)
    {
        if (timeoutMilliseconds <= 0)
            return 50;
        return Math.Min(5000, timeoutMilliseconds);
    }

    private static int NormalizeModelTimeout(int timeoutMilliseconds)
    {
        if (timeoutMilliseconds <= 0)
            return 1000;
        return Math.Min(30000, timeoutMilliseconds);
    }

    private static string EmptyToNewId(string id)
    {
        return string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
    }

    private static long MegabytesToBytes(double megabytes)
    {
        if (double.IsNaN(megabytes) || double.IsInfinity(megabytes) || megabytes < 0D)
            megabytes = 0D;
        double bytes = Math.Min(megabytes, 1024D * 1024D) * 1024D * 1024D;
        return Convert.ToInt64(Math.Round(bytes, MidpointRounding.AwayFromZero));
    }

    private static string BuildPointCode(TagValueSnapshot snapshot)
    {
        if (snapshot == null)
            return string.Empty;
        if (!string.IsNullOrWhiteSpace(snapshot.PointCode))
            return snapshot.PointCode.Trim();

        string group = string.IsNullOrWhiteSpace(snapshot.GroupName) ? "_" : snapshot.GroupName.Trim();
        return ((snapshot.ChannelName ?? string.Empty).Trim() + "." +
                (snapshot.DeviceName ?? string.Empty).Trim() + "." + group + "." +
                (snapshot.TagName ?? string.Empty).Trim()).Trim('.');
    }
}

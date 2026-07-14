/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Configuration
* 项目描述 ：
* 类 名 称 ：ProjectConfigCloner
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
using System.Collections.Generic;
using IPC.Plc.Communication.Core;

namespace IPC.Runtime.Configuration
{
    
    
    
    
    
    
    
    
    
    public static class ProjectConfigCloner
    {
        public static ProjectConfig Clone(ProjectConfig source)
        {
            if (source == null)
                return new ProjectConfig();

            ProjectConfig target = new ProjectConfig
            {
                ProjectId = source.ProjectId,
                Name = source.Name,
                Channels = new List<ChannelConfig>(),
                Devices = new List<DeviceConfig>(),
                Rules = new List<EdgeRuleConfig>(),
                FlowRules = new List<FlowRuleDefinition>()
            };

            if (source.Channels != null)
            {
                for (int i = 0; i < source.Channels.Count; i++)
                {
                    ChannelConfig? channel = CloneChannel(source.Channels[i]);
                    if (channel != null)
                        target.Channels.Add(channel);
                }
            }

            if (source.Devices != null)
            {
                for (int i = 0; i < source.Devices.Count; i++)
                {
                    DeviceConfig? device = CloneDevice(source.Devices[i]);
                    if (device != null)
                        target.Devices.Add(device);
                }
            }

            if (source.Rules != null)
            {
                for (int i = 0; i < source.Rules.Count; i++)
                {
                    EdgeRuleConfig? rule = CloneRule(source.Rules[i]);
                    if (rule != null)
                        target.Rules.Add(rule);
                }
            }

            if (source.FlowRules != null)
            {
                for (int i = 0; i < source.FlowRules.Count; i++)
                {
                    FlowRuleDefinition? rule = CloneFlowRule(source.FlowRules[i]);
                    if (rule != null)
                        target.FlowRules.Add(rule);
                }
            }

            return target;
        }

        public static ChannelConfig? CloneChannel(ChannelConfig? source)
        {
            if (source == null)
                return null;

            return new ChannelConfig
            {
                Id = source.Id,
                Name = source.Name,
                Enabled = source.Enabled,
                Protocol = source.Protocol,
                DriverId = source.DriverId,
                MaxConcurrentDevicePolls = source.MaxConcurrentDevicePolls,
                SchedulingWeight = source.SchedulingWeight
            };
        }

        public static FlowRuleDefinition? CloneFlowRule(FlowRuleDefinition? source)
        {
            if (source == null)
                return null;

            FlowRuleDefinition target = new FlowRuleDefinition
            {
                Id = source.Id,
                Name = source.Name,
                Description = source.Description,
                Enabled = source.Enabled,
                Version = source.Version,
                LifecycleState = source.LifecycleState,
                PublishedVersion = source.PublishedVersion,
                PublishedTime = source.PublishedTime,
                PublishedBy = source.PublishedBy,
                Mode = source.Mode,
                CompiledRuleId = source.CompiledRuleId,
                Nodes = new List<FlowRuleNode>(),
                Edges = new List<FlowRuleEdge>(),
                CreatedTime = source.CreatedTime,
                UpdatedTime = source.UpdatedTime
            };

            if (source.Nodes != null)
            {
                for (int i = 0; i < source.Nodes.Count; i++)
                {
                    FlowRuleNode? node = CloneFlowNode(source.Nodes[i]);
                    if (node != null)
                        target.Nodes.Add(node);
                }
            }

            if (source.Edges != null)
            {
                for (int i = 0; i < source.Edges.Count; i++)
                {
                    FlowRuleEdge? edge = CloneFlowEdge(source.Edges[i]);
                    if (edge != null)
                        target.Edges.Add(edge);
                }
            }

            return target;
        }

        private static FlowRuleNode? CloneFlowNode(FlowRuleNode? source)
        {
            if (source == null)
                return null;

            return new FlowRuleNode
            {
                Id = source.Id,
                NodeType = source.NodeType,
                Label = source.Label,
                X = source.X,
                Y = source.Y,
                DeviceName = source.DeviceName,
                GroupName = source.GroupName,
                TagName = source.TagName,
                PointCode = source.PointCode,
                DataType = source.DataType,
                ConditionType = source.ConditionType,
                Operator = source.Operator,
                CompareValue = source.CompareValue,
                LowLimit = source.LowLimit,
                HighLimit = source.HighLimit,
                Deadband = source.Deadband,
                RateLimitPerSecond = source.RateLimitPerSecond,
                LogicalOperator = source.LogicalOperator,
                DurationSeconds = source.DurationSeconds,
                PublishToMqtt = source.PublishToMqtt,
                PublishOnClear = source.PublishOnClear,
                TopicTemplate = source.TopicTemplate,
                PublishQos = source.PublishQos,
                ActiveMessage = source.ActiveMessage,
                ClearMessage = source.ClearMessage,
                HysteresisMode = source.HysteresisMode,
                HysteresisOnValue = source.HysteresisOnValue,
                HysteresisOffValue = source.HysteresisOffValue,
                Expression = source.Expression,
                AlarmLevels = CloneFlowAlarmLevels(source.AlarmLevels),
                QualityOperator = source.QualityOperator,
                QualityValues = source.QualityValues,
                WindowStatistic = source.WindowStatistic,
                WindowSeconds = source.WindowSeconds,
                WindowSampleCount = source.WindowSampleCount,
                AggregationStatistic = source.AggregationStatistic,
                TrendMode = source.TrendMode,
                TrendWindowSeconds = source.TrendWindowSeconds,
                TrendSampleCount = source.TrendSampleCount,
                TrendMinSlopePerSecond = source.TrendMinSlopePerSecond,
                TrendChangeThreshold = source.TrendChangeThreshold,
                TrendStableDeadband = source.TrendStableDeadband,
                StateName = source.StateName,
                StateExpectedValue = source.StateExpectedValue,
                StateClearValue = source.StateClearValue,
                StateTimeoutSeconds = source.StateTimeoutSeconds,
                RelatedDeviceName = source.RelatedDeviceName,
                RelatedGroupName = source.RelatedGroupName,
                RelatedTagName = source.RelatedTagName,
                RelatedPointCode = source.RelatedPointCode,
                RelatedDataType = source.RelatedDataType,
                RelationOperator = source.RelationOperator,
                RelationMultiplier = source.RelationMultiplier,
                RelationOffset = source.RelationOffset,
                ContextName = source.ContextName,
                ContextExpectedValue = source.ContextExpectedValue,
                ContextOperator = source.ContextOperator,
                ContextDeviceName = source.ContextDeviceName,
                ContextGroupName = source.ContextGroupName,
                ContextTagName = source.ContextTagName,
                ContextPointCode = source.ContextPointCode,
                ContextDataType = source.ContextDataType,
                CycleStartValue = source.CycleStartValue,
                CycleEndValue = source.CycleEndValue,
                CycleMinSeconds = source.CycleMinSeconds,
                CycleMaxSeconds = source.CycleMaxSeconds,
                TaktTargetSeconds = source.TaktTargetSeconds,
                TaktTolerancePercent = source.TaktTolerancePercent,
                AnomalyMode = source.AnomalyMode,
                AnomalyThreshold = source.AnomalyThreshold,
                AnomalyBaselineWindowSeconds = source.AnomalyBaselineWindowSeconds,
                AnomalyBaselineSampleCount = source.AnomalyBaselineSampleCount,
                ModelPurpose = source.ModelPurpose,
                ModelPath = source.ModelPath,
                ModelInputTags = source.ModelInputTags,
                ModelInputName = source.ModelInputName,
                ModelInputNames = source.ModelInputNames,
                ModelOutputName = source.ModelOutputName,
                ModelOutputIndex = source.ModelOutputIndex,
                ModelOperator = source.ModelOperator,
                ModelThreshold = source.ModelThreshold,
                ModelTimeoutMilliseconds = source.ModelTimeoutMilliseconds <= 0 ? 1000 : source.ModelTimeoutMilliseconds,
                AlarmSeverity = source.AlarmSeverity,
                AlarmSuppressSeconds = source.AlarmSuppressSeconds,
                AlarmReTriggerSeconds = source.AlarmReTriggerSeconds,
                AlarmEscalateAfterSeconds = source.AlarmEscalateAfterSeconds,
                ActionDelaySeconds = source.ActionDelaySeconds,
                ActionCooldownSeconds = source.ActionCooldownSeconds,
                ActionMaxPerMinute = source.ActionMaxPerMinute,
                DebugEnabled = source.DebugEnabled,
                DebugLabel = source.DebugLabel,
                TransformMultiplier = source.TransformMultiplier,
                TransformOffset = source.TransformOffset,
                TransformUseAbsolute = source.TransformUseAbsolute,
                TransformExpression = source.TransformExpression,
                TransformTimeoutMilliseconds = source.TransformTimeoutMilliseconds <= 0 ? 50 : source.TransformTimeoutMilliseconds,
                SequenceWindowSeconds = source.SequenceWindowSeconds,
                SequenceStepTimeoutSeconds = source.SequenceStepTimeoutSeconds,
                SequenceMinIntervalSeconds = source.SequenceMinIntervalSeconds,
                SequenceResetOnMismatch = source.SequenceResetOnMismatch,
                ClearDurationSeconds = source.ClearDurationSeconds,
                ExecuteOnActive = source.ExecuteOnActive,
                ExecuteOnClear = source.ExecuteOnClear,
                EmailSmtpHost = source.EmailSmtpHost,
                EmailSmtpPort = source.EmailSmtpPort,
                EmailEnableSsl = source.EmailEnableSsl,
                EmailUsername = source.EmailUsername,
                EmailPassword = source.EmailPassword,
                EmailFrom = source.EmailFrom,
                EmailTo = source.EmailTo,
                EmailCc = source.EmailCc,
                EmailSubjectTemplate = source.EmailSubjectTemplate,
                EmailBodyTemplate = source.EmailBodyTemplate,
                WebhookUrl = source.WebhookUrl,
                WebhookMethod = source.WebhookMethod,
                WebhookHeaders = source.WebhookHeaders,
                WebhookBodyTemplate = source.WebhookBodyTemplate,
                WebhookContentType = source.WebhookContentType,
                WebhookTimeoutSeconds = source.WebhookTimeoutSeconds,
                WebhookRetryCount = source.WebhookRetryCount
            };
        }

        private static List<FlowRuleAlarmLevel> CloneFlowAlarmLevels(List<FlowRuleAlarmLevel>? source)
        {
            List<FlowRuleAlarmLevel> target = new List<FlowRuleAlarmLevel>();
            if (source == null)
                return target;

            for (int i = 0; i < source.Count; i++)
            {
                FlowRuleAlarmLevel level = source[i];
                if (level == null)
                    continue;
                target.Add(new FlowRuleAlarmLevel
                {
                    Id = level.Id,
                    Name = level.Name,
                    Severity = level.Severity,
                    Operator = level.Operator,
                    CompareValue = level.CompareValue,
                    Message = level.Message
                });
            }

            return target;
        }

        private static FlowRuleEdge? CloneFlowEdge(FlowRuleEdge? source)
        {
            if (source == null)
                return null;

            return new FlowRuleEdge
            {
                Id = source.Id,
                SourceNodeId = source.SourceNodeId,
                TargetNodeId = source.TargetNodeId,
                SourcePort = source.SourcePort,
                TargetPort = source.TargetPort
            };
        }

        public static EdgeRuleConfig? CloneRule(EdgeRuleConfig? source)
        {
            if (source == null)
                return null;

            return new EdgeRuleConfig
            {
                Id = source.Id,
                Name = source.Name,
                Enabled = source.Enabled,
                ConditionType = source.ConditionType,
                SourcePointCode = source.SourcePointCode,
                SourceDeviceName = source.SourceDeviceName,
                SourceGroupName = source.SourceGroupName,
                SourceTagName = source.SourceTagName,
                SourceDataType = source.SourceDataType,
                LowLimit = source.LowLimit,
                HighLimit = source.HighLimit,
                Deadband = source.Deadband,
                RateLimitPerSecond = source.RateLimitPerSecond,
                Operator = source.Operator,
                CompareValue = source.CompareValue,
                LogicalOperator = source.LogicalOperator,
                Conditions = CloneConditions(source.Conditions),
                DurationSeconds = source.DurationSeconds,
                PublishToMqtt = source.PublishToMqtt,
                PublishOnClear = source.PublishOnClear,
                PublishTopicTemplate = source.PublishTopicTemplate,
                PublishQos = source.PublishQos,
                ActiveMessage = source.ActiveMessage,
                ClearMessage = source.ClearMessage,
                Description = source.Description,
                HysteresisMode = source.HysteresisMode,
                HysteresisOnValue = source.HysteresisOnValue,
                HysteresisOffValue = source.HysteresisOffValue,
                Expression = source.Expression,
                AlarmLevels = CloneAlarmLevels(source.AlarmLevels),
                QualityOperator = source.QualityOperator,
                QualityValues = source.QualityValues,
                WindowStatistic = source.WindowStatistic,
                WindowSeconds = source.WindowSeconds,
                WindowSampleCount = source.WindowSampleCount,
                AggregationStatistic = source.AggregationStatistic,
                TrendMode = source.TrendMode,
                TrendWindowSeconds = source.TrendWindowSeconds,
                TrendSampleCount = source.TrendSampleCount,
                TrendMinSlopePerSecond = source.TrendMinSlopePerSecond,
                TrendChangeThreshold = source.TrendChangeThreshold,
                TrendStableDeadband = source.TrendStableDeadband,
                StateName = source.StateName,
                StateExpectedValue = source.StateExpectedValue,
                StateClearValue = source.StateClearValue,
                StateTimeoutSeconds = source.StateTimeoutSeconds,
                RelatedDeviceName = source.RelatedDeviceName,
                RelatedGroupName = source.RelatedGroupName,
                RelatedTagName = source.RelatedTagName,
                RelatedPointCode = source.RelatedPointCode,
                RelatedDataType = source.RelatedDataType,
                RelationOperator = source.RelationOperator,
                RelationMultiplier = source.RelationMultiplier,
                RelationOffset = source.RelationOffset,
                ContextName = source.ContextName,
                ContextExpectedValue = source.ContextExpectedValue,
                ContextOperator = source.ContextOperator,
                ContextDeviceName = source.ContextDeviceName,
                ContextGroupName = source.ContextGroupName,
                ContextTagName = source.ContextTagName,
                ContextPointCode = source.ContextPointCode,
                ContextDataType = source.ContextDataType,
                CycleStartValue = source.CycleStartValue,
                CycleEndValue = source.CycleEndValue,
                CycleMinSeconds = source.CycleMinSeconds,
                CycleMaxSeconds = source.CycleMaxSeconds,
                TaktTargetSeconds = source.TaktTargetSeconds,
                TaktTolerancePercent = source.TaktTolerancePercent,
                AnomalyMode = source.AnomalyMode,
                AnomalyThreshold = source.AnomalyThreshold,
                AnomalyBaselineWindowSeconds = source.AnomalyBaselineWindowSeconds,
                AnomalyBaselineSampleCount = source.AnomalyBaselineSampleCount,
                ModelPurpose = source.ModelPurpose,
                ModelPath = source.ModelPath,
                ModelInputTags = source.ModelInputTags,
                ModelInputName = source.ModelInputName,
                ModelInputNames = source.ModelInputNames,
                ModelOutputName = source.ModelOutputName,
                ModelOutputIndex = source.ModelOutputIndex,
                ModelOperator = source.ModelOperator,
                ModelThreshold = source.ModelThreshold,
                ModelTimeoutMilliseconds = source.ModelTimeoutMilliseconds <= 0 ? 1000 : source.ModelTimeoutMilliseconds,
                AlarmSeverity = source.AlarmSeverity,
                AlarmSuppressSeconds = source.AlarmSuppressSeconds,
                AlarmReTriggerSeconds = source.AlarmReTriggerSeconds,
                AlarmEscalateAfterSeconds = source.AlarmEscalateAfterSeconds,
                ActionDelaySeconds = source.ActionDelaySeconds,
                ActionCooldownSeconds = source.ActionCooldownSeconds,
                ActionMaxPerMinute = source.ActionMaxPerMinute,
                TransformMultiplier = source.TransformMultiplier,
                TransformOffset = source.TransformOffset,
                TransformUseAbsolute = source.TransformUseAbsolute,
                TransformExpression = source.TransformExpression,
                TransformTimeoutMilliseconds = source.TransformTimeoutMilliseconds <= 0 ? 50 : source.TransformTimeoutMilliseconds,
                SequenceWindowSeconds = source.SequenceWindowSeconds,
                SequenceStepTimeoutSeconds = source.SequenceStepTimeoutSeconds,
                SequenceMinIntervalSeconds = source.SequenceMinIntervalSeconds,
                SequenceResetOnMismatch = source.SequenceResetOnMismatch,
                ClearDurationSeconds = source.ClearDurationSeconds,
                Actions = CloneActions(source.Actions)
            };
        }

        private static List<EdgeRuleActionConfig> CloneActions(List<EdgeRuleActionConfig>? source)
        {
            List<EdgeRuleActionConfig> target = new List<EdgeRuleActionConfig>();
            if (source == null)
                return target;

            for (int i = 0; i < source.Count; i++)
            {
                EdgeRuleActionConfig? action = CloneAction(source[i]);
                if (action != null)
                    target.Add(action);
            }

            return target;
        }

        private static EdgeRuleActionConfig? CloneAction(EdgeRuleActionConfig? source)
        {
            if (source == null)
                return null;

            return new EdgeRuleActionConfig
            {
                Id = source.Id,
                ActionType = source.ActionType,
                Enabled = source.Enabled,
                ExecuteOnActive = source.ExecuteOnActive,
                ExecuteOnClear = source.ExecuteOnClear,
                TopicTemplate = source.TopicTemplate,
                Qos = source.Qos,
                ActiveMessage = source.ActiveMessage,
                ClearMessage = source.ClearMessage,
                EmailSmtpHost = source.EmailSmtpHost,
                EmailSmtpPort = source.EmailSmtpPort,
                EmailEnableSsl = source.EmailEnableSsl,
                EmailUsername = source.EmailUsername,
                EmailPassword = source.EmailPassword,
                EmailFrom = source.EmailFrom,
                EmailTo = source.EmailTo,
                EmailCc = source.EmailCc,
                EmailSubjectTemplate = source.EmailSubjectTemplate,
                EmailBodyTemplate = source.EmailBodyTemplate,
                WebhookUrl = source.WebhookUrl,
                WebhookMethod = source.WebhookMethod,
                WebhookHeaders = source.WebhookHeaders,
                WebhookBodyTemplate = source.WebhookBodyTemplate,
                WebhookContentType = source.WebhookContentType,
                WebhookTimeoutSeconds = source.WebhookTimeoutSeconds,
                WebhookRetryCount = source.WebhookRetryCount,
                DebugLabel = source.DebugLabel
            };
        }

        private static List<EdgeRuleAlarmLevelConfig> CloneAlarmLevels(List<EdgeRuleAlarmLevelConfig>? source)
        {
            List<EdgeRuleAlarmLevelConfig> target = new List<EdgeRuleAlarmLevelConfig>();
            if (source == null)
                return target;

            for (int i = 0; i < source.Count; i++)
            {
                EdgeRuleAlarmLevelConfig level = source[i];
                if (level == null)
                    continue;
                target.Add(new EdgeRuleAlarmLevelConfig
                {
                    Id = level.Id,
                    Name = level.Name,
                    Severity = level.Severity,
                    Operator = level.Operator,
                    CompareValue = level.CompareValue,
                    Message = level.Message
                });
            }

            return target;
        }

        private static List<EdgeRuleConditionConfig> CloneConditions(List<EdgeRuleConditionConfig>? source)
        {
            List<EdgeRuleConditionConfig> target = new List<EdgeRuleConditionConfig>();
            if (source == null)
                return target;

            for (int i = 0; i < source.Count; i++)
            {
                EdgeRuleConditionConfig? condition = CloneCondition(source[i]);
                if (condition != null)
                    target.Add(condition);
            }
            return target;
        }

        public static EdgeRuleConditionConfig? CloneCondition(EdgeRuleConditionConfig? source)
        {
            if (source == null)
                return null;

            return new EdgeRuleConditionConfig
            {
                Id = source.Id,
                SourcePointCode = source.SourcePointCode,
                SourceDeviceName = source.SourceDeviceName,
                SourceGroupName = source.SourceGroupName,
                SourceTagName = source.SourceTagName,
                SourceDataType = source.SourceDataType,
                Operator = source.Operator,
                CompareValue = source.CompareValue,
                TransformMultiplier = source.TransformMultiplier,
                TransformOffset = source.TransformOffset,
                TransformUseAbsolute = source.TransformUseAbsolute,
                TransformExpression = source.TransformExpression
            };
        }

        private static DeviceConfig? CloneDevice(DeviceConfig? source)
        {
            if (source == null)
                return null;

            DeviceConfig target = new DeviceConfig
            {
                Id = source.Id,
                ChannelId = source.ChannelId,
                Name = source.Name,
                Enabled = source.Enabled,
                Protocol = source.Protocol,
                Connection = CloneConnection(source.Connection),
                DefaultScanRateMs = source.DefaultScanRateMs,
                FailureRetryDelayMs = source.FailureRetryDelayMs,
                MaxFailureRetryDelayMs = source.MaxFailureRetryDelayMs,
                Tags = new List<TagConfig>(),
                Groups = new List<GroupConfig>()
            };

            if (source.Tags != null)
            {
                for (int i = 0; i < source.Tags.Count; i++)
                {
                    TagConfig? tag = CloneTag(source.Tags[i]);
                    if (tag != null)
                    {
                        tag.DeviceId = target.Id;
                        tag.GroupId = string.Empty;
                        target.Tags.Add(tag);
                    }
                }
            }

            if (source.Groups != null)
            {
                for (int i = 0; i < source.Groups.Count; i++)
                {
                    GroupConfig? group = CloneGroup(source.Groups[i], target.Id);
                    if (group != null)
                        target.Groups.Add(group);
                }
            }

            return target;
        }

        private static GroupConfig? CloneGroup(GroupConfig? source, string deviceId)
        {
            if (source == null)
                return null;

            GroupConfig target = new GroupConfig
            {
                Id = source.Id,
                DeviceId = deviceId,
                Name = source.Name,
                Enabled = source.Enabled,
                ScanRateMs = source.ScanRateMs,
                Tags = new List<TagConfig>()
            };

            if (source.Tags != null)
            {
                for (int i = 0; i < source.Tags.Count; i++)
                {
                    TagConfig? tag = CloneTag(source.Tags[i]);
                    if (tag != null)
                    {
                        tag.DeviceId = deviceId;
                        tag.GroupId = target.Id;
                        target.Tags.Add(tag);
                    }
                }
            }

            return target;
        }

        private static TagConfig? CloneTag(TagConfig? source)
        {
            if (source == null)
                return null;

            return new TagConfig
            {
                Id = source.Id,
                DeviceId = source.DeviceId,
                GroupId = source.GroupId,
                Name = source.Name,
                Address = source.Address,
                MeterAddress = source.MeterAddress,
                MeterDataIdentifier = source.MeterDataIdentifier,
                MeterType = source.MeterType,
                DataType = source.DataType,
                ElementCount = source.ElementCount,
                ElementOffset = source.ElementOffset,
                Enabled = source.Enabled,
                MqttPublishEnabled = source.MqttPublishEnabled,
                AccessMode = source.AccessMode,
                ScanRateMs = source.ScanRateMs,
                Unit = source.Unit,
                PointCode = source.PointCode,
                AssetPath = source.AssetPath,
                BusinessType = source.BusinessType,
                Source = source.Source,
                Precision = source.Precision,
                Scaling = CloneScaling(source.Scaling),
                Cleaning = CloneCleaning(source.Cleaning),
                Alarm = CloneAlarm(source.Alarm),
                Description = source.Description
            };
        }

        private static PlcConnectionOptions CloneConnection(PlcConnectionOptions? source)
        {
            if (source == null)
                return new PlcConnectionOptions();

            return new PlcConnectionOptions
            {
                Protocol = source.Protocol,
                Host = source.Host,
                Port = source.Port,
                Rack = source.Rack,
                Slot = source.Slot,
                TimeoutMilliseconds = source.TimeoutMilliseconds,
                WordOrder = source.WordOrder,
                Transport = source.Transport,
                DataBits = source.DataBits,
                SerialParity = source.SerialParity,
                SerialStopBits = source.SerialStopBits,
                Username = source.Username,
                Password = source.Password,
                OpcUaSecurityPolicy = source.OpcUaSecurityPolicy,
                OpcUaMessageSecurityMode = source.OpcUaMessageSecurityMode,
                OpcUaAutoTrustServerCertificate = source.OpcUaAutoTrustServerCertificate,
                OpcDaServerProgId = source.OpcDaServerProgId,
                OpcDaGroupName = source.OpcDaGroupName,
                DriverId = source.DriverId,
                DriverOptionsJson = source.DriverOptionsJson
            };
        }

        private static ScalingConfig CloneScaling(ScalingConfig? source)
        {
            if (source == null)
                return ScalingConfig.Default();

            return new ScalingConfig
            {
                Enabled = source.Enabled,
                Multiplier = source.Multiplier,
                Offset = source.Offset,
                ClampEnabled = source.ClampEnabled,
                MinValue = source.MinValue,
                MaxValue = source.MaxValue,
                DecimalPlaces = source.DecimalPlaces
            };
        }

        private static DataCleaningConfig CloneCleaning(DataCleaningConfig? source)
        {
            if (source == null)
                return DataCleaningConfig.Default();

            DataCleaningConfig target = new DataCleaningConfig
            {
                Enabled = source.Enabled,
                OutOfRangeEnabled = source.OutOfRangeEnabled,
                MinValue = source.MinValue,
                MaxValue = source.MaxValue,
                DeadbandEnabled = source.DeadbandEnabled,
                Deadband = source.Deadband,
                DuplicateFilterEnabled = source.DuplicateFilterEnabled,
                SpikeFilterEnabled = source.SpikeFilterEnabled,
                SpikeThreshold = source.SpikeThreshold,
                SpikeWindowSeconds = source.SpikeWindowSeconds,
                EnumMappingEnabled = source.EnumMappingEnabled,
                UnitConversionEnabled = source.UnitConversionEnabled,
                SourceUnit = source.SourceUnit,
                TargetUnit = source.TargetUnit,
                UnitMultiplier = source.UnitMultiplier,
                UnitOffset = source.UnitOffset,
                PreserveLastGoodOnFilter = source.PreserveLastGoodOnFilter
            };

            if (source.EnumMappings != null)
            {
                for (int i = 0; i < source.EnumMappings.Count; i++)
                {
                    DataCleaningEnumMappingConfig item = source.EnumMappings[i];
                    if (item == null)
                        continue;
                    target.EnumMappings.Add(new DataCleaningEnumMappingConfig
                    {
                        RawValue = item.RawValue,
                        CleanValue = item.CleanValue,
                        Description = item.Description
                    });
                }
            }

            return target;
        }

        private static TagAlarmConfig CloneAlarm(TagAlarmConfig? source)
        {
            if (source == null)
                return TagAlarmConfig.Default();

            return new TagAlarmConfig
            {
                Enabled = source.Enabled,
                LowLimit = source.LowLimit,
                HighLimit = source.HighLimit,
                LowAlarmMessage = source.LowAlarmMessage,
                HighAlarmMessage = source.HighAlarmMessage,
                WarningDeviation = source.WarningDeviation,
                LowWarningMessage = source.LowWarningMessage,
                HighWarningMessage = source.HighWarningMessage
            };
        }
    }
}

/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Configuration
* 项目描述 ：
* 类 名 称 ：FlowRuleCompiler
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
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace IPC.Runtime.Configuration
{
    public static class FlowRuleCompiler
    {
        public static bool TryCompile(FlowRuleDefinition? definition, [NotNullWhen(true)] out EdgeRuleConfig? rule)
        {
            rule = null;
            if (definition == null || definition.Nodes == null)
                return false;

            List<FlowRuleNode> nodes = MaterializeNodes(definition.Nodes);
            FlowRuleGraphMap graph = new FlowRuleGraphMap(definition);
            if (nodes.Any(IsSequenceNode))
                return false;

            FlowRuleNode? tagNode = nodes.FirstOrDefault(IsTagNode);
            FlowRuleNode? conditionNode = nodes.FirstOrDefault(IsRuleNode);
            FlowRuleNode? logicNode = nodes.FirstOrDefault(IsLogicNode);
            if (tagNode == null || conditionNode == null || logicNode != null)
                return false;

            if (nodes.Count(IsTagNode) != 1 || nodes.Count(IsRuleNode) != 1)
                return false;

            if (!graph.IsReachable(tagNode.Id, conditionNode.Id))
                return false;

            HashSet<string> ancestors = graph.GetAncestorIds(conditionNode.Id);
            HashSet<string> descendants = graph.GetDescendantIds(conditionNode.Id);
            if (!ancestors.Contains(tagNode.Id))
                return false;

            EdgeRuleConditionType conditionType = ResolveConditionType(conditionNode);
            FlowRuleNode? durationNode = nodes.FirstOrDefault(node => descendants.Contains(node.Id) && IsDurationNode(node));
            FlowRuleNode? mqttNode = nodes.FirstOrDefault(node => descendants.Contains(node.Id) && IsMqttNode(node));
            List<EdgeRuleActionConfig> actions = BuildActions(nodes.Where(node => descendants.Contains(node.Id)).ToList());
            FlowRuleNode? transformNode = nodes.FirstOrDefault(node => ancestors.Contains(node.Id) && IsTransformNode(node));
            FlowRuleNode? qualityNode = nodes.FirstOrDefault(node => ancestors.Contains(node.Id) && IsQualityGateNode(node));
            FlowRuleNode? lifecycleNode = nodes.FirstOrDefault(node => descendants.Contains(node.Id) && IsAlarmLifecycleNode(node));
            FlowRuleNode? actionPolicyNode = nodes.FirstOrDefault(node => descendants.Contains(node.Id) && IsActionPolicyNode(node));
            string id = string.IsNullOrWhiteSpace(definition.CompiledRuleId)
                ? Guid.NewGuid().ToString("N")
                : definition.CompiledRuleId;

            rule = new EdgeRuleConfig
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(definition.Name) ? "Flow Rule" : definition.Name,
                Enabled = definition.Enabled,
                ConditionType = conditionType,
                SourceChannelId = FirstText(conditionNode.ChannelId, tagNode.ChannelId),
                SourceChannelName = FirstText(conditionNode.ChannelName, tagNode.ChannelName),
                SourceDeviceId = FirstText(conditionNode.DeviceId, tagNode.DeviceId),
                SourceGroupId = FirstText(conditionNode.GroupId, tagNode.GroupId),
                SourceTagId = FirstText(conditionNode.TagId, tagNode.TagId),
                SourcePointCode = FirstText(conditionNode.PointCode, tagNode.PointCode),
                SourceDeviceName = FirstText(conditionNode.DeviceName, tagNode.DeviceName),
                SourceGroupName = FirstText(conditionNode.GroupName, tagNode.GroupName),
                SourceTagName = FirstText(conditionNode.TagName, tagNode.TagName),
                SourceDataType = FirstText(conditionNode.DataType, tagNode.DataType),
                LowLimit = conditionNode.LowLimit,
                HighLimit = conditionNode.HighLimit,
                Deadband = conditionNode.Deadband,
                RateLimitPerSecond = conditionNode.RateLimitPerSecond,
                Operator = ParseEnum(conditionNode.Operator, EdgeRuleComparisonOperator.GreaterThan),
                CompareValue = conditionNode.CompareValue,
                LogicalOperator = ParseEnum(conditionNode.LogicalOperator, EdgeRuleLogicalOperator.And),
                DurationSeconds = durationNode == null
                    ? Math.Max(0, conditionNode.DurationSeconds)
                    : Math.Max(0, durationNode.DurationSeconds),
                PublishToMqtt = mqttNode == null ? conditionNode.PublishToMqtt : mqttNode.PublishToMqtt,
                PublishOnClear = mqttNode == null ? conditionNode.PublishOnClear : mqttNode.PublishOnClear,
                PublishTopicTemplate = FirstText(mqttNode == null ? string.Empty : mqttNode.TopicTemplate, conditionNode.TopicTemplate, "ipc/rule/{pointCode}/{ruleName}"),
                PublishQos = mqttNode == null ? conditionNode.PublishQos : mqttNode.PublishQos,
                ActiveMessage = FirstText(mqttNode == null ? string.Empty : mqttNode.ActiveMessage, conditionNode.ActiveMessage),
                ClearMessage = FirstText(mqttNode == null ? string.Empty : mqttNode.ClearMessage, conditionNode.ClearMessage),
                Description = definition.Description,
                HysteresisMode = conditionNode.HysteresisMode,
                HysteresisOnValue = conditionNode.HysteresisOnValue,
                HysteresisOffValue = conditionNode.HysteresisOffValue,
                Expression = conditionNode.Expression,
                AlarmLevels = CloneAlarmLevels(conditionNode.AlarmLevels),
                QualityOperator = qualityNode == null ? conditionNode.QualityOperator : qualityNode.QualityOperator,
                QualityValues = qualityNode == null ? conditionNode.QualityValues : qualityNode.QualityValues,
                WindowStatistic = conditionNode.WindowStatistic,
                WindowSeconds = conditionNode.WindowSeconds,
                WindowSampleCount = conditionNode.WindowSampleCount,
                AggregationStatistic = conditionNode.AggregationStatistic,
                TrendMode = conditionNode.TrendMode,
                TrendWindowSeconds = conditionNode.TrendWindowSeconds,
                TrendSampleCount = conditionNode.TrendSampleCount,
                TrendMinSlopePerSecond = conditionNode.TrendMinSlopePerSecond,
                TrendChangeThreshold = conditionNode.TrendChangeThreshold,
                TrendStableDeadband = conditionNode.TrendStableDeadband,
                StateName = conditionNode.StateName,
                StateExpectedValue = conditionNode.StateExpectedValue,
                StateClearValue = conditionNode.StateClearValue,
                StateTimeoutSeconds = conditionNode.StateTimeoutSeconds,
                RelatedChannelId = conditionNode.RelatedChannelId,
                RelatedChannelName = conditionNode.RelatedChannelName,
                RelatedDeviceId = conditionNode.RelatedDeviceId,
                RelatedGroupId = conditionNode.RelatedGroupId,
                RelatedTagId = conditionNode.RelatedTagId,
                RelatedDeviceName = conditionNode.RelatedDeviceName,
                RelatedGroupName = conditionNode.RelatedGroupName,
                RelatedTagName = conditionNode.RelatedTagName,
                RelatedPointCode = conditionNode.RelatedPointCode,
                RelatedDataType = conditionNode.RelatedDataType,
                RelationOperator = ParseEnum(conditionNode.RelationOperator, EdgeRuleComparisonOperator.GreaterThan),
                RelationMultiplier = conditionNode.RelationMultiplier,
                RelationOffset = conditionNode.RelationOffset,
                ContextName = conditionNode.ContextName,
                ContextExpectedValue = conditionNode.ContextExpectedValue,
                ContextOperator = ParseEnum(conditionNode.ContextOperator, EdgeRuleComparisonOperator.Equal),
                ContextChannelId = conditionNode.ContextChannelId,
                ContextChannelName = conditionNode.ContextChannelName,
                ContextDeviceId = conditionNode.ContextDeviceId,
                ContextGroupId = conditionNode.ContextGroupId,
                ContextTagId = conditionNode.ContextTagId,
                ContextDeviceName = conditionNode.ContextDeviceName,
                ContextGroupName = conditionNode.ContextGroupName,
                ContextTagName = conditionNode.ContextTagName,
                ContextPointCode = conditionNode.ContextPointCode,
                ContextDataType = conditionNode.ContextDataType,
                CycleStartValue = conditionNode.CycleStartValue,
                CycleEndValue = conditionNode.CycleEndValue,
                CycleMinSeconds = conditionNode.CycleMinSeconds,
                CycleMaxSeconds = conditionNode.CycleMaxSeconds,
                TaktTargetSeconds = conditionNode.TaktTargetSeconds,
                TaktTolerancePercent = conditionNode.TaktTolerancePercent,
                AnomalyMode = conditionNode.AnomalyMode,
                AnomalyThreshold = conditionNode.AnomalyThreshold,
                AnomalyBaselineWindowSeconds = conditionNode.AnomalyBaselineWindowSeconds,
                AnomalyBaselineSampleCount = conditionNode.AnomalyBaselineSampleCount,
                ModelPurpose = conditionNode.ModelPurpose,
                ModelPath = conditionNode.ModelPath,
                ModelInputTags = conditionNode.ModelInputTags,
                ModelInputName = conditionNode.ModelInputName,
                ModelInputNames = conditionNode.ModelInputNames,
                ModelOutputName = conditionNode.ModelOutputName,
                ModelOutputIndex = Math.Max(0, conditionNode.ModelOutputIndex),
                ModelOperator = ParseEnum(conditionNode.ModelOperator, EdgeRuleComparisonOperator.GreaterThanOrEqual),
                ModelThreshold = conditionNode.ModelThreshold,
                ModelTimeoutMilliseconds = NormalizeModelTimeout(conditionNode.ModelTimeoutMilliseconds),
                AlarmSeverity = lifecycleNode == null ? conditionNode.AlarmSeverity : lifecycleNode.AlarmSeverity,
                AlarmSuppressSeconds = lifecycleNode == null ? conditionNode.AlarmSuppressSeconds : lifecycleNode.AlarmSuppressSeconds,
                AlarmReTriggerSeconds = lifecycleNode == null ? conditionNode.AlarmReTriggerSeconds : lifecycleNode.AlarmReTriggerSeconds,
                AlarmEscalateAfterSeconds = lifecycleNode == null ? conditionNode.AlarmEscalateAfterSeconds : lifecycleNode.AlarmEscalateAfterSeconds,
                ActionDelaySeconds = actionPolicyNode == null ? conditionNode.ActionDelaySeconds : actionPolicyNode.ActionDelaySeconds,
                ActionCooldownSeconds = actionPolicyNode == null ? conditionNode.ActionCooldownSeconds : actionPolicyNode.ActionCooldownSeconds,
                ActionMaxPerMinute = actionPolicyNode == null ? conditionNode.ActionMaxPerMinute : actionPolicyNode.ActionMaxPerMinute,
                TransformMultiplier = transformNode == null ? 1D : transformNode.TransformMultiplier,
                TransformOffset = transformNode == null ? 0D : transformNode.TransformOffset,
                TransformUseAbsolute = transformNode != null && transformNode.TransformUseAbsolute,
                TransformExpression = transformNode == null ? string.Empty : transformNode.TransformExpression,
                TransformTimeoutMilliseconds = transformNode == null ? 50 : NormalizeTransformTimeout(transformNode.TransformTimeoutMilliseconds),
                ClearDurationSeconds = durationNode == null ? Math.Max(0, conditionNode.ClearDurationSeconds) : Math.Max(0, durationNode.ClearDurationSeconds),
                Actions = actions
            };

            return true;
        }

        public static void SyncCompiledRule(ProjectConfig project, FlowRuleDefinition definition, string previousCompiledRuleId)
        {
            if (project == null || definition == null)
                return;

            if (project.Rules == null)
                project.Rules = new List<EdgeRuleConfig>();

            if (string.Equals(definition.LifecycleState, FlowRuleLifecycleStates.Archived, StringComparison.OrdinalIgnoreCase))
            {
                RemoveRule(project, previousCompiledRuleId);
                definition.Mode = FlowRuleModes.Flow;
                definition.CompiledRuleId = string.Empty;
                return;
            }

            EdgeRuleConfig? compiledRule;
            bool compiled = TryCompile(definition, out compiledRule);
            if (!compiled)
            {
                RemoveRule(project, previousCompiledRuleId);
                definition.Mode = FlowRuleModes.Flow;
                definition.CompiledRuleId = string.Empty;
                return;
            }
            if (compiledRule == null)
                return;

            definition.Mode = FlowRuleModes.SimpleCompiled;
            definition.CompiledRuleId = compiledRule.Id;
            if (!string.IsNullOrWhiteSpace(previousCompiledRuleId) &&
                !string.Equals(previousCompiledRuleId, compiledRule.Id, StringComparison.OrdinalIgnoreCase))
            {
                RemoveRule(project, previousCompiledRuleId);
            }

            EdgeRuleConfig? existing = project.Rules.FirstOrDefault(rule =>
                rule != null && string.Equals(rule.Id, compiledRule.Id, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                project.Rules.Add(compiledRule);
                return;
            }

            int index = project.Rules.IndexOf(existing);
            project.Rules[index] = compiledRule;
        }

        public static void RemoveCompiledRule(ProjectConfig project, FlowRuleDefinition definition)
        {
            if (project == null || definition == null)
                return;
            RemoveRule(project, definition.CompiledRuleId);
        }

        private static void RemoveRule(ProjectConfig project, string ruleId)
        {
            if (project == null || project.Rules == null || string.IsNullOrWhiteSpace(ruleId))
                return;

            EdgeRuleConfig? existing = project.Rules.FirstOrDefault(rule =>
                rule != null && string.Equals(rule.Id, ruleId, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                project.Rules.Remove(existing);
        }

        private static bool IsTagNode(FlowRuleNode? node)
        {
            return IsNodeType(node, FlowRuleNodeTypes.TagInput);
        }

        private static bool IsRuleNode(FlowRuleNode? node)
        {
            return IsNodeType(node, FlowRuleNodeTypes.Condition) ||
                   IsNodeType(node, FlowRuleNodeTypes.Threshold) ||
                   IsNodeType(node, FlowRuleNodeTypes.Deadband) ||
                   IsNodeType(node, FlowRuleNodeTypes.RateOfChange) ||
                   IsNodeType(node, FlowRuleNodeTypes.Hysteresis) ||
                   IsNodeType(node, FlowRuleNodeTypes.MultiLevelAlarm) ||
                   IsNodeType(node, FlowRuleNodeTypes.Expression) ||
                   IsNodeType(node, FlowRuleNodeTypes.QualityGate) ||
                   IsNodeType(node, FlowRuleNodeTypes.SlidingWindow) ||
                   IsNodeType(node, FlowRuleNodeTypes.Aggregation) ||
                   IsNodeType(node, FlowRuleNodeTypes.WindowCalculation) ||
                   IsNodeType(node, FlowRuleNodeTypes.Trend) ||
                   IsNodeType(node, FlowRuleNodeTypes.StateMachine) ||
                   IsNodeType(node, FlowRuleNodeTypes.CycleTime) ||
                   IsNodeType(node, FlowRuleNodeTypes.ProcessTakt) ||
                   IsNodeType(node, FlowRuleNodeTypes.AnomalyDetection) ||
                   IsNodeType(node, FlowRuleNodeTypes.ModelInference) ||
                   IsNodeType(node, FlowRuleNodeTypes.TagRelation) ||
                   IsNodeType(node, FlowRuleNodeTypes.ContextGate);
        }

        private static bool IsLogicNode(FlowRuleNode? node)
        {
            return IsNodeType(node, FlowRuleNodeTypes.Logic);
        }

        private static bool IsDurationNode(FlowRuleNode? node)
        {
            return IsNodeType(node, FlowRuleNodeTypes.Duration);
        }

        private static bool IsTransformNode(FlowRuleNode? node)
        {
            return IsNodeType(node, FlowRuleNodeTypes.Transform) ||
                   IsNodeType(node, FlowRuleNodeTypes.Function);
        }

        private static bool IsQualityGateNode(FlowRuleNode? node)
        {
            return IsNodeType(node, FlowRuleNodeTypes.QualityGate);
        }

        private static bool IsAlarmLifecycleNode(FlowRuleNode? node)
        {
            return IsNodeType(node, FlowRuleNodeTypes.AlarmLifecycle);
        }

        private static bool IsActionPolicyNode(FlowRuleNode? node)
        {
            return IsNodeType(node, FlowRuleNodeTypes.ActionPolicy);
        }

        private static bool IsSequenceNode(FlowRuleNode? node)
        {
            return IsNodeType(node, FlowRuleNodeTypes.Sequence);
        }

        private static bool IsMqttNode(FlowRuleNode? node)
        {
            return IsNodeType(node, FlowRuleNodeTypes.MqttPublish);
        }

        private static bool IsActionNode(FlowRuleNode? node)
        {
            return IsNodeType(node, FlowRuleNodeTypes.MqttPublish) ||
                   IsNodeType(node, FlowRuleNodeTypes.EmailNotify) ||
                   IsNodeType(node, FlowRuleNodeTypes.WebhookCall) ||
                   IsNodeType(node, FlowRuleNodeTypes.DebugProbe);
        }

        private static bool IsNodeType(FlowRuleNode? node, string type)
        {
            return node != null && string.Equals(node.NodeType, type, StringComparison.OrdinalIgnoreCase);
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

        private static EdgeRuleConditionType ResolveConditionType(FlowRuleNode node)
        {
            if (IsNodeType(node, FlowRuleNodeTypes.Threshold))
                return EdgeRuleConditionType.Threshold;
            if (IsNodeType(node, FlowRuleNodeTypes.Deadband))
                return EdgeRuleConditionType.Deadband;
            if (IsNodeType(node, FlowRuleNodeTypes.RateOfChange))
                return EdgeRuleConditionType.RateOfChange;
            if (IsNodeType(node, FlowRuleNodeTypes.Hysteresis))
                return EdgeRuleConditionType.Hysteresis;
            if (IsNodeType(node, FlowRuleNodeTypes.MultiLevelAlarm))
                return EdgeRuleConditionType.MultiLevelAlarm;
            if (IsNodeType(node, FlowRuleNodeTypes.Expression))
                return EdgeRuleConditionType.Expression;
            if (IsNodeType(node, FlowRuleNodeTypes.QualityGate))
                return EdgeRuleConditionType.QualityGate;
            if (IsNodeType(node, FlowRuleNodeTypes.SlidingWindow))
                return EdgeRuleConditionType.SlidingWindow;
            if (IsNodeType(node, FlowRuleNodeTypes.Aggregation))
                return EdgeRuleConditionType.Aggregation;
            if (IsNodeType(node, FlowRuleNodeTypes.WindowCalculation))
                return EdgeRuleConditionType.WindowCalculation;
            if (IsNodeType(node, FlowRuleNodeTypes.Trend))
                return EdgeRuleConditionType.Trend;
            if (IsNodeType(node, FlowRuleNodeTypes.StateMachine))
                return EdgeRuleConditionType.StateMachine;
            if (IsNodeType(node, FlowRuleNodeTypes.CycleTime))
                return EdgeRuleConditionType.CycleTime;
            if (IsNodeType(node, FlowRuleNodeTypes.ProcessTakt))
                return EdgeRuleConditionType.ProcessTakt;
            if (IsNodeType(node, FlowRuleNodeTypes.AnomalyDetection))
                return EdgeRuleConditionType.AnomalyDetection;
            if (IsNodeType(node, FlowRuleNodeTypes.ModelInference))
                return EdgeRuleConditionType.ModelInference;
            if (IsNodeType(node, FlowRuleNodeTypes.TagRelation))
                return EdgeRuleConditionType.TagRelation;
            if (IsNodeType(node, FlowRuleNodeTypes.ContextGate))
                return EdgeRuleConditionType.ContextGate;
            return ParseEnum(node.ConditionType, EdgeRuleConditionType.Condition);
        }

        private static List<EdgeRuleAlarmLevelConfig> CloneAlarmLevels(List<FlowRuleAlarmLevel>? source)
        {
            List<EdgeRuleAlarmLevelConfig> target = new List<EdgeRuleAlarmLevelConfig>();
            if (source == null)
                return target;

            for (int i = 0; i < source.Count; i++)
            {
                FlowRuleAlarmLevel? level = source[i];
                if (level == null)
                    continue;
                target.Add(new EdgeRuleAlarmLevelConfig
                {
                    Id = string.IsNullOrWhiteSpace(level.Id) ? Guid.NewGuid().ToString("N") : level.Id,
                    Name = level.Name,
                    Severity = level.Severity,
                    Operator = ParseEnum(level.Operator, EdgeRuleComparisonOperator.GreaterThanOrEqual),
                    CompareValue = level.CompareValue,
                    Message = level.Message
                });
            }

            return target;
        }

        private static List<EdgeRuleActionConfig> BuildActions(IList<FlowRuleNode>? nodes)
        {
            List<EdgeRuleActionConfig> actions = new List<EdgeRuleActionConfig>();
            if (nodes == null)
                return actions;

            for (int i = 0; i < nodes.Count; i++)
            {
                FlowRuleNode? node = nodes[i];
                if (!IsActionNode(node))
                    continue;

                EdgeRuleActionConfig? action = ToAction(node);
                if (action != null)
                    actions.Add(action);
            }

            return actions;
        }

        private static EdgeRuleActionConfig? ToAction(FlowRuleNode? node)
        {
            if (node == null)
                return null;

            bool isMqtt = IsMqttNode(node);
            bool isDebug = IsNodeType(node, FlowRuleNodeTypes.DebugProbe);
            return new EdgeRuleActionConfig
            {
                Id = string.IsNullOrWhiteSpace(node.Id) ? Guid.NewGuid().ToString("N") : node.Id,
                ActionType = node.NodeType,
                Enabled = isMqtt ? node.PublishToMqtt : !isDebug || node.DebugEnabled,
                ExecuteOnActive = isMqtt ? node.PublishToMqtt : node.ExecuteOnActive,
                ExecuteOnClear = isMqtt ? node.PublishToMqtt && node.PublishOnClear : node.ExecuteOnClear,
                TopicTemplate = FirstText(node.TopicTemplate, "ipc/rule/{pointCode}/{ruleName}"),
                Qos = node.PublishQos,
                ActiveMessage = node.ActiveMessage,
                ClearMessage = node.ClearMessage,
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
                WebhookRetryCount = node.WebhookRetryCount,
                DebugLabel = node.DebugLabel
            };
        }

        private static TEnum ParseEnum<TEnum>(string? value, TEnum defaultValue) where TEnum : struct
        {
            return Enum.TryParse(value, true, out TEnum parsed) ? parsed : defaultValue;
        }

        private static string FirstText(params string?[] values)
        {
            if (values == null)
                return string.Empty;

            for (int i = 0; i < values.Length; i++)
            {
                string? value = values[i];
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return string.Empty;
        }

        private static List<FlowRuleNode> MaterializeNodes(IList<FlowRuleNode>? source)
        {
            List<FlowRuleNode> nodes = new List<FlowRuleNode>();
            if (source == null)
                return nodes;

            for (int i = 0; i < source.Count; i++)
            {
                FlowRuleNode? node = source[i];
                if (node != null)
                    nodes.Add(node);
            }

            return nodes;
        }
    }
}

/*----------------------------------------------------------------
* 项目名称 ：IPC.EdgeGateway
* 项目描述 ：
* 类 名 称 ：FlowRuleEngineService
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.EdgeGateway
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
using System.Linq;
using IPC.Runtime.Configuration;

namespace IPC.EdgeGateway
{
    public sealed partial class FlowRuleEngineService
    {
        private ProjectConfig BuildRuntimeProject()
        {
            ProjectConfig runtimeProject = new ProjectConfig
            {
                ProjectId = _projectConfig == null ? string.Empty : _projectConfig.ProjectId,
                Name = _projectConfig == null ? string.Empty : _projectConfig.Name,
                Devices = new List<DeviceConfig>(),
                Rules = new List<EdgeRuleConfig>(),
                FlowRules = new List<FlowRuleDefinition>()
            };

            if (_projectConfig == null || _projectConfig.FlowRules == null)
                return runtimeProject;

            for (int i = 0; i < _projectConfig.FlowRules.Count; i++)
            {
                FlowRuleDefinition? flowRule = _projectConfig.FlowRules[i];
                EdgeRuleConfig? compiledProjectRule = FindCompiledProjectRule(flowRule);
                if (compiledProjectRule != null)
                {
                    AddRuntimeRule(runtimeProject.Rules, compiledProjectRule);
                    continue;
                }

                EdgeRuleConfig? compiled = CompileFlowRule(flowRule);
                AddRuntimeRule(runtimeProject.Rules, compiled);
            }

            return runtimeProject;
        }

        private EdgeRuleConfig? FindCompiledProjectRule(FlowRuleDefinition? flowRule)
        {
            if (flowRule == null ||
                !flowRule.Enabled ||
                string.Equals(flowRule.LifecycleState, FlowRuleLifecycleStates.Archived, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(flowRule.Mode, FlowRuleModes.SimpleCompiled, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(flowRule.CompiledRuleId) ||
                _projectConfig == null ||
                _projectConfig.Rules == null)
                return null;

            return _projectConfig.Rules.FirstOrDefault(rule =>
                rule != null &&
                rule.Enabled &&
                string.Equals(rule.Id, flowRule.CompiledRuleId, StringComparison.OrdinalIgnoreCase));
        }

        private static void AddRuntimeRule(IList<EdgeRuleConfig> rules, EdgeRuleConfig? rule)
        {
            if (rules == null || rule == null || !rule.Enabled)
                return;

            if (!string.IsNullOrWhiteSpace(rule.Id) &&
                rules.Any(item => item != null && string.Equals(item.Id, rule.Id, StringComparison.OrdinalIgnoreCase)))
                return;

            rules.Add(rule);
        }

        private EdgeRuleConfig? CompileFlowRule(FlowRuleDefinition? flowRule)
        {
            if (flowRule == null ||
                !flowRule.Enabled ||
                string.Equals(flowRule.LifecycleState, FlowRuleLifecycleStates.Archived, StringComparison.OrdinalIgnoreCase))
                return null;

            if (string.Equals(flowRule.Mode, FlowRuleModes.SimpleCompiled, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(flowRule.CompiledRuleId))
            {
                return null;
            }

            if (FlowRuleGraphValidator.Validate(flowRule).Count > 0)
                return null;

            FlowRuleGraphMap graph = new FlowRuleGraphMap(flowRule);
            List<FlowRuleNode> nodes = MaterializeFlowNodes(flowRule.Nodes);
            FlowRuleNode? sequenceNode = nodes.FirstOrDefault(IsSequenceNode);
            FlowRuleNode? logicNode = nodes.FirstOrDefault(IsLogicNode);
            FlowRuleNode? resultNode = sequenceNode ?? logicNode;
            List<FlowRuleNode> conditionNodes = resultNode == null
                ? nodes.Where(IsConditionLikeNode).ToList()
                : ResolveDirectConditionNodes(resultNode, graph);
            if (resultNode == null && conditionNodes.Count == 0)
                conditionNodes = nodes.Where(IsQualityGateNode).ToList();
            if (conditionNodes.Count == 0)
                return null;

            if (resultNode == null && conditionNodes.Count != 1)
                return null;

            resultNode ??= conditionNodes[0];
            HashSet<string> descendants = graph.GetDescendantIds(resultNode.Id);

            FlowRuleNode firstCondition = conditionNodes[0];
            FlowRuleNode firstSource = ResolveSourceNode(firstCondition, nodes, flowRule.Edges) ?? firstCondition;
            FlowRuleNode? firstTransform = ResolveTransformNode(firstCondition, nodes, flowRule.Edges);
            FlowRuleNode? firstQualityGate = IsQualityGateNode(firstCondition)
                ? firstCondition
                : ResolveQualityGateNode(firstCondition, nodes, flowRule.Edges);
            FlowRuleNode? mqttNode = nodes.FirstOrDefault(node => descendants.Contains(node.Id) && IsMqttNode(node));
            FlowRuleNode? durationNode = nodes.FirstOrDefault(node => descendants.Contains(node.Id) && IsDurationNode(node));
            FlowRuleNode? lifecycleNode = nodes.FirstOrDefault(node => descendants.Contains(node.Id) && IsAlarmLifecycleNode(node));
            FlowRuleNode? actionPolicyNode = nodes.FirstOrDefault(node => descendants.Contains(node.Id) && IsActionPolicyNode(node));
            List<EdgeRuleActionConfig> actions = BuildActions(nodes.Where(node => descendants.Contains(node.Id)).ToList());

            EdgeRuleConfig rule = new EdgeRuleConfig
            {
                Id = flowRule.Id,
                Name = flowRule.Name,
                Enabled = flowRule.Enabled,
                Description = flowRule.Description,
                ConditionType = sequenceNode != null
                    ? EdgeRuleConditionType.Sequence
                    : conditionNodes.Count > 1 ? EdgeRuleConditionType.Combination : ResolveConditionType(firstCondition),
                SourceChannelId = FirstText(firstCondition.ChannelId, firstSource.ChannelId),
                SourceChannelName = FirstText(firstCondition.ChannelName, firstSource.ChannelName),
                SourceDeviceId = FirstText(firstCondition.DeviceId, firstSource.DeviceId),
                SourceGroupId = FirstText(firstCondition.GroupId, firstSource.GroupId),
                SourceTagId = FirstText(firstCondition.TagId, firstSource.TagId),
                SourcePointCode = FirstText(firstCondition.PointCode, firstSource.PointCode),
                SourceDeviceName = FirstText(firstCondition.DeviceName, firstSource.DeviceName),
                SourceGroupName = FirstText(firstCondition.GroupName, firstSource.GroupName),
                SourceTagName = FirstText(firstCondition.TagName, firstSource.TagName),
                SourceDataType = FirstText(firstCondition.DataType, firstSource.DataType),
                LowLimit = firstCondition.LowLimit,
                HighLimit = firstCondition.HighLimit,
                Deadband = firstCondition.Deadband,
                RateLimitPerSecond = firstCondition.RateLimitPerSecond,
                Operator = ParseEnum(firstCondition.Operator, EdgeRuleComparisonOperator.GreaterThan),
                CompareValue = firstCondition.CompareValue,
                LogicalOperator = ParseEnum(logicNode == null ? firstCondition.LogicalOperator : logicNode.LogicalOperator, EdgeRuleLogicalOperator.And),
                DurationSeconds = durationNode == null ? Math.Max(0, firstCondition.DurationSeconds) : Math.Max(0, durationNode.DurationSeconds),
                PublishToMqtt = mqttNode == null ? firstCondition.PublishToMqtt : mqttNode.PublishToMqtt,
                PublishOnClear = mqttNode == null ? firstCondition.PublishOnClear : mqttNode.PublishOnClear,
                PublishTopicTemplate = FirstText(mqttNode == null ? string.Empty : mqttNode.TopicTemplate, firstCondition.TopicTemplate, "ipc/rule/{pointCode}/{ruleName}"),
                PublishQos = mqttNode == null ? firstCondition.PublishQos : mqttNode.PublishQos,
                ActiveMessage = FirstText(mqttNode == null ? string.Empty : mqttNode.ActiveMessage, firstCondition.ActiveMessage),
                ClearMessage = FirstText(mqttNode == null ? string.Empty : mqttNode.ClearMessage, firstCondition.ClearMessage),
                HysteresisMode = firstCondition.HysteresisMode,
                HysteresisOnValue = firstCondition.HysteresisOnValue,
                HysteresisOffValue = firstCondition.HysteresisOffValue,
                Expression = firstCondition.Expression,
                AlarmLevels = CloneAlarmLevels(firstCondition.AlarmLevels),
                QualityOperator = firstQualityGate == null ? firstCondition.QualityOperator : firstQualityGate.QualityOperator,
                QualityValues = firstQualityGate == null ? firstCondition.QualityValues : firstQualityGate.QualityValues,
                WindowStatistic = firstCondition.WindowStatistic,
                WindowSeconds = firstCondition.WindowSeconds,
                WindowSampleCount = firstCondition.WindowSampleCount,
                AggregationStatistic = firstCondition.AggregationStatistic,
                TrendMode = firstCondition.TrendMode,
                TrendWindowSeconds = firstCondition.TrendWindowSeconds,
                TrendSampleCount = firstCondition.TrendSampleCount,
                TrendMinSlopePerSecond = firstCondition.TrendMinSlopePerSecond,
                TrendChangeThreshold = firstCondition.TrendChangeThreshold,
                TrendStableDeadband = firstCondition.TrendStableDeadband,
                StateName = firstCondition.StateName,
                StateExpectedValue = firstCondition.StateExpectedValue,
                StateClearValue = firstCondition.StateClearValue,
                StateTimeoutSeconds = firstCondition.StateTimeoutSeconds,
                RelatedChannelId = firstCondition.RelatedChannelId,
                RelatedChannelName = firstCondition.RelatedChannelName,
                RelatedDeviceId = firstCondition.RelatedDeviceId,
                RelatedGroupId = firstCondition.RelatedGroupId,
                RelatedTagId = firstCondition.RelatedTagId,
                RelatedDeviceName = firstCondition.RelatedDeviceName,
                RelatedGroupName = firstCondition.RelatedGroupName,
                RelatedTagName = firstCondition.RelatedTagName,
                RelatedPointCode = firstCondition.RelatedPointCode,
                RelatedDataType = firstCondition.RelatedDataType,
                RelationOperator = ParseEnum(firstCondition.RelationOperator, EdgeRuleComparisonOperator.GreaterThan),
                RelationMultiplier = firstCondition.RelationMultiplier,
                RelationOffset = firstCondition.RelationOffset,
                ContextName = firstCondition.ContextName,
                ContextExpectedValue = firstCondition.ContextExpectedValue,
                ContextOperator = ParseEnum(firstCondition.ContextOperator, EdgeRuleComparisonOperator.Equal),
                ContextChannelId = firstCondition.ContextChannelId,
                ContextChannelName = firstCondition.ContextChannelName,
                ContextDeviceId = firstCondition.ContextDeviceId,
                ContextGroupId = firstCondition.ContextGroupId,
                ContextTagId = firstCondition.ContextTagId,
                ContextDeviceName = firstCondition.ContextDeviceName,
                ContextGroupName = firstCondition.ContextGroupName,
                ContextTagName = firstCondition.ContextTagName,
                ContextPointCode = firstCondition.ContextPointCode,
                ContextDataType = firstCondition.ContextDataType,
                CycleStartValue = firstCondition.CycleStartValue,
                CycleEndValue = firstCondition.CycleEndValue,
                CycleMinSeconds = firstCondition.CycleMinSeconds,
                CycleMaxSeconds = firstCondition.CycleMaxSeconds,
                TaktTargetSeconds = firstCondition.TaktTargetSeconds,
                TaktTolerancePercent = firstCondition.TaktTolerancePercent,
                AnomalyMode = firstCondition.AnomalyMode,
                AnomalyThreshold = firstCondition.AnomalyThreshold,
                AnomalyBaselineWindowSeconds = firstCondition.AnomalyBaselineWindowSeconds,
                AnomalyBaselineSampleCount = firstCondition.AnomalyBaselineSampleCount,
                ModelPurpose = firstCondition.ModelPurpose,
                ModelPath = firstCondition.ModelPath,
                ModelInputTags = firstCondition.ModelInputTags,
                ModelInputName = firstCondition.ModelInputName,
                ModelInputNames = firstCondition.ModelInputNames,
                ModelOutputName = firstCondition.ModelOutputName,
                ModelOutputIndex = Math.Max(0, firstCondition.ModelOutputIndex),
                ModelOperator = ParseEnum(firstCondition.ModelOperator, EdgeRuleComparisonOperator.GreaterThanOrEqual),
                ModelThreshold = firstCondition.ModelThreshold,
                ModelTimeoutMilliseconds = NormalizeModelTimeout(firstCondition.ModelTimeoutMilliseconds),
                AlarmSeverity = lifecycleNode == null ? firstCondition.AlarmSeverity : lifecycleNode.AlarmSeverity,
                AlarmSuppressSeconds = lifecycleNode == null ? firstCondition.AlarmSuppressSeconds : lifecycleNode.AlarmSuppressSeconds,
                AlarmReTriggerSeconds = lifecycleNode == null ? firstCondition.AlarmReTriggerSeconds : lifecycleNode.AlarmReTriggerSeconds,
                AlarmEscalateAfterSeconds = lifecycleNode == null ? firstCondition.AlarmEscalateAfterSeconds : lifecycleNode.AlarmEscalateAfterSeconds,
                ActionDelaySeconds = actionPolicyNode == null ? firstCondition.ActionDelaySeconds : actionPolicyNode.ActionDelaySeconds,
                ActionCooldownSeconds = actionPolicyNode == null ? firstCondition.ActionCooldownSeconds : actionPolicyNode.ActionCooldownSeconds,
                ActionMaxPerMinute = actionPolicyNode == null ? firstCondition.ActionMaxPerMinute : actionPolicyNode.ActionMaxPerMinute,
                TransformMultiplier = firstTransform == null ? 1D : firstTransform.TransformMultiplier,
                TransformOffset = firstTransform == null ? 0D : firstTransform.TransformOffset,
                TransformUseAbsolute = firstTransform != null && firstTransform.TransformUseAbsolute,
                TransformExpression = firstTransform == null ? string.Empty : firstTransform.TransformExpression,
                TransformTimeoutMilliseconds = firstTransform == null ? 50 : NormalizeTransformTimeout(firstTransform.TransformTimeoutMilliseconds),
                SequenceWindowSeconds = sequenceNode == null ? 60 : Math.Max(1, sequenceNode.SequenceWindowSeconds),
                SequenceStepTimeoutSeconds = sequenceNode == null ? 0 : Math.Max(0, sequenceNode.SequenceStepTimeoutSeconds),
                SequenceMinIntervalSeconds = sequenceNode == null ? 0 : Math.Max(0, sequenceNode.SequenceMinIntervalSeconds),
                SequenceResetOnMismatch = sequenceNode == null || sequenceNode.SequenceResetOnMismatch,
                ClearDurationSeconds = durationNode == null ? Math.Max(0, firstCondition.ClearDurationSeconds) : Math.Max(0, durationNode.ClearDurationSeconds),
                Actions = actions
            };

            if (rule.ConditionType == EdgeRuleConditionType.Combination || rule.ConditionType == EdgeRuleConditionType.Sequence)
                rule.Conditions = BuildConditions(conditionNodes, nodes, flowRule.Edges);

            return rule;
        }

        private static List<FlowRuleNode> ResolveDirectConditionNodes(
            FlowRuleNode resultNode,
            FlowRuleGraphMap graph)
        {
            if (resultNode == null || graph == null)
                return new List<FlowRuleNode>();

            List<FlowRuleNode> conditions = new List<FlowRuleNode>();
            IList<FlowRuleEdge> incoming = graph.GetIncomingEdges(resultNode.Id);
            for (int i = 0; i < incoming.Count; i++)
            {
                FlowRuleNode? source = graph.FindNode(incoming[i].SourceNodeId);
                if (IsConditionLikeNode(source))
                    conditions.Add(source!);
            }

            return conditions;
        }

        private static List<EdgeRuleConditionConfig> BuildConditions(
            IList<FlowRuleNode>? conditionNodes,
            IList<FlowRuleNode>? allNodes,
            IList<FlowRuleEdge>? edges)
        {
            List<EdgeRuleConditionConfig> conditions = new List<EdgeRuleConditionConfig>();
            if (conditionNodes == null)
                return conditions;

            for (int i = 0; i < conditionNodes.Count; i++)
            {
                FlowRuleNode? conditionNode = conditionNodes[i];
                if (conditionNode == null)
                    continue;
                FlowRuleNode sourceNode = ResolveSourceNode(conditionNode, allNodes, edges) ?? conditionNode;
                FlowRuleNode? transformNode = ResolveTransformNode(conditionNode, allNodes, edges);
                conditions.Add(new EdgeRuleConditionConfig
                {
                    Id = string.IsNullOrWhiteSpace(conditionNode.Id) ? Guid.NewGuid().ToString("N") : conditionNode.Id,
                    SourceChannelId = FirstText(conditionNode.ChannelId, sourceNode.ChannelId),
                    SourceChannelName = FirstText(conditionNode.ChannelName, sourceNode.ChannelName),
                    SourceDeviceId = FirstText(conditionNode.DeviceId, sourceNode.DeviceId),
                    SourceGroupId = FirstText(conditionNode.GroupId, sourceNode.GroupId),
                    SourceTagId = FirstText(conditionNode.TagId, sourceNode.TagId),
                    SourcePointCode = FirstText(conditionNode.PointCode, sourceNode.PointCode),
                    SourceDeviceName = FirstText(conditionNode.DeviceName, sourceNode.DeviceName),
                    SourceGroupName = FirstText(conditionNode.GroupName, sourceNode.GroupName),
                    SourceTagName = FirstText(conditionNode.TagName, sourceNode.TagName),
                    SourceDataType = FirstText(conditionNode.DataType, sourceNode.DataType),
                    Operator = ParseEnum(conditionNode.Operator, EdgeRuleComparisonOperator.GreaterThan),
                    CompareValue = conditionNode.CompareValue,
                    TransformMultiplier = transformNode == null ? 1D : transformNode.TransformMultiplier,
                    TransformOffset = transformNode == null ? 0D : transformNode.TransformOffset,
                    TransformUseAbsolute = transformNode != null && transformNode.TransformUseAbsolute,
                    TransformExpression = transformNode == null ? string.Empty : transformNode.TransformExpression
                });
            }

            return conditions;
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

        private static List<FlowRuleNode> MaterializeFlowNodes(IList<FlowRuleNode>? source)
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

/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Configuration
* 项目描述 ：
* 类 名 称 ：ProjectConfigStore
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
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IPC.Runtime.Configuration
{
    
    
    
    
    
    
    
    
    
    public sealed class ProjectConfigStore
    {
        private readonly JsonSerializerOptions _jsonOptions;

        public ProjectConfigStore()
        {
            _jsonOptions = CreateJsonOptions();
        }

        public ProjectConfig LoadOrCreate(string path, Func<ProjectConfig> defaultFactory)
        {
            string resolvedPath = ResolvePath(path);
            if (File.Exists(resolvedPath))
                return Load(resolvedPath);

            ProjectConfig config = defaultFactory == null ? new ProjectConfig() : defaultFactory();
            Save(resolvedPath, config);
            return config;
        }

        public ProjectConfig Load(string path)
        {
            string resolvedPath = ResolvePath(path);
            string json = File.ReadAllText(resolvedPath, Encoding.UTF8);
            ProjectConfig? config = JsonSerializer.Deserialize<ProjectConfig>(json, _jsonOptions);
            if (config == null)
                throw new InvalidOperationException("项目配置文件内容为空。");

            Normalize(config);
            ProjectConfigValidationResult validation = ProjectConfigValidator.Validate(config);
            if (!validation.IsValid)
                throw new InvalidOperationException("项目配置校验失败：" + string.Join("；", validation.Errors.ToArray()));

            return config;
        }

        public void Save(string path, ProjectConfig config)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            Normalize(config);
            ProjectConfigValidationResult validation = ProjectConfigValidator.Validate(config);
            if (!validation.IsValid)
                throw new InvalidOperationException("项目配置校验失败：" + string.Join("；", validation.Errors.ToArray()));

            string resolvedPath = ResolvePath(path);
            string? directory = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string json = JsonSerializer.Serialize(config, _jsonOptions);
            File.WriteAllText(resolvedPath, json, new UTF8Encoding(false));
        }

        public static void Normalize(ProjectConfig config)
        {
            if (config == null)
                return;

            if (string.IsNullOrWhiteSpace(config.ProjectId))
                config.ProjectId = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(config.Name))
                config.Name = "IPC Gateway";
            if (config.Devices == null)
                config.Devices = new System.Collections.Generic.List<DeviceConfig>();
            if (config.Rules == null)
                config.Rules = new System.Collections.Generic.List<EdgeRuleConfig>();
            if (config.FlowRules == null)
                config.FlowRules = new System.Collections.Generic.List<FlowRuleDefinition>();

            for (int d = 0; d < config.Devices.Count; d++)
            {
                DeviceConfig device = config.Devices[d];
                if (device == null)
                    continue;

                if (string.IsNullOrWhiteSpace(device.Id))
                    device.Id = Guid.NewGuid().ToString("N");
                if (device.Connection == null)
                    device.Connection = new IPC.Plc.Communication.Core.PlcConnectionOptions();
                device.Connection.Protocol = device.Protocol;
                if (device.DefaultScanRateMs <= 0)
                    device.DefaultScanRateMs = 1000;
                if (device.FailureRetryDelayMs <= 0)
                    device.FailureRetryDelayMs = 1000;
                if (device.MaxFailureRetryDelayMs <= 0)
                    device.MaxFailureRetryDelayMs = 30000;
                if (device.MaxFailureRetryDelayMs < device.FailureRetryDelayMs)
                    device.MaxFailureRetryDelayMs = device.FailureRetryDelayMs;
                if (device.Tags == null)
                    device.Tags = new System.Collections.Generic.List<TagConfig>();
                if (device.Groups == null)
                    device.Groups = new System.Collections.Generic.List<GroupConfig>();

                NormalizeTags(device, null, device.Tags);
                for (int g = 0; g < device.Groups.Count; g++)
                {
                    GroupConfig group = device.Groups[g];
                    if (group == null)
                        continue;
                    if (string.IsNullOrWhiteSpace(group.Id))
                        group.Id = Guid.NewGuid().ToString("N");
                    group.DeviceId = device.Id;
                    if (group.Tags == null)
                        group.Tags = new System.Collections.Generic.List<TagConfig>();
                    NormalizeTags(device, group, group.Tags);
                }
            }

            NormalizeRules(config.Rules);
            NormalizeFlowRules(config.FlowRules);
            SyncFlowRuleCompiledRules(config);
        }

        private static void NormalizeRules(System.Collections.Generic.List<EdgeRuleConfig>? rules)
        {
            if (rules == null)
                return;

            for (int i = 0; i < rules.Count; i++)
            {
                EdgeRuleConfig rule = rules[i];
                if (rule == null)
                    continue;
                if (string.IsNullOrWhiteSpace(rule.Id))
                    rule.Id = Guid.NewGuid().ToString("N");
                rule.Name = rule.Name ?? string.Empty;
                rule.SourcePointCode = rule.SourcePointCode ?? string.Empty;
                rule.SourceDeviceName = rule.SourceDeviceName ?? string.Empty;
                rule.SourceGroupName = rule.SourceGroupName ?? string.Empty;
                rule.SourceTagName = rule.SourceTagName ?? string.Empty;
                rule.SourceDataType = rule.SourceDataType ?? string.Empty;
                rule.PublishTopicTemplate = rule.PublishTopicTemplate ?? string.Empty;
                rule.ActiveMessage = rule.ActiveMessage ?? string.Empty;
                rule.ClearMessage = rule.ClearMessage ?? string.Empty;
                rule.Description = rule.Description ?? string.Empty;
                rule.HysteresisMode = string.IsNullOrWhiteSpace(rule.HysteresisMode) ? "High" : rule.HysteresisMode;
                rule.Expression = string.IsNullOrWhiteSpace(rule.Expression) ? "{value} > 0" : rule.Expression;
                rule.QualityOperator = string.IsNullOrWhiteSpace(rule.QualityOperator) ? "In" : rule.QualityOperator;
                rule.QualityValues = string.IsNullOrWhiteSpace(rule.QualityValues) ? "Good" : rule.QualityValues;
                rule.WindowStatistic = string.IsNullOrWhiteSpace(rule.WindowStatistic) ? "Average" : rule.WindowStatistic;
                if (rule.WindowSeconds <= 0)
                    rule.WindowSeconds = 60;
                if (rule.WindowSampleCount < 0)
                    rule.WindowSampleCount = 0;
                rule.StateName = string.IsNullOrWhiteSpace(rule.StateName) ? "State" : rule.StateName;
                rule.StateExpectedValue = string.IsNullOrWhiteSpace(rule.StateExpectedValue) ? "1" : rule.StateExpectedValue;
                rule.StateClearValue = rule.StateClearValue ?? string.Empty;
                if (rule.StateTimeoutSeconds < 0)
                    rule.StateTimeoutSeconds = 0;
                rule.RelatedDeviceName = rule.RelatedDeviceName ?? string.Empty;
                rule.RelatedGroupName = rule.RelatedGroupName ?? string.Empty;
                rule.RelatedTagName = rule.RelatedTagName ?? string.Empty;
                rule.RelatedPointCode = rule.RelatedPointCode ?? string.Empty;
                rule.RelatedDataType = rule.RelatedDataType ?? string.Empty;
                rule.ContextName = string.IsNullOrWhiteSpace(rule.ContextName) ? "Context" : rule.ContextName;
                rule.ContextExpectedValue = rule.ContextExpectedValue ?? string.Empty;
                rule.ContextDeviceName = rule.ContextDeviceName ?? string.Empty;
                rule.ContextGroupName = rule.ContextGroupName ?? string.Empty;
                rule.ContextTagName = rule.ContextTagName ?? string.Empty;
                rule.ContextPointCode = rule.ContextPointCode ?? string.Empty;
                rule.ContextDataType = rule.ContextDataType ?? string.Empty;
                rule.CycleStartValue = string.IsNullOrWhiteSpace(rule.CycleStartValue) ? "1" : rule.CycleStartValue;
                rule.CycleEndValue = string.IsNullOrWhiteSpace(rule.CycleEndValue) ? "0" : rule.CycleEndValue;
                if (rule.CycleMinSeconds < 0)
                    rule.CycleMinSeconds = 0;
                if (rule.CycleMaxSeconds < 0)
                    rule.CycleMaxSeconds = 0;
                rule.AlarmSeverity = string.IsNullOrWhiteSpace(rule.AlarmSeverity) ? "Warning" : rule.AlarmSeverity;
                if (rule.AlarmSuppressSeconds < 0)
                    rule.AlarmSuppressSeconds = 0;
                if (rule.AlarmReTriggerSeconds < 0)
                    rule.AlarmReTriggerSeconds = 0;
                if (rule.AlarmEscalateAfterSeconds < 0)
                    rule.AlarmEscalateAfterSeconds = 0;
                if (rule.ActionDelaySeconds < 0)
                    rule.ActionDelaySeconds = 0;
                if (rule.ActionCooldownSeconds < 0)
                    rule.ActionCooldownSeconds = 0;
                if (rule.ActionMaxPerMinute < 0)
                    rule.ActionMaxPerMinute = 0;
                rule.TransformExpression = rule.TransformExpression ?? string.Empty;
                NormalizeModelInferenceRule(rule);
                if (rule.TransformTimeoutMilliseconds <= 0)
                    rule.TransformTimeoutMilliseconds = 50;
                if (rule.TransformTimeoutMilliseconds > 5000)
                    rule.TransformTimeoutMilliseconds = 5000;
                if (rule.SequenceWindowSeconds <= 0)
                    rule.SequenceWindowSeconds = 60;
                if (rule.SequenceStepTimeoutSeconds < 0)
                    rule.SequenceStepTimeoutSeconds = 0;
                if (rule.SequenceMinIntervalSeconds < 0)
                    rule.SequenceMinIntervalSeconds = 0;
                if (rule.ClearDurationSeconds < 0)
                    rule.ClearDurationSeconds = 0;
                if (rule.AlarmLevels == null)
                    rule.AlarmLevels = new System.Collections.Generic.List<EdgeRuleAlarmLevelConfig>();
                NormalizeAlarmLevels(rule.AlarmLevels);
                if (rule.Actions == null)
                    rule.Actions = new System.Collections.Generic.List<EdgeRuleActionConfig>();
                NormalizeRuleActions(rule.Actions);
                if (rule.Conditions == null)
                    rule.Conditions = new System.Collections.Generic.List<EdgeRuleConditionConfig>();
                NormalizeRuleConditions(rule.Conditions);
            }
        }

        private static void NormalizeRuleActions(System.Collections.Generic.List<EdgeRuleActionConfig>? actions)
        {
            if (actions == null)
                return;

            for (int i = 0; i < actions.Count; i++)
            {
                EdgeRuleActionConfig action = actions[i];
                if (action == null)
                    continue;
                if (string.IsNullOrWhiteSpace(action.Id))
                    action.Id = Guid.NewGuid().ToString("N");
                action.ActionType = string.IsNullOrWhiteSpace(action.ActionType) ? FlowRuleNodeTypes.MqttPublish : action.ActionType;
                if (!action.ExecuteOnActive && !action.ExecuteOnClear)
                    action.ExecuteOnActive = true;
                action.TopicTemplate = string.IsNullOrWhiteSpace(action.TopicTemplate) ? "ipc/rule/{pointCode}/{ruleName}" : action.TopicTemplate;
                action.ActiveMessage = action.ActiveMessage ?? string.Empty;
                action.ClearMessage = action.ClearMessage ?? string.Empty;
                action.EmailSmtpHost = action.EmailSmtpHost ?? string.Empty;
                if (action.EmailSmtpPort <= 0)
                    action.EmailSmtpPort = 25;
                action.EmailUsername = action.EmailUsername ?? string.Empty;
                action.EmailPassword = action.EmailPassword ?? string.Empty;
                action.EmailFrom = action.EmailFrom ?? string.Empty;
                action.EmailTo = action.EmailTo ?? string.Empty;
                action.EmailCc = action.EmailCc ?? string.Empty;
                action.EmailSubjectTemplate = string.IsNullOrWhiteSpace(action.EmailSubjectTemplate) ? "{ruleName} {state}" : action.EmailSubjectTemplate;
                action.EmailBodyTemplate = string.IsNullOrWhiteSpace(action.EmailBodyTemplate) ? "{message}" : action.EmailBodyTemplate;
                action.WebhookUrl = action.WebhookUrl ?? string.Empty;
                action.WebhookMethod = string.IsNullOrWhiteSpace(action.WebhookMethod) ? "POST" : action.WebhookMethod;
                action.WebhookHeaders = action.WebhookHeaders ?? string.Empty;
                action.WebhookBodyTemplate = string.IsNullOrWhiteSpace(action.WebhookBodyTemplate)
                    ? "{\"ruleName\":\"{ruleName}\",\"state\":\"{state}\",\"value\":\"{value}\",\"message\":\"{message}\"}"
                    : action.WebhookBodyTemplate;
                action.WebhookContentType = string.IsNullOrWhiteSpace(action.WebhookContentType) ? "application/json" : action.WebhookContentType;
                if (action.WebhookTimeoutSeconds <= 0)
                    action.WebhookTimeoutSeconds = 5;
                if (action.WebhookRetryCount < 0)
                    action.WebhookRetryCount = 0;
                action.DebugLabel = action.DebugLabel ?? string.Empty;
            }
        }

        private static void NormalizeAlarmLevels(System.Collections.Generic.List<EdgeRuleAlarmLevelConfig>? levels)
        {
            if (levels == null)
                return;

            for (int i = 0; i < levels.Count; i++)
            {
                EdgeRuleAlarmLevelConfig level = levels[i];
                if (level == null)
                    continue;
                if (string.IsNullOrWhiteSpace(level.Id))
                    level.Id = Guid.NewGuid().ToString("N");
                level.Name = level.Name ?? string.Empty;
                level.Severity = level.Severity ?? string.Empty;
                level.Message = level.Message ?? string.Empty;
            }
        }

        private static void NormalizeRuleConditions(System.Collections.Generic.List<EdgeRuleConditionConfig>? conditions)
        {
            if (conditions == null)
                return;

            for (int i = 0; i < conditions.Count; i++)
            {
                EdgeRuleConditionConfig condition = conditions[i];
                if (condition == null)
                    continue;
                if (string.IsNullOrWhiteSpace(condition.Id))
                    condition.Id = Guid.NewGuid().ToString("N");
                condition.SourcePointCode = condition.SourcePointCode ?? string.Empty;
                condition.SourceDeviceName = condition.SourceDeviceName ?? string.Empty;
                condition.SourceGroupName = condition.SourceGroupName ?? string.Empty;
                condition.SourceTagName = condition.SourceTagName ?? string.Empty;
                condition.SourceDataType = condition.SourceDataType ?? string.Empty;
                condition.TransformExpression = condition.TransformExpression ?? string.Empty;
            }
        }

        private static void NormalizeFlowRules(System.Collections.Generic.List<FlowRuleDefinition>? flowRules)
        {
            if (flowRules == null)
                return;

            for (int i = 0; i < flowRules.Count; i++)
            {
                FlowRuleDefinition rule = flowRules[i];
                if (rule == null)
                    continue;

                if (string.IsNullOrWhiteSpace(rule.Id))
                    rule.Id = Guid.NewGuid().ToString("N");
                rule.Name = rule.Name ?? string.Empty;
                rule.Description = rule.Description ?? string.Empty;
                rule.Mode = string.IsNullOrWhiteSpace(rule.Mode) ? FlowRuleModes.Flow : rule.Mode;
                rule.LifecycleState = string.IsNullOrWhiteSpace(rule.LifecycleState) ? FlowRuleLifecycleStates.Draft : rule.LifecycleState;
                if (rule.PublishedVersion < 0)
                    rule.PublishedVersion = 0;
                rule.PublishedBy = rule.PublishedBy ?? string.Empty;
                rule.CompiledRuleId = rule.CompiledRuleId ?? string.Empty;
                if (rule.Version <= 0)
                    rule.Version = 1;
                if (rule.CreatedTime == DateTime.MinValue)
                    rule.CreatedTime = DateTime.Now;
                if (rule.UpdatedTime == DateTime.MinValue)
                    rule.UpdatedTime = rule.CreatedTime;
                if (rule.Nodes == null)
                    rule.Nodes = new System.Collections.Generic.List<FlowRuleNode>();
                if (rule.Edges == null)
                    rule.Edges = new System.Collections.Generic.List<FlowRuleEdge>();

                NormalizeFlowNodes(rule.Nodes);
                NormalizeFlowEdges(rule.Edges);
            }
        }

        private static void SyncFlowRuleCompiledRules(ProjectConfig config)
        {
            if (config == null || config.FlowRules == null)
                return;

            for (int i = 0; i < config.FlowRules.Count; i++)
            {
                FlowRuleDefinition rule = config.FlowRules[i];
                if (rule == null)
                    continue;
                FlowRuleCompiler.SyncCompiledRule(config, rule, rule.CompiledRuleId);
            }
        }

        private static void NormalizeFlowNodes(System.Collections.Generic.List<FlowRuleNode>? nodes)
        {
            if (nodes == null)
                return;

            for (int i = 0; i < nodes.Count; i++)
            {
                FlowRuleNode node = nodes[i];
                if (node == null)
                    continue;

                if (string.IsNullOrWhiteSpace(node.Id))
                    node.Id = Guid.NewGuid().ToString("N");
                node.NodeType = string.IsNullOrWhiteSpace(node.NodeType) ? FlowRuleNodeTypes.Condition : node.NodeType;
                node.Label = node.Label ?? string.Empty;
                node.DeviceName = node.DeviceName ?? string.Empty;
                node.GroupName = node.GroupName ?? string.Empty;
                node.TagName = node.TagName ?? string.Empty;
                node.PointCode = node.PointCode ?? string.Empty;
                node.DataType = node.DataType ?? string.Empty;
                node.ConditionType = string.IsNullOrWhiteSpace(node.ConditionType) ? EdgeRuleConditionType.Condition.ToString() : node.ConditionType;
                node.Operator = string.IsNullOrWhiteSpace(node.Operator) ? EdgeRuleComparisonOperator.GreaterThan.ToString() : node.Operator;
                node.LogicalOperator = string.IsNullOrWhiteSpace(node.LogicalOperator) ? EdgeRuleLogicalOperator.And.ToString() : node.LogicalOperator;
                node.TopicTemplate = node.TopicTemplate ?? string.Empty;
                node.ActiveMessage = node.ActiveMessage ?? string.Empty;
                node.ClearMessage = node.ClearMessage ?? string.Empty;
                node.HysteresisMode = string.IsNullOrWhiteSpace(node.HysteresisMode) ? "High" : node.HysteresisMode;
                node.Expression = string.IsNullOrWhiteSpace(node.Expression) ? "{value} > 0" : node.Expression;
                node.QualityOperator = string.IsNullOrWhiteSpace(node.QualityOperator) ? "In" : node.QualityOperator;
                node.QualityValues = string.IsNullOrWhiteSpace(node.QualityValues) ? "Good" : node.QualityValues;
                node.WindowStatistic = string.IsNullOrWhiteSpace(node.WindowStatistic) ? "Average" : node.WindowStatistic;
                if (node.WindowSeconds <= 0)
                    node.WindowSeconds = 60;
                if (node.WindowSampleCount < 0)
                    node.WindowSampleCount = 0;
                node.StateName = string.IsNullOrWhiteSpace(node.StateName) ? "State" : node.StateName;
                node.StateExpectedValue = string.IsNullOrWhiteSpace(node.StateExpectedValue) ? "1" : node.StateExpectedValue;
                node.StateClearValue = node.StateClearValue ?? string.Empty;
                if (node.StateTimeoutSeconds < 0)
                    node.StateTimeoutSeconds = 0;
                node.RelatedDeviceName = node.RelatedDeviceName ?? string.Empty;
                node.RelatedGroupName = node.RelatedGroupName ?? string.Empty;
                node.RelatedTagName = node.RelatedTagName ?? string.Empty;
                node.RelatedPointCode = node.RelatedPointCode ?? string.Empty;
                node.RelatedDataType = node.RelatedDataType ?? string.Empty;
                node.RelationOperator = string.IsNullOrWhiteSpace(node.RelationOperator) ? EdgeRuleComparisonOperator.GreaterThan.ToString() : node.RelationOperator;
                node.ContextName = string.IsNullOrWhiteSpace(node.ContextName) ? "Context" : node.ContextName;
                node.ContextExpectedValue = node.ContextExpectedValue ?? string.Empty;
                node.ContextOperator = string.IsNullOrWhiteSpace(node.ContextOperator) ? EdgeRuleComparisonOperator.Equal.ToString() : node.ContextOperator;
                node.ContextDeviceName = node.ContextDeviceName ?? string.Empty;
                node.ContextGroupName = node.ContextGroupName ?? string.Empty;
                node.ContextTagName = node.ContextTagName ?? string.Empty;
                node.ContextPointCode = node.ContextPointCode ?? string.Empty;
                node.ContextDataType = node.ContextDataType ?? string.Empty;
                node.CycleStartValue = string.IsNullOrWhiteSpace(node.CycleStartValue) ? "1" : node.CycleStartValue;
                node.CycleEndValue = string.IsNullOrWhiteSpace(node.CycleEndValue) ? "0" : node.CycleEndValue;
                if (node.CycleMinSeconds < 0)
                    node.CycleMinSeconds = 0;
                if (node.CycleMaxSeconds < 0)
                    node.CycleMaxSeconds = 0;
                node.AlarmSeverity = string.IsNullOrWhiteSpace(node.AlarmSeverity) ? "Warning" : node.AlarmSeverity;
                if (node.AlarmSuppressSeconds < 0)
                    node.AlarmSuppressSeconds = 0;
                if (node.AlarmReTriggerSeconds < 0)
                    node.AlarmReTriggerSeconds = 0;
                if (node.AlarmEscalateAfterSeconds < 0)
                    node.AlarmEscalateAfterSeconds = 0;
                if (node.ActionDelaySeconds < 0)
                    node.ActionDelaySeconds = 0;
                if (node.ActionCooldownSeconds < 0)
                    node.ActionCooldownSeconds = 0;
                if (node.ActionMaxPerMinute < 0)
                    node.ActionMaxPerMinute = 0;
                node.DebugLabel = node.DebugLabel ?? string.Empty;
                node.TransformExpression = node.TransformExpression ?? string.Empty;
                NormalizeModelInferenceNode(node);
                if (node.TransformTimeoutMilliseconds <= 0)
                    node.TransformTimeoutMilliseconds = 50;
                if (node.TransformTimeoutMilliseconds > 5000)
                    node.TransformTimeoutMilliseconds = 5000;
                if (node.SequenceWindowSeconds <= 0)
                    node.SequenceWindowSeconds = 60;
                if (node.SequenceStepTimeoutSeconds < 0)
                    node.SequenceStepTimeoutSeconds = 0;
                if (node.SequenceMinIntervalSeconds < 0)
                    node.SequenceMinIntervalSeconds = 0;
                if (node.ClearDurationSeconds < 0)
                    node.ClearDurationSeconds = 0;
                if (node.AlarmLevels == null)
                    node.AlarmLevels = new System.Collections.Generic.List<FlowRuleAlarmLevel>();
                NormalizeFlowAlarmLevels(node.AlarmLevels);
                if ((string.Equals(node.NodeType, FlowRuleNodeTypes.MqttPublish, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(node.NodeType, FlowRuleNodeTypes.EmailNotify, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(node.NodeType, FlowRuleNodeTypes.WebhookCall, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(node.NodeType, FlowRuleNodeTypes.DebugProbe, StringComparison.OrdinalIgnoreCase)) &&
                    !node.ExecuteOnActive && !node.ExecuteOnClear)
                {
                    node.ExecuteOnActive = true;
                }
                if (node.EmailSmtpPort <= 0)
                    node.EmailSmtpPort = 25;
                node.EmailSmtpHost = node.EmailSmtpHost ?? string.Empty;
                node.EmailUsername = node.EmailUsername ?? string.Empty;
                node.EmailPassword = node.EmailPassword ?? string.Empty;
                node.EmailFrom = node.EmailFrom ?? string.Empty;
                node.EmailTo = node.EmailTo ?? string.Empty;
                node.EmailCc = node.EmailCc ?? string.Empty;
                node.EmailSubjectTemplate = string.IsNullOrWhiteSpace(node.EmailSubjectTemplate) ? "{ruleName} {state}" : node.EmailSubjectTemplate;
                node.EmailBodyTemplate = string.IsNullOrWhiteSpace(node.EmailBodyTemplate) ? "{message}" : node.EmailBodyTemplate;
                node.WebhookUrl = node.WebhookUrl ?? string.Empty;
                node.WebhookMethod = string.IsNullOrWhiteSpace(node.WebhookMethod) ? "POST" : node.WebhookMethod;
                node.WebhookHeaders = node.WebhookHeaders ?? string.Empty;
                node.WebhookBodyTemplate = string.IsNullOrWhiteSpace(node.WebhookBodyTemplate)
                    ? "{\"ruleName\":\"{ruleName}\",\"state\":\"{state}\",\"value\":\"{value}\",\"message\":\"{message}\"}"
                    : node.WebhookBodyTemplate;
                node.WebhookContentType = string.IsNullOrWhiteSpace(node.WebhookContentType) ? "application/json" : node.WebhookContentType;
                if (node.WebhookTimeoutSeconds <= 0)
                    node.WebhookTimeoutSeconds = 5;
                if (node.WebhookRetryCount < 0)
                    node.WebhookRetryCount = 0;
            }
        }

        private static void NormalizeFlowAlarmLevels(System.Collections.Generic.List<FlowRuleAlarmLevel>? levels)
        {
            if (levels == null)
                return;

            for (int i = 0; i < levels.Count; i++)
            {
                FlowRuleAlarmLevel level = levels[i];
                if (level == null)
                    continue;
                if (string.IsNullOrWhiteSpace(level.Id))
                    level.Id = Guid.NewGuid().ToString("N");
                level.Name = level.Name ?? string.Empty;
                level.Severity = level.Severity ?? string.Empty;
                level.Operator = string.IsNullOrWhiteSpace(level.Operator) ? EdgeRuleComparisonOperator.GreaterThanOrEqual.ToString() : level.Operator;
                level.Message = level.Message ?? string.Empty;
            }
        }

        private static void NormalizeModelInferenceRule(EdgeRuleConfig rule)
        {
            if (rule == null)
                return;

            rule.ModelPurpose = string.IsNullOrWhiteSpace(rule.ModelPurpose) ? "DeviceAnomaly" : rule.ModelPurpose.Trim();
            rule.ModelPath = rule.ModelPath ?? string.Empty;
            rule.ModelInputTags = rule.ModelInputTags ?? string.Empty;
            rule.ModelInputName = rule.ModelInputName ?? string.Empty;
            rule.ModelInputNames = rule.ModelInputNames ?? string.Empty;
            rule.ModelOutputName = rule.ModelOutputName ?? string.Empty;
            if (rule.ModelOutputIndex < 0)
                rule.ModelOutputIndex = 0;
            if (rule.ModelTimeoutMilliseconds <= 0)
                rule.ModelTimeoutMilliseconds = 1000;
            if (rule.ModelTimeoutMilliseconds > 30000)
                rule.ModelTimeoutMilliseconds = 30000;
            if (double.IsNaN(rule.ModelThreshold) || double.IsInfinity(rule.ModelThreshold))
                rule.ModelThreshold = 0.5D;
        }

        private static void NormalizeModelInferenceNode(FlowRuleNode node)
        {
            if (node == null)
                return;

            node.ModelPurpose = string.IsNullOrWhiteSpace(node.ModelPurpose) ? "DeviceAnomaly" : node.ModelPurpose.Trim();
            node.ModelPath = node.ModelPath ?? string.Empty;
            node.ModelInputTags = node.ModelInputTags ?? string.Empty;
            node.ModelInputName = node.ModelInputName ?? string.Empty;
            node.ModelInputNames = node.ModelInputNames ?? string.Empty;
            node.ModelOutputName = node.ModelOutputName ?? string.Empty;
            node.ModelOperator = string.IsNullOrWhiteSpace(node.ModelOperator)
                ? EdgeRuleComparisonOperator.GreaterThanOrEqual.ToString()
                : node.ModelOperator;
            if (node.ModelOutputIndex < 0)
                node.ModelOutputIndex = 0;
            if (node.ModelTimeoutMilliseconds <= 0)
                node.ModelTimeoutMilliseconds = 1000;
            if (node.ModelTimeoutMilliseconds > 30000)
                node.ModelTimeoutMilliseconds = 30000;
            if (double.IsNaN(node.ModelThreshold) || double.IsInfinity(node.ModelThreshold))
                node.ModelThreshold = 0.5D;
        }

        private static void NormalizeFlowEdges(System.Collections.Generic.List<FlowRuleEdge>? edges)
        {
            if (edges == null)
                return;

            for (int i = 0; i < edges.Count; i++)
            {
                FlowRuleEdge edge = edges[i];
                if (edge == null)
                    continue;

                if (string.IsNullOrWhiteSpace(edge.Id))
                    edge.Id = Guid.NewGuid().ToString("N");
                edge.SourceNodeId = edge.SourceNodeId ?? string.Empty;
                edge.TargetNodeId = edge.TargetNodeId ?? string.Empty;
                edge.SourcePort = edge.SourcePort ?? string.Empty;
                edge.TargetPort = edge.TargetPort ?? string.Empty;
            }
        }

        private static void NormalizeTags(DeviceConfig device, GroupConfig? group, System.Collections.Generic.List<TagConfig>? tags)
        {
            if (tags == null)
                return;

            for (int i = 0; i < tags.Count; i++)
            {
                TagConfig tag = tags[i];
                if (tag == null)
                    continue;
                if (string.IsNullOrWhiteSpace(tag.Id))
                    tag.Id = Guid.NewGuid().ToString("N");
                tag.DeviceId = device.Id;
                tag.GroupId = group == null ? string.Empty : group.Id;
                if (tag.ElementCount <= 0)
                    tag.ElementCount = 1;
                if (tag.FailureRetryDelayMs < 0)
                    tag.FailureRetryDelayMs = 0;
                if (tag.Scaling == null)
                    tag.Scaling = ScalingConfig.Default();
                NormalizeCleaning(tag);
                if (tag.Alarm == null)
                    tag.Alarm = TagAlarmConfig.Default();
            }
        }

        private static void NormalizeCleaning(TagConfig tag)
        {
            if (tag.Cleaning == null)
                tag.Cleaning = DataCleaningConfig.Default();

            if (tag.Cleaning.Deadband < 0D)
                tag.Cleaning.Deadband = 0D;
            if (tag.Cleaning.SpikeThreshold < 0D)
                tag.Cleaning.SpikeThreshold = 0D;
            if (tag.Cleaning.SpikeWindowSeconds < 0)
                tag.Cleaning.SpikeWindowSeconds = 0;
            if (Math.Abs(tag.Cleaning.UnitMultiplier) < 0.000000001D)
                tag.Cleaning.UnitMultiplier = 1D;
            tag.Cleaning.SourceUnit = tag.Cleaning.SourceUnit ?? string.Empty;
            tag.Cleaning.TargetUnit = tag.Cleaning.TargetUnit ?? string.Empty;
            if (tag.Cleaning.EnumMappings == null)
                tag.Cleaning.EnumMappings = new System.Collections.Generic.List<DataCleaningEnumMappingConfig>();

            for (int i = 0; i < tag.Cleaning.EnumMappings.Count; i++)
            {
                DataCleaningEnumMappingConfig item = tag.Cleaning.EnumMappings[i];
                if (item == null)
                    continue;
                item.RawValue = item.RawValue ?? string.Empty;
                item.CleanValue = item.CleanValue ?? string.Empty;
                item.Description = item.Description ?? string.Empty;
            }
        }

        private static JsonSerializerOptions CreateJsonOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        private static string ResolvePath(string path)
        {
            string value = string.IsNullOrWhiteSpace(path) ? "Data\\gateway-project.json" : path.Trim();
            if (!Path.IsPathRooted(value))
                value = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, value);
            return value;
        }
    }
}

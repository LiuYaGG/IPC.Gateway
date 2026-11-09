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
        private static FlowRuleNode? ResolveSourceNode(
            FlowRuleNode? target,
            IList<FlowRuleNode>? nodes,
            IList<FlowRuleEdge>? edges)
        {
            return ResolveSourceNode(target, nodes, edges, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        private static FlowRuleNode? ResolveTransformNode(
            FlowRuleNode? target,
            IList<FlowRuleNode>? nodes,
            IList<FlowRuleEdge>? edges)
        {
            return ResolveTransformNode(target, nodes, edges, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        private static FlowRuleNode? ResolveQualityGateNode(
            FlowRuleNode? target,
            IList<FlowRuleNode>? nodes,
            IList<FlowRuleEdge>? edges)
        {
            return ResolveQualityGateNode(target, nodes, edges, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        private static FlowRuleNode? ResolveSourceNode(
            FlowRuleNode? target,
            IList<FlowRuleNode>? nodes,
            IList<FlowRuleEdge>? edges,
            HashSet<string> visited)
        {
            if (target == null || nodes == null || edges == null)
                return null;

            string targetId = target.Id ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(targetId) && !visited.Add(targetId))
                return null;

            if (IsTagNode(target))
                return target;

            List<FlowRuleEdge> incomingEdges = edges
                .Where(item => item != null && string.Equals(item.TargetNodeId, target.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();

            for (int i = 0; i < incomingEdges.Count; i++)
            {
                FlowRuleEdge edge = incomingEdges[i];
                FlowRuleNode? source = nodes.FirstOrDefault(node =>
                    node != null && string.Equals(node.Id, edge.SourceNodeId, StringComparison.OrdinalIgnoreCase));
                if (source == null)
                    continue;

                if (IsTagNode(source))
                    return source;

                FlowRuleNode? nestedSource = ResolveSourceNode(source, nodes, edges, visited);
                if (nestedSource != null)
                    return nestedSource;
            }

            return null;
        }

        private static FlowRuleNode? ResolveQualityGateNode(
            FlowRuleNode? target,
            IList<FlowRuleNode>? nodes,
            IList<FlowRuleEdge>? edges,
            HashSet<string> visited)
        {
            if (target == null || nodes == null || edges == null)
                return null;

            string targetId = target.Id ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(targetId) && !visited.Add(targetId))
                return null;

            List<FlowRuleEdge> incomingEdges = edges
                .Where(item => item != null && string.Equals(item.TargetNodeId, target.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();

            for (int i = 0; i < incomingEdges.Count; i++)
            {
                FlowRuleEdge edge = incomingEdges[i];
                FlowRuleNode? source = nodes.FirstOrDefault(node =>
                    node != null && string.Equals(node.Id, edge.SourceNodeId, StringComparison.OrdinalIgnoreCase));
                if (source == null)
                    continue;

                if (IsQualityGateNode(source))
                    return source;

                FlowRuleNode? nestedQualityGate = ResolveQualityGateNode(source, nodes, edges, visited);
                if (nestedQualityGate != null)
                    return nestedQualityGate;
            }

            return null;
        }

        private static FlowRuleNode? ResolveTransformNode(
            FlowRuleNode? target,
            IList<FlowRuleNode>? nodes,
            IList<FlowRuleEdge>? edges,
            HashSet<string> visited)
        {
            if (target == null || nodes == null || edges == null)
                return null;

            string targetId = target.Id ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(targetId) && !visited.Add(targetId))
                return null;

            List<FlowRuleEdge> incomingEdges = edges
                .Where(item => item != null && string.Equals(item.TargetNodeId, target.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();

            for (int i = 0; i < incomingEdges.Count; i++)
            {
                FlowRuleEdge edge = incomingEdges[i];
                FlowRuleNode? source = nodes.FirstOrDefault(node =>
                    node != null && string.Equals(node.Id, edge.SourceNodeId, StringComparison.OrdinalIgnoreCase));
                if (source == null)
                    continue;

                if (IsTransformNode(source))
                    return source;

                FlowRuleNode? nestedTransform = ResolveTransformNode(source, nodes, edges, visited);
                if (nestedTransform != null)
                    return nestedTransform;
            }

            return null;
        }

        private static bool IsTagNode(FlowRuleNode? node)
        {
            return IsNodeType(node, FlowRuleNodeTypes.TagInput);
        }

        private static bool IsConditionLikeNode(FlowRuleNode? node)
        {
            return IsNodeType(node, FlowRuleNodeTypes.Condition) ||
                   IsNodeType(node, FlowRuleNodeTypes.Threshold) ||
                   IsNodeType(node, FlowRuleNodeTypes.Deadband) ||
                   IsNodeType(node, FlowRuleNodeTypes.RateOfChange) ||
                   IsNodeType(node, FlowRuleNodeTypes.Hysteresis) ||
                   IsNodeType(node, FlowRuleNodeTypes.MultiLevelAlarm) ||
                   IsNodeType(node, FlowRuleNodeTypes.Expression) ||
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
                   IsNodeType(node, FlowRuleNodeTypes.Function) ||
                   IsNodeType(node, FlowRuleNodeTypes.ValueScript);
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

        private static bool IsNodeType(FlowRuleNode? node, string nodeType)
        {
            return node != null && string.Equals(node.NodeType, nodeType, StringComparison.OrdinalIgnoreCase);
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

        private static EdgeRuleConditionType ResolveConditionType(FlowRuleNode? node)
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
            return ParseEnum(node == null ? string.Empty : node.ConditionType, EdgeRuleConditionType.Condition);
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
    }
}

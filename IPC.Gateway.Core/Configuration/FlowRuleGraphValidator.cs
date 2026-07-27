using System;
using System.Collections.Generic;
using System.Linq;

namespace IPC.Runtime.Configuration
{
    public sealed class FlowRuleGraphMap
    {
        private readonly Dictionary<string, FlowRuleNode> _nodesById;
        private readonly Dictionary<string, List<FlowRuleEdge>> _incoming;
        private readonly Dictionary<string, List<FlowRuleEdge>> _outgoing;

        public FlowRuleGraphMap(FlowRuleDefinition definition)
        {
            Definition = definition ?? new FlowRuleDefinition();
            Nodes = (Definition.Nodes ?? new List<FlowRuleNode>()).Where(node => node != null).ToList();
            Edges = (Definition.Edges ?? new List<FlowRuleEdge>()).Where(edge => edge != null).ToList();
            _nodesById = new Dictionary<string, FlowRuleNode>(StringComparer.OrdinalIgnoreCase);
            _incoming = new Dictionary<string, List<FlowRuleEdge>>(StringComparer.OrdinalIgnoreCase);
            _outgoing = new Dictionary<string, List<FlowRuleEdge>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < Nodes.Count; i++)
            {
                FlowRuleNode node = Nodes[i];
                if (!string.IsNullOrWhiteSpace(node.Id) && !_nodesById.ContainsKey(node.Id))
                    _nodesById[node.Id] = node;
            }

            for (int i = 0; i < Edges.Count; i++)
            {
                FlowRuleEdge edge = Edges[i];
                AddEdge(_outgoing, edge.SourceNodeId, edge);
                AddEdge(_incoming, edge.TargetNodeId, edge);
            }
        }

        public FlowRuleDefinition Definition { get; }
        public IList<FlowRuleNode> Nodes { get; }
        public IList<FlowRuleEdge> Edges { get; }

        public FlowRuleNode? FindNode(string nodeId)
        {
            return _nodesById.TryGetValue(nodeId ?? string.Empty, out FlowRuleNode? node) ? node : null;
        }

        public IList<FlowRuleNode> GetIncomingNodes(string nodeId)
        {
            return GetLinkedNodes(_incoming, nodeId, edge => edge.SourceNodeId);
        }

        public IList<FlowRuleNode> GetOutgoingNodes(string nodeId)
        {
            return GetLinkedNodes(_outgoing, nodeId, edge => edge.TargetNodeId);
        }

        public IList<FlowRuleEdge> GetIncomingEdges(string nodeId)
        {
            return _incoming.TryGetValue(nodeId ?? string.Empty, out List<FlowRuleEdge>? edges)
                ? edges.ToList()
                : new List<FlowRuleEdge>();
        }

        public IList<FlowRuleEdge> GetOutgoingEdges(string nodeId)
        {
            return _outgoing.TryGetValue(nodeId ?? string.Empty, out List<FlowRuleEdge>? edges)
                ? edges.ToList()
                : new List<FlowRuleEdge>();
        }

        public bool IsReachable(string sourceNodeId, string targetNodeId)
        {
            if (string.Equals(sourceNodeId, targetNodeId, StringComparison.OrdinalIgnoreCase))
                return true;
            return GetDescendantIds(sourceNodeId).Contains(targetNodeId ?? string.Empty);
        }

        public HashSet<string> GetDescendantIds(string nodeId)
        {
            return Traverse(nodeId, _outgoing, edge => edge.TargetNodeId);
        }

        public HashSet<string> GetAncestorIds(string nodeId)
        {
            return Traverse(nodeId, _incoming, edge => edge.SourceNodeId);
        }

        private IList<FlowRuleNode> GetLinkedNodes(
            Dictionary<string, List<FlowRuleEdge>> index,
            string nodeId,
            Func<FlowRuleEdge, string> selector)
        {
            List<FlowRuleNode> result = new List<FlowRuleNode>();
            if (!index.TryGetValue(nodeId ?? string.Empty, out List<FlowRuleEdge>? edges))
                return result;

            for (int i = 0; i < edges.Count; i++)
            {
                FlowRuleNode? node = FindNode(selector(edges[i]));
                if (node != null)
                    result.Add(node);
            }
            return result;
        }

        private HashSet<string> Traverse(
            string nodeId,
            Dictionary<string, List<FlowRuleEdge>> index,
            Func<FlowRuleEdge, string> selector)
        {
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Stack<string> pending = new Stack<string>();
            pending.Push(nodeId ?? string.Empty);
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                if (!index.TryGetValue(current, out List<FlowRuleEdge>? edges))
                    continue;
                for (int i = 0; i < edges.Count; i++)
                {
                    string next = selector(edges[i]);
                    if (visited.Add(next))
                        pending.Push(next);
                }
            }
            return visited;
        }

        private static void AddEdge(Dictionary<string, List<FlowRuleEdge>> index, string nodeId, FlowRuleEdge edge)
        {
            string id = nodeId ?? string.Empty;
            if (!index.TryGetValue(id, out List<FlowRuleEdge>? edges))
            {
                edges = new List<FlowRuleEdge>();
                index[id] = edges;
            }
            edges.Add(edge);
        }
    }

    public static class FlowRuleGraphValidator
    {
        public static IList<string> Validate(FlowRuleDefinition? definition)
        {
            List<string> errors = new List<string>();
            if (definition == null)
            {
                errors.Add("规则引擎不能为空。");
                return errors;
            }

            FlowRuleGraphMap graph = new FlowRuleGraphMap(definition);
            ValidateIdentityAndEdges(graph, errors);
            if (errors.Count > 0)
                return errors;

            ValidateAcyclic(graph, errors);
            ValidateTopology(graph, errors);
            ValidateNodeSettings(graph, errors);
            return errors;
        }

        public static void ValidateOrThrow(FlowRuleDefinition? definition)
        {
            IList<string> errors = Validate(definition);
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join("；", errors));
        }

        private static void ValidateIdentityAndEdges(FlowRuleGraphMap graph, List<string> errors)
        {
            HashSet<string> nodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                FlowRuleNode node = graph.Nodes[i];
                if (string.IsNullOrWhiteSpace(node.Id))
                    errors.Add("流程节点 ID 不能为空。");
                else if (!nodeIds.Add(node.Id))
                    errors.Add("流程节点 ID 重复：" + node.Id);
            }

            HashSet<string> edgePairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < graph.Edges.Count; i++)
            {
                FlowRuleEdge edge = graph.Edges[i];
                if (!nodeIds.Contains(edge.SourceNodeId) || !nodeIds.Contains(edge.TargetNodeId))
                    errors.Add("流程连线引用了不存在的节点。");
                if (string.Equals(edge.SourceNodeId, edge.TargetNodeId, StringComparison.OrdinalIgnoreCase))
                    errors.Add("流程节点不能连接到自身。");
                if (!edgePairs.Add((edge.SourceNodeId ?? string.Empty) + "\u001F" + (edge.TargetNodeId ?? string.Empty)))
                    errors.Add("流程中存在重复连线。");
            }
        }

        private static void ValidateAcyclic(FlowRuleGraphMap graph, List<string> errors)
        {
            Dictionary<string, int> indegrees = graph.Nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < graph.Edges.Count; i++)
                indegrees[graph.Edges[i].TargetNodeId]++;

            Queue<string> queue = new Queue<string>(indegrees.Where(item => item.Value == 0).Select(item => item.Key));
            int visited = 0;
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                visited++;
                IList<FlowRuleEdge> outgoing = graph.GetOutgoingEdges(current);
                for (int i = 0; i < outgoing.Count; i++)
                {
                    string target = outgoing[i].TargetNodeId;
                    indegrees[target]--;
                    if (indegrees[target] == 0)
                        queue.Enqueue(target);
                }
            }
            if (visited != graph.Nodes.Count)
                errors.Add("规则引擎不能包含环路。");
        }

        private static void ValidateTopology(FlowRuleGraphMap graph, List<string> errors)
        {
            List<FlowRuleNode> conditions = graph.Nodes.Where(IsConditionNode).ToList();
            if (conditions.Count == 0)
                conditions.AddRange(graph.Nodes.Where(node => IsType(node, FlowRuleNodeTypes.QualityGate)));
            List<FlowRuleNode> logicNodes = graph.Nodes.Where(node => IsType(node, FlowRuleNodeTypes.Logic)).ToList();
            List<FlowRuleNode> sequenceNodes = graph.Nodes.Where(node => IsType(node, FlowRuleNodeTypes.Sequence)).ToList();
            List<FlowRuleNode> actions = graph.Nodes.Where(IsActionNode).ToList();

            if (conditions.Count == 0)
                errors.Add("流程至少需要一个判断节点。");
            if (logicNodes.Count > 1)
                errors.Add("当前版本每条流程只支持一个 AND/OR 节点。");
            if (sequenceNodes.Count > 1)
                errors.Add("当前版本每条流程只支持一个顺序/时序节点。");
            if (logicNodes.Count > 0 && sequenceNodes.Count > 0)
                errors.Add("同一流程不能同时使用 AND/OR 和顺序/时序节点。");

            FlowRuleNode? root = sequenceNodes.FirstOrDefault() ?? logicNodes.FirstOrDefault();
            if (root != null)
            {
                List<FlowRuleNode> incomingConditions = graph.GetIncomingNodes(root.Id).Where(IsConditionNode).ToList();
                if (incomingConditions.Count < 2)
                    errors.Add((IsType(root, FlowRuleNodeTypes.Sequence) ? "顺序/时序" : "AND/OR") + "节点至少需要两个直接连接的条件。");
                if (incomingConditions.Any(node => !IsBasicConditionNode(node)))
                    errors.Add("组合与顺序节点当前只接受基础条件，不能静默降级高级判断节点。");
                if (conditions.Any(node => !incomingConditions.Any(item => string.Equals(item.Id, node.Id, StringComparison.OrdinalIgnoreCase))))
                    errors.Add("组合流程中的条件必须直接连接到 AND/OR 或顺序节点。");
            }
            else if (conditions.Count != 1)
            {
                errors.Add("多个条件必须通过一个 AND/OR 或顺序节点汇合。");
            }
            else
            {
                root = conditions[0];
            }

            if (root == null)
                return;

            for (int i = 0; i < actions.Count; i++)
            {
                FlowRuleNode action = actions[i];
                if (!graph.IsReachable(root.Id, action.Id))
                    errors.Add("动作节点必须连接在规则判断结果之后：" + (action.Label ?? action.NodeType));
            }

            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                FlowRuleNode node = graph.Nodes[i];
                if (IsActionNode(node) && graph.GetOutgoingEdges(node.Id).Count > 0)
                    errors.Add("动作节点不能再连接下游节点。");
                if (!IsType(node, FlowRuleNodeTypes.TagInput) &&
                    !HasEmbeddedSource(node) &&
                    graph.GetIncomingEdges(node.Id).Count == 0)
                    errors.Add("流程节点未连接输入：" + (node.Label ?? node.NodeType));
            }
        }

        private static void ValidateNodeSettings(FlowRuleGraphMap graph, List<string> errors)
        {
            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                FlowRuleNode node = graph.Nodes[i];
                if ((IsType(node, FlowRuleNodeTypes.CycleTime) || IsType(node, FlowRuleNodeTypes.ProcessTakt)) &&
                    string.Equals(node.CycleStartValue, node.CycleEndValue, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("周期开始值与结束值不能相同：" + (node.Label ?? node.NodeType));
                }

                if (node.CycleMinSeconds > 0 && node.CycleMaxSeconds > 0 &&
                    node.CycleMinSeconds > node.CycleMaxSeconds)
                {
                    errors.Add("周期最小时间不能大于最大时间：" + (node.Label ?? node.NodeType));
                }

                if (IsType(node, FlowRuleNodeTypes.StateMachine) &&
                    !string.IsNullOrWhiteSpace(node.StateClearValue) &&
                    string.Equals(node.StateExpectedValue, node.StateClearValue, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("状态机目标值与明确恢复值不能相同：" + (node.Label ?? node.NodeType));
                }
            }
        }

        private static bool IsConditionNode(FlowRuleNode node)
        {
            return IsBasicConditionNode(node) ||
                   IsType(node, FlowRuleNodeTypes.Deadband) ||
                   IsType(node, FlowRuleNodeTypes.RateOfChange) ||
                   IsType(node, FlowRuleNodeTypes.Hysteresis) ||
                   IsType(node, FlowRuleNodeTypes.MultiLevelAlarm) ||
                   IsType(node, FlowRuleNodeTypes.Expression) ||
                   IsType(node, FlowRuleNodeTypes.SlidingWindow) ||
                   IsType(node, FlowRuleNodeTypes.WindowCalculation) ||
                   IsType(node, FlowRuleNodeTypes.Aggregation) ||
                   IsType(node, FlowRuleNodeTypes.Trend) ||
                   IsType(node, FlowRuleNodeTypes.StateMachine) ||
                   IsType(node, FlowRuleNodeTypes.CycleTime) ||
                   IsType(node, FlowRuleNodeTypes.ProcessTakt) ||
                   IsType(node, FlowRuleNodeTypes.AnomalyDetection) ||
                   IsType(node, FlowRuleNodeTypes.ModelInference) ||
                   IsType(node, FlowRuleNodeTypes.TagRelation) ||
                   IsType(node, FlowRuleNodeTypes.ContextGate);
        }

        private static bool IsBasicConditionNode(FlowRuleNode node)
        {
            return IsType(node, FlowRuleNodeTypes.Condition) || IsType(node, FlowRuleNodeTypes.Threshold);
        }

        private static bool IsActionNode(FlowRuleNode node)
        {
            return IsType(node, FlowRuleNodeTypes.MqttPublish) ||
                   IsType(node, FlowRuleNodeTypes.EmailNotify) ||
                   IsType(node, FlowRuleNodeTypes.WebhookCall) ||
                   IsType(node, FlowRuleNodeTypes.DebugProbe);
        }

        private static bool HasEmbeddedSource(FlowRuleNode node)
        {
            return !string.IsNullOrWhiteSpace(node.TagId) ||
                   !string.IsNullOrWhiteSpace(node.PointCode) ||
                   !string.IsNullOrWhiteSpace(node.RelatedTagId) ||
                   !string.IsNullOrWhiteSpace(node.ContextTagId);
        }

        private static bool IsType(FlowRuleNode node, string type)
        {
            return node != null && string.Equals(node.NodeType, type, StringComparison.OrdinalIgnoreCase);
        }
    }
}

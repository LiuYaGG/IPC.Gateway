namespace IPC.Runtime.Configuration;

/// <summary>Orders the numeric path by graph edges, never by canvas position or node list order.</summary>
internal sealed record FlowWindowCalculationPlan(FlowRuleNode Source, FlowRuleNode Result,
    FlowRuleNode? Condition, IReadOnlyList<FlowRuleNode> Processors)
{
    internal static bool Contains(FlowRuleDefinition definition) =>
        definition.Nodes?.Any(n => n != null && Is(n, FlowRuleNodeTypes.WindowCalculation)) == true;

    internal static FlowWindowCalculationPlan? Create(FlowRuleGraphMap graph, IList<string> errors)
    {
        const string topologyError = "窗口计算需要单一路径：标签输入 → 处理节点 → 可选判断节点 → 动作；多个动作可以共用计算结果。";
        var sources = graph.Nodes.Where(n => Is(n, FlowRuleNodeTypes.TagInput)).ToList();
        var conditions = graph.Nodes.Where(FlowRuleGraphValidator.IsConditionNode).ToList();
        if (sources.Count != 1 || conditions.Count > 1 || graph.Nodes.Any(n => Is(n, FlowRuleNodeTypes.Logic) || Is(n, FlowRuleNodeTypes.Sequence)))
        { errors.Add(topologyError); return null; }

        var source = sources[0];
        if (string.IsNullOrWhiteSpace(source.TagId) && string.IsNullOrWhiteSpace(source.PointCode))
        { errors.Add("窗口计算需要先配置标签输入的点位。"); return null; }
        var current = source;
        var processors = new List<FlowRuleNode>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { source.Id };
        while (true)
        {
            var next = graph.GetOutgoingNodes(current.Id);
            if (next.Count != 1 || (!IsProcessor(next[0]) && !conditions.Contains(next[0]))) break;
            current = next[0];
            if (!visited.Add(current.Id) || graph.GetIncomingEdges(current.Id).Count != 1)
            { errors.Add(topologyError); return null; }
            if (conditions.Contains(current)) break;
            processors.Add(current);
        }
        var condition = conditions.SingleOrDefault();
        var tail = graph.GetDescendantIds(current.Id);
        if (!processors.Any(n => Is(n, FlowRuleNodeTypes.WindowCalculation)) ||
            (condition != null && current != condition) ||
            graph.Nodes.Any(n => !visited.Contains(n.Id) && (!tail.Contains(n.Id) || !IsTail(n))) ||
            graph.Nodes.Where(IsAction).Any(n => graph.GetOutgoingEdges(n.Id).Count > 0) ||
            !graph.Nodes.Any(IsAction))
        { errors.Add(topologyError); return null; }
        if (condition != null && (Is(condition, FlowRuleNodeTypes.Aggregation) || Is(condition, FlowRuleNodeTypes.TagRelation) ||
            Is(condition, FlowRuleNodeTypes.ContextGate) || Is(condition, FlowRuleNodeTypes.ModelInference)))
        { errors.Add("窗口计算之后请使用单值判断节点，多标签关联与模型输入需要直接连接标签来源。"); return null; }
        return new(source, current, condition, processors);
    }

    internal static bool IsProcessor(FlowRuleNode node) => Is(node, FlowRuleNodeTypes.WindowCalculation) ||
        Is(node, FlowRuleNodeTypes.Transform) || Is(node, FlowRuleNodeTypes.Function) ||
        Is(node, FlowRuleNodeTypes.ValueScript) || Is(node, FlowRuleNodeTypes.QualityGate);
    private static bool IsTail(FlowRuleNode node) => IsAction(node) || Is(node, FlowRuleNodeTypes.Duration) ||
        Is(node, FlowRuleNodeTypes.AlarmLifecycle) || Is(node, FlowRuleNodeTypes.ActionPolicy);
    private static bool IsAction(FlowRuleNode node) => Is(node, FlowRuleNodeTypes.MqttPublish) ||
        Is(node, FlowRuleNodeTypes.EmailNotify) || Is(node, FlowRuleNodeTypes.WebhookCall) || Is(node, FlowRuleNodeTypes.DebugProbe);
    private static bool Is(FlowRuleNode node, string type) => string.Equals(node.NodeType, type, StringComparison.OrdinalIgnoreCase);
}

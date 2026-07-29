using IPC.Runtime.Configuration;

namespace IPC.Gateway.Tests;

public sealed class FlowRuleGraphValidatorTests
{
    [Fact]
    public void Validate_RejectsDisconnectedAction()
    {
        FlowRuleDefinition rule = CreateLinearRule();
        rule.Nodes.Add(new FlowRuleNode
        {
            Id = "orphan-webhook",
            NodeType = FlowRuleNodeTypes.WebhookCall,
            Label = "Orphan webhook",
            WebhookUrl = "https://example.com/hook"
        });

        IList<string> errors = FlowRuleGraphValidator.Validate(rule);

        Assert.Contains(errors, error => error.Contains("动作节点必须连接", StringComparison.Ordinal));
    }

    [Fact]
    public void SimpleCompiler_OnlyIncludesReachableActions()
    {
        FlowRuleDefinition rule = CreateLinearRule();
        rule.Nodes.Add(new FlowRuleNode
        {
            Id = "orphan-email",
            NodeType = FlowRuleNodeTypes.EmailNotify,
            EmailSmtpHost = "smtp.example.com"
        });

        Assert.True(FlowRuleCompiler.TryCompile(rule, out EdgeRuleConfig? compiled));
        Assert.NotNull(compiled);
        Assert.Single(compiled.Actions);
        Assert.Equal(FlowRuleNodeTypes.MqttPublish, compiled.Actions[0].ActionType);
    }

    [Fact]
    public void Validate_RejectsCycle()
    {
        FlowRuleDefinition rule = CreateLinearRule();
        rule.Edges.Add(new FlowRuleEdge { SourceNodeId = "mqtt", TargetNodeId = "condition" });

        IList<string> errors = FlowRuleGraphValidator.Validate(rule);

        Assert.Contains(errors, error => error.Contains("环路", StringComparison.Ordinal));
    }

    /// <summary>
    /// 验证流程编译会把值处理脚本的固定发布版本传给运行规则。
    /// </summary>
    [Fact]
    public void SimpleCompiler_ValueScriptNode_ShouldPreservePinnedVersion()
    {
        FlowRuleDefinition rule = CreateLinearRule();
        FlowRuleNode condition = rule.Nodes.Single(node => node.Id == "condition");
        FlowRuleNode script = new()
        {
            Id = "value-script",
            NodeType = FlowRuleNodeTypes.ValueScript,
            Label = "正弦处理",
            ValueScriptId = "script-1",
            ValueScriptVersion = 3,
            TransformTimeoutMilliseconds = 120
        };
        rule.Nodes.Add(script);
        rule.Edges.RemoveAll(edge => edge.SourceNodeId == "tag" && edge.TargetNodeId == condition.Id);
        rule.Edges.Add(new FlowRuleEdge { SourceNodeId = "tag", TargetNodeId = script.Id });
        rule.Edges.Add(new FlowRuleEdge { SourceNodeId = script.Id, TargetNodeId = condition.Id });

        Assert.True(FlowRuleCompiler.TryCompile(rule, out EdgeRuleConfig? compiled));
        Assert.NotNull(compiled);
        Assert.Equal("script-1", compiled.ValueScriptId);
        Assert.Equal(3, compiled.ValueScriptVersion);
        Assert.Equal(120, compiled.TransformTimeoutMilliseconds);
    }

    private static FlowRuleDefinition CreateLinearRule()
    {
        FlowRuleNode tag = new FlowRuleNode
        {
            Id = "tag",
            NodeType = FlowRuleNodeTypes.TagInput,
            TagId = "tag:pressure",
            TagName = "Pressure"
        };
        FlowRuleNode condition = new FlowRuleNode
        {
            Id = "condition",
            NodeType = FlowRuleNodeTypes.Condition,
            Operator = EdgeRuleComparisonOperator.GreaterThan.ToString(),
            CompareValue = 10D
        };
        FlowRuleNode mqtt = new FlowRuleNode
        {
            Id = "mqtt",
            NodeType = FlowRuleNodeTypes.MqttPublish,
            PublishToMqtt = true
        };
        return new FlowRuleDefinition
        {
            Id = "flow",
            Name = "Flow",
            Enabled = true,
            Nodes = { tag, condition, mqtt },
            Edges =
            {
                new FlowRuleEdge { SourceNodeId = tag.Id, TargetNodeId = condition.Id },
                new FlowRuleEdge { SourceNodeId = condition.Id, TargetNodeId = mqtt.Id }
            }
        };
    }
}

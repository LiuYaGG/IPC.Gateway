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

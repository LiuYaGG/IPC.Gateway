/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：FlowRuleEngineServiceTests
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Tests
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
using System.Text.Json;
using IPC.EdgeGateway;
using IPC.Gateway.Core.Domain.Gateway;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Core.Resilience;
using IPC.Runtime.Api;
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;
using IPC.Runtime.Values;

namespace IPC.Gateway.Tests;

public sealed class FlowRuleEngineServiceTests
{
    [Fact]
    public void FlowRule_WithIndirectTagInput_Triggers()
    {
        using FlowRuleHarness harness = FlowRuleHarness.Start(IndirectFlowRule());

        harness.Raise("Pressure", 11D);

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.Equal(1, status.RuleCount);
        Assert.Equal(1, status.TriggeredCount);
        Assert.True(status.Rules.Single().IsActive);
    }

    [Fact]
    public void FlowRule_WithFallbackPointCode_StillMatchesDeviceAndTag()
    {
        FlowRuleDefinition rule = IndirectFlowRule();
        rule.Nodes.Single(node => node.Id == "tag").PointCode = "Pressure";
        using FlowRuleHarness harness = FlowRuleHarness.Start(rule);

        harness.Raise("Pressure", 11D);

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.Equal(1, status.TriggeredCount);
        Assert.True(status.Rules.Single().IsActive);
    }

    [Fact]
    public void FlowRule_WithDuplicatePointCode_DoesNotUseOtherDevice()
    {
        using FlowRuleHarness harness = FlowRuleHarness.Start(BarcodeCombinationFlowRule());

        harness.Raise("D1007", string.Empty, "AskBARCODE2", "AskBARCODE2", 1200D);
        harness.Raise("D1002", string.Empty, "AskBARCODE1", "AskBARCODE1", 1200D);

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.Equal(0, status.TriggeredCount);

        harness.Raise("D1002", string.Empty, "AskBARCODE2", "AskBARCODE2", 1200D);

        status = harness.Engine.GetStatus();
        Assert.Equal(1, status.TriggeredCount);

        EdgeRuleRuntimeEvent ruleEvent = status.RecentEvents.Single();
        Assert.Equal("D1002", ruleEvent.Snapshot.DeviceName);
        Assert.Equal("AskBARCODE2", ruleEvent.Snapshot.TagName);
        Assert.Equal(2, ruleEvent.SourceValues.Count);
        Assert.All(ruleEvent.SourceValues, item => Assert.Equal("D1002", item.Snapshot.DeviceName));
        Assert.Contains(ruleEvent.SourceValues, item => item.Snapshot.TagName == "AskBARCODE1" && item.Snapshot.ValueText == "1200");
        Assert.Contains(ruleEvent.SourceValues, item => item.Snapshot.TagName == "AskBARCODE2" && item.Snapshot.ValueText == "1200");

        using JsonDocument payload = JsonDocument.Parse(ruleEvent.Payload);
        JsonElement root = payload.RootElement;
        Assert.Equal("D1002", root.GetProperty("device").GetString());
        Assert.Equal("AskBARCODE2", root.GetProperty("tag").GetString());
        JsonElement sourceValues = root.GetProperty("sourceValues");
        Assert.Equal(JsonValueKind.Array, sourceValues.ValueKind);
        Assert.Equal(2, sourceValues.GetArrayLength());
        Assert.All(sourceValues.EnumerateArray(), item => Assert.Equal("D1002", item.GetProperty("device").GetString()));
        Assert.Contains(sourceValues.EnumerateArray(), item => item.GetProperty("tag").GetString() == "AskBARCODE1");
        Assert.Contains(sourceValues.EnumerateArray(), item => item.GetProperty("tag").GetString() == "AskBARCODE2");
    }

    [Fact]
    public void FlowRule_SimpleCompiledMode_TriggersFromCompiledProjectRule()
    {
        FlowRuleDefinition rule = SimpleCompiledFlowRule();
        ProjectConfig project = new ProjectConfig();
        project.FlowRules.Add(rule);
        FlowRuleCompiler.SyncCompiledRule(project, rule, string.Empty);
        using FlowRuleHarness harness = FlowRuleHarness.StartProject(project);

        harness.Raise("Temperature", 11D);

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.Equal(FlowRuleModes.SimpleCompiled, rule.Mode);
        Assert.Equal(1, status.RuleCount);
        Assert.Equal(1, status.TriggeredCount);
    }

    [Fact]
    public void FlowRule_Hysteresis_HoldsUntilClearThreshold()
    {
        using FlowRuleHarness harness = FlowRuleHarness.Start(HysteresisFlowRule());

        harness.Raise("Temperature", 11D);

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.True(status.Rules.Single().IsActive);
        Assert.Equal("HysteresisHigh", status.Rules.Single().ActiveState);
        Assert.Equal(1, status.TriggeredCount);

        harness.Raise("Temperature", 9D);

        status = harness.Engine.GetStatus();
        Assert.True(status.Rules.Single().IsActive);
        Assert.Equal(1, status.TriggeredCount);

        harness.Raise("Temperature", 7D);

        status = harness.Engine.GetStatus();
        Assert.False(status.Rules.Single().IsActive);
        Assert.Equal(1, status.ClearedCount);
    }

    [Fact]
    public void FlowRule_MultiLevelAlarm_UsesMatchedLevel()
    {
        using FlowRuleHarness harness = FlowRuleHarness.Start(MultiLevelFlowRule());

        harness.Raise("Temperature", 25D);

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.True(status.Rules.Single().IsActive);
        Assert.Equal("Critical", status.Rules.Single().ActiveState);
        Assert.Equal(1, status.TriggeredCount);
    }

    [Fact]
    public void FlowRule_Expression_TriggersFromValueExpression()
    {
        using FlowRuleHarness harness = FlowRuleHarness.Start(ExpressionFlowRule());

        harness.Raise("Temperature", 11D);

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.True(status.Rules.Single().IsActive);
        Assert.Equal("Expression", status.Rules.Single().ActiveState);
        Assert.Equal(1, status.TriggeredCount);
    }

    [Fact]
    public void FlowRule_Transform_MultiplierOffsetAndAbsolute_Triggers()
    {
        using FlowRuleHarness harness = FlowRuleHarness.Start(TransformFlowRule());

        harness.Raise("Temperature", -5D);

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.True(status.Rules.Single().IsActive);
        Assert.Equal(1, status.TriggeredCount);
    }

    [Fact]
    public void FlowRule_TransformExpression_Triggers()
    {
        using FlowRuleHarness harness = FlowRuleHarness.Start(TransformExpressionFlowRule());

        harness.Raise("Temperature", -5D);

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.True(status.Rules.Single().IsActive);
        Assert.Equal(1, status.TriggeredCount);
    }

    [Fact]
    public void FlowRule_FunctionNode_TriggersWithSandboxedExpression()
    {
        using FlowRuleHarness harness = FlowRuleHarness.Start(FunctionFlowRule());

        harness.Raise("Temperature", -5D);

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.True(status.Rules.Single().IsActive);
        Assert.Equal(1, status.TriggeredCount);
    }

    [Fact]
    public void FlowRule_FunctionNode_RejectsUnsafeExpression()
    {
        using FlowRuleHarness harness = FlowRuleHarness.Start(UnsafeFunctionFlowRule());

        harness.Raise("Temperature", 5D);

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.Equal(0, status.TriggeredCount);
        Assert.Equal(1, status.FailedEvaluationCount);
    }

    [Fact]
    public void FlowRule_Sequence_TriggersOnlyInOrder()
    {
        using FlowRuleHarness harness = FlowRuleHarness.Start(SequenceFlowRule(30));

        harness.Raise("Pressure", 25D);
        harness.Raise("Temperature", 11D);

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.Equal(0, status.TriggeredCount);

        harness.Raise("Pressure", 25D);

        status = harness.Engine.GetStatus();
        Assert.Equal(1, status.TriggeredCount);
        Assert.Equal("Sequence", status.RecentEvents.Single().State);
    }

    [Fact]
    public void FlowRule_Sequence_DoesNotTriggerAfterWindowExpired()
    {
        DateTime start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);
        using FlowRuleHarness harness = FlowRuleHarness.Start(SequenceFlowRule(5));

        harness.Raise("Temperature", 11D, start);
        harness.Raise("Pressure", 25D, start.AddSeconds(10));

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.Equal(0, status.TriggeredCount);
    }

    [Fact]
    public void FlowRule_Sequence_SupportsSameTagEscalation()
    {
        using FlowRuleHarness harness = FlowRuleHarness.Start(SameTagSequenceFlowRule());

        harness.Raise("Temperature", 11D);
        harness.Raise("Temperature", 21D);

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.Equal(1, status.TriggeredCount);
    }

    [Fact]
    public void FlowRule_QualityGate_TriggersOnBadQuality()
    {
        using FlowRuleHarness harness = FlowRuleHarness.Start(QualityGateFlowRule());

        harness.Raise("Temperature", 0D, TagQuality.ReadError);

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.True(status.Rules.Single().IsActive);
        Assert.Equal("Quality:ReadError", status.Rules.Single().ActiveState);
    }

    [Fact]
    public void FlowRule_QualityGateBeforeCondition_ClearsWhenQualityTurnsBad()
    {
        using FlowRuleHarness harness = FlowRuleHarness.Start(QualityGateBeforeConditionFlowRule());

        harness.Raise("Temperature", 11D);
        harness.Raise("Temperature", 11D, TagQuality.ReadError);

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.False(status.Rules.Single().IsActive);
        Assert.Equal(1, status.ClearedCount);
    }

    [Fact]
    public void FlowRule_SlidingWindow_TriggersFromAverage()
    {
        using FlowRuleHarness harness = FlowRuleHarness.Start(SlidingWindowFlowRule());

        harness.Raise("Temperature", 10D);
        harness.Raise("Temperature", 20D);

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.True(status.Rules.Single().IsActive);
        Assert.Equal("WindowAverage", status.Rules.Single().ActiveState);
    }

    [Fact]
    public void FlowRule_WindowCalculation_TriggersFromMax()
    {
        using FlowRuleHarness harness = FlowRuleHarness.Start(WindowCalculationFlowRule());

        harness.Raise("Temperature", 10D);
        harness.Raise("Temperature", 22D);

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.True(status.Rules.Single().IsActive);
        Assert.Equal("WindowMax", status.Rules.Single().ActiveState);
    }

    [Fact]
    public void FlowRule_Aggregation_TriggersFromRelatedSnapshot()
    {
        using FlowRuleHarness harness = FlowRuleHarness.Start(AggregationFlowRule());

        harness.Raise("Temperature", 10D);
        harness.Raise("Pressure", 40D);

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.True(status.Rules.Single().IsActive);
        Assert.Equal("AggregationAverage", status.Rules.Single().ActiveState);
    }

    [Fact]
    public void FlowRule_Trend_TriggersFromRisingSlope()
    {
        DateTime start = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Local);
        using FlowRuleHarness harness = FlowRuleHarness.Start(TrendFlowRule());

        harness.Raise("Temperature", 10D, start);
        harness.Raise("Temperature", 13D, start.AddSeconds(2));

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.True(status.Rules.Single().IsActive);
        Assert.Equal("TrendRising", status.Rules.Single().ActiveState);
    }

    [Fact]
    public void FlowRule_StateMachine_TriggersFromTextState()
    {
        using FlowRuleHarness harness = FlowRuleHarness.Start(StateMachineFlowRule());

        harness.RaiseText("MachineState", "RUN");

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.True(status.Rules.Single().IsActive);
        Assert.Equal("Running", status.Rules.Single().ActiveState);
    }

    [Fact]
    public void FlowRule_CycleTime_TriggersWhenCycleIsTooSlow()
    {
        DateTime start = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Local);
        using FlowRuleHarness harness = FlowRuleHarness.Start(CycleTimeFlowRule());

        harness.RaiseText("MachineState", "START", start);
        harness.RaiseText("MachineState", "END", start.AddSeconds(10));

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.True(status.Rules.Single().IsActive);
        Assert.Equal("CycleTooSlow", status.Rules.Single().ActiveState);
    }

    [Fact]
    public void FlowRule_ProcessTakt_TriggersWhenTaktIsTooSlow()
    {
        DateTime start = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Local);
        using FlowRuleHarness harness = FlowRuleHarness.Start(ProcessTaktFlowRule());

        harness.RaiseText("MachineState", "START", start);
        harness.RaiseText("MachineState", "END", start.AddSeconds(7));

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.True(status.Rules.Single().IsActive);
        Assert.Equal("TaktTooSlow", status.Rules.Single().ActiveState);
    }

    [Fact]
    public void FlowRule_AnomalyDetection_TriggersFromSpike()
    {
        using FlowRuleHarness harness = FlowRuleHarness.Start(AnomalyDetectionFlowRule());

        harness.Raise("Temperature", 10D);
        harness.Raise("Temperature", 11D);
        harness.Raise("Temperature", 40D);

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.True(status.Rules.Single().IsActive);
        Assert.Equal("AnomalySpike", status.Rules.Single().ActiveState);
    }

    [Fact]
    public void FlowRule_ModelInference_TriggersFromInferenceScore()
    {
        FakeModelInferenceService inference = new FakeModelInferenceService(0.82D);
        using FlowRuleHarness harness = FlowRuleHarness.StartWithInference(inference, ModelInferenceFlowRule());

        harness.Raise("Temperature", 10D);

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.True(status.Rules.Single().IsActive);
        Assert.Equal("DeviceAnomaly", status.Rules.Single().ActiveState);
        Assert.Equal(1, status.TriggeredCount);
        Assert.NotNull(inference.LastRequest);
        Assert.Equal("Models/anomaly.onnx", inference.LastRequest!.ModelPath);
        Assert.Single(inference.LastRequest.Features);
        Assert.Equal(10F, inference.LastRequest.Features[0]);
    }

    [Fact]
    public void FlowRule_TagRelation_TriggersFromRelatedSnapshot()
    {
        using FlowRuleHarness harness = FlowRuleHarness.Start(TagRelationFlowRule());

        harness.Raise("Pressure", 10D);
        harness.Raise("Temperature", 16D);

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.True(status.Rules.Single().IsActive);
        Assert.Equal("TagRelation", status.Rules.Single().ActiveState);
    }

    [Fact]
    public void FlowRule_ContextGate_TriggersFromTextContext()
    {
        using FlowRuleHarness harness = FlowRuleHarness.Start(ContextGateFlowRule());

        harness.RaiseText("MachineState", "SHIFT-A");

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.True(status.Rules.Single().IsActive);
        Assert.Equal("Shift", status.Rules.Single().ActiveState);
    }

    [Fact]
    public void FlowRule_Duration_ClearConfirmationDelaysClear()
    {
        using FlowRuleHarness harness = FlowRuleHarness.Start(ClearDurationFlowRule());

        harness.Raise("Temperature", 11D);
        harness.Raise("Temperature", 5D);

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.True(status.Rules.Single().IsActive);

        Thread.Sleep(1100);
        harness.Raise("Temperature", 5D);

        status = harness.Engine.GetStatus();
        Assert.False(status.Rules.Single().IsActive);
        Assert.Equal(1, status.ClearedCount);
    }

    [Fact]
    public void GatewayProjectAggregate_UpdateFlowRuleIncrementsVersionAndPublishedVersion()
    {
        ProjectConfig project = new ProjectConfig();
        GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
        FlowRuleDefinition added = aggregate.AddFlowRule(IndirectFlowRule());

        FlowRuleDefinition update = ProjectConfigCloner.CloneFlowRule(added)!;
        update.Description = "updated";
        FlowRuleDefinition updated = aggregate.UpdateFlowRule(added.Id, update);

        Assert.Equal(2, updated.Version);
        Assert.Equal(2, updated.PublishedVersion);
        Assert.Equal(FlowRuleLifecycleStates.Published, updated.LifecycleState);
    }

    [Fact]
    public void FlowRuleCompiler_WithEmailAndWebhookActions_MapsActions()
    {
        FlowRuleDefinition rule = NotificationActionFlowRule();

        bool compiled = FlowRuleCompiler.TryCompile(rule, out EdgeRuleConfig? config);

        Assert.True(compiled);
        Assert.NotNull(config);
        EdgeRuleConfig compiledRule = config;
        Assert.Equal(2, compiledRule.Actions.Count);
        Assert.Contains(compiledRule.Actions, action =>
            action.ActionType == FlowRuleNodeTypes.EmailNotify &&
            action.EmailSmtpHost == "smtp.example.com" &&
            action.EmailTo == "ops@example.com");
        Assert.Contains(compiledRule.Actions, action =>
            action.ActionType == FlowRuleNodeTypes.WebhookCall &&
            action.WebhookUrl == "http://example.com/hook" &&
            action.WebhookMethod == "POST");
    }

    [Fact]
    public void FlowRuleCompiler_WithLifecyclePolicyAndDebugProbe_MapsRuntimePolicies()
    {
        FlowRuleDefinition rule = PolicyAndDebugFlowRule();

        bool compiled = FlowRuleCompiler.TryCompile(rule, out EdgeRuleConfig? config);

        Assert.True(compiled);
        Assert.NotNull(config);
        EdgeRuleConfig compiledRule = config;
        Assert.Equal("Critical", compiledRule.AlarmSeverity);
        Assert.Equal(5, compiledRule.AlarmSuppressSeconds);
        Assert.Equal(30, compiledRule.AlarmEscalateAfterSeconds);
        Assert.Equal(2, compiledRule.ActionDelaySeconds);
        Assert.Equal(10, compiledRule.ActionCooldownSeconds);
        Assert.Contains(compiledRule.Actions, action =>
            action.ActionType == FlowRuleNodeTypes.DebugProbe &&
            action.Enabled &&
            action.DebugLabel == "trace");
    }

    [Fact]
    public void FlowRuleCompiler_NullDefinitionReturnsFalseAndNullRule()
    {
        bool compiled = FlowRuleCompiler.TryCompile(null, out EdgeRuleConfig? config);

        Assert.False(compiled);
        Assert.Null(config);
    }

    [Fact]
    public void FlowRuleCompiler_IgnoresNullNodes()
    {
        FlowRuleDefinition rule = NotificationActionFlowRule();
        rule.Nodes.Insert(1, null!);

        bool compiled = FlowRuleCompiler.TryCompile(rule, out EdgeRuleConfig? config);

        Assert.True(compiled);
        Assert.NotNull(config);
        Assert.Equal(2, config.Actions.Count);
    }

    [Fact]
    public void FlowRule_RuntimeCompiler_IgnoresNullFlowRulesNodesAndEdges()
    {
        FlowRuleDefinition rule = SequenceFlowRule(30);
        rule.Nodes.Insert(0, null!);
        rule.Edges.Insert(0, null!);
        using FlowRuleHarness harness = FlowRuleHarness.Start(null!, rule);

        harness.Raise("Temperature", 11D);
        harness.Raise("Pressure", 25D);

        EdgeRuleEngineStatus status = harness.Engine.GetStatus();
        Assert.Equal(1, status.TriggeredCount);
    }

    private static FlowRuleDefinition IndirectFlowRule()
    {
        FlowRuleNode tag = new FlowRuleNode
        {
            Id = "tag",
            NodeType = FlowRuleNodeTypes.TagInput,
            DeviceName = "Boiler",
            GroupName = "Main",
            TagName = "Pressure",
            PointCode = "Boiler.Main.Pressure",
            DataType = "Double"
        };

        FlowRuleNode logic = new FlowRuleNode
        {
            Id = "logic",
            NodeType = FlowRuleNodeTypes.Logic,
            LogicalOperator = EdgeRuleLogicalOperator.And.ToString()
        };

        FlowRuleNode condition = new FlowRuleNode
        {
            Id = "condition",
            NodeType = FlowRuleNodeTypes.Condition,
            ConditionType = EdgeRuleConditionType.Condition.ToString(),
            Operator = EdgeRuleComparisonOperator.GreaterThan.ToString(),
            CompareValue = 10D,
            PublishToMqtt = true
        };

        FlowRuleNode mqtt = new FlowRuleNode
        {
            Id = "mqtt",
            NodeType = FlowRuleNodeTypes.MqttPublish,
            PublishToMqtt = true,
            TopicTemplate = "ipc/rule/{pointCode}/{ruleName}"
        };

        return new FlowRuleDefinition
        {
            Id = "flow-indirect-source",
            Name = "Indirect flow",
            Enabled = true,
            Mode = FlowRuleModes.Flow,
            Nodes = { tag, logic, condition, mqtt },
            Edges =
            {
                new FlowRuleEdge { SourceNodeId = tag.Id, TargetNodeId = logic.Id },
                new FlowRuleEdge { SourceNodeId = logic.Id, TargetNodeId = condition.Id },
                new FlowRuleEdge { SourceNodeId = condition.Id, TargetNodeId = mqtt.Id }
            }
        };
    }

    private static FlowRuleDefinition HysteresisFlowRule()
    {
        FlowRuleNode tag = TemperatureTagNode();
        FlowRuleNode condition = new FlowRuleNode
        {
            Id = "hysteresis",
            NodeType = FlowRuleNodeTypes.Hysteresis,
            HysteresisMode = "High",
            HysteresisOnValue = 10D,
            HysteresisOffValue = 8D,
            PublishToMqtt = true
        };

        return CreateLinearFlow("flow-hysteresis", "Hysteresis flow", tag, condition);
    }

    private static FlowRuleDefinition SimpleCompiledFlowRule()
    {
        FlowRuleNode tag = TemperatureTagNode();
        FlowRuleNode condition = new FlowRuleNode
        {
            Id = "condition",
            NodeType = FlowRuleNodeTypes.Condition,
            Operator = EdgeRuleComparisonOperator.GreaterThan.ToString(),
            CompareValue = 10D,
            PublishToMqtt = true
        };

        return CreateLinearFlow("flow-simple-compiled", "Simple compiled flow", tag, condition);
    }

    private static FlowRuleDefinition BarcodeCombinationFlowRule()
    {
        FlowRuleNode firstTag = new FlowRuleNode
        {
            Id = "barcode1-tag",
            NodeType = FlowRuleNodeTypes.TagInput,
            DeviceName = "D1002",
            GroupName = string.Empty,
            TagName = "AskBARCODE1",
            PointCode = "AskBARCODE1",
            DataType = "Double"
        };
        FlowRuleNode secondTag = new FlowRuleNode
        {
            Id = "barcode2-tag",
            NodeType = FlowRuleNodeTypes.TagInput,
            DeviceName = "D1002",
            GroupName = string.Empty,
            TagName = "AskBARCODE2",
            PointCode = "AskBARCODE2",
            DataType = "Double"
        };
        FlowRuleNode firstCondition = new FlowRuleNode
        {
            Id = "barcode1-condition",
            NodeType = FlowRuleNodeTypes.Condition,
            Operator = EdgeRuleComparisonOperator.GreaterThan.ToString(),
            CompareValue = 1000D
        };
        FlowRuleNode secondCondition = new FlowRuleNode
        {
            Id = "barcode2-condition",
            NodeType = FlowRuleNodeTypes.Condition,
            Operator = EdgeRuleComparisonOperator.GreaterThan.ToString(),
            CompareValue = 1000D
        };
        FlowRuleNode logic = new FlowRuleNode
        {
            Id = "logic",
            NodeType = FlowRuleNodeTypes.Logic,
            LogicalOperator = EdgeRuleLogicalOperator.And.ToString()
        };
        FlowRuleNode mqtt = new FlowRuleNode
        {
            Id = "mqtt",
            NodeType = FlowRuleNodeTypes.MqttPublish,
            PublishToMqtt = true,
            TopicTemplate = "ipc/rule/{pointCode}/{ruleName}"
        };

        return new FlowRuleDefinition
        {
            Id = "flow-barcode-combination",
            Name = "Barcode combination flow",
            Enabled = true,
            Mode = FlowRuleModes.Flow,
            Nodes = { firstTag, secondTag, firstCondition, secondCondition, logic, mqtt },
            Edges =
            {
                new FlowRuleEdge { SourceNodeId = firstTag.Id, TargetNodeId = firstCondition.Id },
                new FlowRuleEdge { SourceNodeId = secondTag.Id, TargetNodeId = secondCondition.Id },
                new FlowRuleEdge { SourceNodeId = firstCondition.Id, TargetNodeId = logic.Id },
                new FlowRuleEdge { SourceNodeId = secondCondition.Id, TargetNodeId = logic.Id },
                new FlowRuleEdge { SourceNodeId = logic.Id, TargetNodeId = mqtt.Id }
            }
        };
    }

    private static FlowRuleDefinition MultiLevelFlowRule()
    {
        FlowRuleNode tag = TemperatureTagNode();
        FlowRuleNode condition = new FlowRuleNode
        {
            Id = "multi-level",
            NodeType = FlowRuleNodeTypes.MultiLevelAlarm,
            PublishToMqtt = true,
            AlarmLevels =
            {
                new FlowRuleAlarmLevel
                {
                    Id = "warning",
                    Name = "Warning",
                    Severity = "Warning",
                    Operator = EdgeRuleComparisonOperator.GreaterThanOrEqual.ToString(),
                    CompareValue = 10D
                },
                new FlowRuleAlarmLevel
                {
                    Id = "critical",
                    Name = "Critical",
                    Severity = "Critical",
                    Operator = EdgeRuleComparisonOperator.GreaterThanOrEqual.ToString(),
                    CompareValue = 20D
                }
            }
        };

        return CreateLinearFlow("flow-multi-level", "Multi level flow", tag, condition);
    }

    private static FlowRuleDefinition ExpressionFlowRule()
    {
        FlowRuleNode tag = TemperatureTagNode();
        FlowRuleNode condition = new FlowRuleNode
        {
            Id = "expression",
            NodeType = FlowRuleNodeTypes.Expression,
            Expression = "{value} * 2 > 20",
            PublishToMqtt = true
        };

        return CreateLinearFlow("flow-expression", "Expression flow", tag, condition);
    }

    private static FlowRuleDefinition TransformFlowRule()
    {
        FlowRuleNode tag = TemperatureTagNode();
        FlowRuleNode transform = new FlowRuleNode
        {
            Id = "transform",
            NodeType = FlowRuleNodeTypes.Transform,
            TransformUseAbsolute = true,
            TransformMultiplier = 10D,
            TransformOffset = 2D
        };
        FlowRuleNode condition = new FlowRuleNode
        {
            Id = "condition",
            NodeType = FlowRuleNodeTypes.Condition,
            Operator = EdgeRuleComparisonOperator.GreaterThan.ToString(),
            CompareValue = 50D,
            PublishToMqtt = true
        };

        return CreateTransformFlow("flow-transform", "Transform flow", tag, transform, condition);
    }

    private static FlowRuleDefinition TransformExpressionFlowRule()
    {
        FlowRuleNode tag = TemperatureTagNode();
        FlowRuleNode transform = new FlowRuleNode
        {
            Id = "transform",
            NodeType = FlowRuleNodeTypes.Transform,
            TransformMultiplier = 1D,
            TransformExpression = "abs({value}) * 10"
        };
        FlowRuleNode condition = new FlowRuleNode
        {
            Id = "condition",
            NodeType = FlowRuleNodeTypes.Condition,
            Operator = EdgeRuleComparisonOperator.GreaterThan.ToString(),
            CompareValue = 40D,
            PublishToMqtt = true
        };

        return CreateTransformFlow("flow-transform-expression", "Transform expression flow", tag, transform, condition);
    }

    private static FlowRuleDefinition FunctionFlowRule()
    {
        FlowRuleNode tag = TemperatureTagNode();
        FlowRuleNode function = new FlowRuleNode
        {
            Id = "function",
            NodeType = FlowRuleNodeTypes.Function,
            TransformExpression = "abs({value}) * 10",
            TransformTimeoutMilliseconds = 100
        };
        FlowRuleNode condition = new FlowRuleNode
        {
            Id = "condition",
            NodeType = FlowRuleNodeTypes.Condition,
            Operator = EdgeRuleComparisonOperator.GreaterThan.ToString(),
            CompareValue = 40D,
            PublishToMqtt = true
        };

        return CreateTransformFlow("flow-function", "Function flow", tag, function, condition);
    }

    private static FlowRuleDefinition UnsafeFunctionFlowRule()
    {
        FlowRuleNode tag = TemperatureTagNode();
        FlowRuleNode function = new FlowRuleNode
        {
            Id = "function",
            NodeType = FlowRuleNodeTypes.Function,
            TransformExpression = "System.IO.File.Delete({value})",
            TransformTimeoutMilliseconds = 100
        };
        FlowRuleNode condition = new FlowRuleNode
        {
            Id = "condition",
            NodeType = FlowRuleNodeTypes.Condition,
            Operator = EdgeRuleComparisonOperator.GreaterThan.ToString(),
            CompareValue = 0D,
            PublishToMqtt = true
        };

        return CreateTransformFlow("flow-function-unsafe", "Unsafe function flow", tag, function, condition);
    }

    private static FlowRuleDefinition NotificationActionFlowRule()
    {
        FlowRuleNode tag = TemperatureTagNode();
        FlowRuleNode condition = new FlowRuleNode
        {
            Id = "condition",
            NodeType = FlowRuleNodeTypes.Condition,
            Operator = EdgeRuleComparisonOperator.GreaterThan.ToString(),
            CompareValue = 10D
        };
        FlowRuleNode email = new FlowRuleNode
        {
            Id = "email",
            NodeType = FlowRuleNodeTypes.EmailNotify,
            EmailSmtpHost = "smtp.example.com",
            EmailSmtpPort = 587,
            EmailFrom = "gateway@example.com",
            EmailTo = "ops@example.com",
            EmailSubjectTemplate = "{ruleName}",
            EmailBodyTemplate = "{message}"
        };
        FlowRuleNode webhook = new FlowRuleNode
        {
            Id = "webhook",
            NodeType = FlowRuleNodeTypes.WebhookCall,
            WebhookUrl = "http://example.com/hook",
            WebhookMethod = "POST",
            WebhookBodyTemplate = "{payload}"
        };

        return new FlowRuleDefinition
        {
            Id = "flow-actions",
            Name = "Action flow",
            Enabled = true,
            Mode = FlowRuleModes.Flow,
            Nodes = { tag, condition, email, webhook },
            Edges =
            {
                new FlowRuleEdge { SourceNodeId = tag.Id, TargetNodeId = condition.Id },
                new FlowRuleEdge { SourceNodeId = condition.Id, TargetNodeId = email.Id },
                new FlowRuleEdge { SourceNodeId = condition.Id, TargetNodeId = webhook.Id }
            }
        };
    }

    private static FlowRuleDefinition SequenceFlowRule(int windowSeconds)
    {
        FlowRuleNode temperatureTag = TemperatureTagNode();
        temperatureTag.Id = "temperature-tag";
        temperatureTag.X = 40;
        temperatureTag.Y = 80;

        FlowRuleNode pressureTag = new FlowRuleNode
        {
            Id = "pressure-tag",
            NodeType = FlowRuleNodeTypes.TagInput,
            DeviceName = "Boiler",
            GroupName = "Main",
            TagName = "Pressure",
            PointCode = "Boiler.Main.Pressure",
            DataType = "Double",
            X = 40,
            Y = 230
        };

        FlowRuleNode temperatureCondition = new FlowRuleNode
        {
            Id = "temperature-condition",
            NodeType = FlowRuleNodeTypes.Condition,
            Operator = EdgeRuleComparisonOperator.GreaterThan.ToString(),
            CompareValue = 10D,
            X = 260,
            Y = 80
        };

        FlowRuleNode pressureCondition = new FlowRuleNode
        {
            Id = "pressure-condition",
            NodeType = FlowRuleNodeTypes.Condition,
            Operator = EdgeRuleComparisonOperator.GreaterThan.ToString(),
            CompareValue = 20D,
            X = 480,
            Y = 230
        };

        FlowRuleNode sequence = new FlowRuleNode
        {
            Id = "sequence",
            NodeType = FlowRuleNodeTypes.Sequence,
            SequenceWindowSeconds = windowSeconds,
            SequenceResetOnMismatch = true,
            X = 700,
            Y = 150
        };

        FlowRuleNode mqtt = new FlowRuleNode
        {
            Id = "mqtt",
            NodeType = FlowRuleNodeTypes.MqttPublish,
            PublishToMqtt = true,
            TopicTemplate = "ipc/rule/{pointCode}/{ruleName}",
            X = 920,
            Y = 150
        };

        return new FlowRuleDefinition
        {
            Id = "flow-sequence-" + windowSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Name = "Sequence flow",
            Enabled = true,
            Mode = FlowRuleModes.Flow,
            Nodes = { temperatureTag, pressureTag, temperatureCondition, pressureCondition, sequence, mqtt },
            Edges =
            {
                new FlowRuleEdge { SourceNodeId = temperatureTag.Id, TargetNodeId = temperatureCondition.Id },
                new FlowRuleEdge { SourceNodeId = pressureTag.Id, TargetNodeId = pressureCondition.Id },
                new FlowRuleEdge { SourceNodeId = temperatureCondition.Id, TargetNodeId = sequence.Id },
                new FlowRuleEdge { SourceNodeId = pressureCondition.Id, TargetNodeId = sequence.Id },
                new FlowRuleEdge { SourceNodeId = sequence.Id, TargetNodeId = mqtt.Id }
            }
        };
    }

    private static FlowRuleDefinition SameTagSequenceFlowRule()
    {
        FlowRuleNode tag = TemperatureTagNode();
        tag.X = 40;
        tag.Y = 140;

        FlowRuleNode first = new FlowRuleNode
        {
            Id = "temperature-step-1",
            NodeType = FlowRuleNodeTypes.Condition,
            Operator = EdgeRuleComparisonOperator.GreaterThan.ToString(),
            CompareValue = 10D,
            X = 260,
            Y = 100
        };

        FlowRuleNode second = new FlowRuleNode
        {
            Id = "temperature-step-2",
            NodeType = FlowRuleNodeTypes.Condition,
            Operator = EdgeRuleComparisonOperator.GreaterThan.ToString(),
            CompareValue = 20D,
            X = 480,
            Y = 180
        };

        FlowRuleNode sequence = new FlowRuleNode
        {
            Id = "sequence",
            NodeType = FlowRuleNodeTypes.Sequence,
            SequenceWindowSeconds = 30,
            SequenceResetOnMismatch = true,
            X = 700,
            Y = 140
        };

        FlowRuleNode mqtt = new FlowRuleNode
        {
            Id = "mqtt",
            NodeType = FlowRuleNodeTypes.MqttPublish,
            PublishToMqtt = true,
            TopicTemplate = "ipc/rule/{pointCode}/{ruleName}",
            X = 920,
            Y = 140
        };

        return new FlowRuleDefinition
        {
            Id = "flow-sequence-same-tag",
            Name = "Same tag sequence flow",
            Enabled = true,
            Mode = FlowRuleModes.Flow,
            Nodes = { tag, first, second, sequence, mqtt },
            Edges =
            {
                new FlowRuleEdge { SourceNodeId = tag.Id, TargetNodeId = first.Id },
                new FlowRuleEdge { SourceNodeId = tag.Id, TargetNodeId = second.Id },
                new FlowRuleEdge { SourceNodeId = first.Id, TargetNodeId = sequence.Id },
                new FlowRuleEdge { SourceNodeId = second.Id, TargetNodeId = sequence.Id },
                new FlowRuleEdge { SourceNodeId = sequence.Id, TargetNodeId = mqtt.Id }
            }
        };
    }

    private static FlowRuleDefinition QualityGateFlowRule()
    {
        FlowRuleNode tag = TemperatureTagNode();
        FlowRuleNode quality = new FlowRuleNode
        {
            Id = "quality",
            NodeType = FlowRuleNodeTypes.QualityGate,
            QualityOperator = "NotIn",
            QualityValues = "Good"
        };

        return CreateLinearFlow("flow-quality", "Quality flow", tag, quality);
    }

    private static FlowRuleDefinition QualityGateBeforeConditionFlowRule()
    {
        FlowRuleNode tag = TemperatureTagNode();
        FlowRuleNode quality = new FlowRuleNode
        {
            Id = "quality",
            NodeType = FlowRuleNodeTypes.QualityGate,
            QualityOperator = "In",
            QualityValues = "Good"
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
            PublishToMqtt = true,
            TopicTemplate = "ipc/rule/{pointCode}/{ruleName}"
        };

        return new FlowRuleDefinition
        {
            Id = "flow-quality-gated-condition",
            Name = "Quality gated condition",
            Enabled = true,
            Mode = FlowRuleModes.Flow,
            Nodes = { tag, quality, condition, mqtt },
            Edges =
            {
                new FlowRuleEdge { SourceNodeId = tag.Id, TargetNodeId = quality.Id },
                new FlowRuleEdge { SourceNodeId = quality.Id, TargetNodeId = condition.Id },
                new FlowRuleEdge { SourceNodeId = condition.Id, TargetNodeId = mqtt.Id }
            }
        };
    }

    private static FlowRuleDefinition SlidingWindowFlowRule()
    {
        FlowRuleNode tag = TemperatureTagNode();
        FlowRuleNode window = new FlowRuleNode
        {
            Id = "window",
            NodeType = FlowRuleNodeTypes.SlidingWindow,
            WindowStatistic = "Average",
            WindowSeconds = 60,
            Operator = EdgeRuleComparisonOperator.GreaterThanOrEqual.ToString(),
            CompareValue = 15D
        };

        return CreateLinearFlow("flow-sliding-window", "Sliding window flow", tag, window);
    }

    private static FlowRuleDefinition WindowCalculationFlowRule()
    {
        FlowRuleNode tag = TemperatureTagNode();
        FlowRuleNode window = new FlowRuleNode
        {
            Id = "window-calculation",
            NodeType = FlowRuleNodeTypes.WindowCalculation,
            WindowStatistic = "Max",
            WindowSeconds = 60,
            Operator = EdgeRuleComparisonOperator.GreaterThanOrEqual.ToString(),
            CompareValue = 20D
        };

        return CreateLinearFlow("flow-window-calculation", "Window calculation flow", tag, window);
    }

    private static FlowRuleDefinition AggregationFlowRule()
    {
        FlowRuleNode tag = TemperatureTagNode();
        FlowRuleNode aggregation = new FlowRuleNode
        {
            Id = "aggregation",
            NodeType = FlowRuleNodeTypes.Aggregation,
            AggregationStatistic = "Average",
            Operator = EdgeRuleComparisonOperator.GreaterThan.ToString(),
            CompareValue = 20D,
            RelatedDeviceName = "Boiler",
            RelatedGroupName = "Main",
            RelatedTagName = "Pressure",
            RelatedPointCode = "Boiler.Main.Pressure",
            RelatedDataType = "Double"
        };

        return CreateLinearFlow("flow-aggregation", "Aggregation flow", tag, aggregation);
    }

    private static FlowRuleDefinition TrendFlowRule()
    {
        FlowRuleNode tag = TemperatureTagNode();
        FlowRuleNode trend = new FlowRuleNode
        {
            Id = "trend",
            NodeType = FlowRuleNodeTypes.Trend,
            TrendMode = "Rising",
            TrendWindowSeconds = 60,
            TrendMinSlopePerSecond = 1D
        };

        return CreateLinearFlow("flow-trend", "Trend flow", tag, trend);
    }

    private static FlowRuleDefinition StateMachineFlowRule()
    {
        FlowRuleNode tag = new FlowRuleNode
        {
            Id = "state-tag",
            NodeType = FlowRuleNodeTypes.TagInput,
            DeviceName = "Boiler",
            GroupName = "Main",
            TagName = "MachineState",
            PointCode = "Boiler.Main.MachineState",
            DataType = "String"
        };
        FlowRuleNode state = new FlowRuleNode
        {
            Id = "state-machine",
            NodeType = FlowRuleNodeTypes.StateMachine,
            StateName = "Running",
            StateExpectedValue = "RUN",
            StateClearValue = "STOP"
        };

        return CreateLinearFlow("flow-state-machine", "State machine flow", tag, state);
    }

    private static FlowRuleDefinition CycleTimeFlowRule()
    {
        FlowRuleNode tag = MachineStateTagNode();
        FlowRuleNode cycle = new FlowRuleNode
        {
            Id = "cycle",
            NodeType = FlowRuleNodeTypes.CycleTime,
            CycleStartValue = "START",
            CycleEndValue = "END",
            CycleMaxSeconds = 5
        };

        return CreateLinearFlow("flow-cycle-time", "Cycle time flow", tag, cycle);
    }

    private static FlowRuleDefinition ProcessTaktFlowRule()
    {
        FlowRuleNode tag = MachineStateTagNode();
        FlowRuleNode takt = new FlowRuleNode
        {
            Id = "process-takt",
            NodeType = FlowRuleNodeTypes.ProcessTakt,
            CycleStartValue = "START",
            CycleEndValue = "END",
            TaktTargetSeconds = 5D,
            TaktTolerancePercent = 10D
        };

        return CreateLinearFlow("flow-process-takt", "Process takt flow", tag, takt);
    }

    private static FlowRuleDefinition AnomalyDetectionFlowRule()
    {
        FlowRuleNode tag = TemperatureTagNode();
        FlowRuleNode anomaly = new FlowRuleNode
        {
            Id = "anomaly",
            NodeType = FlowRuleNodeTypes.AnomalyDetection,
            AnomalyMode = "Spike",
            AnomalyThreshold = 20D,
            AnomalyBaselineWindowSeconds = 60
        };

        return CreateLinearFlow("flow-anomaly", "Anomaly flow", tag, anomaly);
    }

    private static FlowRuleDefinition ModelInferenceFlowRule()
    {
        FlowRuleNode tag = TemperatureTagNode();
        FlowRuleNode model = new FlowRuleNode
        {
            Id = "model",
            NodeType = FlowRuleNodeTypes.ModelInference,
            ModelPurpose = "DeviceAnomaly",
            ModelPath = "Models/anomaly.onnx",
            ModelOperator = EdgeRuleComparisonOperator.GreaterThanOrEqual.ToString(),
            ModelThreshold = 0.5D,
            ModelTimeoutMilliseconds = 1000,
            PublishToMqtt = true
        };

        return CreateLinearFlow("flow-model-inference", "Model inference flow", tag, model);
    }

    private static FlowRuleDefinition TagRelationFlowRule()
    {
        FlowRuleNode tag = TemperatureTagNode();
        FlowRuleNode relation = new FlowRuleNode
        {
            Id = "relation",
            NodeType = FlowRuleNodeTypes.TagRelation,
            RelatedDeviceName = "Boiler",
            RelatedGroupName = "Main",
            RelatedTagName = "Pressure",
            RelatedPointCode = "Boiler.Main.Pressure",
            RelatedDataType = "Double",
            RelationOperator = EdgeRuleComparisonOperator.GreaterThan.ToString(),
            RelationMultiplier = 1D,
            RelationOffset = 5D
        };

        return CreateLinearFlow("flow-tag-relation", "Tag relation flow", tag, relation);
    }

    private static FlowRuleDefinition ContextGateFlowRule()
    {
        FlowRuleNode tag = MachineStateTagNode();
        FlowRuleNode context = new FlowRuleNode
        {
            Id = "context",
            NodeType = FlowRuleNodeTypes.ContextGate,
            ContextName = "Shift",
            ContextOperator = EdgeRuleComparisonOperator.Equal.ToString(),
            ContextExpectedValue = "SHIFT-A"
        };

        return CreateLinearFlow("flow-context-gate", "Context gate flow", tag, context);
    }

    private static FlowRuleDefinition PolicyAndDebugFlowRule()
    {
        FlowRuleNode tag = TemperatureTagNode();
        FlowRuleNode condition = new FlowRuleNode
        {
            Id = "condition",
            NodeType = FlowRuleNodeTypes.Condition,
            Operator = EdgeRuleComparisonOperator.GreaterThan.ToString(),
            CompareValue = 10D
        };
        FlowRuleNode lifecycle = new FlowRuleNode
        {
            Id = "lifecycle",
            NodeType = FlowRuleNodeTypes.AlarmLifecycle,
            AlarmSeverity = "Critical",
            AlarmSuppressSeconds = 5,
            AlarmEscalateAfterSeconds = 30
        };
        FlowRuleNode policy = new FlowRuleNode
        {
            Id = "policy",
            NodeType = FlowRuleNodeTypes.ActionPolicy,
            ActionDelaySeconds = 2,
            ActionCooldownSeconds = 10,
            ActionMaxPerMinute = 3
        };
        FlowRuleNode debug = new FlowRuleNode
        {
            Id = "debug",
            NodeType = FlowRuleNodeTypes.DebugProbe,
            DebugEnabled = true,
            DebugLabel = "trace",
            ActiveMessage = "{ruleName}:{state}"
        };

        return new FlowRuleDefinition
        {
            Id = "flow-policy-debug",
            Name = "Policy debug flow",
            Enabled = true,
            Mode = FlowRuleModes.Flow,
            Nodes = { tag, condition, lifecycle, policy, debug },
            Edges =
            {
                new FlowRuleEdge { SourceNodeId = tag.Id, TargetNodeId = condition.Id },
                new FlowRuleEdge { SourceNodeId = condition.Id, TargetNodeId = lifecycle.Id },
                new FlowRuleEdge { SourceNodeId = lifecycle.Id, TargetNodeId = policy.Id },
                new FlowRuleEdge { SourceNodeId = policy.Id, TargetNodeId = debug.Id }
            }
        };
    }

    private static FlowRuleDefinition ClearDurationFlowRule()
    {
        FlowRuleNode tag = TemperatureTagNode();
        FlowRuleNode condition = new FlowRuleNode
        {
            Id = "condition",
            NodeType = FlowRuleNodeTypes.Condition,
            Operator = EdgeRuleComparisonOperator.GreaterThan.ToString(),
            CompareValue = 10D
        };
        FlowRuleNode duration = new FlowRuleNode
        {
            Id = "duration",
            NodeType = FlowRuleNodeTypes.Duration,
            DurationSeconds = 0,
            ClearDurationSeconds = 1
        };
        FlowRuleNode mqtt = new FlowRuleNode
        {
            Id = "mqtt",
            NodeType = FlowRuleNodeTypes.MqttPublish,
            PublishToMqtt = true,
            PublishOnClear = true,
            TopicTemplate = "ipc/rule/{pointCode}/{ruleName}"
        };

        return new FlowRuleDefinition
        {
            Id = "flow-clear-duration",
            Name = "Clear duration flow",
            Enabled = true,
            Mode = FlowRuleModes.Flow,
            Nodes = { tag, condition, duration, mqtt },
            Edges =
            {
                new FlowRuleEdge { SourceNodeId = tag.Id, TargetNodeId = condition.Id },
                new FlowRuleEdge { SourceNodeId = condition.Id, TargetNodeId = duration.Id },
                new FlowRuleEdge { SourceNodeId = duration.Id, TargetNodeId = mqtt.Id }
            }
        };
    }

    private static FlowRuleNode TemperatureTagNode()
    {
        return new FlowRuleNode
        {
            Id = "tag",
            NodeType = FlowRuleNodeTypes.TagInput,
            DeviceName = "Boiler",
            GroupName = "Main",
            TagName = "Temperature",
            PointCode = "Boiler.Main.Temperature",
            DataType = "Double"
        };
    }

    private static FlowRuleNode MachineStateTagNode()
    {
        return new FlowRuleNode
        {
            Id = "state-tag",
            NodeType = FlowRuleNodeTypes.TagInput,
            DeviceName = "Boiler",
            GroupName = "Main",
            TagName = "MachineState",
            PointCode = "Boiler.Main.MachineState",
            DataType = "String"
        };
    }

    private static FlowRuleDefinition CreateLinearFlow(string id, string name, FlowRuleNode tag, FlowRuleNode condition)
    {
        FlowRuleNode mqtt = new FlowRuleNode
        {
            Id = "mqtt",
            NodeType = FlowRuleNodeTypes.MqttPublish,
            PublishToMqtt = true,
            TopicTemplate = "ipc/rule/{pointCode}/{ruleName}"
        };

        return new FlowRuleDefinition
        {
            Id = id,
            Name = name,
            Enabled = true,
            Mode = FlowRuleModes.Flow,
            Nodes = { tag, condition, mqtt },
            Edges =
            {
                new FlowRuleEdge { SourceNodeId = tag.Id, TargetNodeId = condition.Id },
                new FlowRuleEdge { SourceNodeId = condition.Id, TargetNodeId = mqtt.Id }
            }
        };
    }

    private static FlowRuleDefinition CreateTransformFlow(string id, string name, FlowRuleNode tag, FlowRuleNode transform, FlowRuleNode condition)
    {
        FlowRuleNode mqtt = new FlowRuleNode
        {
            Id = "mqtt",
            NodeType = FlowRuleNodeTypes.MqttPublish,
            PublishToMqtt = true,
            TopicTemplate = "ipc/rule/{pointCode}/{ruleName}"
        };

        return new FlowRuleDefinition
        {
            Id = id,
            Name = name,
            Enabled = true,
            Mode = FlowRuleModes.Flow,
            Nodes = { tag, transform, condition, mqtt },
            Edges =
            {
                new FlowRuleEdge { SourceNodeId = tag.Id, TargetNodeId = transform.Id },
                new FlowRuleEdge { SourceNodeId = transform.Id, TargetNodeId = condition.Id },
                new FlowRuleEdge { SourceNodeId = condition.Id, TargetNodeId = mqtt.Id }
            }
        };
    }

    private sealed class FlowRuleHarness : IDisposable
    {
        private readonly FakeRuntimeService _runtime;

        private FlowRuleHarness(FakeRuntimeService runtime, FlowRuleEngineService engine)
        {
            _runtime = runtime;
            Engine = engine;
        }

        public FlowRuleEngineService Engine { get; }

        public static FlowRuleHarness Start(params FlowRuleDefinition[] flowRules)
        {
            return StartWithInference(null, flowRules);
        }

        public static FlowRuleHarness StartWithInference(IModelInferenceService? modelInference, params FlowRuleDefinition[] flowRules)
        {
            FakeRuntimeService runtime = new FakeRuntimeService();
            ProjectConfig project = new ProjectConfig();
            project.FlowRules.AddRange(flowRules);
            return StartProject(runtime, project, modelInference);
        }

        public static FlowRuleHarness StartProject(ProjectConfig project)
        {
            FakeRuntimeService runtime = new FakeRuntimeService();
            return StartProject(runtime, project, null);
        }

        private static FlowRuleHarness StartProject(FakeRuntimeService runtime, ProjectConfig project, IModelInferenceService? modelInference)
        {
            FlowRuleEngineService engine = new FlowRuleEngineService(
                runtime,
                project,
                (_, _, _) => true,
                new MqttGatewayOptions
                {
                    GatewayId = "test-gateway",
                    GatewayName = "Test Gateway",
                    CloudProtocolVersion = "test.v1"
                },
                new GatewayResilienceOptions().RuleEngine,
                modelInference);

            engine.Start();
            return new FlowRuleHarness(runtime, engine);
        }

        public void Raise(string tagName, double value, DateTime? timestamp = null)
        {
            Raise(tagName, value, TagQuality.Good, timestamp);
        }

        public void Raise(string tagName, double value, TagQuality quality, DateTime? timestamp = null)
        {
            Raise("Boiler", "Main", tagName, "Boiler.Main." + tagName, value, quality, timestamp);
        }

        public void Raise(string deviceName, string groupName, string tagName, string pointCode, double value, DateTime? timestamp = null)
        {
            Raise(deviceName, groupName, tagName, pointCode, value, TagQuality.Good, timestamp);
        }

        public void Raise(string deviceName, string groupName, string tagName, string pointCode, double value, TagQuality quality, DateTime? timestamp = null)
        {
            _runtime.Raise(new TagValueSnapshot
            {
                DeviceName = deviceName,
                GroupName = groupName,
                TagName = tagName,
                PointCode = pointCode,
                DataType = "Double",
                RawValue = value,
                RawValueText = value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                Value = value,
                ValueText = value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                Quality = quality,
                Timestamp = timestamp ?? DateTime.Now
            });
        }

        public void RaiseText(string tagName, string value, DateTime? timestamp = null, TagQuality quality = TagQuality.Good)
        {
            _runtime.Raise(new TagValueSnapshot
            {
                DeviceName = "Boiler",
                GroupName = "Main",
                TagName = tagName,
                PointCode = "Boiler.Main." + tagName,
                DataType = "String",
                RawValue = value,
                RawValueText = value,
                Value = value,
                ValueText = value,
                Quality = quality,
                Timestamp = timestamp ?? DateTime.Now
            });
        }

        public void Dispose()
        {
            Engine.Dispose();
        }
    }

    private sealed class FakeModelInferenceService : IModelInferenceService
    {
        private readonly double _score;

        public FakeModelInferenceService(double score)
        {
            _score = score;
        }

        public ModelInferenceRequest? LastRequest { get; private set; }

        public ModelInferenceResult Predict(ModelInferenceRequest request)
        {
            LastRequest = request;
            return new ModelInferenceResult
            {
                Success = true,
                Score = _score,
                Outputs = new List<double> { _score },
                Timestamp = DateTime.Now
            };
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeRuntimeService : IRuntimeService
    {
        public event EventHandler<TagValueChangedEventArgs>? TagValueChanged;

        public bool IsRunning { get; private set; }
        public int MaxConcurrentDevicePolls => 1;

        public void Start(ProjectConfig config) => IsRunning = true;
        public void Stop() => IsRunning = false;
        public void Raise(TagValueSnapshot snapshot) => TagValueChanged?.Invoke(this, new TagValueChangedEventArgs(snapshot));
        public bool TryGetSnapshot(string deviceName, string groupName, string tagName, out TagValueSnapshot? snapshot)
        {
            snapshot = null;
            return false;
        }

        public IList<TagValueSnapshot> GetSnapshots() => new List<TagValueSnapshot>();
        public void RestoreSnapshots(IList<TagValueSnapshot> snapshots) { }
        public IList<DeviceRuntimeStatus> GetDeviceStatuses() => new List<DeviceRuntimeStatus>();
        public RuntimeSchedulerStatus GetSchedulerStatus() => new RuntimeSchedulerStatus();
        public IList<RuntimeErrorDetail> GetRecentErrors(int maxCount) => new List<RuntimeErrorDetail>();
        public ReadTagResponse ReadCached(ReadTagRequest request) => new ReadTagResponse();
        public ReadTagsResponse ReadCached(ReadTagsRequest request) => new ReadTagsResponse();
        public ReadTagsResponse QueryCached(ReadTagRequest request) => new ReadTagsResponse();
        public ReadTagsResponse ReadTagByDeviceCached(string deviceName, string tagName) => new ReadTagsResponse();
        public ReadTagsResponse ReadGroupCached(string deviceName, string groupName) => new ReadTagsResponse();
        public WriteTagResponse WriteTag(WriteTagRequest request) => new WriteTagResponse();
        public void Dispose() { }
    }
}

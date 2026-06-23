/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：GatewayProjectAggregateRuleTests
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
using IPC.Gateway.Core.Domain.Gateway;
using IPC.Runtime.Configuration;

namespace IPC.Gateway.Tests;

public sealed class GatewayProjectAggregateRuleTests
{
    [Fact]
    public void RuleCrud_AddUpdateDelete_Works()
    {
        ProjectConfig project = new ProjectConfig();
        GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);

        EdgeRuleConfig added = aggregate.AddRule(new EdgeRuleConfig
        {
            Id = string.Empty,
            Name = "Pressure high",
            SourceDeviceName = "Boiler",
            SourceTagName = "Pressure",
            HighLimit = 10D
        });

        Assert.False(string.IsNullOrWhiteSpace(added.Id));
        Assert.Single(aggregate.Project.Rules);
        Assert.Equal("Pressure high", aggregate.Project.Rules[0].Name);

        EdgeRuleConfig updated = aggregate.UpdateRule(added.Id, new EdgeRuleConfig
        {
            Name = "Pressure high updated",
            ConditionType = EdgeRuleConditionType.Condition,
            SourceDeviceName = "Boiler",
            SourceTagName = "Pressure",
            Operator = EdgeRuleComparisonOperator.GreaterThanOrEqual,
            CompareValue = 12D
        });

        Assert.Equal(added.Id, updated.Id);
        Assert.Equal("Pressure high updated", aggregate.Project.Rules[0].Name);
        Assert.Equal(EdgeRuleConditionType.Condition, aggregate.Project.Rules[0].ConditionType);

        EdgeRuleConfig deleted = aggregate.DeleteRule(added.Id);

        Assert.Equal(added.Id, deleted.Id);
        Assert.Empty(aggregate.Project.Rules);
    }
}

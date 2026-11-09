/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Application.Gateway
* 项目描述 ：
* 类 名 称 ：GatewayRuleConfigurationApplicationService
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Core.Application.Gateway
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
using IPC.Gateway.Core.Gateway;
using IPC.Runtime.Configuration;

namespace IPC.Gateway.Core.Application.Gateway;

public sealed class GatewayRuleConfigurationApplicationService : IGatewayRuleConfigurationApplicationService
{
    private readonly GatewayCoreService _gateway;

    public GatewayRuleConfigurationApplicationService(GatewayCoreService gateway)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    public IList<EdgeRuleConfig> GetRules() => _gateway.CurrentProject.Rules;

    public EdgeRuleConfig AddRule(EdgeRuleConfig input)
    {
        GatewayProjectAggregate aggregate = new GatewayProjectAggregate(_gateway.CurrentProject);
        EdgeRuleConfig result = aggregate.AddRule(input);
        _gateway.ApplyRuleProject(aggregate.Project);
        return result;
    }

    public async Task<EdgeRuleConfig> AddRuleAsync(EdgeRuleConfig input)
    {
        GatewayProjectAggregate aggregate = new GatewayProjectAggregate(_gateway.CurrentProject);
        EdgeRuleConfig result = aggregate.AddRule(input);
        await _gateway.ApplyRuleProjectAsync(aggregate.Project);
        return result;
    }

    public EdgeRuleConfig UpdateRule(string ruleId, EdgeRuleConfig input)
    {
        GatewayProjectAggregate aggregate = new GatewayProjectAggregate(_gateway.CurrentProject);
        EdgeRuleConfig result = aggregate.UpdateRule(ruleId, input);
        _gateway.ApplyRuleProject(aggregate.Project);
        return result;
    }

    public async Task<EdgeRuleConfig> UpdateRuleAsync(string ruleId, EdgeRuleConfig input)
    {
        GatewayProjectAggregate aggregate = new GatewayProjectAggregate(_gateway.CurrentProject);
        EdgeRuleConfig result = aggregate.UpdateRule(ruleId, input);
        await _gateway.ApplyRuleProjectAsync(aggregate.Project);
        return result;
    }

    public EdgeRuleConfig DeleteRule(string ruleId)
    {
        GatewayProjectAggregate aggregate = new GatewayProjectAggregate(_gateway.CurrentProject);
        EdgeRuleConfig result = aggregate.DeleteRule(ruleId);
        _gateway.ApplyRuleProject(aggregate.Project);
        return result;
    }

    public async Task<EdgeRuleConfig> DeleteRuleAsync(string ruleId)
    {
        GatewayProjectAggregate aggregate = new GatewayProjectAggregate(_gateway.CurrentProject);
        EdgeRuleConfig result = aggregate.DeleteRule(ruleId);
        await _gateway.ApplyRuleProjectAsync(aggregate.Project);
        return result;
    }

    public IList<FlowRuleDefinition> GetFlowRules() => _gateway.CurrentProject.FlowRules;

    public FlowRuleDefinition AddFlowRule(FlowRuleDefinition input)
    {
        GatewayProjectAggregate aggregate = new GatewayProjectAggregate(_gateway.CurrentProject);
        FlowRuleDefinition result = aggregate.AddFlowRule(input);
        _gateway.ApplyRuleProject(aggregate.Project);
        return result;
    }

    public async Task<FlowRuleDefinition> AddFlowRuleAsync(FlowRuleDefinition input)
    {
        GatewayProjectAggregate aggregate = new GatewayProjectAggregate(_gateway.CurrentProject);
        FlowRuleDefinition result = aggregate.AddFlowRule(input);
        await _gateway.ApplyRuleProjectAsync(aggregate.Project);
        return result;
    }

    public FlowRuleDefinition UpdateFlowRule(string ruleId, FlowRuleDefinition input)
    {
        GatewayProjectAggregate aggregate = new GatewayProjectAggregate(_gateway.CurrentProject);
        FlowRuleDefinition result = aggregate.UpdateFlowRule(ruleId, input);
        _gateway.ApplyRuleProject(aggregate.Project);
        return result;
    }

    public async Task<FlowRuleDefinition> UpdateFlowRuleAsync(string ruleId, FlowRuleDefinition input)
    {
        GatewayProjectAggregate aggregate = new GatewayProjectAggregate(_gateway.CurrentProject);
        FlowRuleDefinition result = aggregate.UpdateFlowRule(ruleId, input);
        await _gateway.ApplyRuleProjectAsync(aggregate.Project);
        return result;
    }

    public FlowRuleDefinition DeleteFlowRule(string ruleId)
    {
        GatewayProjectAggregate aggregate = new GatewayProjectAggregate(_gateway.CurrentProject);
        FlowRuleDefinition result = aggregate.DeleteFlowRule(ruleId);
        _gateway.ApplyRuleProject(aggregate.Project);
        return result;
    }

    public async Task<FlowRuleDefinition> DeleteFlowRuleAsync(string ruleId)
    {
        GatewayProjectAggregate aggregate = new GatewayProjectAggregate(_gateway.CurrentProject);
        FlowRuleDefinition result = aggregate.DeleteFlowRule(ruleId);
        await _gateway.ApplyRuleProjectAsync(aggregate.Project);
        return result;
    }
}

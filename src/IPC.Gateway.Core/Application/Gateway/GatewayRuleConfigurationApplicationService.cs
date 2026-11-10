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
        return _gateway.MutateDeviceProject(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            EdgeRuleConfig result = aggregate.AddRule(input);
            return result;
        }, rulesOnly: true);
    }

    public Task<EdgeRuleConfig> AddRuleAsync(EdgeRuleConfig input)
    {
        return _gateway.MutateDeviceProjectAsync(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            EdgeRuleConfig result = aggregate.AddRule(input);
            return result;
        }, rulesOnly: true);
    }

    public EdgeRuleConfig UpdateRule(string ruleId, EdgeRuleConfig input)
    {
        return _gateway.MutateDeviceProject(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            EdgeRuleConfig result = aggregate.UpdateRule(ruleId, input);
            return result;
        }, rulesOnly: true);
    }

    public Task<EdgeRuleConfig> UpdateRuleAsync(string ruleId, EdgeRuleConfig input)
    {
        return _gateway.MutateDeviceProjectAsync(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            EdgeRuleConfig result = aggregate.UpdateRule(ruleId, input);
            return result;
        }, rulesOnly: true);
    }

    public EdgeRuleConfig DeleteRule(string ruleId)
    {
        return _gateway.MutateDeviceProject(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            EdgeRuleConfig result = aggregate.DeleteRule(ruleId);
            return result;
        }, rulesOnly: true);
    }

    public Task<EdgeRuleConfig> DeleteRuleAsync(string ruleId)
    {
        return _gateway.MutateDeviceProjectAsync(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            EdgeRuleConfig result = aggregate.DeleteRule(ruleId);
            return result;
        }, rulesOnly: true);
    }

    public IList<FlowRuleDefinition> GetFlowRules() => _gateway.CurrentProject.FlowRules;

    public FlowRuleDefinition AddFlowRule(FlowRuleDefinition input)
    {
        return _gateway.MutateDeviceProject(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            FlowRuleDefinition result = aggregate.AddFlowRule(input);
            return result;
        }, rulesOnly: true);
    }

    public Task<FlowRuleDefinition> AddFlowRuleAsync(FlowRuleDefinition input)
    {
        return _gateway.MutateDeviceProjectAsync(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            FlowRuleDefinition result = aggregate.AddFlowRule(input);
            return result;
        }, rulesOnly: true);
    }

    public FlowRuleDefinition UpdateFlowRule(string ruleId, FlowRuleDefinition input)
    {
        return _gateway.MutateDeviceProject(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            FlowRuleDefinition result = aggregate.UpdateFlowRule(ruleId, input);
            return result;
        }, rulesOnly: true);
    }

    public Task<FlowRuleDefinition> UpdateFlowRuleAsync(string ruleId, FlowRuleDefinition input)
    {
        return _gateway.MutateDeviceProjectAsync(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            FlowRuleDefinition result = aggregate.UpdateFlowRule(ruleId, input);
            return result;
        }, rulesOnly: true);
    }

    public FlowRuleDefinition DeleteFlowRule(string ruleId)
    {
        return _gateway.MutateDeviceProject(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            FlowRuleDefinition result = aggregate.DeleteFlowRule(ruleId);
            return result;
        }, rulesOnly: true);
    }

    public Task<FlowRuleDefinition> DeleteFlowRuleAsync(string ruleId)
    {
        return _gateway.MutateDeviceProjectAsync(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            FlowRuleDefinition result = aggregate.DeleteFlowRule(ruleId);
            return result;
        }, rulesOnly: true);
    }
}

/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Application.Gateway
* 项目描述 ：
* 类 名 称 ：IGatewayRuleConfigurationApplicationService
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
using IPC.Runtime.Configuration;

namespace IPC.Gateway.Core.Application.Gateway;

public interface IGatewayRuleConfigurationApplicationService
{
    IList<EdgeRuleConfig> GetRules();
    EdgeRuleConfig AddRule(EdgeRuleConfig input);
    EdgeRuleConfig UpdateRule(string ruleId, EdgeRuleConfig input);
    EdgeRuleConfig DeleteRule(string ruleId);
    IList<FlowRuleDefinition> GetFlowRules();
    FlowRuleDefinition AddFlowRule(FlowRuleDefinition input);
    FlowRuleDefinition UpdateFlowRule(string ruleId, FlowRuleDefinition input);
    FlowRuleDefinition DeleteFlowRule(string ruleId);
}

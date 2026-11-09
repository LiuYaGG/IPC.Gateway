/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.FlowRules
* 项目描述 ：
* 类 名 称 ：FlowRuleServiceCollectionExtensions
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.FlowRules
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
using IPC.Gateway.Core.Gateway;
using Microsoft.Extensions.DependencyInjection;

namespace IPC.Gateway.FlowRules;

public static class FlowRuleServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayFlowRules(this IServiceCollection services)
    {
        services.AddSingleton<IFlowRuleEngineFactory, FlowRuleEngineFactory>();
        return services;
    }
}

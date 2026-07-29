/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.FlowRules
* 项目描述 ：
* 类 名 称 ：FlowRuleEngineFactory
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
using IPC.EdgeGateway;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Core.Resilience;
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;

namespace IPC.Gateway.FlowRules;

public sealed class FlowRuleEngineFactory : IFlowRuleEngineFactory
{
    private readonly IValueTransformScriptRuntime _valueTransformScripts;

    /// <summary>
    /// 创建携带值处理脚本运行时的规则引擎工厂。
    /// </summary>
    public FlowRuleEngineFactory(IValueTransformScriptRuntime? valueTransformScripts = null)
    {
        _valueTransformScripts = valueTransformScripts ?? NoopValueTransformScriptRuntime.Instance;
    }

    /// <summary>
    /// 创建规则引擎实例。
    /// </summary>
    public IFlowRuleEngineService Create(
        IRuntimeService runtime,
        ProjectConfig projectConfig,
        Func<string, string, int, bool> mqttPublisher,
        MqttGatewayOptions gatewayOptions,
        CircuitBreakerOptions circuitBreakerOptions,
        IModelInferenceService modelInference)
    {
        return new IPC.EdgeGateway.FlowRuleEngineService(
            runtime,
            projectConfig,
            mqttPublisher,
            gatewayOptions,
            circuitBreakerOptions,
            modelInference,
            _valueTransformScripts);
    }
}

/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Application.Gateway
* 项目描述 ：
* 类 名 称 ：GatewayMqttConfigurationApplicationService
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
using IPC.EdgeGateway;
using IPC.Gateway.Core.Gateway;

namespace IPC.Gateway.Core.Application.Gateway;

public sealed class GatewayMqttConfigurationApplicationService : IGatewayMqttConfigurationApplicationService
{
    private readonly GatewayCoreService _gateway;

    public GatewayMqttConfigurationApplicationService(GatewayCoreService gateway)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    public MqttGatewayOptions GetMqttOptions() => _gateway.CurrentMqttOptions;

    public MqttGatewayOptions UpdateMqttOptions(MqttGatewayOptions options)
    {
        _gateway.UpdateMqttOptions(options);
        return _gateway.CurrentMqttOptions;
    }
}

/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Application.Gateway
* 项目描述 ：
* 类 名 称 ：GatewayHistoryConfigurationApplicationService
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

public sealed class GatewayHistoryConfigurationApplicationService : IGatewayHistoryConfigurationApplicationService
{
    private readonly GatewayCoreService _gateway;

    public GatewayHistoryConfigurationApplicationService(GatewayCoreService gateway)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    public LocalHistoryOptions GetHistoryOptions() => _gateway.CurrentHistoryOptions;

    public LocalHistoryOptions UpdateHistoryOptions(LocalHistoryOptions options)
    {
        _gateway.UpdateHistoryOptions(options);
        return _gateway.CurrentHistoryOptions;
    }

    public async Task<LocalHistoryOptions> UpdateHistoryOptionsAsync(LocalHistoryOptions options)
    {
        await _gateway.UpdateHistoryOptionsAsync(options);
        return _gateway.CurrentHistoryOptions;
    }
}

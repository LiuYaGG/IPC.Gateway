/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.WebHost
* 项目描述 ：
* 类 名 称 ：GatewayRuntimeHostedService
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.WebHost
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
using IPC.Gateway.Core.Application.Gateway;

namespace IPC.Gateway.WebHost;










public sealed class GatewayRuntimeHostedService : IHostedService, IDisposable
{
    private readonly ILogger<GatewayRuntimeHostedService> _logger;
    private readonly IGatewayApplicationService _gateway;
    private bool _disposed;

    public GatewayRuntimeHostedService(
        IGatewayApplicationService gateway,
        ILogger<GatewayRuntimeHostedService> logger)
    {
        _logger = logger;
        _gateway = gateway;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _gateway.Start();
        var status = _gateway.GetStatus();
        _logger.LogInformation(
            "IPC.Gateway started. Project={ProjectName}, Devices={DeviceCount}, Tags={TagCount}, Config={ProjectPath}",
            status.ProjectName,
            status.DeviceCount,
            status.TagCount,
            status.ProjectPath);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _gateway.Stop();
        _logger.LogInformation("IPC.Gateway stopped.");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _gateway.Dispose();
    }
}

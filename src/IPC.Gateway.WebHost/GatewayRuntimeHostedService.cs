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
using System.Diagnostics;

namespace IPC.Gateway.WebHost;










public sealed class GatewayRuntimeHostedService : IHostedService, IDisposable
{
    private readonly ILogger<GatewayRuntimeHostedService> _logger;
    private readonly IGatewayApplicationService _gateway;
    private readonly GatewayUpdatePackageService _updates;
    private readonly IServiceProvider _services;
    private readonly GatewayObservabilityOptions _observability;
    private bool _disposed;

    public GatewayRuntimeHostedService(
        IGatewayApplicationService gateway,
        ILogger<GatewayRuntimeHostedService> logger,
        GatewayUpdatePackageService updates,
        GatewayObservabilityOptions observability,
        IServiceProvider services)
    {
        _logger = logger;
        _gateway = gateway;
        _updates = updates;
        _observability = observability;
        _services = services;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // The host lifetime has connected to the Windows service manager here.
        // Restore driver trust before the first project validation or PLC read.
        cancellationToken.ThrowIfCancellationRequested();
        var elapsed = Stopwatch.StartNew();
        try { _updates.RestoreCompletedUpgradeTrust(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Upgrade driver trust could not be restored; normal signature verification remains active."); }
        _logger.LogInformation("Gateway startup driver trust check finished in {ElapsedMs} ms.", elapsed.ElapsedMilliseconds);
        if (_observability.MetricsEnabled)
            _services.GetRequiredService<GatewayMetricsInstrumentation>();
        cancellationToken.ThrowIfCancellationRequested();
        elapsed.Restart();
        _gateway.Start();
        var status = _gateway.GetStatusSummary();
        _logger.LogInformation(
            "IPC.Gateway started in {ElapsedMs} ms. Project={ProjectName}, Devices={DeviceCount}, Tags={TagCount}, Config={ProjectPath}",
            elapsed.ElapsedMilliseconds,
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

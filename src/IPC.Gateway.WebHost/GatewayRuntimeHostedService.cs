using IPC.Gateway.Core.Application.Gateway;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Scripting.Database;
using IPC.Gateway.Scripting.Runtime;
using IPC.Gateway.Watchdog;
using System.Diagnostics;

namespace IPC.Gateway.WebHost;

public sealed class GatewayRuntimeHostedService(
    IServiceProvider services,
    GatewayStartupState startup,
    IHostApplicationLifetime lifetime,
    ILogger<GatewayRuntimeHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan DatabaseRetryWindow = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // ApplicationStopping fires before Kestrel drains requests and the host calls StopAsync.
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, lifetime.ApplicationStopping);
        CancellationToken token = shutdown.Token;
        var workers = new List<BackgroundService>();
        IGatewayApplicationService? gateway = null;
        bool initialized = false;
        string currentStage = "等待网站监听";
        long startupBegan = Stopwatch.GetTimestamp();
        try
        {
            await startup.Listening.WaitAsync(token);
            startupBegan = Stopwatch.GetTimestamp();
            try
            {
                GatewayStartupTiming.Measure("恢复升级驱动信任",
                    services.GetRequiredService<GatewayUpdatePackageService>().RestoreCompletedUpgradeTrust);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Upgrade driver trust could not be restored; normal signature verification remains active.");
            }
            token.ThrowIfCancellationRequested();
            currentStage = "构建网关及数据库依赖";
            gateway = await ResolveGatewayAfterDatabaseReadyAsync(currentStage, token);
            if (services.GetRequiredService<GatewayObservabilityOptions>().MetricsEnabled)
            {
                currentStage = "注册运行指标";
                GatewayStartupTiming.Measure(currentStage, services.GetRequiredService<GatewayMetricsInstrumentation>);
            }
            currentStage = "初始化采集网关";
            gateway.Start(token);

            await StartWorkerAsync<ScriptDatabaseWriteDispatcher>("启动脚本数据库队列");
            await StartWorkerAsync<GatewayScriptRuntimeService>("启动脚本调度");
            await StartWorkerAsync<GatewayRuntimePushHostedService>("启动实时推送");
            await StartWorkerAsync<VirtualModelTagRuntimeService>("启动模型调度");
            await StartWorkerAsync<GatewayWatchdogHostedService>("启动看门狗");
            currentStage = "读取初始运行摘要";
            var status = GatewayStartupTiming.Measure(currentStage, gateway.GetStatusSummary);
            token.ThrowIfCancellationRequested();
            GatewayStartupTiming.RecordCompleted("后台初始化总计", startupBegan);
            startup.MarkReady();
            initialized = true;
            logger.LogInformation("IPC.Gateway started in {ElapsedMs} ms. Project={ProjectName}, Devices={DeviceCount}, Tags={TagCount}",
                (long)Stopwatch.GetElapsedTime(startupBegan).TotalMilliseconds, status.ProjectName, status.DeviceCount, status.TagCount);

            // Propagate worker faults as the generic host did for individually hosted services.
            // Some workers (e.g. a disabled watchdog) can complete normally.
            Task shutdownTask = Task.Delay(Timeout.Infinite, stoppingToken);
            var tasks = workers.Select(worker => worker.ExecuteTask!).Append(shutdownTask).ToList();
            while (true)
            {
                var completed = await Task.WhenAny(tasks);
                await completed;
                tasks.Remove(completed);
            }

            async Task StartWorkerAsync<T>(string stage) where T : BackgroundService
            {
                token.ThrowIfCancellationRequested();
                currentStage = stage;
                await GatewayStartupTiming.MeasureAsync(stage, async () =>
                {
                    var worker = services.GetRequiredService<T>();
                    workers.Add(worker);
                    await worker.StartAsync(stoppingToken);
                    if (worker.ExecuteTask is { IsCompleted: true } task) await task;
                });
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (!initialized)
        {
            startup.MarkFailed();
            logger.LogError(ex, "Gateway runtime initialization failed during {Stage} after {ElapsedMs} ms.", currentStage,
                (long)Stopwatch.GetElapsedTime(startupBegan).TotalMilliseconds);
        }
        catch
        {
            startup.MarkStopping();
            lifetime.StopApplication();
            throw;
        }
        finally
        {
            // A worker is both started and stopped by this task. Disposal belongs to DI.
            for (int i = workers.Count - 1; i >= 0; --i)
            {
                try { await workers[i].StopAsync(CancellationToken.None); }
                catch (Exception ex) { logger.LogError(ex, "Stopping {Worker} failed.", workers[i].GetType().Name); }
            }
            if (gateway != null)
            {
                try { gateway.Stop(); }
                catch (Exception ex) { logger.LogError(ex, "Stopping gateway runtime failed."); }
            }
        }
    }

    private async Task<IGatewayApplicationService> ResolveGatewayAfterDatabaseReadyAsync(string stage, CancellationToken token)
    {
        long retryBegan = Stopwatch.GetTimestamp();
        int failedAttempts = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                return GatewayStartupTiming.Measure(stage, services.GetRequiredService<IGatewayApplicationService>);
            }
            catch (Exception ex)
            {
                failedAttempts++;
                TimeSpan elapsed = Stopwatch.GetElapsedTime(retryBegan);
                if (elapsed >= DatabaseRetryWindow)
                    throw;

                TimeSpan delay = failedAttempts switch
                {
                    1 => TimeSpan.FromSeconds(2),
                    2 => TimeSpan.FromSeconds(5),
                    3 => TimeSpan.FromSeconds(10),
                    _ => TimeSpan.FromSeconds(15)
                };
                TimeSpan remaining = DatabaseRetryWindow - elapsed;
                if (delay > remaining)
                    delay = remaining;

                logger.LogWarning(ex,
                    "Gateway database initialization attempt {Attempt} failed; retrying in {DelayMs} ms.",
                    failedAttempts, (long)delay.TotalMilliseconds);
                GatewayStartupTiming.RecordDetail(stage,
                    $"status=等待重试 attempt={failedAttempts} elapsedMs={(long)elapsed.TotalMilliseconds} nextDelayMs={(long)delay.TotalMilliseconds} errorType={ex.GetType().Name}");
                await Task.Delay(delay, token);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Joins the initialization/cleanup task before DI disposes the same workers and gateway.
        startup.MarkStopping();
        await base.StopAsync(CancellationToken.None);
    }
}

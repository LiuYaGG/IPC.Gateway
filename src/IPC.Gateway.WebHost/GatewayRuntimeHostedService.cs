using IPC.Gateway.Core.Application.Gateway;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Scripting.Database;
using IPC.Gateway.Scripting.Runtime;
using IPC.Gateway.Watchdog;
using System.Diagnostics;
using System.Security.Cryptography;
using IPC.Gateway.HighAvailability;
using IPC.Plc.Communication.Core;
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;
using IPC.Plc.Communication.Infrastructure;
using IPC.Gateway.Scripting.Abstractions;
using IPC.Gateway.Scripting.Models;
using IPC.Gateway.Scripting.Persistence;
using IPC.Gateway.WebHost.HighAvailability;
using IPC.Gateway.Inference;
using System.Text.Json;

namespace IPC.Gateway.WebHost;

public sealed class GatewayRuntimeHostedService(
    IServiceProvider services,
    GatewayStartupState startup,
    GatewayHaClient ha,
    IHostApplicationLifetime lifetime,
    ILogger<GatewayRuntimeHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan DatabaseRetryWindow = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // ApplicationStopping fires before Kestrel drains requests and the host calls StopAsync.
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, lifetime.ApplicationStopping);
        CancellationToken token = shutdown.Token;
        IGatewayApplicationService? gateway = null;
        bool initialized = false;
        string currentStage = "等待网站监听";
        long startupBegan = Stopwatch.GetTimestamp();
        try
        {
            await startup.Listening.WaitAsync(token);
            if (ha.AwaitingProvisioning)
            {
                logger.LogInformation("HA node {NodeId} is in management-only mode until provisioning is complete and the gateway restarts.", ha.NodeId);
                startup.MarkReady();
                return;
            }
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
            var core = services.GetRequiredService<GatewayCoreService>();
            if (!ha.IsCluster)
            {
                currentStage = "迁移共享脚本与模型";
                await SharedArtifactSynchronization.RefreshAsync(services, core, token);
            }
            bool imported = false;
            while (!token.IsCancellationRequested)
            {
                bool clustered = ha.IsCluster;
                string fingerprint = "";
                if (clustered)
                {
                    currentStage = "预热热备网关";
                    string? version = null;
                    while (ha.IsCluster && !ha.CanRunBusiness)
                    {
                        if (ha.IsParked) startup.MarkReady();
                        try
                        {
                            if (!imported)
                            {
                                await ha.WaitForWritesToDrainAsync(token);
                                core.AlarmJournal.ImportStandaloneBeforeEnrollment(ha.ClusterId, ha.NodeId);
                                imported = true;
                                ha.MarkInitialAlarmImportComplete();
                            }
                            if (ha.IsParked)
                            {
                                await Task.Delay(TimeSpan.FromMilliseconds(500), token);
                                continue;
                            }
                            var prepared = core.PrepareStandby(token) + LocalArtifactStamp();
                            if (prepared != version)
                            {
                                await SharedArtifactSynchronization.RefreshAsync(services, core, token);
                                fingerprint = await FingerprintAsync(core, token);
                                version = prepared;
                            }
                            ha.ReadinessError = null;
                            startup.MarkReady();
                            await ha.MaintainLeaseAsync(fingerprint, true, token);
                            if (ha.CanRunBusiness)
                                await SharedArtifactSynchronization.RefreshAsync(services, core, token);
                            if (ha.CanRunBusiness && core.ReadActiveConfigurationVersions() + LocalArtifactStamp() != prepared)
                            {
                                await ha.ReleaseAfterDrainAsync(token);
                                version = null;
                            }
                        }
                        catch (Exception ex) when (!token.IsCancellationRequested)
                        {
                            ha.ReadinessError = ex.Message.StartsWith("HA_", StringComparison.Ordinal) ? ex.Message : "HA_PREPARATION_FAILED";
                            logger.LogWarning(ex, "HA standby readiness check failed.");
                            if (ha.CanRunBusiness) await ha.ReleaseAfterDrainAsync(token);
                            version = null;
                            await ha.MaintainLeaseAsync("", false, token);
                        }
                        if (!ha.CanRunBusiness) await Task.Delay(ha.RenewInterval, token);
                    }
                }
                using var period = CancellationTokenSource.CreateLinkedTokenSource(token);
                long epoch = ha.Epoch;
                if (!ha.TryBeginBusinessPeriod(clustered, epoch)) continue;
                var workers = new List<BackgroundService>();
                bool businessFailed = false;
                void FirstSample(object? sender, TagValueChangedEventArgs args)
                {
                    if (args.Snapshot.Quality != IPC.Runtime.Values.TagQuality.Good) return;
                    ha.MarkFirstSample(epoch);
                    core.Runtime.TagValueSampled -= FirstSample;
                }
                if (clustered) core.Runtime.TagValueSampled += FirstSample;
                Task monitor = ObserveOwnershipAsync(core, clustered, epoch, fingerprint, period);
                try
                {
                    currentStage = "初始化采集网关";
                    gateway.Start(period.Token);
                    await StartWorkerAsync<ScriptDatabaseWriteDispatcher>("启动脚本数据库队列");
                    await StartWorkerAsync<GatewayScriptRuntimeService>("启动脚本调度");
                    await StartWorkerAsync<GatewayRuntimePushHostedService>("启动实时推送");
                    await StartWorkerAsync<VirtualModelTagRuntimeService>("启动模型调度");
                    await StartWorkerAsync<GatewayWatchdogHostedService>("启动看门狗");
                    currentStage = "读取初始运行摘要";
                    var status = GatewayStartupTiming.Measure(currentStage, gateway.GetStatusSummary);
                    period.Token.ThrowIfCancellationRequested();
                    startup.MarkReady();
                    initialized = true;
                    ha.MarkBusinessStarted();
                    GatewayStartupTiming.RecordCompleted("后台初始化总计", startupBegan);
                    logger.LogInformation("IPC.Gateway active. Epoch={Epoch}, Project={ProjectName}, Devices={DeviceCount}, Tags={TagCount}",
                        epoch, status.ProjectName, status.DeviceCount, status.TagCount);
                    var pending = workers.Select(w => w.ExecuteTask!).Append(monitor).ToList();
                    while (!period.IsCancellationRequested)
                    {
                        var completed = await Task.WhenAny(pending);
                        await completed;
                        pending.Remove(completed);
                    }
                }
                catch (OperationCanceledException) when (period.IsCancellationRequested) { }
                catch (PlcWriteDeniedException ex) when (ex.Code == "HA_CONFIG_CHANGED")
                { logger.LogInformation("The shared project changed during promotion; the gateway will prewarm it again."); }
                catch (PlcWriteDeniedException ex) when (ha.IsCluster && (!ha.CanRunBusiness || ex.Code == "HA_CONFIGURATION_CHANGED")) { }
                catch (Exception ex) when (clustered && !token.IsCancellationRequested)
                {
                    businessFailed = true;
                    ha.ReadinessError = "HA_BUSINESS_START_FAILED";
                    logger.LogError(ex, "HA business period failed during {Stage}; draining before rejoining.", currentStage);
                }
                finally
                {
                    if (clustered) core.Runtime.TagValueSampled -= FirstSample;
                    await period.CancelAsync();
                    try { await monitor; } catch (OperationCanceledException) when (period.IsCancellationRequested) { }
                    ha.RevokeLocal();
                    bool drained = true;
                    for (int i = workers.Count - 1; i >= 0; i--)
                    {
                        try { await workers[i].StopAsync(CancellationToken.None); }
                        catch (Exception ex) { drained = false; logger.LogError(ex, "Stopping {Worker} failed.", workers[i].GetType().Name); }
                    }
                    try { gateway.Stop(); }
                    catch (Exception ex) { drained = false; logger.LogError(ex, "Stopping gateway runtime failed."); }
                    if (!drained) throw new InvalidOperationException("HA business drain failed; restart the instance before rejoining.");
                    if (clustered)
                    {
                        await ha.WaitForWritesToDrainAsync(CancellationToken.None);
                        await ha.ReleaseAfterDrainAsync(CancellationToken.None);
                    }
                    ha.MarkBusinessPeriodDrained();
                }
                if (businessFailed)
                {
                    await ha.MaintainLeaseAsync("", false, token);
                    await Task.Delay(ha.RenewInterval * 2, token);
                }

                async Task StartWorkerAsync<T>(string stage) where T : BackgroundService
                {
                    period.Token.ThrowIfCancellationRequested();
                    currentStage = stage;
                    await GatewayStartupTiming.MeasureAsync(stage, async () =>
                    {
                        var worker = services.GetRequiredService<T>();
                        workers.Add(worker);
                        await worker.StartAsync(period.Token);
                        if (worker.ExecuteTask is { IsCompleted: true } task) await task;
                    });
                }
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
    }

    private string LocalArtifactStamp()
    {
        if (services.GetRequiredService<IScriptConfigurationStore>() is SharedScriptConfigurationStore shared)
            return $"|scripts:{shared.CurrentRevision}|models:{services.GetRequiredService<OnnxModelCatalogService>().SharedRevision}";
        var config = services.GetRequiredService<IConfiguration>();
        static long Stamp(string path) => File.GetLastWriteTimeUtc(Path.GetFullPath(path, AppContext.BaseDirectory)).Ticks;
        long scripts = Stamp(config["Gateway:Scripting:ConfigurationFile"] ?? "Data/Scripting/config.json");
        long catalog = Stamp(Path.Combine(config["Gateway:Inference:ModelDirectory"] ?? "Data/Models", "catalog.json"));
        return $"|{scripts}|{catalog}";
    }

    private async Task<string> FingerprintAsync(GatewayCoreService core, CancellationToken token)
    {
        var project = core.CurrentProject;
        var scripts = await services.GetRequiredService<IScriptConfigurationStore>().LoadAsync(token);
        var compiler = services.GetRequiredService<GatewayScriptCompiler>();
        foreach (var script in scripts.Scripts.Where(s => s.Enabled))
        {
            var sources = script.ScriptType == GatewayScriptType.ValueTransform
                ? script.PublishedVersions.Select(v => v.SourceCode).Append(script.PublishedSourceCode).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct()
                : new[] { script.SourceCode };
            foreach (string code in sources)
                if (!(await compiler.ValidateAsync(code, script.ScriptType, token)).Success)
                    throw new InvalidOperationException("HA_SCRIPT_VALIDATION_FAILED");
        }
        var catalog = services.GetRequiredService<OnnxModelCatalogService>();
        var models = new List<object>();
        foreach (var m in catalog.GetModels().Where(m => m.PublishedVersion > 0).OrderBy(m => m.Id, StringComparer.Ordinal))
        {
            var published = catalog.ResolvePublishedVersion(m.Id, m.PublishedVersion);
            await using var file = File.OpenRead(published.FullPath);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
            if (!string.Equals(hash, published.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("HA_MODEL_HASH_MISMATCH");
            models.Add(new { m.Id, m.PublishedVersion, hash });
        }
        return ComputeBusinessFingerprint(ProjectProtobufCodec.SerializeProject(project), scripts, models);
    }

    internal static string ComputeBusinessFingerprint(byte[] projectBytes, ScriptConfigurationDocument scripts, List<object> models)
    {
        // Driver availability is enforced per device by the local acquisition/write paths.
        return "v3:" + Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            project = Convert.ToHexString(SHA256.HashData(projectBytes)),
            scripts, models
        })));
    }

    private async Task ObserveOwnershipAsync(GatewayCoreService core, bool clustered, long epoch,
        string fingerprint, CancellationTokenSource period)
    {
        var token = period.Token;
        string revision = clustered ? core.ProjectRevision + LocalArtifactStamp() : "";
        long activation = Stopwatch.GetTimestamp();
        var runtime = (RuntimeEngine)core.Runtime;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(clustered ? ha.RenewInterval : TimeSpan.FromSeconds(1), token);
                if (clustered != ha.IsCluster || !ha.CanRunBusiness || ha.Epoch != epoch) break;
                if (!clustered) continue; // Enrollment is latched by HaEnrollmentWorker.
                long last = Math.Max(activation, runtime.LastSchedulerTick);
                if (Stopwatch.GetElapsedTime(last) > core.HaProgressTimeout) break;
                string current = core.ProjectRevision + LocalArtifactStamp();
                if (revision != current)
                {
                    revision = current;
                    fingerprint = await FingerprintAsync(core, token);
                }
                await ha.MaintainLeaseAsync(fingerprint, true, token, renewOnly: true);
                if (!ha.CanRunBusiness || ha.Epoch != epoch) break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
        {
            logger.LogError(ex, "HA supervision failed; closing this business epoch.");
        }
        finally
        {
            ha.RevokeLocal();
            await period.CancelAsync();
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

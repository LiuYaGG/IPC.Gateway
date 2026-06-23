/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Watchdog
* 项目描述 ：
* 类 名 称 ：GatewayWatchdogHostedService
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Watchdog
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
using IPC.Gateway.Core.Application.Gateway.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IPC.Gateway.Watchdog;

public sealed class GatewayWatchdogHostedService : BackgroundService, IGatewayWatchdogService
{
    private readonly IGatewayApplicationService _gateway;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<GatewayWatchdogHostedService> _logger;
    private readonly GatewayWatchdogOptions _options;
    private readonly GatewayWatchdogEvaluator _evaluator;
    private readonly GatewayRestartProtectionStore _protectionStore;
    private readonly object _sync = new object();
    private GatewayWatchdogSnapshot _snapshot;

    public GatewayWatchdogHostedService(
        IGatewayApplicationService gateway,
        IHostApplicationLifetime lifetime,
        GatewayWatchdogOptions options,
        ILogger<GatewayWatchdogHostedService> logger)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _options = options ?? new GatewayWatchdogOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _evaluator = new GatewayWatchdogEvaluator(_options);
        _protectionStore = new GatewayRestartProtectionStore(_options);
        _snapshot = new GatewayWatchdogSnapshot
        {
            Enabled = _options.Enabled,
            State = _options.Enabled ? GatewayWatchdogStates.Healthy : GatewayWatchdogStates.Disabled,
            StartedTime = DateTime.Now
        };
    }

    public GatewayWatchdogSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return CloneSnapshot(_snapshot);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            UpdateSnapshot(snapshot =>
            {
                snapshot.Enabled = false;
                snapshot.State = GatewayWatchdogStates.Disabled;
                snapshot.LastIssue = "看门狗未启用。";
            });
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.StartupGraceSeconds)), stoppingToken)
            .ContinueWith(_ => { }, CancellationToken.None);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOneCheckAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.CheckIntervalSeconds)), stoppingToken)
                .ContinueWith(_ => { }, CancellationToken.None);
        }
    }

    internal async Task RunOneCheckAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        DateTime now = DateTime.Now;
        try
        {
            GatewayRuntimeStatusDto status = _gateway.GetStatus();
            IList<GatewayWatchdogCheckResult> checks = _evaluator.Evaluate(status, now);
            GatewayRestartProtectionState protection = _protectionStore.Load();
            GatewayRestartProtectionStatus protectionStatus = protection.ToStatus(DateTime.UtcNow, _options);
            string state = CombineState(checks, protectionStatus);
            GatewayWatchdogCheckResult? issue = checks.FirstOrDefault(item => item.RecoveryRecommended) ??
                                                checks.FirstOrDefault(item => item.State == GatewayWatchdogStates.Unhealthy) ??
                                                checks.FirstOrDefault(item => item.State == GatewayWatchdogStates.Degraded);

            UpdateSnapshot(snapshot =>
            {
                snapshot.Enabled = true;
                snapshot.CheckCount++;
                snapshot.LastCheckTime = now;
                snapshot.Checks = checks;
                snapshot.RestartProtection = protectionStatus;
                snapshot.State = state;
                snapshot.LastIssue = issue?.Message ?? string.Empty;
                if (state == GatewayWatchdogStates.Healthy)
                    snapshot.LastHealthyTime = now;
            });

            if (issue?.RecoveryRecommended == true)
                await TryRecoverAsync(issue.Message, cancellationToken);
        }
        catch (Exception ex)
        {
            UpdateSnapshot(snapshot =>
            {
                snapshot.CheckCount++;
                snapshot.LastCheckTime = now;
                snapshot.State = GatewayWatchdogStates.Unhealthy;
                snapshot.LastIssue = ex.Message;
                snapshot.Checks = new[]
                {
                    new GatewayWatchdogCheckResult
                    {
                        Name = "watchdog",
                        State = GatewayWatchdogStates.Unhealthy,
                        Message = ex.Message,
                        ObservedTime = now,
                        RecoveryRecommended = true
                    }
                };
            });
            await TryRecoverAsync("看门狗读取运行状态失败：" + ex.Message, cancellationToken);
        }
    }

    private async Task TryRecoverAsync(string reason, CancellationToken cancellationToken)
    {
        GatewayRestartProtectionState protection = _protectionStore.Load();
        DateTime nowUtc = DateTime.UtcNow;
        GatewayRestartProtectionStatus protectionStatus = protection.ToStatus(nowUtc, _options);
        if (protectionStatus.RecoveryBlocked)
        {
            AddEvent("recover", "blocked", reason, "恢复动作被冷却时间或恢复风暴保护拦截。");
            UpdateSnapshot(snapshot =>
            {
                snapshot.State = GatewayWatchdogStates.Protected;
                snapshot.BlockedRecoveryCount++;
                snapshot.RestartProtection = protectionStatus;
            });
            await MaybeRequestHostStopAsync(reason, protection, cancellationToken);
            return;
        }

        protection.RecoveryAttemptsUtc.Add(nowUtc);
        protection.LastRecoveryUtc = nowUtc;
        _protectionStore.Save(protection);

        UpdateSnapshot(snapshot =>
        {
            snapshot.State = GatewayWatchdogStates.Recovering;
            snapshot.RecoveryAttemptCount++;
            snapshot.LastRecoveryTime = DateTime.Now;
        });

        try
        {
            _logger.LogWarning("IPC Gateway watchdog recovery started. Reason={Reason}", reason);
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _options.RecoveryTimeoutSeconds)));
            Task recoveryTask = Task.Run(() =>
            {
                _gateway.Stop();
                _gateway.Start();
            }, CancellationToken.None);
            await recoveryTask.WaitAsync(timeout.Token);

            AddEvent("recover", "success", reason, string.Empty);
            UpdateSnapshot(snapshot =>
            {
                snapshot.State = GatewayWatchdogStates.Healthy;
                snapshot.RecoverySuccessCount++;
            });
            _logger.LogWarning("IPC Gateway watchdog recovery completed.");
        }
        catch (Exception ex)
        {
            AddEvent("recover", "failed", reason, ex.Message);
            UpdateSnapshot(snapshot =>
            {
                snapshot.State = GatewayWatchdogStates.Unhealthy;
                snapshot.RecoveryFailureCount++;
                snapshot.LastIssue = ex.Message;
            });
            _logger.LogError(ex, "IPC Gateway watchdog recovery failed.");
            await MaybeRequestHostStopAsync(reason, protection, cancellationToken);
        }
    }

    private Task MaybeRequestHostStopAsync(string reason, GatewayRestartProtectionState protection, CancellationToken cancellationToken)
    {
        if (!_options.RequestHostStopOnUnrecoverable || cancellationToken.IsCancellationRequested)
            return Task.CompletedTask;

        DateTime nowUtc = DateTime.UtcNow;
        GatewayRestartProtectionStatus status = protection.ToStatus(nowUtc, _options);
        if (status.HostRestartBlocked)
        {
            AddEvent("hostStop", "blocked", reason, "异常重启保护已拦截宿主重启请求。");
            UpdateSnapshot(snapshot =>
            {
                snapshot.State = GatewayWatchdogStates.Protected;
                snapshot.RestartProtection = status;
            });
            return Task.CompletedTask;
        }

        protection.HostRestartRequestsUtc.Add(nowUtc);
        _protectionStore.Save(protection);
        AddEvent("hostStop", "requested", reason, "已请求宿主停止，交给外部服务管理器重启。");
        UpdateSnapshot(snapshot =>
        {
            snapshot.HostRestartRequestCount++;
            snapshot.RestartProtection = protection.ToStatus(DateTime.UtcNow, _options);
        });
        _lifetime.StopApplication();
        return Task.CompletedTask;
    }

    private void AddEvent(string action, string outcome, string reason, string errorMessage)
    {
        UpdateSnapshot(snapshot =>
        {
            snapshot.RecentEvents = new[]
            {
                new GatewayWatchdogRecoveryEvent
                {
                    Timestamp = DateTime.Now,
                    Action = action,
                    Outcome = outcome,
                    Reason = reason,
                    ErrorMessage = errorMessage ?? string.Empty
                }
            }
            .Concat(snapshot.RecentEvents)
            .Take(20)
            .ToList();
        });
    }

    private void UpdateSnapshot(Action<GatewayWatchdogSnapshot> update)
    {
        lock (_sync)
        {
            update(_snapshot);
        }
    }

    private static string CombineState(IList<GatewayWatchdogCheckResult> checks, GatewayRestartProtectionStatus protection)
    {
        if (protection.RecoveryBlocked || protection.HostRestartBlocked)
            return GatewayWatchdogStates.Protected;
        if (checks.Any(item => item.State == GatewayWatchdogStates.Unhealthy))
            return GatewayWatchdogStates.Unhealthy;
        if (checks.Any(item => item.State == GatewayWatchdogStates.Degraded))
            return GatewayWatchdogStates.Degraded;
        return GatewayWatchdogStates.Healthy;
    }

    private static GatewayWatchdogSnapshot CloneSnapshot(GatewayWatchdogSnapshot source)
    {
        return new GatewayWatchdogSnapshot
        {
            Enabled = source.Enabled,
            State = source.State,
            StartedTime = source.StartedTime,
            LastCheckTime = source.LastCheckTime,
            LastHealthyTime = source.LastHealthyTime,
            LastRecoveryTime = source.LastRecoveryTime,
            LastIssue = source.LastIssue,
            CheckCount = source.CheckCount,
            RecoveryAttemptCount = source.RecoveryAttemptCount,
            RecoverySuccessCount = source.RecoverySuccessCount,
            RecoveryFailureCount = source.RecoveryFailureCount,
            BlockedRecoveryCount = source.BlockedRecoveryCount,
            HostRestartRequestCount = source.HostRestartRequestCount,
            Checks = source.Checks.Select(CloneCheck).ToList(),
            RecentEvents = source.RecentEvents.Select(CloneEvent).ToList(),
            RestartProtection = new GatewayRestartProtectionStatus
            {
                RecentRecoveryCount = source.RestartProtection.RecentRecoveryCount,
                RecentHostRestartRequestCount = source.RestartProtection.RecentHostRestartRequestCount,
                RecoveryBlocked = source.RestartProtection.RecoveryBlocked,
                HostRestartBlocked = source.RestartProtection.HostRestartBlocked,
                WindowStartTime = source.RestartProtection.WindowStartTime,
                NextAllowedRecoveryTime = source.RestartProtection.NextAllowedRecoveryTime
            }
        };
    }

    private static GatewayWatchdogCheckResult CloneCheck(GatewayWatchdogCheckResult source)
    {
        return new GatewayWatchdogCheckResult
        {
            Name = source.Name,
            State = source.State,
            Message = source.Message,
            ObservedTime = source.ObservedTime,
            RecoveryRecommended = source.RecoveryRecommended
        };
    }

    private static GatewayWatchdogRecoveryEvent CloneEvent(GatewayWatchdogRecoveryEvent source)
    {
        return new GatewayWatchdogRecoveryEvent
        {
            Timestamp = source.Timestamp,
            Action = source.Action,
            Outcome = source.Outcome,
            Reason = source.Reason,
            ErrorMessage = source.ErrorMessage
        };
    }
}

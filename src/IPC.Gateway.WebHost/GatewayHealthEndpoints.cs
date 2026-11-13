/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.WebHost
* 项目描述 ：
* 类 名 称 ：GatewayHealthEndpoints
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
using System.Reflection;
using IPC.Gateway.Core.Application.Gateway;
using IPC.Gateway.Core.Application.Gateway.Contracts;
using IPC.Gateway.Core.Gateway;
using Microsoft.Extensions.Configuration;

namespace IPC.Gateway.WebHost;

public static class GatewayHealthEndpoints
{
    private const string Healthy = "Healthy";
    private const string Degraded = "Degraded";
    private const string Unhealthy = "Unhealthy";
    private const string Disabled = "Disabled";
    private static readonly DateTime StartedTime = DateTime.Now;

    public static IEndpointRouteBuilder MapGatewayHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(CreateLiveResponse()));
        app.MapGet("/health/live", () => Results.Ok(CreateLiveResponse()));
        app.MapGet("/api/health/live", () => Results.Ok(CreateLiveResponse()));

        app.MapGet("/health/ready", (IGatewayApplicationService gateway, IConfiguration configuration) => CreateReadyResult(gateway, CreateSystemResourceHealthOptions(configuration)));
        app.MapGet("/api/health", (IGatewayApplicationService gateway, IConfiguration configuration) => CreateReadyResult(gateway, CreateSystemResourceHealthOptions(configuration)));
        app.MapGet("/api/health/ready", (IGatewayApplicationService gateway, IConfiguration configuration) => CreateReadyResult(gateway, CreateSystemResourceHealthOptions(configuration)));

        return app;
    }

    internal static IResult CreateReadyResult(IGatewayApplicationService gateway)
    {
        return CreateReadyResult(gateway, new SystemResourceHealthOptions());
    }

    internal static IResult CreateReadyResult(IGatewayApplicationService gateway, SystemResourceHealthOptions resourceHealthOptions)
    {
        GatewayHealthResponse response;
        try
        {
            response = CreateReadyResponse(
                gateway.GetStatusSummary(),
                GatewayConfigurationContractMapper.ToConfig(gateway.GetStorageHealthOptions()),
                resourceHealthOptions);
        }
        catch (Exception ex)
        {
            response = CreateFailedResponse(ex);
        }

        int statusCode = string.Equals(response.Status, Unhealthy, StringComparison.OrdinalIgnoreCase)
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status200OK;
        return Results.Json(response, statusCode: statusCode);
    }

    private static GatewayHealthResponse CreateLiveResponse()
    {
        return new GatewayHealthResponse
        {
            Success = true,
            Status = Healthy,
            Service = "IPC.Gateway.WebHost",
            Version = GetVersion(),
            Timestamp = DateTime.Now,
            StartedTime = StartedTime,
            UptimeSeconds = Math.Round((DateTime.Now - StartedTime).TotalSeconds, 3),
            Components =
            {
                new GatewayHealthComponent
                {
                    Name = "webhost",
                    Status = Healthy,
                    Message = "WebHost process is alive."
                }
            }
        };
    }

    private static GatewayHealthResponse CreateReadyResponse(
        GatewayRuntimeStatusDto status,
        StorageHealthThresholds storageHealthThresholds,
        SystemResourceHealthOptions resourceHealthOptions)
    {
        GatewayHealthResponse response = new GatewayHealthResponse
        {
            Service = "IPC.Gateway.WebHost",
            Version = GetVersion(),
            Timestamp = DateTime.Now,
            StartedTime = StartedTime,
            UptimeSeconds = Math.Round((DateTime.Now - StartedTime).TotalSeconds, 3),
            ProjectId = status.ProjectId,
            ProjectName = status.ProjectName,
            IsRunning = status.IsRunning
        };

        response.Components.Add(CreateGatewayComponent(status));
        response.Components.Add(CreateConfigurationComponent(status.ConfigValidation));
        response.Components.Add(CreateMqttComponent(status.Mqtt));
        response.Components.Add(CreateStorageComponent("mqttOutboxStorage", status.Mqtt.Enabled, status.Mqtt.OutboxDirectory, storageHealthThresholds));
        response.Components.Add(CreateHistoryComponent(status.History));
        response.Components.Add(CreateStorageComponent("historyStorage", status.History.Enabled, status.History.Directory, storageHealthThresholds));
        response.Components.Add(CreateRuleEngineComponent(status.FlowRuleEngine));
        response.Components.Add(CreateSchedulerComponent(status.Scheduler));
        response.Components.Add(CreateSystemResourceComponent(status.System, resourceHealthOptions));

        response.Status = CombineStatus(response.Components);
        response.Success = !string.Equals(response.Status, Unhealthy, StringComparison.OrdinalIgnoreCase);
        response.Runtime = new GatewayHealthRuntimeSummary
        {
            DeviceCount = status.DeviceCount,
            OnlineDeviceCount = status.OnlineDeviceCount,
            TagCount = status.TagCount,
            GoodTagCount = status.GoodTagCount,
            BadTagCount = status.BadTagCount,
            NoDataTagCount = status.NoDataTagCount,
            RecentErrorCount = status.RecentErrors.Count,
            MqttConnected = status.Mqtt.IsConnected,
            HistoryRunning = status.History.IsRunning,
            RuleEngineRunning = status.FlowRuleEngine.IsRunning
        };
        return response;
    }

    private static GatewayHealthResponse CreateFailedResponse(Exception ex)
    {
        return new GatewayHealthResponse
        {
            Success = false,
            Status = Unhealthy,
            Service = "IPC.Gateway.WebHost",
            Version = GetVersion(),
            Timestamp = DateTime.Now,
            StartedTime = StartedTime,
            UptimeSeconds = Math.Round((DateTime.Now - StartedTime).TotalSeconds, 3),
            ErrorMessage = ex.Message,
            Components =
            {
                new GatewayHealthComponent
                {
                    Name = "gateway",
                    Status = Unhealthy,
                    Message = ex.Message
                }
            }
        };
    }

    private static GatewayHealthComponent CreateGatewayComponent(GatewayRuntimeStatusDto status)
    {
        return new GatewayHealthComponent
        {
            Name = "gateway",
            Status = status.IsRunning ? Healthy : Unhealthy,
            Message = status.IsRunning ? "Gateway runtime is running." : "Gateway runtime is not running.",
            Data =
            {
                ["projectId"] = status.ProjectId,
                ["projectName"] = status.ProjectName,
                ["configurationStore"] = status.ConfigurationStore,
                ["startedTime"] = status.StartedTime,
                ["lastReloadTime"] = status.LastReloadTime
            }
        };
    }

    private static GatewayHealthComponent CreateConfigurationComponent(ProjectValidationResultDto validation)
    {
        return new GatewayHealthComponent
        {
            Name = "configuration",
            Status = validation.IsValid ? Healthy : Unhealthy,
            Message = validation.IsValid ? "Configuration is valid." : "Configuration validation failed.",
            Data =
            {
                ["errors"] = validation.Errors,
                ["warnings"] = validation.Warnings
            }
        };
    }

    private static GatewayHealthComponent CreateMqttComponent(MqttRuntimeStatusDto mqtt)
    {
        bool circuitOpen = IsCircuitOpen(mqtt.CircuitBreaker);
        string status = !mqtt.Enabled
            ? Disabled
            : circuitOpen || mqtt.PublishConsecutiveFailureCount > 0 || mqtt.OutboxInvalidMessageCount > 0
                ? Degraded
                : mqtt.IsConnected ? Healthy : mqtt.IsRunning ? Degraded : Degraded;
        return new GatewayHealthComponent
        {
            Name = "mqtt",
            Status = status,
            Message = !mqtt.Enabled
                ? "MQTT is disabled."
                : circuitOpen
                    ? "MQTT circuit breaker is open; degraded mode: " + mqtt.CircuitBreaker.DegradedMode + "."
                : mqtt.IsConnected
                    ? "MQTT broker is connected."
                    : string.IsNullOrWhiteSpace(mqtt.LastError) ? "MQTT broker is not connected." : mqtt.LastError,
            Data =
            {
                ["broker"] = mqtt.Broker,
                ["isRunning"] = mqtt.IsRunning,
                ["isConnected"] = mqtt.IsConnected,
                ["reconnectCount"] = mqtt.ReconnectCount,
                ["outboxDirectory"] = mqtt.OutboxDirectory,
                ["outboxPendingCount"] = mqtt.OutboxPendingCount,
                ["outboxOldestPendingAgeSeconds"] = mqtt.OutboxOldestPendingAgeSeconds,
                ["outboxInvalidMessageCount"] = mqtt.OutboxInvalidMessageCount,
                ["outboxQuarantinedMessageCount"] = mqtt.OutboxQuarantinedMessageCount,
                ["outboxQuarantineCount"] = mqtt.OutboxQuarantineCount,
                ["outboxQuarantineBytes"] = mqtt.OutboxQuarantineBytes,
                ["outboxQuarantineExpiredDeletedCount"] = mqtt.OutboxQuarantineExpiredDeletedCount,
                ["outboxOldestQuarantineTime"] = mqtt.OutboxOldestQuarantineTime,
                ["outboxQuarantineDirectory"] = mqtt.OutboxQuarantineDirectory,
                ["failedPublishes"] = mqtt.FailedPublishes,
                ["publishConsecutiveFailureCount"] = mqtt.PublishConsecutiveFailureCount,
                ["publishRetryBackoffSeconds"] = mqtt.PublishRetryBackoffSeconds,
                ["nextPublishRetryTime"] = mqtt.NextPublishRetryTime,
                ["lastPublishTime"] = mqtt.LastPublishTime,
                ["lastPublishFailureTime"] = mqtt.LastPublishFailureTime,
                ["circuitBreaker"] = mqtt.CircuitBreaker
            }
        };
    }

    private static GatewayHealthComponent CreateHistoryComponent(HistoryStatsDto history)
    {
        bool circuitOpen = IsCircuitOpen(history.CircuitBreaker);
        string status = !history.Enabled ? Disabled : circuitOpen || history.IsDegraded ? Degraded : history.IsRunning ? Healthy : Degraded;
        return new GatewayHealthComponent
        {
            Name = "history",
            Status = status,
            Message = !history.Enabled
                ? "Local history is disabled."
                : circuitOpen
                    ? "Local history circuit breaker is open; degraded mode: " + history.CircuitBreaker.DegradedMode + "."
                : history.IsRunning ? "Local history is running." : "Local history is enabled but not running.",
            Data =
            {
                ["directory"] = history.Directory,
                ["retentionDays"] = history.RetentionDays,
                ["valueFiles"] = history.ValueFiles,
                ["alarmFiles"] = history.AlarmFiles,
                ["publishFiles"] = history.PublishFiles,
                ["totalBytes"] = history.TotalBytes,
                ["isDegraded"] = history.IsDegraded,
                ["lastErrorTime"] = history.LastErrorTime,
                ["lastError"] = history.LastError,
                ["circuitBreaker"] = history.CircuitBreaker
            }
        };
    }

    private static GatewayHealthComponent CreateStorageComponent(string name, bool enabled, string path, StorageHealthThresholds storageHealthThresholds)
    {
        if (!enabled)
        {
            return new GatewayHealthComponent
            {
                Name = name,
                Status = Disabled,
                Message = "Storage check is disabled."
            };
        }

        StorageHealthStatus storage = StorageHealthEvaluator.EvaluatePath(path, storageHealthThresholds);
        return new GatewayHealthComponent
        {
            Name = name,
            Status = NormalizeHealthStatus(storage.HealthStatus),
            Message = storage.HealthMessage,
            Data =
            {
                ["path"] = storage.Path,
                ["rootPath"] = storage.RootPath,
                ["isAvailable"] = storage.IsAvailable,
                ["totalBytes"] = storage.TotalBytes,
                ["availableBytes"] = storage.AvailableBytes,
                ["usedBytes"] = storage.UsedBytes,
                ["availablePercent"] = storage.AvailablePercent,
                ["usagePercent"] = storage.UsagePercent,
                ["degradedAvailableBytes"] = storage.DegradedAvailableBytes,
                ["unhealthyAvailableBytes"] = storage.UnhealthyAvailableBytes,
                ["degradedAvailablePercent"] = storage.DegradedAvailablePercent,
                ["unhealthyAvailablePercent"] = storage.UnhealthyAvailablePercent,
                ["sampleTime"] = storage.SampleTime
            }
        };
    }

    private static GatewayHealthComponent CreateRuleEngineComponent(RuleEngineRuntimeStatusDto ruleEngine)
    {
        bool circuitOpen = IsCircuitOpen(ruleEngine.CircuitBreaker);
        string status = !ruleEngine.Enabled ? Disabled : circuitOpen ? Degraded : ruleEngine.IsRunning ? Healthy : Degraded;
        return new GatewayHealthComponent
        {
            Name = "ruleEngine",
            Status = status,
            Message = !ruleEngine.Enabled
                ? "Rule engine has no enabled rules."
                : circuitOpen
                    ? "Rule engine circuit breaker is open; degraded mode: " + ruleEngine.CircuitBreaker.DegradedMode + "."
                : ruleEngine.IsRunning ? "Rule engine is running." : "Rule engine is enabled but not running.",
            Data =
            {
                ["ruleCount"] = ruleEngine.RuleCount,
                ["enabledRuleCount"] = ruleEngine.EnabledRuleCount,
                ["activeRuleCount"] = ruleEngine.ActiveRuleCount,
                ["evaluationCount"] = ruleEngine.EvaluationCount,
                ["failedEvaluationCount"] = ruleEngine.FailedEvaluationCount,
                ["lastError"] = ruleEngine.LastError,
                ["circuitBreaker"] = ruleEngine.CircuitBreaker
            }
        };
    }

    private static GatewayHealthComponent CreateSchedulerComponent(RuntimeSchedulerStatusDto scheduler)
    {
        string status = NormalizeHealthStatus(scheduler.HealthStatus);
        return new GatewayHealthComponent
        {
            Name = "scheduler",
            Status = status,
            Message = string.IsNullOrWhiteSpace(scheduler.HealthMessage) ? "Scheduler status is available." : scheduler.HealthMessage,
            Data =
            {
                ["isolationStrategy"] = scheduler.IsolationStrategy,
                ["maxConcurrentDevicePolls"] = scheduler.MaxConcurrentDevicePolls,
                ["schedulerIntervalMs"] = scheduler.SchedulerIntervalMs,
                ["backpressureEnabled"] = scheduler.BackpressureEnabled,
                ["backpressureActive"] = scheduler.BackpressureActive,
                ["queueHighWatermark"] = scheduler.QueueHighWatermark,
                ["queueLowWatermark"] = scheduler.QueueLowWatermark,
                ["backpressureDelayMs"] = scheduler.BackpressureDelayMs,
                ["maxDevicePollsQueuedPerSchedulerTick"] = scheduler.MaxDevicePollsQueuedPerSchedulerTick,
                ["slowPollThresholdMs"] = scheduler.SlowPollThresholdMs,
                ["pollTimeoutMs"] = scheduler.PollTimeoutMs,
                ["pendingCount"] = scheduler.Queue.PendingCount,
                ["runningCount"] = scheduler.Queue.RunningCount,
                ["queueLimit"] = scheduler.Queue.QueueLimit,
                ["highWatermark"] = scheduler.Queue.HighWatermark,
                ["lowWatermark"] = scheduler.Queue.LowWatermark,
                ["utilizationPercent"] = scheduler.Queue.UtilizationPercent,
                ["queueBackpressureActive"] = scheduler.Queue.BackpressureActive,
                ["availableWorkers"] = scheduler.Queue.AvailableWorkers,
                ["rejectedCount"] = scheduler.Queue.RejectedCount,
                ["backpressureThrottledCount"] = scheduler.Queue.BackpressureThrottledCount,
                ["rateLimitedCount"] = scheduler.Queue.RateLimitedCount,
                ["maxObservedPendingCount"] = scheduler.Queue.MaxObservedPendingCount,
                ["lastBackpressureTime"] = scheduler.Queue.LastBackpressureTime,
                ["lastBackpressureMessage"] = scheduler.Queue.LastBackpressureMessage,
                ["totalQueued"] = scheduler.TotalQueued,
                ["totalStarted"] = scheduler.TotalStarted,
                ["totalCompleted"] = scheduler.TotalCompleted,
                ["totalFailed"] = scheduler.TotalFailed,
                ["totalSlow"] = scheduler.TotalSlow,
                ["totalBackpressureThrottled"] = scheduler.TotalBackpressureThrottled,
                ["totalRateLimited"] = scheduler.TotalRateLimited,
                ["pollTimeoutCount"] = scheduler.Timeout.PollTimeoutCount,
                ["readTimeoutCount"] = scheduler.Timeout.ReadTimeoutCount,
                ["recentPollTimeoutCount"] = scheduler.Timeout.RecentPollTimeoutCount,
                ["recentReadTimeoutCount"] = scheduler.Timeout.RecentReadTimeoutCount,
                ["timeoutWindowSeconds"] = scheduler.Timeout.TimeoutWindowSeconds
            }
        };
    }

    private static GatewayHealthComponent CreateSystemResourceComponent(SystemResourceStatusDto system, SystemResourceHealthOptions options)
    {
        system ??= new SystemResourceStatusDto();
        SystemResourceHealthOptions normalized = SystemResourceHealthEvaluator.NormalizeOptions(options);
        SystemResourceHealthStatus health = SystemResourceHealthEvaluator.Evaluate(ToSystemResourceStatus(system), normalized);
        return new GatewayHealthComponent
        {
            Name = "systemResources",
            Status = string.Equals(health.HealthStatus, Disabled, StringComparison.OrdinalIgnoreCase)
                ? Disabled
                : NormalizeHealthStatus(health.HealthStatus),
            Message = health.HealthMessage,
            Data =
            {
                ["cpuUsagePercent"] = system.CpuUsagePercent,
                ["memoryUsagePercent"] = system.MemoryUsagePercent,
                ["totalMemoryBytes"] = system.TotalMemoryBytes,
                ["availableMemoryBytes"] = system.AvailableMemoryBytes,
                ["usedMemoryBytes"] = system.UsedMemoryBytes,
                ["processWorkingSetBytes"] = system.ProcessWorkingSetBytes,
                ["threadPoolAvailableWorkerThreads"] = system.ThreadPoolAvailableWorkerThreads,
                ["threadPoolMaxWorkerThreads"] = system.ThreadPoolMaxWorkerThreads,
                ["threadPoolAvailableCompletionPortThreads"] = system.ThreadPoolAvailableCompletionPortThreads,
                ["threadPoolMaxCompletionPortThreads"] = system.ThreadPoolMaxCompletionPortThreads,
                ["threadPoolWorkerUtilizationPercent"] = system.ThreadPoolWorkerUtilizationPercent,
                ["degradedCpuUsagePercent"] = normalized.DegradedCpuUsagePercent,
                ["unhealthyCpuUsagePercent"] = normalized.UnhealthyCpuUsagePercent,
                ["degradedMemoryUsagePercent"] = normalized.DegradedMemoryUsagePercent,
                ["unhealthyMemoryUsagePercent"] = normalized.UnhealthyMemoryUsagePercent,
                ["degradedAvailableThreadPoolWorkers"] = normalized.DegradedAvailableThreadPoolWorkers,
                ["unhealthyAvailableThreadPoolWorkers"] = normalized.UnhealthyAvailableThreadPoolWorkers,
                ["sampleTime"] = system.SampleTime,
                ["source"] = system.Source
            }
        };
    }

    private static SystemResourceStatus ToSystemResourceStatus(SystemResourceStatusDto dto)
    {
        dto ??= new SystemResourceStatusDto();
        return new SystemResourceStatus
        {
            CpuUsagePercent = dto.CpuUsagePercent,
            MemoryUsagePercent = dto.MemoryUsagePercent,
            TotalMemoryBytes = dto.TotalMemoryBytes,
            AvailableMemoryBytes = dto.AvailableMemoryBytes,
            UsedMemoryBytes = dto.UsedMemoryBytes,
            ProcessWorkingSetBytes = dto.ProcessWorkingSetBytes,
            ThreadPoolAvailableWorkerThreads = dto.ThreadPoolAvailableWorkerThreads,
            ThreadPoolMaxWorkerThreads = dto.ThreadPoolMaxWorkerThreads,
            ThreadPoolAvailableCompletionPortThreads = dto.ThreadPoolAvailableCompletionPortThreads,
            ThreadPoolMaxCompletionPortThreads = dto.ThreadPoolMaxCompletionPortThreads,
            ThreadPoolWorkerUtilizationPercent = dto.ThreadPoolWorkerUtilizationPercent,
            SampleTime = dto.SampleTime,
            Source = dto.Source
        };
    }

    private static string NormalizeHealthStatus(string status)
    {
        if (string.Equals(status, Unhealthy, StringComparison.OrdinalIgnoreCase))
            return Unhealthy;
        if (string.Equals(status, Degraded, StringComparison.OrdinalIgnoreCase))
            return Degraded;
        return Healthy;
    }

    private static bool IsCircuitOpen(CircuitBreakerStatusDto circuitBreaker)
    {
        return circuitBreaker != null && circuitBreaker.IsOpen;
    }

    private static string CombineStatus(IList<GatewayHealthComponent> components)
    {
        if (components.Any(component => string.Equals(component.Status, Unhealthy, StringComparison.OrdinalIgnoreCase)))
            return Unhealthy;
        if (components.Any(component => string.Equals(component.Status, Degraded, StringComparison.OrdinalIgnoreCase)))
            return Degraded;
        return Healthy;
    }

    private static SystemResourceHealthOptions CreateSystemResourceHealthOptions(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection("Gateway:Reliability");
        SystemResourceHealthOptions defaults = new SystemResourceHealthOptions();
        return SystemResourceHealthEvaluator.NormalizeOptions(new SystemResourceHealthOptions
        {
            Enabled = GetBool(section, "Enabled", defaults.Enabled),
            DegradedCpuUsagePercent = GetDouble(section, "DegradedCpuUsagePercent", defaults.DegradedCpuUsagePercent),
            UnhealthyCpuUsagePercent = GetDouble(section, "UnhealthyCpuUsagePercent", defaults.UnhealthyCpuUsagePercent),
            DegradedMemoryUsagePercent = GetDouble(section, "DegradedMemoryUsagePercent", defaults.DegradedMemoryUsagePercent),
            UnhealthyMemoryUsagePercent = GetDouble(section, "UnhealthyMemoryUsagePercent", defaults.UnhealthyMemoryUsagePercent),
            DegradedAvailableThreadPoolWorkers = GetInt(section, "DegradedAvailableThreadPoolWorkers", defaults.DegradedAvailableThreadPoolWorkers),
            UnhealthyAvailableThreadPoolWorkers = GetInt(section, "UnhealthyAvailableThreadPoolWorkers", defaults.UnhealthyAvailableThreadPoolWorkers)
        });
    }

    private static bool GetBool(IConfiguration configuration, string key, bool defaultValue)
    {
        string? value = configuration[key];
        return bool.TryParse(value, out bool parsed) ? parsed : defaultValue;
    }

    private static int GetInt(IConfiguration configuration, string key, int defaultValue)
    {
        string? value = configuration[key];
        return int.TryParse(value, out int parsed) ? parsed : defaultValue;
    }

    private static double GetDouble(IConfiguration configuration, string key, double defaultValue)
    {
        string? value = configuration[key];
        return double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed) ? parsed : defaultValue;
    }

    private static string GetVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty;
    }

    private sealed class GatewayHealthResponse
    {
        public bool Success { get; set; }
        public string Status { get; set; } = Healthy;
        public string Service { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public DateTime StartedTime { get; set; }
        public double UptimeSeconds { get; set; }
        public bool IsRunning { get; set; }
        public string ProjectId { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public GatewayHealthRuntimeSummary Runtime { get; set; } = new GatewayHealthRuntimeSummary();
        public IList<GatewayHealthComponent> Components { get; set; } = new List<GatewayHealthComponent>();
    }

    private sealed class GatewayHealthRuntimeSummary
    {
        public int DeviceCount { get; set; }
        public int OnlineDeviceCount { get; set; }
        public int TagCount { get; set; }
        public int GoodTagCount { get; set; }
        public int BadTagCount { get; set; }
        public int NoDataTagCount { get; set; }
        public int RecentErrorCount { get; set; }
        public bool MqttConnected { get; set; }
        public bool HistoryRunning { get; set; }
        public bool RuleEngineRunning { get; set; }
    }

    private sealed class GatewayHealthComponent
    {
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = Healthy;
        public string Message { get; set; } = string.Empty;
        public IDictionary<string, object?> Data { get; set; } = new Dictionary<string, object?>();
    }
}

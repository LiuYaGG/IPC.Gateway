/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Watchdog
* 项目描述 ：
* 类 名 称 ：GatewayWatchdogEvaluator
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
using IPC.Gateway.Core.Application.Gateway.Contracts;

namespace IPC.Gateway.Watchdog;

public sealed class GatewayWatchdogEvaluator
{
    private readonly GatewayWatchdogOptions _options;
    private long _lastSchedulerCompleted = -1;
    private DateTime _lastSchedulerProgressTime = DateTime.Now;
    private DateTime _lastMqttConnectedTime = DateTime.Now;

    public GatewayWatchdogEvaluator(GatewayWatchdogOptions options)
    {
        _options = options ?? new GatewayWatchdogOptions();
    }

    public IList<GatewayWatchdogCheckResult> Evaluate(GatewayRuntimeStatusDto status, DateTime now)
    {
        List<GatewayWatchdogCheckResult> checks = new List<GatewayWatchdogCheckResult>
        {
            CheckRuntime(status, now)
        };

        if (_options.MonitorScheduler)
            checks.Add(CheckScheduler(status, now));
        if (_options.MonitorMqtt)
            checks.Add(CheckMqtt(status, now));
        if (_options.MonitorHistory)
            checks.Add(CheckHistory(status, now));
        if (_options.MonitorRuleEngine)
            checks.Add(CheckRuleEngine(status, now));
        if (_options.MonitorOpcUa)
            checks.Add(CheckOpcUa(status, now));

        return checks;
    }

    private static GatewayWatchdogCheckResult CheckRuntime(GatewayRuntimeStatusDto status, DateTime now)
    {
        return new GatewayWatchdogCheckResult
        {
            Name = "runtime",
            State = status.IsRunning ? GatewayWatchdogStates.Healthy : GatewayWatchdogStates.Unhealthy,
            Message = status.IsRunning ? "网关运行时正常。" : "网关运行时未运行。",
            ObservedTime = now,
            RecoveryRecommended = !status.IsRunning
        };
    }

    private GatewayWatchdogCheckResult CheckScheduler(GatewayRuntimeStatusDto status, DateTime now)
    {
        long completed = status.Scheduler.TotalCompleted;
        bool progressed = _lastSchedulerCompleted < 0 || completed > _lastSchedulerCompleted;
        if (progressed)
        {
            _lastSchedulerCompleted = completed;
            _lastSchedulerProgressTime = now;
        }

        bool hasWork = status.EnabledDeviceCount > 0 && status.TagCount > 0;
        double secondsWithoutProgress = (now - _lastSchedulerProgressTime).TotalSeconds;
        bool stalled = hasWork &&
                       status.IsRunning &&
                       secondsWithoutProgress >= Math.Max(30, _options.RuntimeNoProgressSeconds) &&
                       status.Scheduler.Queue.RunningCount > 0;

        return new GatewayWatchdogCheckResult
        {
            Name = "scheduler",
            State = stalled ? GatewayWatchdogStates.Unhealthy : NormalizeState(status.Scheduler.HealthStatus),
            Message = stalled
                ? $"采集调度疑似卡住，{Math.Round(secondsWithoutProgress)} 秒没有完成任务。"
                : string.IsNullOrWhiteSpace(status.Scheduler.HealthMessage) ? "采集调度正常。" : status.Scheduler.HealthMessage,
            ObservedTime = now,
            RecoveryRecommended = stalled
        };
    }

    private GatewayWatchdogCheckResult CheckMqtt(GatewayRuntimeStatusDto status, DateTime now)
    {
        if (!status.Mqtt.Enabled)
            return Disabled("mqtt", "MQTT 未启用。", now);

        if (status.Mqtt.IsConnected)
            _lastMqttConnectedTime = now;

        double disconnectedSeconds = (now - _lastMqttConnectedTime).TotalSeconds;
        bool longDisconnected = disconnectedSeconds >= Math.Max(30, _options.MqttDisconnectedSeconds);
        return new GatewayWatchdogCheckResult
        {
            Name = "mqtt",
            State = status.Mqtt.IsConnected ? GatewayWatchdogStates.Healthy : longDisconnected ? GatewayWatchdogStates.Degraded : GatewayWatchdogStates.Degraded,
            Message = status.Mqtt.IsConnected
                ? "MQTT 连接正常。"
                : string.IsNullOrWhiteSpace(status.Mqtt.LastError) ? "MQTT 当前未连接。" : status.Mqtt.LastError,
            ObservedTime = now,
            RecoveryRecommended = false
        };
    }

    private static GatewayWatchdogCheckResult CheckHistory(GatewayRuntimeStatusDto status, DateTime now)
    {
        if (!status.History.Enabled)
            return Disabled("history", "历史库未启用。", now);

        return new GatewayWatchdogCheckResult
        {
            Name = "history",
            State = status.History.IsRunning ? GatewayWatchdogStates.Healthy : GatewayWatchdogStates.Degraded,
            Message = status.History.IsRunning ? "历史库运行正常。" : "历史库已启用但未运行。",
            ObservedTime = now
        };
    }

    private static GatewayWatchdogCheckResult CheckRuleEngine(GatewayRuntimeStatusDto status, DateTime now)
    {
        if (!status.RuleEngine.Enabled)
            return Disabled("ruleEngine", "规则引擎未启用。", now);

        return new GatewayWatchdogCheckResult
        {
            Name = "ruleEngine",
            State = status.RuleEngine.IsRunning ? GatewayWatchdogStates.Healthy : GatewayWatchdogStates.Degraded,
            Message = status.RuleEngine.IsRunning
                ? "规则引擎运行正常。"
                : string.IsNullOrWhiteSpace(status.RuleEngine.LastError) ? "规则引擎已启用但未运行。" : status.RuleEngine.LastError,
            ObservedTime = now
        };
    }

    private static GatewayWatchdogCheckResult CheckOpcUa(GatewayRuntimeStatusDto status, DateTime now)
    {
        if (!status.OpcUa.Enabled)
            return Disabled("opcUa", "OPC UA Server 未启用。", now);

        return new GatewayWatchdogCheckResult
        {
            Name = "opcUa",
            State = status.OpcUa.IsRunning ? GatewayWatchdogStates.Healthy : GatewayWatchdogStates.Degraded,
            Message = status.OpcUa.IsRunning
                ? "OPC UA Server 运行正常。"
                : string.IsNullOrWhiteSpace(status.OpcUa.LastError) ? "OPC UA Server 已启用但未运行。" : status.OpcUa.LastError,
            ObservedTime = now
        };
    }

    private static GatewayWatchdogCheckResult Disabled(string name, string message, DateTime now)
    {
        return new GatewayWatchdogCheckResult
        {
            Name = name,
            State = GatewayWatchdogStates.Disabled,
            Message = message,
            ObservedTime = now
        };
    }

    private static string NormalizeState(string state)
    {
        if (string.Equals(state, GatewayWatchdogStates.Unhealthy, StringComparison.OrdinalIgnoreCase))
            return GatewayWatchdogStates.Unhealthy;
        if (string.Equals(state, GatewayWatchdogStates.Degraded, StringComparison.OrdinalIgnoreCase))
            return GatewayWatchdogStates.Degraded;
        return GatewayWatchdogStates.Healthy;
    }
}

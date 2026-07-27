using System.Collections.Concurrent;
using System.Diagnostics;
using IPC.Gateway.Scripting.Abstractions;
using IPC.Gateway.Scripting.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IPC.Gateway.Scripting.Runtime;

/// <summary>
/// 负责脚本定义缓存、周期调度、点位变化触发和手动执行。
/// </summary>
public sealed class GatewayScriptRuntimeService : BackgroundService, IScriptRuntimeService
{
    private readonly IScriptConfigurationStore _configurationStore;
    private readonly IScriptTagAccessor _tagAccessor;
    private readonly IScriptDatabaseQueue _databaseQueue;
    private readonly GatewayScriptCompiler _compiler;
    private readonly GatewayScriptingOptions _options;
    private readonly ILogger<GatewayScriptRuntimeService> _logger;
    private readonly object _scriptsSyncRoot = new();
    private readonly Dictionary<string, GatewayScriptDefinition> _scripts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _executionGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ScriptRuntimeStatus> _statuses = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _nextIntervalUtc = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastTagTriggerUtc = new(StringComparer.OrdinalIgnoreCase);
    private CancellationToken _stoppingToken;

    /// <summary>
    /// 创建脚本运行时服务。
    /// </summary>
    public GatewayScriptRuntimeService(
        IScriptConfigurationStore configurationStore,
        IScriptTagAccessor tagAccessor,
        IScriptDatabaseQueue databaseQueue,
        GatewayScriptCompiler compiler,
        GatewayScriptingOptions options,
        ILogger<GatewayScriptRuntimeService> logger)
    {
        _configurationStore = configurationStore;
        _tagAccessor = tagAccessor;
        _databaseQueue = databaseQueue;
        _compiler = compiler;
        _options = options.Normalize();
        _logger = logger;
    }

    /// <summary>
    /// 从独立配置存储重新加载脚本定义并重置周期计划。
    /// </summary>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        ScriptConfigurationDocument document = await _configurationStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        lock (_scriptsSyncRoot)
        {
            _scripts.Clear();
            foreach (GatewayScriptDefinition script in document.Scripts)
                _scripts[script.Id] = script.Clone();
        }
        _nextIntervalUtc.Clear();
    }

    /// <summary>
    /// 校验脚本安全边界并执行编译检查。
    /// </summary>
    public Task<ScriptValidationResult> ValidateAsync(string sourceCode, CancellationToken cancellationToken = default)
    {
        return _compiler.ValidateAsync(sourceCode, cancellationToken);
    }

    /// <summary>
    /// 手动执行指定脚本，无论该脚本的自动触发开关是否启用。
    /// </summary>
    public async Task<ScriptExecutionResult> ExecuteManualAsync(string scriptId, CancellationToken cancellationToken = default)
    {
        GatewayScriptDefinition script = GetScript(scriptId) ?? throw new KeyNotFoundException("未找到指定脚本。");
        return await ExecuteScriptAsync(script, new ScriptTriggerContext
        {
            Type = ScriptTriggerType.Manual,
            TriggeredUtc = DateTimeOffset.UtcNow
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取所有脚本最近运行状态的副本。
    /// </summary>
    public IReadOnlyList<ScriptRuntimeStatus> GetStatuses()
    {
        return _statuses.Values
            .Select(CloneStatus)
            .OrderBy(item => item.ScriptId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 启动脚本定义加载、点位事件订阅和周期调度循环。
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;
        await ReloadAsync(stoppingToken).ConfigureAwait(false);
        _tagAccessor.TagChanged += HandleTagChanged;
        try
        {
            using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(_options.SchedulerResolutionMilliseconds));
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                ScheduleIntervalScripts(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _tagAccessor.TagChanged -= HandleTagChanged;
        }
    }

    /// <summary>
    /// 扫描到期的周期脚本并异步投递执行。
    /// </summary>
    private void ScheduleIntervalScripts(CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (GatewayScriptDefinition script in GetScriptsSnapshot())
        {
            if (!script.Enabled || script.TriggerType != ScriptTriggerType.Interval)
                continue;
            DateTimeOffset due = _nextIntervalUtc.GetOrAdd(script.Id, _ => now.AddSeconds(Math.Max(1, script.IntervalSeconds)));
            if (due > now)
                continue;
            _nextIntervalUtc[script.Id] = now.AddSeconds(Math.Max(1, script.IntervalSeconds));
            _ = ExecuteAutomaticAsync(script, new ScriptTriggerContext
            {
                Type = ScriptTriggerType.Interval,
                TriggeredUtc = now
            }, cancellationToken);
        }
    }

    /// <summary>
    /// 接收点位变化事件并匹配需要触发的脚本。
    /// </summary>
    private void HandleTagChanged(object? sender, ScriptTagChangedEventArgs eventArgs)
    {
        string changedPath = NormalizePath(eventArgs.CurrentValue.Path);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (GatewayScriptDefinition script in GetScriptsSnapshot())
        {
            if (!script.Enabled || script.TriggerType != ScriptTriggerType.TagChanged)
                continue;
            if (!string.Equals(NormalizePath(script.TriggerTagPath), changedPath, StringComparison.OrdinalIgnoreCase))
                continue;
            ScriptTriggerContext trigger = new()
            {
                Type = ScriptTriggerType.TagChanged,
                TagPath = eventArgs.CurrentValue.Path,
                PreviousValue = eventArgs.PreviousValue,
                CurrentValue = eventArgs.CurrentValue,
                TriggeredUtc = now
            };
            if (!MatchesChangeMode(script.TagChangeMode, trigger))
                continue;
            DateTimeOffset last = _lastTagTriggerUtc.GetOrAdd(script.Id, DateTimeOffset.MinValue);
            if ((now - last).TotalMilliseconds < script.DebounceMilliseconds)
                continue;
            _lastTagTriggerUtc[script.Id] = now;
            _ = ExecuteAutomaticAsync(script, trigger, _stoppingToken);
        }
    }

    /// <summary>
    /// 执行自动触发脚本并将异常限定在脚本运行时内部。
    /// </summary>
    private async Task ExecuteAutomaticAsync(
        GatewayScriptDefinition script,
        ScriptTriggerContext trigger,
        CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteScriptAsync(script, trigger, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "自动脚本 {ScriptName} 执行失败。", script.Name);
        }
    }

    /// <summary>
    /// 在单脚本并发锁和超时令牌约束下执行脚本。
    /// </summary>
    private async Task<ScriptExecutionResult> ExecuteScriptAsync(
        GatewayScriptDefinition script,
        ScriptTriggerContext trigger,
        CancellationToken cancellationToken)
    {
        SemaphoreSlim gate = _executionGates.GetOrAdd(script.Id, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return RecordSkipped(script.Id, "同一脚本仍在执行，本次触发已跳过。");

        DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
        Stopwatch stopwatch = Stopwatch.StartNew();
        ScriptLogCollector logs = new();
        UpdateRunning(script.Id, startedUtc);
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(script.TimeoutSeconds, 1, 300)));
            GatewayScriptGlobals globals = new(
                new ScriptTagApi(_tagAccessor, timeout.Token),
                new ScriptDatabaseApi(script.Id, _databaseQueue, timeout.Token),
                logs,
                trigger,
                timeout.Token);
            object? returnValue = await _compiler.RunAsync(script.SourceCode, globals, timeout.Token).ConfigureAwait(false);
            stopwatch.Stop();
            return RecordFinished(script.Id, ScriptExecutionState.Succeeded, returnValue, string.Empty, startedUtc, stopwatch.ElapsedMilliseconds, logs);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return RecordFinished(script.Id, ScriptExecutionState.TimedOut, null, "脚本执行超时。", startedUtc, stopwatch.ElapsedMilliseconds, logs);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return RecordFinished(script.Id, ScriptExecutionState.Failed, null, ex.GetBaseException().Message, startedUtc, stopwatch.ElapsedMilliseconds, logs);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 将脚本状态标记为正在运行。
    /// </summary>
    private void UpdateRunning(string scriptId, DateTimeOffset startedUtc)
    {
        ScriptRuntimeStatus status = _statuses.GetOrAdd(scriptId, id => new ScriptRuntimeStatus { ScriptId = id });
        lock (status)
        {
            status.State = ScriptExecutionState.Running;
            status.LastStartedUtc = startedUtc;
        }
    }

    /// <summary>
    /// 记录脚本跳过结果。
    /// </summary>
    private ScriptExecutionResult RecordSkipped(string scriptId, string message)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ScriptExecutionResult result = new()
        {
            ScriptId = scriptId,
            State = ScriptExecutionState.Skipped,
            ErrorMessage = message,
            StartedUtc = now,
            FinishedUtc = now
        };
        ScriptRuntimeStatus status = _statuses.GetOrAdd(scriptId, id => new ScriptRuntimeStatus { ScriptId = id });
        lock (status)
        {
            status.State = ScriptExecutionState.Skipped;
            status.LastError = message;
        }
        return result;
    }

    /// <summary>
    /// 记录脚本完成结果并更新累计统计和最近日志。
    /// </summary>
    private ScriptExecutionResult RecordFinished(
        string scriptId,
        ScriptExecutionState state,
        object? returnValue,
        string errorMessage,
        DateTimeOffset startedUtc,
        long durationMilliseconds,
        ScriptLogCollector logs)
    {
        DateTimeOffset finishedUtc = DateTimeOffset.UtcNow;
        List<ScriptLogEntry> entries = logs.GetEntries().ToList();
        ScriptRuntimeStatus status = _statuses.GetOrAdd(scriptId, id => new ScriptRuntimeStatus { ScriptId = id });
        lock (status)
        {
            status.State = state;
            status.ExecutionCount++;
            if (state is ScriptExecutionState.Failed or ScriptExecutionState.TimedOut)
                status.FailureCount++;
            status.LastStartedUtc = startedUtc;
            status.LastFinishedUtc = finishedUtc;
            status.LastDurationMilliseconds = durationMilliseconds;
            status.LastError = errorMessage;
            status.RecentLogs.AddRange(entries);
            if (status.RecentLogs.Count > _options.MaxRecentLogsPerScript)
                status.RecentLogs.RemoveRange(0, status.RecentLogs.Count - _options.MaxRecentLogsPerScript);
        }
        return new ScriptExecutionResult
        {
            ScriptId = scriptId,
            State = state,
            ReturnValue = returnValue,
            ErrorMessage = errorMessage,
            StartedUtc = startedUtc,
            FinishedUtc = finishedUtc,
            DurationMilliseconds = durationMilliseconds,
            Logs = entries
        };
    }

    /// <summary>
    /// 获取指定标识的脚本定义副本。
    /// </summary>
    private GatewayScriptDefinition? GetScript(string scriptId)
    {
        lock (_scriptsSyncRoot)
            return _scripts.TryGetValue(scriptId, out GatewayScriptDefinition? script) ? script.Clone() : null;
    }

    /// <summary>
    /// 获取当前全部脚本定义的快照。
    /// </summary>
    private IReadOnlyList<GatewayScriptDefinition> GetScriptsSnapshot()
    {
        lock (_scriptsSyncRoot)
            return _scripts.Values.Select(item => item.Clone()).ToList();
    }

    /// <summary>
    /// 判断点位变化是否满足脚本配置的边沿模式。
    /// </summary>
    private static bool MatchesChangeMode(ScriptTagChangeMode mode, ScriptTriggerContext trigger)
    {
        return mode switch
        {
            ScriptTagChangeMode.Any => true,
            ScriptTagChangeMode.RisingEdge => trigger.IsRisingEdge(),
            ScriptTagChangeMode.FallingEdge => trigger.IsFallingEdge(),
            _ => false
        };
    }

    /// <summary>
    /// 规范化用于比较的点位路径。
    /// </summary>
    private static string NormalizePath(string? path)
    {
        return (path ?? string.Empty).Trim().ToUpperInvariant();
    }

    /// <summary>
    /// 创建脚本运行状态的深拷贝。
    /// </summary>
    private static ScriptRuntimeStatus CloneStatus(ScriptRuntimeStatus source)
    {
        lock (source)
        {
            return new ScriptRuntimeStatus
            {
                ScriptId = source.ScriptId,
                State = source.State,
                ExecutionCount = source.ExecutionCount,
                FailureCount = source.FailureCount,
                LastStartedUtc = source.LastStartedUtc,
                LastFinishedUtc = source.LastFinishedUtc,
                LastDurationMilliseconds = source.LastDurationMilliseconds,
                LastError = source.LastError,
                RecentLogs = source.RecentLogs.Select(item => new ScriptLogEntry
                {
                    TimestampUtc = item.TimestampUtc,
                    Level = item.Level,
                    Message = item.Message
                }).ToList()
            };
        }
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using IPC.Gateway.Scripting.Abstractions;
using IPC.Gateway.Scripting.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IPC.Gateway.Scripting.Database;

/// <summary>
/// 将脚本数据库写入任务按单文件持久化，并在后台执行、重试和隔离失败任务。
/// </summary>
public sealed class ScriptDatabaseWriteDispatcher : BackgroundService, IScriptDatabaseQueue
{
    private readonly IScriptConfigurationStore _configurationStore;
    private readonly ScriptDatabaseWriteExecutor _executor;
    private readonly ScriptDatabaseCommandBuilder _commandBuilder;
    private readonly ILogger<ScriptDatabaseWriteDispatcher> _logger;
    private readonly GatewayScriptingOptions _options;
    private readonly string _outboxDirectory;
    private readonly string _failedDirectory;
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>();
    private readonly SemaphoreSlim _enqueueGate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;
    private int _pendingCount;
    private int _failedCount;
    private long _succeededCount;
    private long _retriedCount;
    private string _lastError = string.Empty;
    private DateTimeOffset? _lastSuccessUtc;

    /// <summary>
    /// 创建数据库持久化写入调度器。
    /// </summary>
    public ScriptDatabaseWriteDispatcher(
        IScriptConfigurationStore configurationStore,
        ScriptDatabaseWriteExecutor executor,
        ScriptDatabaseCommandBuilder commandBuilder,
        GatewayScriptingOptions options,
        ILogger<ScriptDatabaseWriteDispatcher> logger)
    {
        _configurationStore = configurationStore;
        _executor = executor;
        _commandBuilder = commandBuilder;
        _options = options.Normalize();
        _logger = logger;
        _outboxDirectory = Path.GetFullPath(_options.OutboxDirectory, AppContext.BaseDirectory);
        _failedDirectory = Path.Combine(_outboxDirectory, "Failed");
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    /// <summary>
    /// 校验并持久化一个数据库写入请求。
    /// </summary>
    public async Task<ScriptDatabaseWriteReceipt> EnqueueAsync(
        ScriptDatabaseWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ScriptConfigurationDocument configuration = await _configurationStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        (ScriptDatabaseConnectionDefinition connection, ScriptDatabaseWriteTarget target) = ResolveTarget(configuration, request.TargetId);
        _ = _commandBuilder.Build(connection.Provider, target, request);
        request.TargetId = target.Id;
        await _enqueueGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _pendingCount) >= _options.MaxPendingWrites)
                throw new InvalidOperationException($"数据库写入队列已达到上限 {_options.MaxPendingWrites}。");
            request.Id = CreateRequestId(request);
            request.CreatedUtc = request.CreatedUtc == default ? DateTimeOffset.UtcNow : request.CreatedUtc;
            request.NextAttemptUtc = DateTimeOffset.UtcNow;
            string path = GetRequestPath(request.Id);
            if (File.Exists(path))
            {
                return new ScriptDatabaseWriteReceipt
                {
                    RequestId = request.Id,
                    Queued = true,
                    Message = "相同去重键的写入任务已在队列中。"
                };
            }

            Directory.CreateDirectory(_outboxDirectory);
            await SaveRequestAsync(path, request, cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _pendingCount);
            await _channel.Writer.WriteAsync(path, cancellationToken).ConfigureAwait(false);
            return new ScriptDatabaseWriteReceipt
            {
                RequestId = request.Id,
                Queued = true,
                Message = "数据库写入任务已持久化入队。"
            };
        }
        finally
        {
            _enqueueGate.Release();
        }
    }

    /// <summary>
    /// 获取当前持久化数据库队列状态。
    /// </summary>
    public ScriptDatabaseQueueStatus GetStatus()
    {
        return new ScriptDatabaseQueueStatus
        {
            PendingCount = Math.Max(0, Volatile.Read(ref _pendingCount)),
            FailedCount = Math.Max(0, Volatile.Read(ref _failedCount)),
            SucceededCount = Interlocked.Read(ref _succeededCount),
            RetriedCount = Interlocked.Read(ref _retriedCount),
            LastError = _lastError,
            LastSuccessUtc = _lastSuccessUtc
        };
    }

    /// <summary>
    /// 只打开指定数据库连接，不执行查询或写入命令。
    /// </summary>
    public async Task TestConnectionAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        ScriptConfigurationDocument configuration = await _configurationStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        ScriptDatabaseConnectionDefinition connection = configuration.Connections.FirstOrDefault(item => SameId(item.Id, connectionId))
            ?? throw new KeyNotFoundException("未找到指定数据库连接。");
        await _executor.TestConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 加载磁盘待办任务并持续处理后台写入队列。
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(_outboxDirectory);
        Directory.CreateDirectory(_failedDirectory);
        string[] pendingFiles = Directory.GetFiles(_outboxDirectory, "*.json", SearchOption.TopDirectoryOnly);
        Volatile.Write(ref _pendingCount, pendingFiles.Length);
        Volatile.Write(ref _failedCount, Directory.GetFiles(_failedDirectory, "*.json", SearchOption.TopDirectoryOnly).Length);
        foreach (string file in pendingFiles)
            await _channel.Writer.WriteAsync(file, stoppingToken).ConfigureAwait(false);

        await foreach (string path in _channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            if (!File.Exists(path))
                continue;
            try
            {
                ScriptDatabaseWriteRequest request = await LoadRequestAsync(path, stoppingToken).ConfigureAwait(false);
                TimeSpan wait = request.NextAttemptUtc - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero)
                {
                    ScheduleRequeue(path, wait, stoppingToken);
                    continue;
                }
                await ProcessRequestAsync(path, request, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _logger.LogError(ex, "数据库写入队列读取任务 {Path} 失败。", path);
                await MoveUnreadableToFailedAsync(path).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// 执行单个持久化请求，并根据结果完成、重试或隔离。
    /// </summary>
    private async Task ProcessRequestAsync(string path, ScriptDatabaseWriteRequest request, CancellationToken cancellationToken)
    {
        try
        {
            ScriptConfigurationDocument configuration = await _configurationStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            (ScriptDatabaseConnectionDefinition connection, ScriptDatabaseWriteTarget target) = ResolveTarget(configuration, request.TargetId);
            await _executor.ExecuteAsync(connection, target, request, cancellationToken).ConfigureAwait(false);
            File.Delete(path);
            Interlocked.Decrement(ref _pendingCount);
            Interlocked.Increment(ref _succeededCount);
            _lastSuccessUtc = DateTimeOffset.UtcNow;
            _lastError = string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            request.AttemptCount++;
            request.LastError = ex.Message;
            _lastError = ex.Message;
            if (request.AttemptCount >= _options.MaxDatabaseRetryCount)
            {
                await SaveRequestAsync(path, request, CancellationToken.None).ConfigureAwait(false);
                MoveToFailed(path);
                Interlocked.Decrement(ref _pendingCount);
                Interlocked.Increment(ref _failedCount);
                _logger.LogError(ex, "数据库写入任务 {RequestId} 达到最大重试次数并已隔离。", request.Id);
                return;
            }

            int retrySeconds = Math.Min(300, _options.DatabaseRetryBaseSeconds * (1 << Math.Min(10, request.AttemptCount - 1)));
            request.NextAttemptUtc = DateTimeOffset.UtcNow.AddSeconds(retrySeconds);
            await SaveRequestAsync(path, request, CancellationToken.None).ConfigureAwait(false);
            Interlocked.Increment(ref _retriedCount);
            _logger.LogWarning(ex, "数据库写入任务 {RequestId} 失败，将在 {RetrySeconds} 秒后重试。", request.Id, retrySeconds);
            ScheduleRequeue(path, TimeSpan.FromSeconds(retrySeconds), cancellationToken);
        }
    }

    /// <summary>
    /// 安排任务在指定延迟后重新进入内存队列。
    /// </summary>
    private void ScheduleRequeue(string path, TimeSpan delay, CancellationToken cancellationToken)
    {
        _ = RequeueAfterDelayAsync(path, delay, cancellationToken);
    }

    /// <summary>
    /// 等待指定延迟并重新投递仍存在的任务文件。
    /// </summary>
    private async Task RequeueAfterDelayAsync(string path, TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            if (File.Exists(path))
                await _channel.Writer.WriteAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    /// 从配置文档按目标标识或名称解析并验证写入目标和数据库连接。
    /// </summary>
    private static (ScriptDatabaseConnectionDefinition Connection, ScriptDatabaseWriteTarget Target) ResolveTarget(
        ScriptConfigurationDocument configuration,
        string targetId)
    {
        ScriptDatabaseWriteTarget target = configuration.Targets.FirstOrDefault(item =>
                SameId(item.Id, targetId) || SameId(item.Name, targetId))
            ?? throw new KeyNotFoundException("未找到指定数据库写入目标。");
        ScriptDatabaseConnectionDefinition connection = configuration.Connections.FirstOrDefault(item => SameId(item.Id, target.ConnectionId))
            ?? throw new KeyNotFoundException("数据库写入目标引用的连接不存在。");
        return (connection, target);
    }

    /// <summary>
    /// 根据显式去重键或随机标识生成安全文件名。
    /// </summary>
    private static string CreateRequestId(ScriptDatabaseWriteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DeduplicationKey))
            return Guid.NewGuid().ToString("N");
        string source = $"{request.ScriptId}|{request.TargetId}|{request.Operation}|{request.DeduplicationKey}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
    }

    /// <summary>
    /// 获取指定写入请求的持久化路径。
    /// </summary>
    private string GetRequestPath(string requestId)
    {
        return Path.Combine(_outboxDirectory, requestId + ".json");
    }

    /// <summary>
    /// 使用 UTF-8 编码原子保存单个数据库写入任务。
    /// </summary>
    private async Task SaveRequestAsync(string path, ScriptDatabaseWriteRequest request, CancellationToken cancellationToken)
    {
        string temporaryPath = path + ".tmp";
        string json = JsonSerializer.Serialize(request, _jsonOptions);
        await File.WriteAllTextAsync(temporaryPath, json, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, path, true);
    }

    /// <summary>
    /// 从持久化文件读取数据库写入任务。
    /// </summary>
    private async Task<ScriptDatabaseWriteRequest> LoadRequestAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await JsonSerializer.DeserializeAsync<ScriptDatabaseWriteRequest>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("数据库写入任务文件内容为空。");
    }

    /// <summary>
    /// 将达到最大重试次数的任务移动到失败目录。
    /// </summary>
    private void MoveToFailed(string path)
    {
        Directory.CreateDirectory(_failedDirectory);
        string destination = Path.Combine(_failedDirectory, Path.GetFileName(path));
        File.Move(path, destination, true);
    }

    /// <summary>
    /// 将无法读取的任务移动到失败目录并更新统计。
    /// </summary>
    private Task MoveUnreadableToFailedAsync(string path)
    {
        if (File.Exists(path))
        {
            MoveToFailed(path);
            Interlocked.Decrement(ref _pendingCount);
            Interlocked.Increment(ref _failedCount);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 忽略大小写比较配置标识。
    /// </summary>
    private static bool SameId(string? left, string? right)
    {
        return string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

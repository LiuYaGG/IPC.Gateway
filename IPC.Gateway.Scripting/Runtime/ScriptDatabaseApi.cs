using IPC.Gateway.Scripting.Abstractions;
using IPC.Gateway.Scripting.Database;
using IPC.Gateway.Scripting.Models;

namespace IPC.Gateway.Scripting.Runtime;

/// <summary>
/// 向脚本公开仅包含结构化 INSERT 和 UPDATE 的数据库写入 API。
/// </summary>
public sealed class ScriptDatabaseApi
{
    private readonly string _scriptId;
    private readonly IScriptDatabaseQueue _queue;
    private readonly CancellationToken _cancellationToken;
    private readonly bool _enabled;

    /// <summary>
    /// 创建当前脚本执行使用的数据库 API。
    /// </summary>
    public ScriptDatabaseApi(string scriptId, IScriptDatabaseQueue queue, CancellationToken cancellationToken, bool enabled = true)
    {
        _scriptId = scriptId;
        _queue = queue;
        _cancellationToken = cancellationToken;
        _enabled = enabled;
    }

    /// <summary>
    /// 将结构化 INSERT 任务持久化到数据库写入队列。
    /// </summary>
    public Task<ScriptDatabaseWriteReceipt> InsertAsync(string targetId, object values, string deduplicationKey = "")
    {
        EnsureEnabled();
        return _queue.EnqueueAsync(new ScriptDatabaseWriteRequest
        {
            ScriptId = _scriptId,
            TargetId = targetId,
            Operation = ScriptDatabaseOperation.Insert,
            Values = ScriptObjectDictionary.FromObject(values),
            DeduplicationKey = deduplicationKey ?? string.Empty
        }, _cancellationToken);
    }

    /// <summary>
    /// 将带固定更新键的结构化 UPDATE 任务持久化到数据库写入队列。
    /// </summary>
    public Task<ScriptDatabaseWriteReceipt> UpdateAsync(
        string targetId,
        object values,
        object keys,
        string deduplicationKey = "")
    {
        EnsureEnabled();
        return _queue.EnqueueAsync(new ScriptDatabaseWriteRequest
        {
            ScriptId = _scriptId,
            TargetId = targetId,
            Operation = ScriptDatabaseOperation.Update,
            Values = ScriptObjectDictionary.FromObject(values),
            Keys = ScriptObjectDictionary.FromObject(keys),
            DeduplicationKey = deduplicationKey ?? string.Empty
        }, _cancellationToken);
    }

    /// <summary>
    /// 确保当前脚本类型拥有数据库写入能力。
    /// </summary>
    private void EnsureEnabled()
    {
        if (!_enabled)
            throw new InvalidOperationException("点位联动脚本不能调用数据库写入 API。");
    }
}

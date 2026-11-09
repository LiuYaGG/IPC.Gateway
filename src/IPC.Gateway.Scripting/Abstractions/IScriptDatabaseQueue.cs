using IPC.Gateway.Scripting.Models;

namespace IPC.Gateway.Scripting.Abstractions;

/// <summary>
/// 定义脚本数据库写入持久化队列。
/// </summary>
public interface IScriptDatabaseQueue
{
    /// <summary>
    /// 将经过结构校验的数据库写入任务持久化入队。
    /// </summary>
    Task<ScriptDatabaseWriteReceipt> EnqueueAsync(ScriptDatabaseWriteRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取当前数据库写入队列状态。
    /// </summary>
    ScriptDatabaseQueueStatus GetStatus();

    /// <summary>
    /// 仅打开指定数据库连接以验证网络和凭据。
    /// </summary>
    Task TestConnectionAsync(string connectionId, CancellationToken cancellationToken = default);
}

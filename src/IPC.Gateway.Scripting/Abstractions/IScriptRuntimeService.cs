using IPC.Gateway.Scripting.Models;

namespace IPC.Gateway.Scripting.Abstractions;

/// <summary>
/// 定义脚本运行、校验和状态查询能力。
/// </summary>
public interface IScriptRuntimeService
{
    /// <summary>
    /// 从独立配置存储重新加载脚本定义。
    /// </summary>
    Task ReloadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验脚本安全边界并执行编译检查。
    /// </summary>
    Task<ScriptValidationResult> ValidateAsync(string sourceCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按脚本业务类型校验能力边界并执行编译检查。
    /// </summary>
    Task<ScriptValidationResult> ValidateAsync(
        string sourceCode,
        GatewayScriptType scriptType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 手动执行指定脚本。
    /// </summary>
    Task<ScriptExecutionResult> ExecuteManualAsync(string scriptId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取全部脚本的最近运行状态。
    /// </summary>
    IReadOnlyList<ScriptRuntimeStatus> GetStatuses();
}

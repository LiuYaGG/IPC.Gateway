using IPC.Gateway.Scripting.Models;

namespace IPC.Gateway.Scripting.Abstractions;

/// <summary>
/// 定义脚本配置文档的独立持久化边界。
/// </summary>
public interface IScriptConfigurationStore
{
    /// <summary>
    /// 异步读取完整脚本配置。
    /// </summary>
    Task<ScriptConfigurationDocument> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步保存完整脚本配置。
    /// </summary>
    Task SaveAsync(ScriptConfigurationDocument document, CancellationToken cancellationToken = default);
}

using IPC.Gateway.Scripting.Models;

namespace IPC.Gateway.Scripting.Runtime;

/// <summary>
/// 定义每次 C# 脚本运行时可直接使用的受控全局对象。
/// </summary>
public sealed class GatewayScriptGlobals
{
    /// <summary>
    /// 创建脚本全局上下文。
    /// </summary>
    public GatewayScriptGlobals(
        ScriptTagApi tags,
        ScriptDatabaseApi database,
        ScriptLogCollector log,
        ScriptTriggerContext trigger,
        CancellationToken cancellationToken)
    {
        Tags = tags;
        Database = database;
        Log = log;
        Trigger = trigger;
        CancellationToken = cancellationToken;
    }

    public ScriptTagApi Tags { get; }
    public ScriptDatabaseApi Database { get; }
    public ScriptLogCollector Log { get; }
    public ScriptTriggerContext Trigger { get; }
    public CancellationToken CancellationToken { get; }
    public DateTimeOffset Now => DateTimeOffset.Now;
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

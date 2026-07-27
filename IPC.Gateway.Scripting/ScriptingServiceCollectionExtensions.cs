using IPC.Gateway.Scripting.Abstractions;
using IPC.Gateway.Scripting.Application;
using IPC.Gateway.Scripting.Database;
using IPC.Gateway.Scripting.Persistence;
using IPC.Gateway.Scripting.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace IPC.Gateway.Scripting;

/// <summary>
/// 提供脚本模块的依赖注入注册入口。
/// </summary>
public static class ScriptingServiceCollectionExtensions
{
    /// <summary>
    /// 注册独立脚本配置、Roslyn 运行时和持久化数据库写入队列。
    /// </summary>
    public static IServiceCollection AddGatewayScripting(
        this IServiceCollection services,
        GatewayScriptingOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        GatewayScriptingOptions normalized = (options ?? new GatewayScriptingOptions()).Normalize();
        services.AddSingleton(normalized);
        services.AddSingleton<IScriptConfigurationStore, JsonScriptConfigurationStore>();
        services.AddSingleton<ScriptDatabaseConnectionFactory>();
        services.AddSingleton<ScriptDatabaseCommandBuilder>();
        services.AddSingleton<ScriptDatabaseWriteExecutor>();
        services.AddSingleton<ScriptDatabaseWriteDispatcher>();
        services.AddSingleton<IScriptDatabaseQueue>(provider => provider.GetRequiredService<ScriptDatabaseWriteDispatcher>());
        services.AddHostedService(provider => provider.GetRequiredService<ScriptDatabaseWriteDispatcher>());
        services.AddSingleton<GatewayScriptCompiler>();
        services.AddSingleton<GatewayScriptRuntimeService>();
        services.AddSingleton<IScriptRuntimeService>(provider => provider.GetRequiredService<GatewayScriptRuntimeService>());
        services.AddHostedService(provider => provider.GetRequiredService<GatewayScriptRuntimeService>());
        services.AddSingleton<GatewayScriptManager>();
        return services;
    }
}

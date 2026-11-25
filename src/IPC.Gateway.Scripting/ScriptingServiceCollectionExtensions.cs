using IPC.Gateway.Scripting.Abstractions;
using IPC.Gateway.Scripting.Application;
using IPC.Gateway.Scripting.Database;
using IPC.Gateway.Scripting.Persistence;
using IPC.Gateway.Scripting.Runtime;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Core.Infrastructure.Persistence;
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
        GatewayScriptingOptions options,
        bool registerHostedServices = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        GatewayScriptingOptions normalized = (options ?? new GatewayScriptingOptions()).Normalize();
        services.AddSingleton(normalized);
        services.AddSingleton<JsonScriptConfigurationStore>();
        services.AddSingleton<IScriptConfigurationStore>(provider =>
            provider.GetService<GatewaySharedArtifactRepository>() is { } repository
                ? new SharedScriptConfigurationStore(repository,
                    provider.GetRequiredService<JsonScriptConfigurationStore>(),
                    provider.GetRequiredService<IScriptSecretProtector>(),
                    provider.GetService<IGatewayDatabaseOwner>())
                : provider.GetRequiredService<JsonScriptConfigurationStore>());
        services.AddSingleton<ScriptDatabaseConnectionFactory>();
        services.AddSingleton<ScriptDatabaseCommandBuilder>();
        services.AddSingleton<ScriptDatabaseWriteExecutor>();
        services.AddSingleton<ScriptDatabaseWriteDispatcher>();
        services.AddSingleton<IScriptDatabaseQueue>(provider => provider.GetRequiredService<ScriptDatabaseWriteDispatcher>());
        if (registerHostedServices)
            services.AddHostedService(provider => provider.GetRequiredService<ScriptDatabaseWriteDispatcher>());
        services.AddSingleton<GatewayScriptCompiler>();
        services.AddSingleton<ValueTransformScriptService>();
        services.AddSingleton<IValueTransformScriptRuntime>(provider =>
            provider.GetRequiredService<ValueTransformScriptService>());
        services.AddSingleton<GatewayScriptRuntimeService>();
        services.AddSingleton<IScriptRuntimeService>(provider => provider.GetRequiredService<GatewayScriptRuntimeService>());
        if (registerHostedServices)
            services.AddHostedService(provider => provider.GetRequiredService<GatewayScriptRuntimeService>());
        services.AddSingleton<GatewayScriptManager>();
        return services;
    }
}

using IPC.Gateway.Core.Gateway;
using IPC.Gateway.HighAvailability;
using IPC.Gateway.Inference;
using IPC.Gateway.Scripting.Persistence;
using IPC.Gateway.Scripting.Abstractions;
using IPC.Gateway.Scripting.Runtime;

namespace IPC.Gateway.WebHost.HighAvailability;

/// <summary>Refreshes shared definitions independently of PLC scans and lease renewal.</summary>
internal sealed class SharedArtifactSynchronization(IServiceProvider services, GatewayHaClient ha,
    GatewayStartupState startup, ILogger<SharedArtifactSynchronization> logger) : BackgroundService
{
    internal static async Task RefreshAsync(IServiceProvider services, GatewayCoreService core, CancellationToken token)
    {
        var store = services.GetRequiredService<IScriptConfigurationStore>();
        var document = store is SharedScriptConfigurationStore shared
            ? await shared.LoadLatestAsync(token) : await store.LoadAsync(token);
        services.GetRequiredService<ValueTransformScriptService>().Reload(document);
        var catalog = services.GetRequiredService<OnnxModelCatalogService>();
        catalog.SynchronizeShared();
        var project = core.CurrentProject;
        catalog.SynchronizeRuleModels(project.Rules.Select(r => r.ModelPath)
            .Concat(project.FlowRules.SelectMany(r => r.Nodes).Select(n => n.ModelPath)));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await startup.Listening.WaitAsync(stoppingToken);
        string? previousError = null;
        long scriptRevision = -1;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!startup.IsReady) continue;
            try
            {
                if (!ha.BusinessReady && services.GetRequiredService<IScriptConfigurationStore>() is SharedScriptConfigurationStore scripts)
                {
                    var document = await scripts.LoadLatestAsync(stoppingToken);
                    if (document.PersistenceRevision != scriptRevision)
                    {
                        services.GetRequiredService<ValueTransformScriptService>().Reload(document);
                        scriptRevision = document.PersistenceRevision;
                    }
                }
                var catalog = services.GetRequiredService<OnnxModelCatalogService>();
                catalog.SynchronizeShared();
                if (ha.AwaitingProvisioning) { previousError = null; continue; }
                var core = services.GetRequiredService<GatewayCoreService>();
                var project = core.CurrentProject;
                catalog.SynchronizeRuleModels(project.Rules.Select(r => r.ModelPath)
                    .Concat(project.FlowRules.SelectMany(r => r.Nodes).Select(n => n.ModelPath)));
                previousError = null;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                if (previousError != ex.Message) logger.LogWarning(ex, "Shared artifacts refresh failed. Retaining the last fully validated revision.");
                previousError = ex.Message;
            }
        }
    }
}

using IPC.Gateway.HighAvailability;

namespace IPC.Gateway.WebHost.HighAvailability;

internal sealed class HaRoleEvents(GatewayHaClient client, GatewayRuntimeEventHub events) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!client.IsCluster && client.Endpoints.Count == 0) return;
        var previous = (client.IsCluster, client.Epoch, client.BusinessReady);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var current = (client.IsCluster, client.Epoch, client.BusinessReady);
            if (previous == current) continue;
            previous = current;
            events.Publish("haChanged", new { canWrite = current.BusinessReady, epoch = current.Epoch.ToString(),
                sourceInstanceId = client.InstanceId });
        }
    }
}

using IPC.Gateway.HighAvailability;

namespace IPC.Gateway.WebHost.HighAvailability;

internal sealed class HaEnrollmentWorker(GatewayHaClient client, HaManagementService management, ILogger<HaEnrollmentWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (client.Endpoints.Count != 3) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        string? lastError = null;
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await management.ReconcileAsync(stoppingToken); lastError=null; }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                if (e.Message == lastError) continue;
                lastError=e.Message;
                log.LogWarning(e,"HA configuration will resume when the members are available.");
            }
        }
    }
}

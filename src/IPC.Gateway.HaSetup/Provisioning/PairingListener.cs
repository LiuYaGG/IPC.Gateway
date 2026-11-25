using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IPC.Gateway.HaSetup;

// Separate TLS listener keeps pairing credentials away from normal gateway HTTP routes.
internal sealed class PairingListener(WebApplication app, X509Certificate2 transport) : IAsyncDisposable
{
    public string CertificatePin { get; } = transport.GetCertHashString(HashAlgorithmName.SHA256);

    public static async Task<PairingListener> StartAsync(int port, GatewaySetupCoordinator owner, CancellationToken ct)
    {
        using var generated = SetupFiles.Certificate("IPC-Setup", ["localhost"]);
        // Schannel requires a provider-backed key when Kestrel serves TLS.
        var cert = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), (string?)null);
        WebApplication? app = null;
        try
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                Args = [], ApplicationName = typeof(PairingListener).Assembly.GetName().Name, ContentRootPath = AppContext.BaseDirectory
            });
            // The main gateway owns configuration, service lifetime and its HTTP listeners.
            builder.Configuration.Sources.Clear();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton<IHostLifetime, PairingLifetime>();
            builder.WebHost.ConfigureKestrel(k =>
            {
                k.Limits.MaxRequestBodySize = 48 * 1024;
                k.ListenAnyIP(port, ep => ep.UseHttps(cert));
            });
            app = builder.Build();
            app.MapPost("/pair/identity", (HttpContext ctx, RoleChoice input) => owner.RemoteIdentity(ctx, input));
            app.MapPost("/pair/apply", (HttpContext ctx, ApplyPlan input) => owner.ApplyRemote(ctx, input));
            await app.StartAsync(ct);
            return new(app, cert);
        }
        catch
        {
            if (app != null) await app.DisposeAsync();
            cert.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await app.StopAsync(); }
        finally { await app.DisposeAsync(); transport.Dispose(); }
    }

    private sealed class PairingLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

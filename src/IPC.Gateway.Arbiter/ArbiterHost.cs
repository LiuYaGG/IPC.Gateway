using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Net.Cluster.Consensus.Raft.Http;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Https;

namespace IPC.Gateway.Arbiter;

public static class ArbiterHost
{
    public static async Task RunAsync(string[] args, string? configFile, string? expectedNode, bool embedded, CancellationToken cancellationToken)
    {
        if(!string.IsNullOrEmpty(configFile))TransportMigrationFiles.CompletePendingFiles(Path.GetFullPath(configFile),honorEnvironment:!embedded);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ApplicationName = typeof(ArbiterHost).Assembly.GetName().Name,
            ContentRootPath = embedded ? Path.GetDirectoryName(Path.GetFullPath(configFile!)) : null
        });
        if (embedded)
        {
            // The member has its own Kestrel listeners, TLS identity and lifetime.
            // Never inherit the business site's URLs, Kestrel configuration or OS service registration.
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddJsonFile(Path.GetFullPath(configFile!), optional: false, reloadOnChange: false);
            builder.Services.AddSingleton<IHostLifetime, EmbeddedLifetime>();
        }
        else
        {
            if (!string.IsNullOrEmpty(configFile))
                builder.Configuration.AddJsonFile(Path.GetFullPath(configFile), optional: false, reloadOnChange: false).AddEnvironmentVariables();
            builder.Host.UseWindowsService(service => service.ServiceName = Environment.GetEnvironmentVariable("IPC_ARBITER_SERVICE_NAME") ?? "IPCGatewayArbiter").UseSystemd();
        }
        var settings = builder.Configuration.GetRequiredSection("Arbiter").Get<ArbiterOptions>()
            ?? throw new InvalidDataException("Missing Arbiter section.");
        if (expectedNode is { Length: > 0 } && settings.NodeId != expectedNode)
            throw new InvalidDataException("The mounted configuration does not belong to the selected member volume.");
        string configDirectory = string.IsNullOrEmpty(configFile) ? AppContext.BaseDirectory : Path.GetDirectoryName(Path.GetFullPath(configFile))!;
        string? password = string.IsNullOrEmpty(settings.CertificatePasswordFile) ? settings.CertificatePassword :
            File.ReadAllText(Path.GetFullPath(settings.CertificatePasswordFile, configDirectory)).TrimEnd('\r', '\n');
        using var certificate = X509CertificateLoader.LoadPkcs12FromFile(Path.GetFullPath(settings.CertificatePath, configDirectory), password);
        using var memberLock = settings.OpenStateDirectory(certificate);
        var self = settings.Self;
        string membershipFingerprint = settings.MembershipFingerprint;
        builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["coldStart"] = "false", ["partitioning"] = "false", ["publicEndPoint"] = self.PeerUrl.ToString(),
            ["lowerElectionTimeout"] = "900", ["upperElectionTimeout"] = "1800", ["requestTimeout"] = "00:00:02"
        });
        builder.WebHost.ConfigureKestrel(k =>
        {
            // Raft snapshots can be large. Bound browser-facing machine API bodies independently below.
            k.Limits.MaxRequestBodySize = null;
            foreach (var port in new[] { self.PeerUrl.Port, self.ClientUrl.Port })
            {
                bool peer = port == self.PeerUrl.Port;
                k.ListenAnyIP(port, endpoint => endpoint.UseHttps(https =>
                {
                    https.ServerCertificate = certificate;
                    https.ClientCertificateMode = peer ? ClientCertificateMode.RequireCertificate : ClientCertificateMode.AllowCertificate;
                    https.ClientCertificateValidation = (c, _, _) => ArbiterOptions.Current(c) &&
                        settings.Members.Any(m => m.Matches(ArbiterOptions.Fingerprint(c), peer));
                }));
            }
        });
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton(certificate);
        builder.Services.UseInMemoryConfigurationStorage(members =>
        {
            foreach (var m in settings.Members) members.Add(new UriEndPoint(m.PeerUrl));
        });
        builder.Services.UseStateMachine<OwnershipState>(new() { Location = Path.Combine(settings.StateDirectory, "wal") });
        builder.Services.AddSingleton<IHttpMessageHandlerFactory, PeerHandlerFactory>();
        builder.Services.AddSingleton<MembershipPreflight>();
        builder.JoinCluster();
        await using var app = builder.Build();
        await app.RestoreStateAsync<OwnershipState>();
        var migration = !string.IsNullOrEmpty(configFile) ? new TransportMigration(settings,certificate,Path.GetFullPath(configFile)) : null;
        migration?.RestorePolicy(app.Services.GetRequiredService<OwnershipState>());
        using var serial = new SemaphoreSlim(1, 1);
        app.Use(async (ctx, next) =>
        {
            bool peer = ctx.Connection.LocalPort == self.PeerUrl.Port;
            bool peerPath = ctx.Request.Path.StartsWithSegments("/cluster-consensus/raft") || ctx.Request.Path == "/v1/peer-health";
            if (peer != peerPath) { ctx.Response.StatusCode = 404; return; }
            if (peer && ctx.Request.Headers["X-IPC-Raft-Cluster"] != MembershipPreflight.PeerHeader(membershipFingerprint)) { ctx.Response.StatusCode = 403; return; }
            if (peer || ctx.Request.Path == "/health/live") { await next(); return; }
            var limits = ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if (limits is { IsReadOnly: false }) limits.MaxRequestBodySize = 64 * 1024;
            var cert = ctx.Connection.ClientCertificate;
            var caller = cert != null ? settings.Members.FirstOrDefault(m => m.Matches(ArbiterOptions.Fingerprint(cert), peer: false)) : null;
            if (caller == null) { ctx.Response.StatusCode = 403; return; }
            ctx.Items["caller"] = caller.NodeId;
            await next();
        });
        app.UseConsensusProtocolHandler();
        TransportMigration.Map(app,migration,serial);
        app.MapGet("/health/live", () => Results.Ok(new { live = true }));
        app.MapGet("/v1/peer-health", () => new PeerIdentity(settings.ClusterId, settings.NodeId, membershipFingerprint,
            MembershipPreflight.WireVersion, MembershipPreflight.SoftwareVersion,1));
        app.MapGet("/v1/preflight", async (MembershipPreflight probe, CancellationToken token) =>
            Results.Ok(new { success = true, data = await probe.CheckAsync(token) }));
        app.MapGet("/v1/member", (OwnershipState state) =>
        {
            var progress = state.LocalProgress();
            return Results.Ok(new { success = true, data = new {
                settings.NodeId, settings.ClusterId, self.ClientUrl, self.PeerUrl, progress.ConfigVersion, progress.CommitIndex,
                wireVersion = MembershipPreflight.WireVersion, softwareVersion = MembershipPreflight.SoftwareVersion,
                certificateSha256 = ArbiterOptions.Fingerprint(certificate),
                transportSupported=true,transportPlanId = state.ActiveTransportPlanId, transportPending=state.TransportPending, transportMembers = settings.Members,
                members = settings.Members.Select(m => new {m.NodeId, m.ClientUrl, m.PeerUrl, m.CertificateSha256})
            }});
        });
        app.MapGet("/v1/status", async (IRaftCluster raft, OwnershipState state, CancellationToken cancel) =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                await raft.ApplyReadBarrierAsync(timeout.Token);
                return Results.Ok(new { success = true, data = Status(raft, state, true) });
            }
            catch (Exception e) when (Unavailable(e))
            {
                return Results.Ok(new { success = true, data = Status(raft, state, false) });
            }
        });
        app.MapGet("/v1/{list}", async (string list, int? page, int? pageSize, string? nodeId, string? result, string? operation, DateTimeOffset? from,
            DateTimeOffset? to, OwnershipState state, IRaftCluster raft, CancellationToken cancel) =>
        {
            if (list is not ("write-attempts" or "switchovers")) return Results.NotFound();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                await raft.ApplyReadBarrierAsync(timeout.Token);
                return Results.Ok(new { success = true, data = state.Page(list == "write-attempts", Math.Max(1, page ?? 1),
                    Math.Clamp(pageSize ?? 20, 1, 500), nodeId, result, operation, from, to) });
            }
            catch (Exception e) when (Unavailable(e)) { return Error("HA_NO_QUORUM", 503); }
        });
        app.MapGet("/v1/{collection}/{id}", async (string collection, string id, OwnershipState state, IRaftCluster raft, CancellationToken cancel) =>
        {
            string? type = collection switch { "config" => "config", "maintenance" => "maintenance", "mode" => "mode", "switchovers" => "transfer", "write-attempts" => "permit", _ => null };
            if (type == null) return Results.NotFound();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                await raft.ApplyReadBarrierAsync(timeout.Token);
                return state.Operation(type, id) is { } result
                    ? Results.Ok(new { success = true, data = result }) : Error("HA_OPERATION_NOT_FOUND", 404);
            }
            catch (Exception e) when (Unavailable(e)) { return Error("HA_NO_QUORUM", 503); }
        });
        app.MapPost("/v1/{op}", async (string op, Request input, HttpContext context, IRaftCluster raft, OwnershipState state,MembershipPreflight probe) =>
        {
            if (op is not ("config" or "enroll" or "cancel-enroll" or "mode" or "mode-ack" or "maintenance" or "transfer" or "acquire" or "release" or "renew" or "permit" or "readiness" or "transport" or "transport-finish" or "transport-lock" or "transport-cancel")) return Results.NotFound();
            if(op=="mode" && input.Enabled==true && (state.TransportPending||migration?.BlocksEnable==true))return Error("HA_TRANSPORT_PENDING",409);
            if(op=="config" && (state.TransportPending||migration?.BlocksEnable==true))return Error("HA_TRANSPORT_PENDING",409);
            if(op=="transport-finish" && migration?.ReadyToFinish(input.Target)!=true)return Error("HA_TRANSPORT_PENDING",409);
            if (input.ClusterId != settings.ClusterId || input.NodeId != (string?)context.Items["caller"]) return Error("HA_WRONG_IDENTITY", 403);
            if (input.InstanceId is not { Length: > 0 and <= 128 } || input.RequestId is not { Length: > 0 and <= 160 } ||
                input.ConfigVersion is null or { Length: > 128 } || input.CommandId is null or { Length: > 160 } || input.AttemptId is null or { Length: > 160 } ||
                input.BusinessEpoch is { Length: > 20 } || input.Epoch is null or { Length: > 20 } || input.AttemptSequence is null or { Length: > 20 } ||
                input.SourceType is null or { Length: > 64 } || input.SourceType.Any(char.IsControl) ||
                input.TargetDigest is null || input.TargetDigest.Length != 0 && (input.TargetDigest.Length != 64 || !input.TargetDigest.All(Uri.IsHexDigit)) ||
                input.Digest is null || input.Digest.Length != 0 && (input.Digest.Length != 64 || !input.Digest.All(Uri.IsHexDigit)) ||
                input.ModeGeneration is { Length: > 20 } || input.Actor is { Length: > 160 } || input.Reason is { Length: > 2048 })
                return Error("HA_INVALID_REQUEST", 400);
            if (raft.LeadershipToken.IsCancellationRequested) return Error("HA_NOT_LEADER", 503);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, raft.LeadershipToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(op.StartsWith("transport",StringComparison.Ordinal)?15:4));
            try
            {
                await serial.WaitAsync(deadline.Token);
                try
                {
                    await raft.ApplyReadBarrierAsync(deadline.Token);
                    if(op.StartsWith("transport",StringComparison.Ordinal) && !await probe.SupportsTransportAsync(deadline.Token))return Error("HA_TRANSPORT_UPGRADE_REQUIRED",409);
                    if(op=="mode" && input.Enabled==true && (state.TransportPending||migration?.BlocksEnable==true))return Error("HA_TRANSPORT_PENDING",409);
                    if(op=="config" && (state.TransportPending||migration?.BlocksEnable==true))return Error("HA_TRANSPORT_PENDING",409);
                    if(op=="transport") {
                        if(migration==null)return Error("HA_TRANSPORT_EMBEDDED_REQUIRED",409);
                        migration.ValidateConsensus(input);
                    }
                    bool allReachable = op is "maintenance" or "config" && raft.Members.Count(m =>
                        !m.IsRemote || m.Status == DotNext.Net.Cluster.ClusterMemberStatus.Available) == 3;
                    var proposal = state.Prepare(op, input, raft.Term, allReachable);
                    await raft.ReplicateAsync(JsonSerializer.SerializeToUtf8Bytes(proposal, OwnershipState.Json).AsMemory(), token: deadline.Token);
                    var outcome = state.Result(proposal.Id);
                    if (outcome != "") return Error(outcome, outcome == "HA_COMMIT_UNKNOWN" ? 503 : 409);
                    object data = op switch {
                        "enroll" or "cancel-enroll" or "mode-ack" => Status(raft, state, true),
                        "config" or "mode" or "transfer" or "maintenance" or "transport" or "transport-finish" or "transport-lock" or "transport-cancel" => state.Operation(op, input.RequestId)!,
                        _ => state.Lease()
                    };
                    return Results.Ok(new { success = true, data });
                }
                finally { serial.Release(); }
            }
            catch(Exception e) when(op=="transport" && e is (InvalidDataException or InvalidOperationException or ArgumentException or System.Security.Cryptography.CryptographicException))
            {return Error("HA_TRANSPORT_INVALID_PROOF",409);}
            catch (Exception e) when (Unavailable(e)) { return Error("HA_NO_QUORUM", 503); }
        });
        await app.RunAsync(cancellationToken);

        static bool Unavailable(Exception e) => e is OperationCanceledException or InvalidOperationException or TimeoutException or IOException or HttpRequestException or AggregateException;
        static IResult Error(string code, int status) => Results.Json(new { success = false, errorCode = code, errorMessage = code }, statusCode: status);
        object Status(IRaftCluster raft, OwnershipState state, bool quorum)
        {
            var leaderEndpoint = (raft.Leader?.EndPoint as UriEndPoint)?.Uri;
            var leader = settings.Members.FirstOrDefault(m => m.PeerUrl == leaderEndpoint)?.NodeId;
            var reachable = raft.Members.Where(m => !m.IsRemote || m.Status == DotNext.Net.Cluster.ClusterMemberStatus.Available)
                .Select(m => (m.EndPoint as UriEndPoint)?.Uri).OfType<Uri>().ToHashSet();
            return state.Status(quorum, leader, raft.Term, reachable);
        }
    }

    private sealed class EmbeddedLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

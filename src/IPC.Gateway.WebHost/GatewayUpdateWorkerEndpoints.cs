using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using IPC.Gateway.HighAvailability;
using IPC.Gateway.WebHost.HighAvailability;

namespace IPC.Gateway.WebHost;

/// <summary>Local worker handoff, authenticated by a short-lived per-action secret, never by a browser cookie.</summary>
internal static class GatewayUpdateWorkerEndpoints
{
    private static readonly SemaphoreSlim AdmissionGate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    internal sealed record Request(string ActionId, string Token);
    internal sealed record StopProof(string ActionId, string Token, int ProcessId, DateTimeOffset ExpiresAtUtc);
    internal sealed record CompletionProof(string ActionId, string Token, string ExpectedVersion, string ExpectedEntrySha256,
        int OldProcessId, DateTimeOffset ExpiresAtUtc);

    public static void MapGatewayUpdateWorkerEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/maintenance/updates/stop-admission", async (HttpContext context,
            GatewayUpdateMaintenanceOptions options, GatewayUpdatePackageService updates, GatewayHaClient client,
            HaDraftStore drafts, HaTransportManagementService transport, EmbeddedGatewayMember member, GatewayStartupState startup) =>
        {
            var request = await ReadRequest(context);
            if (request == null) return Denied();
            await AdmissionGate.WaitAsync(context.RequestAborted);
            try
            {
                string path = Path.Combine(updates.WorkerUpdateDirectory, "stop-authorization.json");
                var proof = ReadSmall<StopProof>(path);
                if (proof == null || !ValidSecret(request, proof.ActionId, proof.Token, proof.ExpiresAtUtc) ||
                    proof.ProcessId != Environment.ProcessId || !startup.IsReady) return Denied();
                // A handoff can never be replayed, including after a failed safety check.
                File.Delete(path);
                var snapshot = updates.ValidateWorkerStop(request.ActionId);
                var state = await HaBusinessMiddleware.ValidateUpdateStateAsync(client, drafts, transport, member, context.RequestAborted);
                HaBusinessMiddleware.ValidateApplyState(client, state);
                var completion = new CompletionProof(request.ActionId, request.Token, snapshot.Action.Version,
                    snapshot.EntrySha256, Environment.ProcessId,
                    DateTimeOffset.UtcNow.AddSeconds(options.ServiceTransitionTimeoutSeconds + 120));
                WritePrivate(Path.Combine(updates.WorkerUpdateDirectory, "worker-completion.json"), completion);
                return Results.Json(new { ready = true });
            }
            catch (HaManagementError ex) { return HaManagementEndpoints.Error(ex.Status, ex.Code, ex.Message); }
            catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
            { return Results.Json(new { ready = false, errorMessage = "停服前集群检查超时，未允许停止服务。" }, statusCode: 503); }
            catch (Exception ex) when (ex is IOException or JsonException or HaRequestException or InvalidOperationException or HttpRequestException)
            { return Results.Json(new { ready = false, errorMessage = "停服前安全检查未通过，请核对集群状态和本次更新动作。" }, statusCode: 409); }
            finally { AdmissionGate.Release(); }
        });
        routes.MapPost("/api/maintenance/updates/worker-ready", async (HttpContext context,
            GatewayUpdateMaintenanceOptions options, GatewayUpdatePackageService updates, GatewayStartupState startup) =>
        {
            var request = await ReadRequest(context);
            if (request == null) return Denied();
            try
            {
                var proof = ReadSmall<CompletionProof>(Path.Combine(updates.WorkerUpdateDirectory, "worker-completion.json"));
                if (proof == null || !ValidSecret(request, proof.ActionId, proof.Token, proof.ExpiresAtUtc) ||
                    proof.OldProcessId == Environment.ProcessId) return Denied();
                var action = updates.GetProgress().PendingAction;
                if (action?.ActionId != request.ActionId || action.Status is not ("Scheduled" or "Applying" or "Applied") ||
                    !startup.IsReady) return Results.Json(new { ready = false }, statusCode: 503);
                string entry = Path.Combine(AppContext.BaseDirectory, "IPC.Gateway.WebHost.dll");
                using var file = File.OpenRead(entry);
                if (!string.Equals(Convert.ToHexString(SHA256.HashData(file)), proof.ExpectedEntrySha256, StringComparison.OrdinalIgnoreCase))
                    return Results.Json(new { ready = false, errorMessage = "运行程序与本次更新目标不一致。" }, statusCode: 409);
                // Readiness is idempotent: losing an HTTP response must not consume the only completion proof.
                return Results.Json(new { ready = true, currentVersion = GatewayUpdateVersion.Current });
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
            { return Results.Json(new { ready = false }, statusCode: 503); }
        });
    }

    private static IResult Denied() => Results.Json(new { ready = false }, statusCode: 403);
    private static bool ValidSecret(Request request, string actionId, string token, DateTimeOffset expires) =>
        expires > DateTimeOffset.UtcNow && request.ActionId == actionId && request.Token is { Length: 64 } &&
        token is { Length: 64 } && CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(request.Token), System.Text.Encoding.ASCII.GetBytes(token));
    private static async Task<Request?> ReadRequest(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var address = context.Connection.RemoteIpAddress;
        if (address?.IsIPv4MappedToIPv6 == true) address = address.MapToIPv4();
        if (address == null || !IPAddress.IsLoopback(address) || context.Request.ContentLength > 4096) return null;
        byte[] buffer = new byte[4097]; int count = 0;
        while (count < buffer.Length)
        {
            int read = await context.Request.Body.ReadAsync(buffer.AsMemory(count), context.RequestAborted);
            if (read == 0) break;
            count += read;
        }
        if (count > 4096) return null;
        try { return JsonSerializer.Deserialize<Request>(buffer.AsSpan(0, count), Json); }
        catch (JsonException) { return null; }
    }
    private static T? ReadSmall<T>(string path)
    {
        if (!File.Exists(path)) return default;
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        byte[] bytes = new byte[8193]; int count = 0, read;
        while (count < bytes.Length && (read = file.Read(bytes.AsSpan(count))) != 0) count += read;
        if (count > 8192) throw new InvalidDataException("Invalid worker proof.");
        return JsonSerializer.Deserialize<T>(bytes.AsSpan(0, count), Json);
    }
    private static void WritePrivate<T>(string path, T value)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var file = new FileStream(temporary, options)) { JsonSerializer.Serialize(file, value, Json); file.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); }
    }
}

public sealed partial class GatewayUpdatePackageService
{
    internal string WorkerUpdateDirectory => ResolveUpdateDirectory();
    internal (GatewayPendingUpdateAction Action, string EntrySha256) ValidateWorkerStop(string actionId)
    {
        lock (_actionLock)
        {
            var action = ReadJson<GatewayPendingUpdateAction>(GetPendingActionPath()) ?? throw new InvalidOperationException("更新动作不存在。");
            if (action.ActionId != actionId || action.Status is not ("Scheduled" or "Applying") ||
                Path.GetFullPath(action.TargetDirectory) != Path.GetFullPath(ResolveInstallDirectory()))
                throw new InvalidOperationException("更新动作或安装目录已变化。");
            var compatibility = action.ActionType == "Upgrade" ? FindPackage(action.PackageId).Manifest.Compatibility :
                action.ActionType == "Rollback" ? FindRollback(action.RollbackId).Compatibility : null;
            ValidateExtractedCompatibility(action.SourceDirectory, compatibility);
            using var file = File.OpenRead(Path.Combine(action.SourceDirectory, "IPC.Gateway.WebHost.dll"));
            return (action, Convert.ToHexString(SHA256.HashData(file)));
        }
    }
}

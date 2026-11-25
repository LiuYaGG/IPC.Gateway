using System.Security.Claims;
using System.Text.Json;
using IPC.Gateway.Core.Gateway;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.Infrastructure;
using IPC.Runtime.Engine;
using Microsoft.AspNetCore.Mvc;

namespace IPC.Gateway.WebHost;

internal static class ProtocolDiagnosticEndpoints
{
    private static readonly HashSet<string> ActiveTargets = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxConcurrentTests = 4;

    public static IEndpointRouteBuilder MapProtocolDiagnosticEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/diagnostics/protocols", (ClaimsPrincipal user, GatewayProtocolCatalogService catalog) =>
            GatewayAuthEndpoints.CanRunProtocolDiagnostics(user)
                ? Results.Ok(new { success = true, data = catalog.GetProtocols() })
                : Error(403, "Protocol diagnostics permission is required."));
        app.MapPost("/api/diagnostics/protocol/run", RunAsync)
            .WithMetadata(new RequestSizeLimitAttribute(128 * 1024));
        return app;
    }

    internal static async Task<IResult> RunAsync(HttpContext context, ProtocolDiagnosticRequest request,
        GatewayCoreService gateway, IGatewayAuditLogStore auditLog)
    {
        bool write = request.Operation == "write";
        if (!GatewayAuthEndpoints.CanRunProtocolDiagnostics(context.User) ||
            (write && !GatewayAuthEndpoints.CanWriteProtocolDiagnostics(context.User)))
            return Error(403, "You do not have permission for this diagnostic operation.");

        string? invalid = Validate(request);
        if (invalid != null) return Error(400, invalid);
        var connection = request.Connection!;
        var item = request.Items.FirstOrDefault();
        string? CheckBusAvailability() => gateway.CheckDiagnosticBusAvailability(connection);
        var audit = write ? GatewayConfigurationEndpoints.CreateConfigurationAuditEvent(context, "Success", "") : null;
        if (audit != null)
        {
            string host = Uri.TryCreate(connection.Host, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
                ? uri.Host : connection.Host;
            audit.Target = $"Protocol diagnostic write · {connection.Protocol} · {host}:{connection.Port} · {item!.Address} ({item.DataType})";
        }

        var policyError = CheckBusAvailability();
        if (policyError != null)
        {
            await AuditAsync(audit, auditLog, "Denied", policyError);
            return Error(409, policyError);
        }

        bool bus = GatewayCoreService.IsExclusiveDiagnosticBus(connection.Protocol);
        string key = bus ? "bus|" + connection.Host.Trim() : $"{connection.Protocol}|{connection.Host.Trim()}|{connection.Port}";
        lock (ActiveTargets)
        {
            if (ActiveTargets.Count >= MaxConcurrentTests || !ActiveTargets.Add(key))
                return Error(429, "A diagnostic is already running for this target or all diagnostic slots are busy.");
        }

        var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        // Some SDKs provide only blocking operations. Keep their slot and client until the
        // worker exits, even when the HTTP caller has disconnected or its deadline expired.
        var authority = gateway.WriteAuthority;
        long admissionEpoch = authority?.Epoch ?? 0;
        var worker = Task.Run(async () =>
        {
            try { return await ProtocolDiagnosticRunner.ExecuteAsync(request, CheckBusAvailability, token, authority, admissionEpoch); }
            finally
            {
                lock (ActiveTargets) ActiveTargets.Remove(key);
                deadline.Dispose();
            }
        });
        ProtocolDiagnosticReport report;
        try { report = await worker.WaitAsync(token); }
        catch (OperationCanceledException)
        {
            string error = write
                ? "Diagnostic deadline or cancellation. If the write was sent, its outcome is unknown. Read the address before retrying."
                : "Diagnostic deadline or cancellation.";
            await AuditAsync(audit, auditLog, "Unknown", error);
            return Error(504, error);
        }
        await AuditAsync(audit, auditLog, report.Outcome, report.Error);
        if (report.WriteExecution?.Outcome == "Unknown")
            return Results.Json(new { success = false, errorCode = "HA_DEVICE_RESULT_UNKNOWN", errorMessage = report.Error, data = report }, statusCode: 502);
        if (authority is { IsCluster: true } && report.Error.StartsWith("HA_", StringComparison.Ordinal))
        {
            string code = report.Error.Split(' ')[0];
            int status = code is "HA_NO_QUORUM" or "HA_MEMBER_UNAVAILABLE" or "HA_WRITE_JOURNAL_UNAVAILABLE" ? 503 : 409;
            return Results.Json(new { success = false, errorCode = code, errorMessage = report.Error, data = report }, statusCode: status);
        }
        if (request.SampleOnly)
            return Results.Ok(new { success = true, data = new { report.Error, items = report.Items.Select(i => new { i.Index, i.Address, i.Status, i.Value, i.Error }) } });
        return Results.Ok(new { success = true, data = report });
    }

    private static async Task AuditAsync(GatewayConfigurationAuditEvent? audit, IGatewayAuditLogStore store, string outcome, string error)
    {
        if (audit == null) return;
        audit.Outcome = outcome;
        audit.ErrorMessage = error;
        await GatewayAuditLog.WriteConfigurationChangeAsync(audit, store);
    }

    private static string? Validate(ProtocolDiagnosticRequest request)
    {
        var c = request.Connection;
        if (c == null || !Enum.IsDefined(c.Protocol) || !Enum.IsDefined(c.Transport) ||
            !Enum.IsDefined(c.WordOrder) || !Enum.IsDefined(c.SerialParity) || !Enum.IsDefined(c.SerialStopBits))
            return "A valid protocol and connection are required.";
        if (!GatewayProtocolCatalogService.IsVisibleProtocol(c.Protocol.ToString()) ||
            !PlcDriverPluginRegistry.GetRegisteredDrivers().Any(driver =>
                driver.Protocol.Equals(c.Protocol.ToString(), StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(c.DriverId) || driver.DriverId.Equals(c.DriverId, StringComparison.OrdinalIgnoreCase))))
            return "The selected protocol driver is unavailable.";
        c.Host = c.Host?.Trim() ?? "";
        if (c.Host.Length > 512 || (c.Host.Length == 0 && c.Protocol is not (PlcProtocol.VirtualPlc or PlcProtocol.Plugin)))
            return "A valid target host or port name is required.";
        if (c.Host.Contains('@') || c.Host.Any(char.IsControl))
            return "Specify credentials in the authentication fields.";
        if (!GatewayCoreService.IsExclusiveDiagnosticBus(c.Protocol) && c.Protocol is not (PlcProtocol.VirtualPlc or PlcProtocol.OpcDa or PlcProtocol.Plugin) &&
            c.Port is < 1 or > 65535) return "Invalid network port.";
        if (c.TimeoutMilliseconds is < 100 or > 10000) return "Timeout must be between 100 and 10000 ms.";
        if ((c.DriverOptionsJson?.Length ?? 0) > 16384) return "Driver options are too large.";
        if (!string.IsNullOrWhiteSpace(c.DriverOptionsJson))
        {
            try
            {
                using var options = JsonDocument.Parse(c.DriverOptionsJson);
                if (options.RootElement.ValueKind != JsonValueKind.Object) return "Driver options must be a JSON object.";
            }
            catch (JsonException) { return "Driver options must be a JSON object."; }
        }
        if (request.Operation is not ("connect" or "read" or "batch" or "write")) return "Invalid diagnostic operation.";
        bool probe = request.Operation == "connect" && ProtocolDiagnosticRunner.NeedsProbe(c);
        int n = request.Items?.Count ?? -1;
        if (request.Operation == "connect" && !probe ? n is < 0 or > 1 :
            request.Operation == "batch" ? n is < 1 or > 32 : n != 1)
            return probe ? "A probe address is required to test this connection." : "Invalid number of diagnostic addresses.";
        foreach (var item in request.Items!)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Address) || item.Address.Length > 256 ||
                item.Address.Any(char.IsControl) || !Enum.IsDefined(item.DataType)) return "Invalid address or data type.";
            item.Address = item.Address.Trim();
            if (item.ElementCount is < 1 or > 4096 || item.ElementOffset is < 0 or > 65535) return "Invalid element count or offset.";
        }
        if (request.Operation == "write")
        {
            if (!request.ConfirmWrite) return "Explicit write confirmation is required.";
            var item = request.Items![0];
            if (item.ValueText == null || item.ValueText.Length > 32768) return "Write value is missing or too large.";
            if (!RuntimeEngine.TryValidateWriteValue(item.DataType, item.ValueText, item.ElementCount, out var error)) return error;
        }
        return null;
    }

    private static IResult Error(int status, string error) => Results.Json(new { success = false, errorMessage = error }, statusCode: status);
}

internal sealed class ProtocolDiagnosticRequest
{
    internal bool SampleOnly { get; init; }
    public PlcConnectionOptions? Connection { get; set; }
    public string Operation { get; set; } = "";
    public List<ProtocolDiagnosticItem> Items { get; set; } = [];
    public bool ConfirmWrite { get; set; }
    public bool ReadBack { get; set; }
}

internal sealed class ProtocolDiagnosticItem
{
    public string Address { get; set; } = "";
    public PlcDataType DataType { get; set; }
    public int ElementCount { get; set; } = 1;
    public int ElementOffset { get; set; }
    public string? ValueText { get; set; }
}

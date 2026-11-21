using System.Security.Claims;
using IPC.Gateway.Core.Gateway;
using IPC.Runtime.Engine;

namespace IPC.Gateway.WebHost;

public static class GatewayAcquisitionDiagnosticsEndpoints
{
    public static IEndpointRouteBuilder MapAcquisitionDiagnosticsEndpoints(this IEndpointRouteBuilder app)
    {
        var routes = app.MapGroup("/api/diagnostics/acquisition");
        routes.MapGet("/{deviceId}", (string deviceId, int? periodMs, string? slowSort, ClaimsPrincipal user, GatewayCoreService core) =>
        {
            if (!GatewayAuthEndpoints.CanViewDevices(user)) return Forbidden();
            if (periodMs is <= 0) return Results.BadRequest(new { success = false, errorMessage = "Invalid sampling period." });
            if (slowSort is not (null or "gap" or "misses"))
                return Results.BadRequest(new { success = false, errorMessage = "Invalid slow-tag sort order." });
            var report = (core.Runtime as RuntimeEngine)?.GetAcquisitionReport(deviceId, periodMs, slowSort);
            return report == null ? Results.NotFound(new { success = false, errorMessage = "Device not found." })
                : Results.Ok(new { success = true, data = report });
        });
        routes.MapPost("/{deviceId}/trace/{tagId}", (string deviceId, string tagId, ClaimsPrincipal user, GatewayCoreService core) =>
        {
            if (!GatewayAuthEndpoints.CanViewDevices(user)) return Forbidden();
            bool busy = false;
            var trace = (core.Runtime as RuntimeEngine)?.StartAcquisitionTrace(deviceId, tagId, out busy);
            if (busy) return Results.Conflict(new { success = false, errorMessage = "Another tag is being traced on this device." });
            return trace == null ? Results.NotFound(new { success = false, errorMessage = "Device or readable tag not found." })
                : Results.Ok(new { success = true, data = trace });
        });
        return app;
    }

    private static IResult Forbidden() => Results.Json(new { success = false, errorMessage = "Permission denied." }, statusCode: 403);
}

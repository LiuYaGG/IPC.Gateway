using IPC.Gateway.Core.Application.Gateway;
using IPC.Gateway.HighAvailability;

namespace IPC.Gateway.WebHost;

public static class GatewayOpcUaHealthEndpoints
{
    public static IEndpointRouteBuilder MapGatewayOpcUaHealthEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/health/opcua-active", (HttpContext context, GatewayHaClient ha, GatewayStartupState startup) =>
        {
            // Resolve the runtime only after startup/ownership gates. A proxy probe must
            // never initialize the gateway or database while a node is being provisioned.
            bool active = !ha.AwaitingProvisioning && ha.BusinessReady && startup.IsReady;
            if (active)
            {
                var opc = context.RequestServices.GetRequiredService<IGatewayApplicationService>().GetOpcUaStatus();
                active = opc.Enabled && opc.IsRunning && ha.BusinessReady && startup.IsReady;
            }
            context.Response.Headers.CacheControl = "no-store";
            return Results.Json(new { active }, statusCode: active ? 200 : 503);
        });
        return routes;
    }
}

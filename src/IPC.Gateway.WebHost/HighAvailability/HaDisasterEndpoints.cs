using System.Security.Claims;
using IPC.Gateway.Core.Domain.Users;

namespace IPC.Gateway.WebHost.HighAvailability;

internal static partial class HaManagementEndpoints
{
    private static void MapDisasterManagement(RouteGroupBuilder group)
    {
        group.MapGet("/disaster", async (HaDisasterTakeoverService service, CancellationToken ct) =>
            Ok(await service.StatusAsync(ct))).Needs(GatewayPermissions.ViewHa);
        group.MapPost("/disaster", async (HaDisasterRequest request, HaDisasterTakeoverService service,
            ClaimsPrincipal actor, CancellationToken ct) =>
            Ok(await service.ActivateAsync(request, actor.Identity?.Name ?? "", ct))).Needs(GatewayPermissions.ManageHaDisaster);
        group.MapPost("/disaster/exit", async (HaDisasterExitRequest request, HaDisasterTakeoverService service,
            ClaimsPrincipal actor, CancellationToken ct) =>
            Ok(await service.ExitAsync(request, actor.Identity?.Name ?? "", ct))).Needs(GatewayPermissions.ManageHaDisaster);
    }
}

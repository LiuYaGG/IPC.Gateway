using System.Security.Claims;
using System.Text.Json;
using System.Text;
using IPC.Plc.Communication.Infrastructure;

namespace IPC.Gateway.WebHost;

public static class GatewayCommercialEndpoints
{
    public static IEndpointRouteBuilder MapGatewayCommercialEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/commercial");

        group.MapGet("/device-templates", (ClaimsPrincipal user, GatewayDeviceTemplateService templates) =>
        {
            if (!GatewayAuthEndpoints.CanViewDevices(user))
                return Forbidden("Current user is not allowed to view device templates.");
            return Ok(templates.ListTemplates());
        });

        group.MapPost("/device-templates/{templateId}/apply", (ClaimsPrincipal user, GatewayDeviceTemplateService templates, string templateId, GatewayDeviceTemplateApplyRequest request) =>
        {
            if (!GatewayAuthEndpoints.CanCreateDevice(user) || !GatewayAuthEndpoints.CanCreateTag(user))
                return Forbidden("Current user is not allowed to apply device templates.");
            return Execute(() => templates.Apply(templateId, request));
        });

        group.MapGet("/tags/export", (ClaimsPrincipal user, GatewayTagBulkService tags, string? channelId, string? deviceId) =>
        {
            if (!GatewayAuthEndpoints.CanViewDevices(user))
                return Forbidden("Current user is not allowed to export tags.");
            byte[] payload = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(tags.ExportCsv(channelId ?? string.Empty, deviceId ?? string.Empty))).ToArray();
            return Results.File(payload, "text/csv", "ipc-gateway-tags.csv");
        });

        group.MapPost("/tags/import", async (ClaimsPrincipal user, HttpRequest request, GatewayTagBulkService tags, string? channelId, string? deviceId) =>
        {
            if (!GatewayAuthEndpoints.CanCreateTag(user) && !GatewayAuthEndpoints.CanEditTag(user))
                return Forbidden("Current user is not allowed to import tags.");
            using StreamReader reader = new StreamReader(request.Body, Encoding.UTF8);
            string csv = await reader.ReadToEndAsync();
            return Execute(() => tags.ImportCsv(csv, channelId ?? string.Empty, deviceId ?? string.Empty));
        });

        group.MapGet("/project/backup", (ClaimsPrincipal user, GatewayProjectBackupService backups) =>
        {
            if (!GatewayAuthEndpoints.CanViewProject(user))
                return Forbidden("Current user is not allowed to export project backups.");
            return Results.File(backups.CreateBackupBytes(), "application/json", "ipc-gateway-project-backup.json");
        });

        group.MapPost("/project/restore", async (ClaimsPrincipal user, HttpRequest request, GatewayProjectBackupService backups) =>
        {
            if (!GatewayAuthEndpoints.CanEditProject(user))
                return Forbidden("Current user is not allowed to restore project backups.");
            using StreamReader reader = new StreamReader(request.Body, Encoding.UTF8);
            string json = await reader.ReadToEndAsync();
            return Execute(() => backups.Restore(json));
        });

        group.MapGet("/license", (ClaimsPrincipal user, GatewayLicenseService licenses) =>
        {
            if (!GatewayAuthEndpoints.CanViewMaintenance(user))
                return Forbidden("Current user is not allowed to view license status.");
            return Ok(licenses.GetStatus());
        });

        group.MapPost("/license", async (ClaimsPrincipal user, HttpRequest request, GatewayLicenseService licenses) =>
        {
            if (!GatewayAuthEndpoints.CanUploadUpdatePackage(user))
                return Forbidden("Current user is not allowed to install licenses.");
            try
            {
                using StreamReader reader = new(request.Body, Encoding.UTF8);
                string licenseText = await reader.ReadToEndAsync();
                return Ok(licenses.Install(licenseText));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (InvalidDataException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (JsonException ex)
            {
                return Results.BadRequest(ApiResult.Fail("License JSON is invalid: " + ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
        });

        group.MapGet("/compatibility", (ClaimsPrincipal user, GatewayCompatibilityService compatibility) =>
        {
            if (!GatewayAuthEndpoints.CanViewMaintenance(user))
                return Forbidden("Current user is not allowed to view compatibility status.");
            return Ok(compatibility.GetMatrix());
        });

        group.MapGet("/drivers", (ClaimsPrincipal user) =>
        {
            if (!GatewayAuthEndpoints.CanViewMaintenance(user))
                return Forbidden("Current user is not allowed to view protocol driver status.");
            return Ok(PlcDriverPluginRegistry.GetRegisteredDrivers());
        });

        return app;
    }

    private static IResult Execute(Func<object?> action)
    {
        try
        {
            return Ok(action());
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(ApiResult.Fail(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(ApiResult.Fail(ex.Message));
        }
    }

    private static IResult Ok(object? data)
    {
        return Results.Ok(ApiResult.Ok(data));
    }

    private static IResult Forbidden(string message)
    {
        return Results.Json(ApiResult.Fail(message), statusCode: StatusCodes.Status403Forbidden);
    }

    private sealed class ApiResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public object? Data { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public static ApiResult Ok(object? data)
        {
            return new ApiResult { Success = true, Data = data };
        }

        public static ApiResult Fail(string message)
        {
            return new ApiResult { Success = false, ErrorMessage = message ?? string.Empty };
        }
    }
}

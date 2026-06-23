/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.WebHost
* 项目描述 ：
* 类 名 称 ：GatewayUpdateEndpoints
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.WebHost
* 机器名称 ：UNKNOWN 
* CLR 版本 ：10.0.0
* 作    者 ：ipc
* 创建时间 ：2026-06-23 17:52:06
* 更新时间 ：2026-06-23 17:52:06
* 版 本 号 ：v1.0.0.0
*******************************************************************
* Copyright @ ipc 2026. All rights reserved.
*******************************************************************
//----------------------------------------------------------------*/
using IPC.Gateway.Core.Gateway;
using Microsoft.AspNetCore.Http.Features;
using System.Security.Claims;

namespace IPC.Gateway.WebHost;

public static class GatewayUpdateEndpoints
{
    public static IEndpointRouteBuilder MapGatewayUpdateEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/maintenance");

        group.MapGet("/updates/status", (ClaimsPrincipal user, GatewayUpdatePackageService updates) =>
        {
            if (!GatewayAuthEndpoints.CanViewMaintenance(user))
                return Forbidden("当前用户没有查看安装升级的权限。");
            return Ok(updates.GetStatus());
        });

        group.MapPost("/updates/packages", async (HttpContext context, ClaimsPrincipal user, GatewayUpdatePackageService updates, GatewayUpdateMaintenanceOptions updateOptions, IGatewayAuditLogStore auditStore, CancellationToken cancellationToken) =>
        {
            if (!GatewayAuthEndpoints.CanUploadUpdatePackage(user))
                return Forbidden("当前用户没有上传升级包的权限。");

            try
            {
                IHttpMaxRequestBodySizeFeature? sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (sizeFeature is { IsReadOnly: false })
                    sizeFeature.MaxRequestBodySize = Math.Max(1, updateOptions.MaxPackageMegabytes) * 1024L * 1024L;

                if (!context.Request.HasFormContentType)
                    return Results.BadRequest(ApiResult.Fail("请使用 multipart/form-data 上传升级包。"));

                IFormCollection form = await context.Request.ReadFormAsync(cancellationToken);
                IFormFile? file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
                GatewayUpdatePackageRecord record = await updates.StorePackageAsync(file!, cancellationToken);
                WriteAudit(context, auditStore, "maintenance.package.upload", "success", "package:" + record.PackageId, string.Empty);
                return Ok(record);
            }
            catch (ArgumentException ex)
            {
                WriteAudit(context, auditStore, "maintenance.package.upload", "bad_request", "package", ex.Message);
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                WriteAudit(context, auditStore, "maintenance.package.upload", "bad_request", "package", ex.Message);
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
        });

        group.MapPost("/updates/packages/{packageId}/prepare", (HttpContext context, ClaimsPrincipal user, GatewayUpdatePackageService updates, IGatewayAuditLogStore auditStore, string packageId) =>
        {
            if (!GatewayAuthEndpoints.CanPrepareUpdate(user))
                return Forbidden("当前用户没有准备离线升级的权限。");

            return ExecuteMaintenanceAction(context, auditStore, "maintenance.update.prepare", "package:" + packageId, () => updates.PrepareUpgrade(packageId));
        });

        group.MapPost("/updates/rollback/{rollbackId}/prepare", (HttpContext context, ClaimsPrincipal user, GatewayUpdatePackageService updates, IGatewayAuditLogStore auditStore, string rollbackId) =>
        {
            if (!GatewayAuthEndpoints.CanRollbackUpdate(user))
                return Forbidden("当前用户没有准备版本回滚的权限。");

            return ExecuteMaintenanceAction(context, auditStore, "maintenance.rollback.prepare", "rollback:" + rollbackId, () => updates.PrepareRollback(rollbackId));
        });

        return app;
    }

    private static IResult ExecuteMaintenanceAction(HttpContext context, IGatewayAuditLogStore auditStore, string action, string target, Func<object> handler)
    {
        try
        {
            object data = handler();
            WriteAudit(context, auditStore, action, "success", target, string.Empty);
            return Ok(data);
        }
        catch (ArgumentException ex)
        {
            WriteAudit(context, auditStore, action, "bad_request", target, ex.Message);
            return Results.BadRequest(ApiResult.Fail(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            WriteAudit(context, auditStore, action, "bad_request", target, ex.Message);
            return Results.BadRequest(ApiResult.Fail(ex.Message));
        }
    }

    private static void WriteAudit(HttpContext context, IGatewayAuditLogStore auditStore, string action, string outcome, string target, string errorMessage)
    {
        GatewayAuthEndpoints.WriteSecurityAudit(
            context,
            auditStore,
            action,
            outcome,
            target,
            string.Empty,
            string.Empty,
            errorMessage);
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

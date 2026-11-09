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
using IPC.Gateway.Core.Application.Gateway;
using IPC.Gateway.Watchdog;
using Microsoft.AspNetCore.Http.Features;
using System.Security.Claims;

namespace IPC.Gateway.WebHost;

public static class GatewayUpdateEndpoints
{
    public static IEndpointRouteBuilder MapGatewayUpdateEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/maintenance");

        group.MapGet("/support/snapshot", async (HttpContext context, ClaimsPrincipal user, IGatewayApplicationService gateway, GatewayUpdatePackageService updates, IGatewayWatchdogService watchdog, IGatewayAuditLogStore auditStore, IWebHostEnvironment environment) =>
        {
            if (!GatewayAuthEndpoints.CanViewMaintenance(user))
                return Forbidden("Current user is not allowed to view maintenance support snapshots.");

            bool includeAuditDetails = GatewayAuthEndpoints.CanViewAudit(user);
            IReadOnlyList<GatewayAuditLogEntry> recentAudit = await GatewayAuditLog.ReadRecentAsync(new GatewayAuditLogQuery { Limit = 20 }, auditStore);
            GatewaySupportSnapshot snapshot = GatewaySupportSnapshotService.Create(
                context.TraceIdentifier,
                user.Identity?.Name ?? string.Empty,
                environment.EnvironmentName,
                gateway.GetStatus(),
                updates.GetStatus(),
                watchdog.GetSnapshot(),
                recentAudit,
                includeAuditDetails);

            await WriteAuditAsync(context, auditStore, "maintenance.support.snapshot", "success", "support", string.Empty);
            return Ok(snapshot);
        });

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
                if (!context.Request.HasFormContentType)
                    return Results.BadRequest(ApiResult.Fail("请使用 multipart/form-data 上传升级包。"));

                IFormCollection form = await ReadPackageFormAsync(context, updateOptions, cancellationToken);
                IFormFile? file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
                GatewayUpdatePackageRecord record = await updates.StorePackageAsync(file!, cancellationToken);
                await WriteAuditAsync(context, auditStore, "maintenance.package.upload", "success", "package:" + record.PackageId, string.Empty);
                return Ok(record);
            }
            catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                return Results.Json(ApiResult.Fail($"升级包不能超过 {Math.Max(1, updateOptions.MaxPackageMegabytes)} MB。"), statusCode: StatusCodes.Status413PayloadTooLarge);
            }
            catch (InvalidDataException ex)
            {
                await WriteAuditAsync(context, auditStore, "maintenance.package.upload", "bad_request", "package", ex.Message);
                return Results.BadRequest(ApiResult.Fail($"升级包或上传表单无效：{ex.Message}（文件上限 {Math.Max(1, updateOptions.MaxPackageMegabytes)} MB）"));
            }
            catch (System.Text.Json.JsonException)
            {
                return Results.BadRequest(ApiResult.Fail("升级包清单不是有效的 JSON。"));
            }
            catch (ArgumentException ex)
            {
                await WriteAuditAsync(context, auditStore, "maintenance.package.upload", "bad_request", "package", ex.Message);
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                await WriteAuditAsync(context, auditStore, "maintenance.package.upload", "bad_request", "package", ex.Message);
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
        });

        group.MapPost("/updates/packages/{packageId}/prepare", async (HttpContext context, ClaimsPrincipal user, GatewayUpdatePackageService updates, IGatewayAuditLogStore auditStore, string packageId) =>
        {
            if (!GatewayAuthEndpoints.CanPrepareUpdate(user))
                return Forbidden("当前用户没有准备离线升级的权限。");

            return await ExecuteMaintenanceActionAsync(context, auditStore, "maintenance.update.prepare", "package:" + packageId, () => updates.PrepareUpgrade(packageId));
        });

        group.MapPost("/updates/rollback/{rollbackId}/prepare", async (HttpContext context, ClaimsPrincipal user, GatewayUpdatePackageService updates, IGatewayAuditLogStore auditStore, string rollbackId) =>
        {
            if (!GatewayAuthEndpoints.CanRollbackUpdate(user))
                return Forbidden("当前用户没有准备版本回滚的权限。");

            return await ExecuteMaintenanceActionAsync(context, auditStore, "maintenance.rollback.prepare", "rollback:" + rollbackId, () => updates.PrepareRollback(rollbackId));
        });

        group.MapPost("/updates/apply", async (HttpContext context, ClaimsPrincipal user, GatewayUpdatePackageService updates, IGatewayAuditLogStore auditStore, GatewayApplyUpdateRequest request) =>
        {
            bool allowUpgrade = GatewayAuthEndpoints.CanApplyUpdate(user);
            bool allowRollback = GatewayAuthEndpoints.CanApplyRollback(user);
            bool allowed = allowUpgrade || allowRollback;
            if (!allowed) return Forbidden("当前用户没有执行升级或回滚的权限。");
            // Persist the operator's request before scheduling a process that stops this host.
            await WriteAuditAsync(context, auditStore, "maintenance.update.apply", "requested", request.ActionId, string.Empty);
            try { return Ok(updates.ApplyPendingAction(request.ActionId, allowUpgrade, allowRollback)); }
            catch (UnauthorizedAccessException ex) { return Forbidden(ex.Message); }
            catch (InvalidOperationException ex) { return Results.BadRequest(ApiResult.Fail(ex.Message)); }
            catch (ArgumentException ex) { return Results.BadRequest(ApiResult.Fail(ex.Message)); }
        });

        return app;
    }

    internal static async Task<IFormCollection> ReadPackageFormAsync(HttpContext context, GatewayUpdateMaintenanceOptions options, CancellationToken cancellationToken)
    {
        long fileLimit = Math.Max(1, options.MaxPackageMegabytes) * 1024L * 1024L;
        long requestLimit = fileLimit + 1024 * 1024; // Multipart headers are additional to the ZIP bytes.
        if (context.Request.ContentLength > requestLimit)
            throw new BadHttpRequestException("Upgrade package is too large.", StatusCodes.Status413PayloadTooLarge);
        IHttpMaxRequestBodySizeFeature? sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = requestLimit;
        // Override only this endpoint; retain the existing limits on other upload APIs.
        context.Features.Set<IFormFeature>(new FormFeature(context.Request, new FormOptions { MultipartBodyLengthLimit = fileLimit }));
        return await context.Request.ReadFormAsync(cancellationToken);
    }

    private static async Task<IResult> ExecuteMaintenanceActionAsync(HttpContext context, IGatewayAuditLogStore auditStore, string action, string target, Func<object> handler)
    {
        try
        {
            object data = handler();
            await WriteAuditAsync(context, auditStore, action, "success", target, string.Empty);
            return Ok(data);
        }
        catch (ArgumentException ex)
        {
            await WriteAuditAsync(context, auditStore, action, "bad_request", target, ex.Message);
            return Results.BadRequest(ApiResult.Fail(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            await WriteAuditAsync(context, auditStore, action, "bad_request", target, ex.Message);
            return Results.BadRequest(ApiResult.Fail(ex.Message));
        }
    }

    private static Task WriteAuditAsync(HttpContext context, IGatewayAuditLogStore auditStore, string action, string outcome, string target, string errorMessage)
    {
        return GatewayAuthEndpoints.WriteSecurityAuditAsync(
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

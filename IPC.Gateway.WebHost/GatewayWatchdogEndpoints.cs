/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.WebHost
* 项目描述 ：
* 类 名 称 ：GatewayWatchdogEndpoints
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
using IPC.Gateway.Watchdog;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace IPC.Gateway.WebHost;

public static class GatewayWatchdogEndpoints
{
    public static IEndpointRouteBuilder MapGatewayWatchdogEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/maintenance/watchdog");

        group.MapGet("/status", (ClaimsPrincipal user, IGatewayWatchdogService watchdog) =>
        {
            if (!GatewayAuthEndpoints.CanViewMaintenance(user))
                return Results.Json(ApiResult.Fail("当前用户没有查看看门狗状态的权限。"), statusCode: StatusCodes.Status403Forbidden);

            return Results.Ok(ApiResult.Ok(watchdog.GetSnapshot()));
        });

        group.MapGet("/config", (ClaimsPrincipal user, GatewayWatchdogConfigurationStore store) =>
        {
            if (!GatewayAuthEndpoints.CanViewMaintenance(user))
                return Results.Json(ApiResult.Fail("当前用户没有查看看门狗配置的权限。"), statusCode: StatusCodes.Status403Forbidden);

            return Results.Ok(ApiResult.Ok(store.Get()));
        });

        group.MapPut("/config", async (HttpContext context, ClaimsPrincipal user, [FromBody] GatewayWatchdogOptions options, GatewayWatchdogConfigurationStore store, IGatewayAuditLogStore auditStore) =>
        {
            if (!GatewayAuthEndpoints.CanEditWatchdog(user))
                return Results.Json(ApiResult.Fail("当前用户没有保存看门狗配置的权限。"), statusCode: StatusCodes.Status403Forbidden);

            try
            {
                GatewayWatchdogOptions saved = store.Save(options);
                await GatewayAuthEndpoints.WriteSecurityAuditAsync(context, auditStore, "maintenance.watchdog.save", "success", "watchdog", string.Empty, string.Empty, string.Empty);
                return Results.Ok(ApiResult.Ok(saved));
            }
            catch (Exception ex)
            {
                await GatewayAuthEndpoints.WriteSecurityAuditAsync(context, auditStore, "maintenance.watchdog.save", "failed", "watchdog", string.Empty, string.Empty, ex.Message);
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
        });

        return app;
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

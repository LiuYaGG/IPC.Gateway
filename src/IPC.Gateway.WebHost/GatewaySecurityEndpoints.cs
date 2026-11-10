/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.WebHost
* 项目描述 ：
* 类 名 称 ：GatewaySecurityEndpoints
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
using System.Security.Claims;
using IPC.Gateway.Core.Domain.Users;
using IPC.Gateway.Core.Gateway;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.Infrastructure;

namespace IPC.Gateway.WebHost;

public static class GatewaySecurityEndpoints
{
    public static IEndpointRouteBuilder MapGatewaySecurityEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/security");

        group.MapGet("/summary", (ClaimsPrincipal user, GatewayIndustrialSecurityOptions security, GatewayAccountSecurityOptions accountSecurity, GatewaySecretProtectionOptions secretStorage) =>
        {
            if (!GatewayAuthEndpoints.CanViewSecurity(user))
                return Results.Json(ApiResult.Fail("当前用户没有查看工业安全配置的权限。"), statusCode: StatusCodes.Status403Forbidden);

            return Results.Ok(ApiResult.Ok(new
            {
                passwordPolicy = accountSecurity.Password,
                accountLockout = accountSecurity.Lockout,
                tls = new
                {
                    security.Tls.RequireHttps,
                    security.Tls.EnableHttpsRedirection,
                    security.Tls.EnableHsts,
                    security.Tls.HstsMaxAgeDays,
                    security.Tls.HttpsPort,
                    security.Tls.MinimumProtocol,
                    certificateConfigured = !string.IsNullOrWhiteSpace(security.Tls.CertificatePath)
                },
                api = security.Api,
                apiTokens = new
                {
                    security.ApiTokens.Enabled,
                    security.ApiTokens.HeaderName,
                    security.ApiTokens.RequireHttps,
                    configuredTokenCount = security.ApiTokens.Tokens.Count,
                    enabledTokenCount = security.ApiTokens.Tokens.Count(item => item.Enabled)
                },
                secretStorage = new
                {
                    secretStorage.Enabled,
                    secretStorage.EnvironmentVariableName,
                    masterKeyConfigured = !string.IsNullOrWhiteSpace(secretStorage.MasterKey)
                },
                certificates = security.Certificates,
                protocolDrivers = PlcDriverPluginRegistry.GetDriverTrustPolicy()
            }));
        });

        group.MapGet("/certificates", (ClaimsPrincipal user, GatewayCertificateManager certificates) =>
        {
            if (!GatewayAuthEndpoints.CanManageCertificates(user))
                return Results.Json(ApiResult.Fail("当前用户没有查看证书状态的权限。"), statusCode: StatusCodes.Status403Forbidden);

            return Results.Ok(ApiResult.Ok(certificates.GetInventory()));
        });

        group.MapPut("/driver-trust", (ClaimsPrincipal user, PlcDriverTrustPolicy policy) =>
        {
            if (!GatewayAuthEndpoints.CanViewSecurity(user) || !GatewayAuthEndpoints.CanManageCertificates(user))
                return Results.Json(ApiResult.Fail("当前用户没有修改驱动验签策略的权限。"), statusCode: StatusCodes.Status403Forbidden);
            try { return Results.Ok(ApiResult.Ok(PlcDriverPluginRegistry.SaveDriverTrustPolicy(policy.RequireSignature))); }
            catch (InvalidOperationException ex) { return Results.BadRequest(ApiResult.Fail(ex.Message)); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            { return Results.BadRequest(ApiResult.Fail("无法保存驱动验签策略，请检查服务账户的目录权限。")); }
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

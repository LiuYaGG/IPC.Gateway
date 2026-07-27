/*----------------------------------------------------------------
* 项目名称 ：Program
* 项目描述 ：
* 类 名 称 ：Program
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：Program
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
using IPC.Gateway.Core.Infrastructure;
using IPC.Gateway.FlowRules;
using IPC.Gateway.Inference;
using IPC.Gateway.Scripting;
using IPC.Gateway.Scripting.Abstractions;
using IPC.Gateway.WebHost;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting.WindowsServices;
using System.Security.Claims;
using System.Text.Json.Serialization;

string startupWebRoot = GatewayWebRoot.PrepareBeforeBuilder(Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = startupWebRoot
});
builder.Host.UseWindowsService(options => options.ServiceName = "IPC Gateway");
builder.Host.UseSystemd();

GatewayIndustrialSecurityOptions securityOptions = GatewayIndustrialSecurityOptions.FromConfiguration(builder.Configuration);
GatewayUpdateMaintenanceOptions updateOptions = GatewayUpdateMaintenanceOptions.FromConfiguration(builder.Configuration);
GatewayObservabilityOptions observabilityOptions = GatewayObservabilityOptions.FromConfiguration(builder.Configuration);
GatewayLicenseOptions licenseOptions = GatewayLicenseOptions.FromConfiguration(builder.Configuration);
GatewayScriptingOptions scriptingOptions = GatewayScriptingSetup.FromConfiguration(builder.Configuration);
builder.ConfigureGatewayTls(securityOptions);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddGatewayFlowRules();
builder.Services.AddGatewayOnnxInference();
builder.Services.AddGatewayCore(builder.Configuration);
builder.Services.AddSingleton(securityOptions);
builder.Services.AddSingleton(updateOptions);
builder.Services.AddSingleton(observabilityOptions);
builder.Services.AddSingleton(licenseOptions);
builder.Services.AddSingleton<GatewayCertificateManager>();
builder.Services.AddSingleton<GatewayApiTokenService>();
builder.Services.AddSingleton<GatewayUpdatePackageService>();
builder.Services.AddSingleton<GatewayWatchdogConfigurationStore>();
builder.Services.AddSingleton<GatewayAuthService>();
builder.Services.AddSingleton<GatewayMetricsCollector>();
builder.Services.AddSingleton<GatewayMetricsInstrumentation>();
builder.Services.AddSingleton<GatewayLicenseService>();
builder.Services.AddSingleton<GatewayProtocolCatalogService>();
builder.Services.AddSingleton<GatewayDeviceTemplateService>();
builder.Services.AddSingleton<GatewayTagBulkService>();
builder.Services.AddSingleton<GatewayProjectBackupService>();
builder.Services.AddSingleton<GatewayCompatibilityService>();
builder.Services.AddSingleton<GatewayRuntimeEventHub>();
builder.Services.AddHostedService<GatewayRuntimeHostedService>();
builder.Services.AddSingleton<IScriptSecretProtector, GatewayScriptSecretProtector>();
builder.Services.AddSingleton<IScriptTagAccessor, GatewayScriptTagAccessor>();
builder.Services.AddGatewayScripting(scriptingOptions);
builder.Services.AddHostedService<GatewayRuntimePushHostedService>();
builder.Services.AddGatewayWatchdog(builder.Configuration);

var app = builder.Build();
if (observabilityOptions.MetricsEnabled)
    _ = app.Services.GetRequiredService<GatewayMetricsInstrumentation>();

app.UseConfiguredForwardedHeaders(builder.Configuration);
app.UseGatewayTlsControls(securityOptions);
app.UseGatewaySecurityHeaders();
app.UseGatewayApiSecurityControls(securityOptions);

string webRoot = GatewayWebRoot.Resolve(app.Environment.ContentRootPath, AppContext.BaseDirectory);
Directory.CreateDirectory(webRoot);
PhysicalFileProvider webFileProvider = new PhysicalFileProvider(webRoot);
app.UseDefaultFiles(new DefaultFilesOptions
{
    FileProvider = webFileProvider,
    DefaultFileNames = new List<string> { "index.html" }
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = webFileProvider
});

app.Use(async (context, next) =>
{
    if (!GatewayAuthEndpoints.IsPublicRequest(context.Request))
    {
        GatewayAuthService auth = context.RequestServices.GetRequiredService<GatewayAuthService>();
        GatewayApiTokenService apiTokens = context.RequestServices.GetRequiredService<GatewayApiTokenService>();
        ClaimsPrincipal principal = new ClaimsPrincipal(new ClaimsIdentity());
        string tokenErrorMessage = string.Empty;
        bool hasApiTokenHeader = !string.IsNullOrWhiteSpace(context.Request.Headers[securityOptions.ApiTokens.HeaderName].ToString());
        bool hasBearerToken = context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
        string tokenName = string.Empty;
        bool apiTokenValidated = false;
        if (hasApiTokenHeader || hasBearerToken)
            apiTokenValidated = apiTokens.TryValidate(context.Request, out principal, out tokenName, out tokenErrorMessage);

        if (apiTokenValidated)
        {
            if (securityOptions.Api.AuditUnauthorizedRequests)
            {
                await GatewayAuthEndpoints.WriteSecurityAuditAsync(
                    context,
                    context.RequestServices.GetService<IGatewayAuditLogStore>(),
                    "api.token",
                    "success",
                    context.Request.Path.Value ?? string.Empty,
                    tokenName,
                    principal.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty,
                    string.Empty);
            }
        }
        else if (hasApiTokenHeader)
        {
            if (securityOptions.Api.AuditUnauthorizedRequests)
            {
                await GatewayAuthEndpoints.WriteSecurityAuditAsync(
                    context,
                    context.RequestServices.GetService<IGatewayAuditLogStore>(),
                    "api.token",
                    "unauthorized",
                    "api-token",
                    string.Empty,
                    string.Empty,
                    string.IsNullOrWhiteSpace(tokenErrorMessage) ? "API Token 无效。" : tokenErrorMessage);
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { success = false, errorMessage = string.IsNullOrWhiteSpace(tokenErrorMessage) ? "API Token 无效。" : tokenErrorMessage });
            return;
        }
        else
        {
            string token = GatewayAuthEndpoints.ReadToken(context.Request);
            GatewayTokenValidationResult tokenValidation = await auth.ValidateTokenAsync(token);
            if (!tokenValidation.Success)
            {
                if (securityOptions.Api.AuditUnauthorizedRequests)
                {
                    await GatewayAuthEndpoints.WriteSecurityAuditAsync(
                        context,
                        context.RequestServices.GetService<IGatewayAuditLogStore>(),
                        "api.unauthorized",
                        "unauthorized",
                        context.Request.Path.Value ?? string.Empty,
                        string.Empty,
                        string.Empty,
                        "未登录或会话已过期。");
                }

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { success = false, errorMessage = "未登录或会话已过期。" });
                return;
            }

            principal = tokenValidation.Principal;
        }

        if (principal?.Identity?.IsAuthenticated != true)
        {
            if (securityOptions.Api.AuditUnauthorizedRequests)
            {
                await GatewayAuthEndpoints.WriteSecurityAuditAsync(
                    context,
                    context.RequestServices.GetService<IGatewayAuditLogStore>(),
                    "api.unauthorized",
                    "unauthorized",
                    context.Request.Path.Value ?? string.Empty,
                    string.Empty,
                    string.Empty,
                    "未登录或会话已过期。");
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { success = false, errorMessage = "未登录或会话已过期。" });
            return;
        }

        context.User = principal;

        string path = context.Request.Path.Value ?? string.Empty;
        bool writesConfiguration = path.StartsWith("/api/config", StringComparison.OrdinalIgnoreCase) &&
                                   !HttpMethods.IsGet(context.Request.Method);
        if (writesConfiguration && !GatewayAuthEndpoints.CanWriteConfiguration(principal))
        {
            if (securityOptions.Api.AuditForbiddenRequests)
            {
                await GatewayAuthEndpoints.WriteSecurityAuditAsync(
                    context,
                    context.RequestServices.GetService<IGatewayAuditLogStore>(),
                    "api.forbidden",
                    "forbidden",
                    path,
                    principal.Identity?.Name ?? string.Empty,
                    principal.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty,
                    "当前用户没有修改网关配置权限。");
            }

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { success = false, errorMessage = "当前用户没有修改网关配置权限。" });
            return;
        }
    }

    await next();
});


app.MapGatewayAuthEndpoints();
app.MapGatewaySecurityEndpoints();
app.MapGatewayHealthEndpoints();
app.MapGatewayMetricsEndpoints(observabilityOptions);
app.MapGatewayConfigurationEndpoints();
app.MapGatewayCommercialEndpoints();
app.MapGatewayUpdateEndpoints();
app.MapGatewayWatchdogEndpoints();
app.MapGatewayScriptEndpoints();
app.MapFallback(async context =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    string indexPath = Path.Combine(webRoot, "index.html");
    if (!File.Exists(indexPath))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsync("IPC.Gateway.Web static assets were not found. Run npm install && npm run build in IPC.Gateway.Web before publishing IPC.Gateway.WebHost.");
        return;
    }

    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(indexPath);
});
app.Run();

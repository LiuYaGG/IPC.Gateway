/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.WebHost
* 项目描述 ：
* 类 名 称 ：GatewayTlsSetup
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
using System.Security.Cryptography.X509Certificates;

namespace IPC.Gateway.WebHost;

public static class GatewayTlsSetup
{
    public static void ConfigureGatewayTls(this WebApplicationBuilder builder, GatewayIndustrialSecurityOptions securityOptions)
    {
        GatewayTlsOptions tls = securityOptions?.Tls ?? new GatewayTlsOptions();
        if (!tls.EnableHsts && !tls.EnableHttpsRedirection && string.IsNullOrWhiteSpace(tls.CertificatePath))
            return;

        builder.Services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromDays(Math.Max(1, Math.Min(3650, tls.HstsMaxAgeDays)));
            options.IncludeSubDomains = false;
            options.Preload = false;
        });

        builder.Services.AddHttpsRedirection(options =>
        {
            if (tls.HttpsPort > 0)
                options.HttpsPort = tls.HttpsPort;
        });

        if (string.IsNullOrWhiteSpace(tls.CertificatePath))
            return;

        string certificatePath = ResolvePath(tls.CertificatePath);
        if (!File.Exists(certificatePath))
            throw new InvalidOperationException($"TLS 证书文件不存在：{certificatePath}");

        X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12FromFile(
            certificatePath,
            tls.CertificatePassword,
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.EphemeralKeySet);

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ConfigureHttpsDefaults(https =>
            {
                https.ServerCertificate = certificate;
                https.SslProtocols = tls.ResolveMinimumProtocol();
            });
        });
    }

    public static IApplicationBuilder UseGatewayTlsControls(this IApplicationBuilder app, GatewayIndustrialSecurityOptions securityOptions)
    {
        GatewayTlsOptions tls = securityOptions?.Tls ?? new GatewayTlsOptions();
        if (tls.EnableHsts)
            app.UseHsts();
        if (tls.EnableHttpsRedirection)
            app.UseHttpsRedirection();

        if (!tls.RequireHttps)
            return app;

        return app.Use(async (context, next) =>
        {
            if (!context.Request.IsHttps && context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { success = false, errorMessage = "当前接口要求通过 HTTPS/TLS 访问。" });
                return;
            }

            await next();
        });
    }

    private static string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        string value = path.Trim();
        return Path.IsPathRooted(value) ? value : Path.Combine(AppContext.BaseDirectory, value);
    }
}

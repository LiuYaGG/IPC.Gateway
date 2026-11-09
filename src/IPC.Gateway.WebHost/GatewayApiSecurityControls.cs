/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.WebHost
* 项目描述 ：
* 类 名 称 ：GatewayApiSecurityControls
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
using System.Security.Cryptography;

namespace IPC.Gateway.WebHost;

public static class GatewayApiSecurityControls
{
    public const string RequestBodySha256ItemKey = "Gateway.Security.RequestBodySha256";
    public const string RequestBodyLengthItemKey = "Gateway.Security.RequestBodyLength";

    public static IApplicationBuilder UseGatewayApiSecurityControls(this IApplicationBuilder app, GatewayIndustrialSecurityOptions securityOptions)
    {
        GatewayApiSecurityOptions options = securityOptions?.Api ?? new GatewayApiSecurityOptions();
        return app.Use(async (context, next) =>
        {
            if (options.AuditConfigurationRequestHash && IsConfigurationWrite(context.Request))
                await CaptureRequestBodyHash(context, options);

            await next();
        });
    }

    private static bool IsConfigurationWrite(HttpRequest request)
    {
        return request.Path.StartsWithSegments("/api/config") &&
               !HttpMethods.IsGet(request.Method) &&
               request.Body.CanRead;
    }

    private static async Task CaptureRequestBodyHash(HttpContext context, GatewayApiSecurityOptions options)
    {
        long? contentLength = context.Request.ContentLength;
        context.Items[RequestBodyLengthItemKey] = contentLength ?? 0L;
        int maxBytes = Math.Max(0, options.MaxAuditedBodyBytes);
        if (maxBytes == 0 || (contentLength.HasValue && contentLength.Value > maxBytes))
            return;

        context.Request.EnableBuffering();
        using MemoryStream buffer = new MemoryStream();
        await context.Request.Body.CopyToAsync(buffer);
        context.Request.Body.Position = 0;

        if (buffer.Length > maxBytes)
            return;

        byte[] hash = SHA256.HashData(buffer.ToArray());
        context.Items[RequestBodySha256ItemKey] = Convert.ToHexString(hash).ToLowerInvariant();
        context.Items[RequestBodyLengthItemKey] = buffer.Length;
    }
}

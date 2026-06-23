/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：GatewaySecurityHeadersTests
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Tests
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
using IPC.Gateway.WebHost;
using Microsoft.AspNetCore.Http;

namespace IPC.Gateway.Tests;

public sealed class GatewaySecurityHeadersTests
{
    [Fact]
    public void Apply_AddsBaselineHeadersToStaticResponses()
    {
        DefaultHttpContext context = new DefaultHttpContext();
        context.Request.Path = "/assets/index.js";

        GatewaySecurityHeaders.Apply(context);

        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"]);
        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"]);
        Assert.Equal("no-referrer", context.Response.Headers["Referrer-Policy"]);
        Assert.Equal("same-origin", context.Response.Headers["Cross-Origin-Opener-Policy"]);
        Assert.False(context.Response.Headers.ContainsKey("Cache-Control"));
        Assert.False(context.Response.Headers.ContainsKey("Pragma"));
    }

    [Fact]
    public void Apply_DisablesCachingForApiResponses()
    {
        DefaultHttpContext context = new DefaultHttpContext();
        context.Request.Path = "/api/config/audit";

        GatewaySecurityHeaders.Apply(context);

        Assert.Equal("no-store", context.Response.Headers["Cache-Control"]);
        Assert.Equal("no-cache", context.Response.Headers["Pragma"]);
    }
}

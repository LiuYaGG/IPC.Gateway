/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：GatewayForwardedHeadersTests
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
using System.Net;
using IPC.Gateway.WebHost;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IPC.Gateway.Tests;

public sealed class GatewayForwardedHeadersTests
{
    [Fact]
    public void CreateSetup_DisabledByDefault()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();

        GatewayForwardedHeadersSetup setup = GatewayForwardedHeaders.CreateSetup(configuration);

        Assert.False(setup.Enabled);
        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, setup.Options.ForwardedHeaders);
        Assert.Equal(1, setup.Options.ForwardLimit);
        Assert.Empty(setup.Options.KnownProxies);
        Assert.Empty(setup.Options.KnownIPNetworks);
    }

    [Fact]
    public void CreateSetup_AddsConfiguredTrustedProxyAndNetwork()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:ForwardedHeaders:Enabled"] = "true",
                ["Gateway:ForwardedHeaders:KnownProxies:0"] = "10.0.0.5",
                ["Gateway:ForwardedHeaders:KnownNetworks:0"] = "10.10.0.0/16"
            })
            .Build();

        GatewayForwardedHeadersSetup setup = GatewayForwardedHeaders.CreateSetup(configuration);

        Assert.True(setup.Enabled);
        Assert.Contains(IPAddress.Parse("10.0.0.5"), setup.Options.KnownProxies);
        Assert.True(setup.Options.KnownIPNetworks.Count > 0);
    }

    [Fact]
    public void CreateSetup_RejectsInvalidKnownNetwork()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:ForwardedHeaders:Enabled"] = "true",
                ["Gateway:ForwardedHeaders:KnownNetworks:0"] = "10.10.0.0/40"
            })
            .Build();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => GatewayForwardedHeaders.CreateSetup(configuration));
        Assert.Contains("Gateway:ForwardedHeaders:KnownNetworks", exception.Message);
    }

    [Fact]
    public void CreateSetup_RejectsEnabledConfigurationWithoutTrustBoundary()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:ForwardedHeaders:Enabled"] = "true"
            })
            .Build();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => GatewayForwardedHeaders.CreateSetup(configuration));
        Assert.Contains("KnownProxies or KnownNetworks", exception.Message);
    }

    [Fact]
    public async Task Middleware_UsesForwardedProtoFromTrustedProxy()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:ForwardedHeaders:Enabled"] = "true",
                ["Gateway:ForwardedHeaders:KnownProxies:0"] = "10.0.0.5"
            })
            .Build();
        GatewayForwardedHeadersSetup setup = GatewayForwardedHeaders.CreateSetup(configuration);
        DefaultHttpContext context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");
        context.Request.Scheme = "http";
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.10";
        context.Request.Headers["X-Forwarded-Proto"] = "https";

        ForwardedHeadersMiddleware middleware = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            NullLoggerFactory.Instance,
            Options.Create(setup.Options));

        await middleware.Invoke(context);

        Assert.Equal("https", context.Request.Scheme);
        Assert.Equal(IPAddress.Parse("203.0.113.10"), context.Connection.RemoteIpAddress);
    }
}

/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.WebHost
* 项目描述 ：
* 类 名 称 ：GatewayForwardedHeaders
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
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace IPC.Gateway.WebHost;

public static class GatewayForwardedHeaders
{
    private const string SectionName = "Gateway:ForwardedHeaders";

    public static IApplicationBuilder UseConfiguredForwardedHeaders(this IApplicationBuilder app, IConfiguration configuration)
    {
        GatewayForwardedHeadersSetup setup = CreateSetup(configuration);
        if (!setup.Enabled)
            return app;

        return app.UseForwardedHeaders(setup.Options);
    }

    internal static GatewayForwardedHeadersSetup CreateSetup(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection(SectionName);
        bool enabled = bool.TryParse(section["Enabled"], out bool configuredEnabled) && configuredEnabled;
        ForwardedHeadersOptions options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1
        };
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        AddKnownProxies(options, section.GetSection("KnownProxies").GetChildren().Select(item => item.Value));
        AddKnownIPNetworks(options, section.GetSection("KnownNetworks").GetChildren().Select(item => item.Value));
        if (enabled && options.KnownProxies.Count == 0 && options.KnownIPNetworks.Count == 0)
            throw new InvalidOperationException("Gateway:ForwardedHeaders is enabled, but no KnownProxies or KnownNetworks trust boundary is configured.");

        return new GatewayForwardedHeadersSetup(enabled, options);
    }

    private static void AddKnownProxies(ForwardedHeadersOptions options, IEnumerable<string?> values)
    {
        foreach (string? value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (!IPAddress.TryParse(value.Trim(), out IPAddress? address))
                throw new InvalidOperationException($"Invalid Gateway:ForwardedHeaders:KnownProxies value '{value}'.");

            options.KnownProxies.Add(address);
        }
    }

    private static void AddKnownIPNetworks(ForwardedHeadersOptions options, IEnumerable<string?> values)
    {
        foreach (string? value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (!System.Net.IPNetwork.TryParse(value.Trim(), out System.Net.IPNetwork network))
                throw new InvalidOperationException($"Invalid Gateway:ForwardedHeaders:KnownNetworks value '{value}'. Expected CIDR format such as 10.0.0.0/8.");

            options.KnownIPNetworks.Add(network);
        }
    }
}

internal sealed record GatewayForwardedHeadersSetup(bool Enabled, ForwardedHeadersOptions Options);

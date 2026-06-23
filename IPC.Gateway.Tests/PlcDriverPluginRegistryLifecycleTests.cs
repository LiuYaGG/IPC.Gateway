/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：PlcDriverPluginRegistryLifecycleTests
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
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.Infrastructure;

namespace IPC.Gateway.Tests;

public sealed class PlcDriverPluginRegistryLifecycleTests
{
    [Fact]
    public void Register_IgnoresNullPluginInputs()
    {
        IProtocolDriver? protocolDriver = null;
        IPlcDriverPlugin? legacyPlugin = null;

        PlcDriverPluginRegistry.Register(protocolDriver);
        PlcDriverPluginRegistry.Register(legacyPlugin);
    }

    [Fact]
    public void TryCreateClient_UnknownDriverLeavesClientEmpty()
    {
        PlcConnectionOptions options = new PlcConnectionOptions
        {
            Protocol = PlcProtocol.Plugin,
            DriverId = "__missing_driver__"
        };

        bool created = PlcDriverPluginRegistry.TryCreateClient(options, out IPlcClient? client);

        Assert.False(created);
        Assert.Null(client);
    }

    [Fact]
    public void PluginDiscovery_InvalidInputsReturnStableResults()
    {
        Assert.Empty(PlcDriverPluginRegistry.DiscoverPlugins(string.Empty));
        Assert.Empty(PlcDriverPluginRegistry.LoadPluginsFromDirectory(string.Empty));

        PlcDriverPluginCandidate candidate = PlcDriverPluginRegistry.DiscoverPlugin(string.Empty);
        Assert.Equal("Invalid", candidate.Status);
        Assert.False(PlcDriverPluginRegistry.UnloadPlugin(string.Empty));

        PlcDriverPluginLoadResult loadResult = PlcDriverPluginRegistry.LoadPlugin(string.Empty);
        Assert.False(loadResult.Success);
        Assert.Equal("Assembly path is empty.", loadResult.ErrorMessage);
    }
}

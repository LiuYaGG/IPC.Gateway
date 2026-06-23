/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.TestProtocolPlugin
* 项目描述 ：
* 类 名 称 ：TestVirtualProtocolDriver
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.TestProtocolPlugin
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
using IPC.Plc.Communication.VirtualPlc;

namespace IPC.Gateway.TestProtocolPlugin;

public sealed class TestVirtualProtocolDriver : IProtocolDriver
{
    public string DriverId
    {
        get { return "test.virtual-plugin"; }
    }

    public string DisplayName
    {
        get { return "Test Virtual Protocol Plugin"; }
    }

    public PlcProtocol Protocol
    {
        get { return PlcProtocol.Plugin; }
    }

    public bool Supports(PlcConnectionOptions options)
    {
        return options != null &&
               string.Equals(options.DriverId, DriverId, StringComparison.OrdinalIgnoreCase);
    }

    public IPlcClient CreateClient(PlcConnectionOptions options)
    {
        PlcConnectionOptions clientOptions = options ?? new PlcConnectionOptions();
        clientOptions.Protocol = PlcProtocol.VirtualPlc;
        if (string.IsNullOrWhiteSpace(clientOptions.Host))
            clientOptions.Host = "test-virtual-plugin";
        return new VirtualPlcClient(clientOptions);
    }
}

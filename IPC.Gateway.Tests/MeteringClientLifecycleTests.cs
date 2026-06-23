/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：MeteringClientLifecycleTests
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
using IPC.Plc.Communication.Metering.Cjt188;
using IPC.Plc.Communication.Metering.Dlt645;

namespace IPC.Gateway.Tests;

public sealed class MeteringClientLifecycleTests
{
    [Fact]
    public void Dlt645Client_DisconnectedLifecycleIsExplicitAndSafe()
    {
        using Dlt645Client client = new Dlt645Client(new PlcConnectionOptions());

        Assert.False(client.IsConnected);
        client.Disconnect();
        Assert.False(client.IsConnected);
        Assert.Throws<InvalidOperationException>(() => client.Read("DLT645:000000000001:00000000", PlcDataType.Int16, 1, 0));
        Assert.Throws<InvalidOperationException>(() => client.Connect());
        Assert.False(client.IsConnected);
    }

    [Fact]
    public void Cjt188Client_DisconnectedLifecycleIsExplicitAndSafe()
    {
        using Cjt188Client client = new Cjt188Client(new PlcConnectionOptions());

        Assert.False(client.IsConnected);
        client.Disconnect();
        Assert.False(client.IsConnected);
        Assert.Throws<InvalidOperationException>(() => client.Read("CJ188:000000000001:1F90", PlcDataType.Int16, 1, 0));
        Assert.Throws<InvalidOperationException>(() => client.Connect());
        Assert.False(client.IsConnected);
    }
}

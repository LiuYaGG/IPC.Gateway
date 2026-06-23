/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：CoreValueConversionTests
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
using IPC.Plc.Communication.ModbusTcp;
using IPC.Runtime.Configuration;
using IPC.Runtime.Scaling;

namespace IPC.Gateway.Tests;

public sealed class CoreValueConversionTests
{
    [Fact]
    public void PlcValueFormatter_FormatsNullAsEmptyText()
    {
        Assert.Equal(string.Empty, PlcValueFormatter.Format(null));
        Assert.Equal("1, , 3", PlcValueFormatter.Format(new object?[] { 1, null, 3 }));
    }

    [Fact]
    public void TagValueScaler_PreservesScalarNullAndFormatsCollectionNulls()
    {
        ScalingConfig scaling = ScalingConfig.Default();
        scaling.Enabled = true;
        scaling.Multiplier = 2D;
        scaling.DecimalPlaces = 1;

        Assert.Null(TagValueScaler.Scale(null, scaling));
        Assert.Equal("2.0, , 6.0", TagValueScaler.Format(TagValueScaler.Scale(new object?[] { 1, null, 3 }, scaling), scaling));
    }

    [Fact]
    public void ModbusTcpClient_DisconnectBeforeConnectIsSafe()
    {
        ModbusTcpClient client = new ModbusTcpClient(new PlcConnectionOptions { Host = "127.0.0.1" });

        client.Disconnect();

        Assert.False(client.IsConnected);
    }
}

/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：CoreNullabilityDefaultsTests
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
using IPC.EdgeGateway;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.VirtualPlc;
using IPC.Runtime.Api;
using IPC.Runtime.Engine;
using IPC.Runtime.Values;

namespace IPC.Gateway.Tests;

public sealed class CoreNullabilityDefaultsTests
{
    [Fact]
    public void TagValueSnapshot_DefaultsAreNonNull()
    {
        TagValueSnapshot snapshot = new TagValueSnapshot();

        Assert.NotNull(snapshot.RawValue);
        Assert.NotNull(snapshot.Value);
        Assert.NotNull(snapshot.Alarm);
        Assert.Equal(string.Empty, snapshot.RawValueText);
        Assert.Equal(string.Empty, snapshot.ValueText);
    }

    [Fact]
    public void TagValueChangedEventArgs_RejectsNullSnapshot()
    {
        Assert.Throws<ArgumentNullException>(() => new TagValueChangedEventArgs(null!));
    }

    [Fact]
    public void EdgeRuleRuntimeEvent_DefaultSnapshotIsNonNull()
    {
        EdgeRuleRuntimeEvent runtimeEvent = new EdgeRuleRuntimeEvent();

        Assert.NotNull(runtimeEvent.Snapshot);
        Assert.Equal(string.Empty, runtimeEvent.Snapshot.DeviceName);
    }

    [Fact]
    public void ApiContracts_DefaultsAreNonNull()
    {
        ReadTagResponse read = new ReadTagResponse();
        WriteTagResponse write = new WriteTagResponse();
        WriteTagRequest request = new WriteTagRequest();

        Assert.NotNull(read.RawValue);
        Assert.NotNull(read.Value);
        Assert.NotNull(write.CurrentValue);
        Assert.NotNull(request.Value);
        Assert.Equal(string.Empty, request.DeviceName);
        Assert.Equal(string.Empty, request.GroupName);
        Assert.Equal(string.Empty, request.TagName);
        Assert.Equal(string.Empty, request.DataType);
        Assert.Equal(string.Empty, request.ValueText);
    }

    [Fact]
    public void PlcConnectionOptions_DefaultStringsAreNonNull()
    {
        PlcConnectionOptions options = new PlcConnectionOptions();

        Assert.Equal(string.Empty, options.Host);
        Assert.Equal(string.Empty, options.Username);
        Assert.Equal(string.Empty, options.Password);
        Assert.Equal(string.Empty, options.DriverOptionsJson);
    }

    [Fact]
    public void PlcDriverPluginManifest_VersionParsingUsesStableDefault()
    {
        PlcDriverPluginManifest manifest = new PlcDriverPluginManifest();

        Assert.Equal(new Version(0, 0, 0, 0), manifest.GetVersionOrDefault());

        manifest.Version = null!;
        Assert.Equal(new Version(0, 0, 0, 0), manifest.GetVersionOrDefault());

        manifest.Version = "not-a-version";
        Assert.Equal(new Version(0, 0, 0, 0), manifest.GetVersionOrDefault());

        manifest.Version = "1.2.3";
        Assert.Equal(new Version(1, 2, 3), manifest.GetVersionOrDefault());
    }

    [Fact]
    public void VirtualPlcClient_StringAndBoolValuesStayNonNull()
    {
        using VirtualPlcClient client = new VirtualPlcClient(new PlcConnectionOptions
        {
            Host = Guid.NewGuid().ToString("N")
        });
        client.Connect();

        string defaultText = Assert.IsType<string>(client.Read("D100", PlcDataType.String, 1, 0).Value);
        client.Write("D100", PlcDataType.String, "line-1", 0);
        string storedText = Assert.IsType<string>(client.Read("D100", PlcDataType.String, 1, 0).Value);
        client.Write("M100", PlcDataType.Bool, "yes", 0);
        bool enabled = Assert.IsType<bool>(client.Read("M100", PlcDataType.Bool, 1, 0).Value);
        client.Write("M101", PlcDataType.Bool, string.Empty, 0);
        bool emptyBool = Assert.IsType<bool>(client.Read("M101", PlcDataType.Bool, 1, 0).Value);

        Assert.Equal(string.Empty, defaultText);
        Assert.Equal("line-1", storedText);
        Assert.True(enabled);
        Assert.False(emptyBool);
    }
}

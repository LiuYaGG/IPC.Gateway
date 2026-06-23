/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：GatewayCoreServiceConfigurationCommandTests
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
using IPC.Gateway.Core.Gateway;

namespace IPC.Gateway.Tests;

public sealed class GatewayCoreServiceConfigurationCommandTests : IDisposable
{
    private readonly string _rootDirectory;
    private readonly string _databasePath;

    public GatewayCoreServiceConfigurationCommandTests()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), "ipc-gateway-tests", Guid.NewGuid().ToString("N"));
        _databasePath = Path.Combine(_rootDirectory, "gateway.db");
    }

    [Fact]
    public void ApplyConfigurationCommand_RejectsNullNestedConfiguration()
    {
        using GatewayCoreService gateway = CreateGateway();

        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            gateway.ApplyConfigurationCommand("test", "{\"action\":\"upsertDevice\",\"device\":null}"));

        Assert.Contains("device", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyConfigurationCommand_UpsertDeviceUpdatesStatusCounts()
    {
        using GatewayCoreService gateway = CreateGateway();

        GatewayRuntimeStatus status = (GatewayRuntimeStatus)gateway.ApplyConfigurationCommand("test", """
{
  "action": "upsertDevice",
  "device": {
    "id": "device-1",
    "name": "Line 1",
    "enabled": true,
    "protocol": "VirtualPlc",
    "connection": {
      "protocol": "VirtualPlc",
      "host": "default",
      "timeoutMilliseconds": 3000
    },
    "tags": [],
    "groups": []
  }
}
""");

        Assert.Equal(1, status.DeviceCount);
        Assert.Equal(1, status.EnabledDeviceCount);
        Assert.Equal("Line 1", gateway.CurrentProject.Devices.Single().Name);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (!Directory.Exists(_rootDirectory))
            return;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(_rootDirectory, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(50);
            }
        }
    }

    private GatewayCoreService CreateGateway()
    {
        return new GatewayCoreService(
            new GatewayRuntimeOptions
            {
                AutoCreateDefaultProject = true,
                Database = new GatewayDatabaseOptions
                {
                    Provider = "Sqlite",
                    Database = _databasePath,
                    AutoCreateDatabase = true
                }
            },
            new MqttGatewayOptions
            {
                Enabled = false,
                OutboxDirectory = Path.Combine(_rootDirectory, "mqtt-outbox")
            },
            new OpcUaServerOptions
            {
                Enabled = false
            },
            new LocalHistoryOptions
            {
                Enabled = false,
                Directory = Path.Combine(_rootDirectory, "history")
            },
            new StorageHealthThresholds());
    }
}

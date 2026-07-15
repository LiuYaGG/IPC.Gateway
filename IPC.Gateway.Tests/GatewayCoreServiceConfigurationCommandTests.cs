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
using IPC.Gateway.Core.Application.Gateway;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Core.Infrastructure.Persistence;
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;
using IPC.Runtime.Values;
using System.Reflection;

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

    [Fact]
    public void ApplyConfigurationCommand_RuleChangesDoNotRestartRuntime()
    {
        using GatewayCoreService gateway = CreateGateway();
        gateway.Start();
        int runtimeGeneration = GetRuntimeGeneration(gateway.Runtime);

        gateway.ApplyConfigurationCommand("test", """
{
  "action": "upsertRule",
  "rule": {
    "id": "hot-rule",
    "name": "Hot Rule",
    "enabled": false
  }
}
""");

        Assert.Equal(runtimeGeneration, GetRuntimeGeneration(gateway.Runtime));
        Assert.Contains(gateway.CurrentProject.Rules, rule => rule.Id == "hot-rule");

        gateway.ApplyConfigurationCommand("test", """
{
  "action": "deleteRule",
  "ruleId": "hot-rule"
}
""");

        Assert.Equal(runtimeGeneration, GetRuntimeGeneration(gateway.Runtime));
        Assert.DoesNotContain(gateway.CurrentProject.Rules, rule => rule.Id == "hot-rule");
    }

    [Fact]
    public async Task ApplyConfigurationCommandAsync_FlowRuleChangesDoNotRestartRuntime()
    {
        using GatewayCoreService gateway = CreateGateway();
        gateway.Start();
        int runtimeGeneration = GetRuntimeGeneration(gateway.Runtime);

        await gateway.ApplyConfigurationCommandAsync("test", """
{
  "action": "upsertFlowRule",
  "flowRule": {
    "id": "hot-flow-rule",
    "name": "Hot Flow Rule",
    "enabled": false,
    "lifecycleState": "Draft",
    "mode": "Flow",
    "nodes": [],
    "edges": []
  }
}
""");

        Assert.Equal(runtimeGeneration, GetRuntimeGeneration(gateway.Runtime));
        Assert.Contains(gateway.CurrentProject.FlowRules, rule => rule.Id == "hot-flow-rule");

        await gateway.ApplyConfigurationCommandAsync("test", """
{
  "action": "deleteFlowRule",
  "flowRuleId": "hot-flow-rule"
}
""");

        Assert.Equal(runtimeGeneration, GetRuntimeGeneration(gateway.Runtime));
        Assert.DoesNotContain(gateway.CurrentProject.FlowRules, rule => rule.Id == "hot-flow-rule");
    }

    [Fact]
    public void RuleApplicationService_RuleChangesDoNotRestartRuntime()
    {
        using GatewayCoreService gateway = CreateGateway();
        gateway.Start();
        GatewayRuleConfigurationApplicationService rules = new GatewayRuleConfigurationApplicationService(gateway);
        int runtimeGeneration = GetRuntimeGeneration(gateway.Runtime);

        EdgeRuleConfig added = rules.AddRule(new EdgeRuleConfig
        {
            Id = "application-rule",
            Name = "Application Rule",
            Enabled = false
        });
        Assert.Equal(runtimeGeneration, GetRuntimeGeneration(gateway.Runtime));

        added.Description = "updated";
        rules.UpdateRule(added.Id, added);
        Assert.Equal(runtimeGeneration, GetRuntimeGeneration(gateway.Runtime));

        rules.DeleteRule(added.Id);
        Assert.Equal(runtimeGeneration, GetRuntimeGeneration(gateway.Runtime));
    }

    [Fact]
    public async Task RuleApplicationServiceAsync_FlowRuleChangesDoNotRestartRuntime()
    {
        using GatewayCoreService gateway = CreateGateway();
        gateway.Start();
        GatewayRuleConfigurationApplicationService rules = new GatewayRuleConfigurationApplicationService(gateway);
        int runtimeGeneration = GetRuntimeGeneration(gateway.Runtime);

        FlowRuleDefinition added = await rules.AddFlowRuleAsync(new FlowRuleDefinition
        {
            Id = "application-flow-rule",
            Name = "Application Flow Rule",
            Enabled = false
        });
        Assert.Equal(runtimeGeneration, GetRuntimeGeneration(gateway.Runtime));

        added.Description = "updated";
        await rules.UpdateFlowRuleAsync(added.Id, added);
        Assert.Equal(runtimeGeneration, GetRuntimeGeneration(gateway.Runtime));

        await rules.DeleteFlowRuleAsync(added.Id);
        Assert.Equal(runtimeGeneration, GetRuntimeGeneration(gateway.Runtime));
    }

    [Fact]
    public void RuntimeStateRepository_SaveDeletesMissingDeviceStatuses()
    {
        SqlSugarRuntimeStateRepository repository = new SqlSugarRuntimeStateRepository(new GatewayDatabaseOptions
        {
            Provider = "Sqlite",
            Database = _databasePath,
            AutoCreateDatabase = true
        });

        repository.Save("project-1", new GatewayRuntimeStateSnapshot
        {
            Devices = new List<DeviceRuntimeStatus>
            {
                new DeviceRuntimeStatus { DeviceId = "old-device", DeviceName = "Line 1", Status = "Offline" },
                new DeviceRuntimeStatus { DeviceId = "new-device", DeviceName = "Line 1", Status = "Online" }
            }
        });

        repository.Save("project-1", new GatewayRuntimeStateSnapshot
        {
            Devices = new List<DeviceRuntimeStatus>
            {
                new DeviceRuntimeStatus { DeviceId = "new-device", DeviceName = "Line 1", Status = "Online" }
            }
        });

        GatewayRuntimeStateSnapshot loaded = repository.Load("project-1");
        DeviceRuntimeStatus device = Assert.Single(loaded.Devices);
        Assert.Equal("new-device", device.DeviceId);
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

    private static int GetRuntimeGeneration(IRuntimeService runtime)
    {
        FieldInfo field = typeof(RuntimeEngine).GetField("_runtimeGeneration", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Runtime generation field was not found.");
        return (int)(field.GetValue(runtime) ?? 0);
    }
}

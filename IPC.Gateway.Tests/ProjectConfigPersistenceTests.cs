/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：ProjectConfigPersistenceTests
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
using IPC.Runtime.Configuration;

namespace IPC.Gateway.Tests;

public sealed class ProjectConfigPersistenceTests
{
    [Fact]
    public void Clone_SkipsNullChildrenAndDeepCopiesNestedConfig()
    {
        ProjectConfig source = CreateValidProject();
        source.Devices.Add(null!);
        source.Rules.Add(null!);
        source.FlowRules.Add(null!);

        ProjectConfig clone = ProjectConfigCloner.Clone(source);

        Assert.NotSame(source, clone);
        Assert.Single(clone.Devices);
        Assert.Single(clone.Rules);
        Assert.Single(clone.FlowRules);
        Assert.NotSame(source.Devices[0], clone.Devices[0]);
        Assert.NotSame(source.Devices[0].Tags[0], clone.Devices[0].Tags[0]);
        Assert.NotSame(source.Rules[0], clone.Rules[0]);
        Assert.NotSame(source.FlowRules[0], clone.FlowRules[0]);
    }

    [Fact]
    public void Load_NormalizesMissingCollectionsAndNestedDefaults()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ipc-gateway-config-tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "gateway-project.json");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(path, """
{
  "name": "Loaded Project",
  "devices": [
    {
      "name": "Virtual Line",
      "protocol": "VirtualPlc",
      "tags": [
        {
          "name": "Temperature",
          "address": "D100",
          "enabled": true,
          "scaling": null,
          "alarm": null
        }
      ],
      "groups": [
        {
          "name": "Press",
          "tags": [
            {
              "name": "Speed",
              "address": "D101",
              "enabled": true
            }
          ]
        }
      ]
    }
  ],
  "rules": null,
  "flowRules": null
}
""");

            ProjectConfig loaded = new ProjectConfigStore().Load(path);

            Assert.False(string.IsNullOrWhiteSpace(loaded.ProjectId));
            Assert.NotNull(loaded.Rules);
            Assert.NotNull(loaded.FlowRules);
            DeviceConfig device = Assert.Single(loaded.Devices);
            Assert.False(string.IsNullOrWhiteSpace(device.Id));
            Assert.Equal(PlcProtocol.VirtualPlc, device.Connection.Protocol);
            Assert.Equal(1000, device.DefaultScanRateMs);
            Assert.Equal(1000, device.FailureRetryDelayMs);
            Assert.Equal(30000, device.MaxFailureRetryDelayMs);
            TagConfig deviceTag = Assert.Single(device.Tags);
            Assert.Equal(device.Id, deviceTag.DeviceId);
            Assert.Equal(string.Empty, deviceTag.GroupId);
            Assert.NotNull(deviceTag.Scaling);
            Assert.NotNull(deviceTag.Alarm);
            GroupConfig group = Assert.Single(device.Groups);
            TagConfig groupTag = Assert.Single(group.Tags);
            Assert.Equal(device.Id, groupTag.DeviceId);
            Assert.Equal(group.Id, groupTag.GroupId);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Save_CreatesDirectoryAndCanReloadConfiguration()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ipc-gateway-config-tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "nested", "gateway-project.json");
        try
        {
            ProjectConfigStore store = new ProjectConfigStore();
            ProjectConfig config = CreateValidProject();

            store.Save(path, config);
            ProjectConfig loaded = store.Load(path);

            Assert.True(File.Exists(path));
            Assert.Equal(config.ProjectId, loaded.ProjectId);
            Assert.Equal(config.Devices[0].Id, loaded.Devices[0].Id);
            Assert.Equal(config.Devices[0].Tags[0].Id, loaded.Devices[0].Tags[0].Id);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Clone_NullSourceReturnsEmptyProject()
    {
        ProjectConfig clone = ProjectConfigCloner.Clone(null!);

        Assert.NotNull(clone.Devices);
        Assert.Empty(clone.Devices);
    }

    private static ProjectConfig CreateValidProject()
    {
        DeviceConfig device = new DeviceConfig
        {
            Name = "Virtual Line",
            Protocol = PlcProtocol.VirtualPlc,
            Connection = new PlcConnectionOptions { Protocol = PlcProtocol.VirtualPlc }
        };
        device.Tags.Add(new TagConfig
        {
            Name = "Temperature",
            Address = "D100",
            Enabled = true
        });

        ProjectConfig config = new ProjectConfig
        {
            Name = "Gateway",
            Devices = new List<DeviceConfig> { device },
            Rules = new List<EdgeRuleConfig>
            {
                new EdgeRuleConfig
                {
                    Name = "High temperature",
                    SourceDeviceName = device.Name,
                    SourceTagName = "Temperature"
                }
            },
            FlowRules = new List<FlowRuleDefinition>
            {
                new FlowRuleDefinition
                {
                    Name = "Flow high temperature"
                }
            }
        };

        return config;
    }
}

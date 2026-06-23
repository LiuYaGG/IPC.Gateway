/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：GatewayHealthEndpointsTests
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
using System.Text.Json;
using IPC.Gateway.Core.Application.Gateway;
using IPC.Gateway.Core.Application.Gateway.Contracts;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.WebHost;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace IPC.Gateway.Tests;

public sealed class GatewayHealthEndpointsTests
{
    [Fact]
    public async Task CreateReadyResult_ReturnsStructuredJson_WhenRuntimeIsUnhealthy()
    {
        FakeGatewayApplicationService gateway = new FakeGatewayApplicationService
        {
            StatusFactory = () => new GatewayRuntimeStatusDto
            {
                IsRunning = false,
                ProjectId = "project-1",
                ProjectName = "Line 1 Gateway",
                ConfigurationStore = "SqlSugar",
                DeviceCount = 2,
                OnlineDeviceCount = 1,
                TagCount = 12,
                GoodTagCount = 10,
                BadTagCount = 1,
                NoDataTagCount = 1,
                ConfigValidation = new ProjectValidationResultDto { IsValid = true },
                Mqtt = new MqttRuntimeStatusDto
                {
                    Enabled = false,
                    OutboxDirectory = "Data/MqttOutbox"
                },
                History = new HistoryStatsDto
                {
                    Enabled = false,
                    Directory = "Data/History"
                },
                RuleEngine = new RuleEngineRuntimeStatusDto
                {
                    Enabled = true,
                    IsRunning = true,
                    RuleCount = 2,
                    EnabledRuleCount = 2
                },
                Scheduler = new RuntimeSchedulerStatusDto
                {
                    HealthStatus = "Healthy",
                    HealthMessage = "Scheduler is healthy.",
                    Queue = new RuntimePollingQueueStatusDto
                    {
                        QueueLimit = 1024,
                        AvailableWorkers = 4
                    },
                    Timeout = new RuntimeTimeoutStatsDto()
                }
            }
        };

        using JsonDocument document = await ExecuteReadyResultAsync(gateway);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, document.RootElement.GetProperty("_statusCode").GetInt32());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Unhealthy", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("Line 1 Gateway", document.RootElement.GetProperty("projectName").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("runtime").GetProperty("deviceCount").GetInt32());

        JsonElement components = document.RootElement.GetProperty("components");
        Assert.Equal(8, components.GetArrayLength());
        Assert.Contains(components.EnumerateArray(), component =>
            component.GetProperty("name").GetString() == "gateway" &&
            component.GetProperty("status").GetString() == "Unhealthy");
        Assert.Contains(components.EnumerateArray(), component =>
            component.GetProperty("name").GetString() == "mqttOutboxStorage" &&
            component.GetProperty("status").GetString() == "Disabled");
    }

    [Fact]
    public async Task CreateReadyResult_ReturnsStructuredJson_WhenStatusReadThrows()
    {
        FakeGatewayApplicationService gateway = new FakeGatewayApplicationService
        {
            StatusFactory = () => throw new InvalidOperationException("status cache is unavailable")
        };

        using JsonDocument document = await ExecuteReadyResultAsync(gateway);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, document.RootElement.GetProperty("_statusCode").GetInt32());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Unhealthy", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("status cache is unavailable", document.RootElement.GetProperty("errorMessage").GetString());

        JsonElement component = Assert.Single(document.RootElement.GetProperty("components").EnumerateArray());
        Assert.Equal("gateway", component.GetProperty("name").GetString());
        Assert.Equal("Unhealthy", component.GetProperty("status").GetString());
        Assert.Equal("status cache is unavailable", component.GetProperty("message").GetString());
    }

    private static async Task<JsonDocument> ExecuteReadyResultAsync(IGatewayApplicationService gateway)
    {
        DefaultHttpContext context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddLogging()
                .ConfigureHttpJsonOptions(_ => { })
                .BuildServiceProvider()
        };
        context.Response.Body = new MemoryStream();

        IResult result = GatewayHealthEndpoints.CreateReadyResult(gateway);
        await result.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        using JsonDocument payload = await JsonDocument.ParseAsync(context.Response.Body);
        using MemoryStream enriched = new MemoryStream();
        using (Utf8JsonWriter writer = new Utf8JsonWriter(enriched))
        {
            writer.WriteStartObject();
            writer.WriteNumber("_statusCode", context.Response.StatusCode);
            foreach (JsonProperty property in payload.RootElement.EnumerateObject())
            {
                property.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return JsonDocument.Parse(enriched.ToArray());
    }

    private sealed class FakeGatewayApplicationService : IGatewayApplicationService
    {
        public Func<GatewayRuntimeStatusDto> StatusFactory { get; init; } = () => new GatewayRuntimeStatusDto();

        public void Dispose()
        {
        }

        public void Start()
        {
            throw new NotSupportedException();
        }

        public void Stop()
        {
            throw new NotSupportedException();
        }

        public GatewayRuntimeStatusDto GetStatus()
        {
            return StatusFactory();
        }

        public MqttRuntimeStatusDto GetMqttStatus()
        {
            throw new NotSupportedException();
        }

        public OpcUaServerRuntimeStatusDto GetOpcUaStatus()
        {
            throw new NotSupportedException();
        }

        public RuleEngineRuntimeStatusDto GetRuleEngineStatus()
        {
            throw new NotSupportedException();
        }

        public HistoryStatsDto GetHistoryStatus()
        {
            throw new NotSupportedException();
        }

        public GatewaySyncDto GetSync()
        {
            throw new NotSupportedException();
        }

        public IList<TagValueSnapshotDto> GetTagSnapshots(RuntimeTagSnapshotQuery query)
        {
            throw new NotSupportedException();
        }

        public IList<GatewayConfigurationVersionDto> GetConfigurationVersions(ConfigurationVersionsQuery query)
        {
            throw new NotSupportedException();
        }

        public GatewayRuntimeStatusDto RollbackConfiguration(RollbackConfigurationCommand command)
        {
            throw new NotSupportedException();
        }

        public GatewayRuntimeStatusDto ApplyConfigurationCommand(RawConfigurationCommand command)
        {
            throw new NotSupportedException();
        }

        public ProjectConfigurationDto GetProject()
        {
            throw new NotSupportedException();
        }

        public ProjectConfigurationDto SaveProject(SaveProjectConfigurationCommand command)
        {
            throw new NotSupportedException();
        }

        public ProjectValidationResultDto ValidateProject(ValidateProjectConfigurationCommand command)
        {
            throw new NotSupportedException();
        }

        public IList<DeviceConfigurationDto> GetDevices()
        {
            throw new NotSupportedException();
        }

        public DeviceConfigurationDto AddDevice(SaveDeviceConfigurationCommand command)
        {
            throw new NotSupportedException();
        }

        public DeviceConfigurationDto UpdateDevice(string deviceId, SaveDeviceConfigurationCommand command)
        {
            throw new NotSupportedException();
        }

        public DeviceConfigurationDto DeleteDevice(string deviceId)
        {
            throw new NotSupportedException();
        }

        public IList<GroupConfigurationDto> GetDeviceGroups(string deviceId)
        {
            throw new NotSupportedException();
        }

        public GroupConfigurationDto AddGroup(string deviceId, SaveGroupConfigurationCommand command)
        {
            throw new NotSupportedException();
        }

        public GroupConfigurationDto UpdateGroup(string groupId, SaveGroupConfigurationCommand command)
        {
            throw new NotSupportedException();
        }

        public GroupConfigurationDto DeleteGroup(string groupId)
        {
            throw new NotSupportedException();
        }

        public IList<TagConfigurationDto> GetDeviceTags(string deviceId)
        {
            throw new NotSupportedException();
        }

        public TagConfigurationDto AddDeviceTag(string deviceId, SaveTagConfigurationCommand command)
        {
            throw new NotSupportedException();
        }

        public IList<TagConfigurationDto> GetGroupTags(string groupId)
        {
            throw new NotSupportedException();
        }

        public TagConfigurationDto AddGroupTag(string groupId, SaveTagConfigurationCommand command)
        {
            throw new NotSupportedException();
        }

        public TagConfigurationDto UpdateTag(string tagId, SaveTagConfigurationCommand command)
        {
            throw new NotSupportedException();
        }

        public TagConfigurationDto DeleteTag(string tagId)
        {
            throw new NotSupportedException();
        }

        public Task<WriteTagResultDto> WriteTagAsync(WriteTagCommand command)
        {
            throw new NotSupportedException();
        }

        public IList<EdgeRuleConfigurationDto> GetRules()
        {
            throw new NotSupportedException();
        }

        public EdgeRuleConfigurationDto AddRule(SaveRuleConfigurationCommand command)
        {
            throw new NotSupportedException();
        }

        public EdgeRuleConfigurationDto UpdateRule(string ruleId, SaveRuleConfigurationCommand command)
        {
            throw new NotSupportedException();
        }

        public EdgeRuleConfigurationDto DeleteRule(string ruleId)
        {
            throw new NotSupportedException();
        }

        public IList<FlowRuleDefinitionDto> GetFlowRules()
        {
            throw new NotSupportedException();
        }

        public FlowRuleDefinitionDto AddFlowRule(SaveFlowRuleDefinitionCommand command)
        {
            throw new NotSupportedException();
        }

        public FlowRuleDefinitionDto UpdateFlowRule(string ruleId, FlowRuleDefinitionDto rule)
        {
            throw new NotSupportedException();
        }

        public FlowRuleDefinitionDto UpdateFlowRule(string ruleId, SaveFlowRuleDefinitionCommand command)
        {
            throw new NotSupportedException();
        }

        public FlowRuleDefinitionDto DeleteFlowRule(string ruleId)
        {
            throw new NotSupportedException();
        }

        public MqttConfigurationDto GetMqttOptions()
        {
            throw new NotSupportedException();
        }

        public MqttConfigurationDto UpdateMqttOptions(SaveMqttConfigurationCommand command)
        {
            throw new NotSupportedException();
        }

        public OpcUaServerConfigurationDto GetOpcUaOptions()
        {
            throw new NotSupportedException();
        }

        public OpcUaServerConfigurationDto UpdateOpcUaOptions(SaveOpcUaServerConfigurationCommand command)
        {
            throw new NotSupportedException();
        }

        public HistoryConfigurationDto GetHistoryOptions()
        {
            throw new NotSupportedException();
        }

        public HistoryConfigurationDto UpdateHistoryOptions(SaveHistoryConfigurationCommand command)
        {
            throw new NotSupportedException();
        }

        public StorageHealthConfigurationDto GetStorageHealthOptions()
        {
            return new StorageHealthConfigurationDto();
        }

        public StorageHealthConfigurationDto UpdateStorageHealthOptions(SaveStorageHealthConfigurationCommand command)
        {
            throw new NotSupportedException();
        }
    }
}

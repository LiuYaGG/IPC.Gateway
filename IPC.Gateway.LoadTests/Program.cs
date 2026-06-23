/*----------------------------------------------------------------
* 项目名称 ：Program
* 项目描述 ：
* 类 名 称 ：LoadTestOptions
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：Program
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
using System.Diagnostics;
using IPC.EdgeGateway;
using IPC.Gateway.Core.Application.Gateway;
using IPC.Gateway.Core.Application.Gateway.Contracts;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Core.Infrastructure.Persistence;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.Infrastructure;
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;
using IPC.Runtime.Values;

LoadTestOptions options = LoadTestOptions.Parse(args);
VerifyProtocolDrivers();
VerifyPluginLifecycle();
VerifyLegacyProtocolPluginPackage();
VerifyMeterFriendlyFields();
VerifyRuntimeStatePersistence();
VerifyConfigurationCrudSmokeTests();
ProjectConfig project = CreateVirtualProject(options);

using RuntimeEngine runtime = new RuntimeEngine(new RuntimeSchedulerOptions
{
    IsolationStrategy = "SemaphoreLimitedPerDeviceQueue",
    MaxConcurrentDevicePolls = options.MaxConcurrentDevicePolls,
    SchedulerIntervalMs = options.SchedulerIntervalMs,
    DevicePollQueueLimit = options.QueueLimit,
    SlowPollThresholdMs = options.SlowPollThresholdMs,
    PollTimeoutMs = options.PollTimeoutMs
});

Console.WriteLine("IPC Gateway virtual device load test");
Console.WriteLine("Protocol drivers verified");
Console.WriteLine("Plugin lifecycle verified");
Console.WriteLine("Legacy protocol plugin verified");
Console.WriteLine("Meter friendly fields verified");
Console.WriteLine("Runtime state persistence verified");
Console.WriteLine("Configuration CRUD smoke tests verified");
Console.WriteLine("Devices={0}, TagsPerDevice={1}, DurationSeconds={2}, Workers={3}",
    options.DeviceCount,
    options.TagsPerDevice,
    options.DurationSeconds,
    options.MaxConcurrentDevicePolls);

Stopwatch stopwatch = Stopwatch.StartNew();
runtime.Start(project);

try
{
    Thread.Sleep(TimeSpan.FromSeconds(options.DurationSeconds));
}
finally
{
    stopwatch.Stop();
}

IList<TagValueSnapshot> snapshots = runtime.GetSnapshots();
IList<DeviceRuntimeStatus> devices = runtime.GetDeviceStatuses();
RuntimeSchedulerStatus scheduler = runtime.GetSchedulerStatus();

long totalReads = devices.Sum(item => item.TotalReads);
long successfulReads = devices.Sum(item => item.SuccessfulReads);
long failedReads = devices.Sum(item => item.FailedReads);
double successRate = totalReads <= 0 ? 0D : Math.Round(successfulReads * 100D / totalReads, 2);
int expectedSnapshots = options.DeviceCount * options.TagsPerDevice;
int goodSnapshots = snapshots.Count(item => item.Quality == TagQuality.Good);
int onlineDevices = devices.Count(item => item.IsConnected || item.SuccessfulReads > 0);

Console.WriteLine("ElapsedMs={0}", stopwatch.ElapsedMilliseconds);
Console.WriteLine("Snapshots={0}/{1}, GoodSnapshots={2}", snapshots.Count, expectedSnapshots, goodSnapshots);
Console.WriteLine("Reads Total={0}, Success={1}, Failed={2}, SuccessRate={3}%",
    totalReads,
    successfulReads,
    failedReads,
    successRate);
Console.WriteLine("Devices Online={0}/{1}", onlineDevices, options.DeviceCount);
Console.WriteLine("Queue Pending={0}, Running={1}, Rejected={2}, MaxPending={3}, Limit={4}",
    scheduler.Queue.PendingCount,
    scheduler.Queue.RunningCount,
    scheduler.Queue.RejectedCount,
    scheduler.Queue.MaxObservedPendingCount,
    scheduler.Queue.QueueLimit);
Console.WriteLine("Tasks Queued={0}, Started={1}, Completed={2}, Failed={3}, Slow={4}",
    scheduler.TotalQueued,
    scheduler.TotalStarted,
    scheduler.TotalCompleted,
    scheduler.TotalFailed,
    scheduler.TotalSlow);
Console.WriteLine("Timeout Poll={0}, Read={1}",
    scheduler.Timeout.PollTimeoutCount,
    scheduler.Timeout.ReadTimeoutCount);

List<string> failures = new List<string>();
if (snapshots.Count != expectedSnapshots)
    failures.Add("snapshot count mismatch");
if (goodSnapshots < expectedSnapshots)
    failures.Add("not all virtual tags reached Good quality");
if (onlineDevices < options.DeviceCount)
    failures.Add("not all virtual devices produced successful reads");
if (successRate < options.MinimumSuccessRate)
    failures.Add("success rate below threshold");
if (scheduler.Queue.RejectedCount > options.MaximumRejectedTasks)
    failures.Add("queue rejected tasks exceeded threshold");
if (scheduler.Timeout.PollTimeoutCount > options.MaximumPollTimeouts)
    failures.Add("poll timeout count exceeded threshold");
if (scheduler.Timeout.ReadTimeoutCount > options.MaximumReadTimeouts)
    failures.Add("read timeout count exceeded threshold");
if (scheduler.TotalSlow > options.MaximumSlowTasks)
    failures.Add("slow task count exceeded threshold");

runtime.Stop();

if (failures.Count == 0)
{
    Console.WriteLine("PASS");
    return 0;
}

Console.Error.WriteLine("FAIL: " + string.Join("; ", failures));
return 1;

static void VerifyProtocolDrivers()
{
    IList<PlcDriverPluginInfo> drivers = PlcDriverPluginRegistry.GetRegisteredDrivers();
    RequireDriver(drivers, "builtin.modbus-tcp", PlcProtocol.ModbusTcp);
    RequireDriver(drivers, "builtin.dlt645-2007", PlcProtocol.Dlt6452007);
    RequireDriver(drivers, "builtin.cjt188-2004", PlcProtocol.Cjt1882004);
    RequireDriver(drivers, "builtin.virtual-plc", PlcProtocol.VirtualPlc);

    VerifyClient(PlcProtocol.ModbusTcp);
    VerifyClient(PlcProtocol.Dlt6452007);
    VerifyClient(PlcProtocol.Cjt1882004);
    VerifyClient(PlcProtocol.VirtualPlc);
    VerifyClientByDriverId("builtin.modbus-tcp", PlcProtocol.ModbusTcp);
    VerifyClientByDriverId("builtin.dlt645-2007", PlcProtocol.Dlt6452007);
    VerifyClientByDriverId("builtin.cjt188-2004", PlcProtocol.Cjt1882004);
}

static void RequireDriver(IList<PlcDriverPluginInfo> drivers, string driverId, PlcProtocol protocol)
{
    bool found = drivers.Any(item =>
        string.Equals(item.DriverId, driverId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(item.Protocol, protocol.ToString(), StringComparison.OrdinalIgnoreCase) &&
        item.BuiltIn);

    if (!found)
        throw new InvalidOperationException("Protocol driver was not registered: " + driverId);
}

static void VerifyClient(PlcProtocol protocol)
{
    using IPlcClient client = PlcClientFactory.Create(new PlcConnectionOptions
    {
        Protocol = protocol,
        Host = "127.0.0.1",
        TimeoutMilliseconds = 100
    });

    if (client.Protocol != protocol)
        throw new InvalidOperationException("Protocol client mismatch: " + protocol);
}

static void VerifyClientByDriverId(string driverId, PlcProtocol expectedProtocol)
{
    using IPlcClient client = PlcClientFactory.Create(new PlcConnectionOptions
    {
        Protocol = PlcProtocol.Plugin,
        DriverId = driverId,
        Host = "127.0.0.1",
        TimeoutMilliseconds = 100
    });

    if (client.Protocol != expectedProtocol)
        throw new InvalidOperationException("Driver client mismatch: " + driverId);
}

static void VerifyPluginLifecycle()
{
    string pluginSource = FindTestPluginAssembly();
    string tempDirectory = Path.Combine(Path.GetTempPath(), "ipc-gateway-plugin-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDirectory);

    string pluginFile = Path.Combine(tempDirectory, Path.GetFileName(pluginSource));
    File.Copy(pluginSource, pluginFile, true);
    string manifestFile = Path.Combine(tempDirectory, Path.GetFileNameWithoutExtension(pluginFile) + ".ipc-driver.json");

    try
    {
        File.WriteAllText(manifestFile, BuildPluginManifest("1.2.3", "0.0.0", "999.0.0"));
        IList<PlcDriverPluginCandidate> candidates = PlcDriverPluginRegistry.DiscoverPlugins(tempDirectory);
        PlcDriverPluginCandidate? candidate = candidates.FirstOrDefault(item => string.Equals(item.AssemblyPath, pluginFile, StringComparison.OrdinalIgnoreCase));
        if (candidate == null)
            throw new InvalidOperationException("Plugin discovery did not return the test plugin.");
        if (!candidate.IsVersionCompatible)
            throw new InvalidOperationException("Compatible plugin manifest was rejected: " + candidate.ErrorMessage);

        PlcDriverPluginLoadResult loadResult = PlcDriverPluginRegistry.LoadPlugin(pluginFile);
        if (!loadResult.Success)
            throw new InvalidOperationException("Plugin load failed: " + loadResult.ErrorMessage);
        if (!loadResult.DriverIds.Contains("test.virtual-plugin", StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Plugin load did not register the test driver.");

        PlcDriverPluginInfo? loadedInfo = PlcDriverPluginRegistry.GetRegisteredDrivers()
            .FirstOrDefault(item => string.Equals(item.DriverId, "test.virtual-plugin", StringComparison.OrdinalIgnoreCase));
        if (loadedInfo == null || loadedInfo.BuiltIn || loadedInfo.Version != "1.2.3")
            throw new InvalidOperationException("Loaded plugin metadata is invalid.");

        using (IPlcClient client = PlcClientFactory.Create(new PlcConnectionOptions
        {
            Protocol = PlcProtocol.Plugin,
            DriverId = "test.virtual-plugin",
            Host = "plugin-lifecycle-test"
        }))
        {
            if (client.Protocol != PlcProtocol.VirtualPlc)
                throw new InvalidOperationException("Plugin-created client protocol is invalid.");
        }

        if (!PlcDriverPluginRegistry.UnloadPlugin("test.virtual-plugin"))
            throw new InvalidOperationException("Plugin unload returned false.");
        if (PlcDriverPluginRegistry.GetRegisteredDrivers().Any(item => string.Equals(item.DriverId, "test.virtual-plugin", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Plugin driver remains registered after unload.");

        File.WriteAllText(manifestFile, BuildPluginManifest("1.2.3", "0.0.0", "0.0.0"));
        candidate = PlcDriverPluginRegistry.DiscoverPlugin(pluginFile);
        if (candidate.IsVersionCompatible)
            throw new InvalidOperationException("Incompatible plugin manifest was accepted.");
        loadResult = PlcDriverPluginRegistry.LoadPlugin(pluginFile);
        if (loadResult.Success)
            throw new InvalidOperationException("Incompatible plugin was loaded.");
    }
    finally
    {
        try
        {
            PlcDriverPluginRegistry.UnloadPlugin("test.virtual-plugin");
            Directory.Delete(tempDirectory, true);
        }
        catch
        {
        }
    }
}

static void VerifyLegacyProtocolPluginPackage()
{
    string pluginFile = FindWebHostDriverAssembly("IPC.Gateway.LegacyProtocolPlugins");
    PlcDriverPluginCandidate candidate = PlcDriverPluginRegistry.DiscoverPlugin(pluginFile);
    if (!candidate.IsVersionCompatible)
        throw new InvalidOperationException("Legacy protocol plugin manifest was rejected: " + candidate.ErrorMessage);

    string pluginDirectory = Path.GetDirectoryName(pluginFile) ?? string.Empty;
    IList<PlcDriverPluginLoadResult> loadResults = PlcDriverPluginRegistry.LoadPluginsFromDirectory(pluginDirectory);
    PlcDriverPluginLoadResult? loadResult = loadResults.FirstOrDefault(item =>
        string.Equals(item.AssemblyPath, pluginFile, StringComparison.OrdinalIgnoreCase));
    if (loadResult == null || !loadResult.Success)
        throw new InvalidOperationException("Legacy protocol plugin load failed: " + (loadResult == null ? "no load result" : loadResult.ErrorMessage));

    try
    {
        RequirePluginDriver(loadResult, "legacy.rockwell-cip", PlcProtocol.RockwellCip);
        RequirePluginDriver(loadResult, "legacy.siemens-s7", PlcProtocol.SiemensS7);
        RequirePluginDriver(loadResult, "legacy.mitsubishi-mc", PlcProtocol.MitsubishiMc);
        RequirePluginDriver(loadResult, "legacy.mitsubishi-mc-1e", PlcProtocol.MitsubishiMc1E);
        RequirePluginDriver(loadResult, "legacy.mitsubishi-serial", PlcProtocol.MitsubishiSerial);
        RequirePluginDriver(loadResult, "legacy.mitsubishi-ql-serial", PlcProtocol.MitsubishiQlSerial);
        RequirePluginDriver(loadResult, "legacy.omron-fins", PlcProtocol.OmronFins);
        RequirePluginDriver(loadResult, "legacy.modbus-rtu", PlcProtocol.ModbusRtu);
        RequirePluginDriver(loadResult, "legacy.opc-ua", PlcProtocol.OpcUa);
        RequirePluginDriver(loadResult, "legacy.opc-da", PlcProtocol.OpcDa);

        VerifyLegacyClientByProtocol(PlcProtocol.RockwellCip);
        VerifyLegacyClientByProtocol(PlcProtocol.SiemensS7);
        VerifyLegacyClientByProtocol(PlcProtocol.MitsubishiMc);
        VerifyLegacyClientByProtocol(PlcProtocol.MitsubishiMc1E);
        VerifyLegacyClientByProtocol(PlcProtocol.MitsubishiSerial);
        VerifyLegacyClientByProtocol(PlcProtocol.MitsubishiQlSerial);
        VerifyLegacyClientByProtocol(PlcProtocol.OmronFins);
        VerifyLegacyClientByProtocol(PlcProtocol.ModbusRtu);
        VerifyLegacyClientByProtocol(PlcProtocol.OpcUa);
        VerifyLegacyClientByProtocol(PlcProtocol.OpcDa);

        VerifyClientByDriverId("legacy.rockwell-cip", PlcProtocol.RockwellCip);
        VerifyClientByDriverId("legacy.siemens-s7", PlcProtocol.SiemensS7);
        VerifyClientByDriverId("legacy.mitsubishi-mc", PlcProtocol.MitsubishiMc);
        VerifyClientByDriverId("legacy.mitsubishi-mc-1e", PlcProtocol.MitsubishiMc1E);
        VerifyClientByDriverId("legacy.mitsubishi-serial", PlcProtocol.MitsubishiSerial);
        VerifyClientByDriverId("legacy.mitsubishi-ql-serial", PlcProtocol.MitsubishiQlSerial);
        VerifyClientByDriverId("legacy.omron-fins", PlcProtocol.OmronFins);
        VerifyClientByDriverId("legacy.modbus-rtu", PlcProtocol.ModbusRtu);
        VerifyClientByDriverId("legacy.opc-ua", PlcProtocol.OpcUa);
        VerifyClientByDriverId("legacy.opc-da", PlcProtocol.OpcDa);
    }
    finally
    {
        PlcDriverPluginRegistry.UnloadPlugin("legacy.rockwell-cip");
    }
}

static void RequirePluginDriver(PlcDriverPluginLoadResult loadResult, string driverId, PlcProtocol protocol)
{
    if (!loadResult.DriverIds.Contains(driverId, StringComparer.OrdinalIgnoreCase))
        throw new InvalidOperationException("Legacy plugin did not register driver: " + driverId);

    PlcDriverPluginInfo? driver = PlcDriverPluginRegistry.GetRegisteredDrivers()
        .FirstOrDefault(item => string.Equals(item.DriverId, driverId, StringComparison.OrdinalIgnoreCase));
    if (driver == null ||
        driver.BuiltIn ||
        !string.Equals(driver.Protocol, protocol.ToString(), StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Legacy plugin driver metadata is invalid: " + driverId);
    }
}

static void VerifyLegacyClientByProtocol(PlcProtocol protocol)
{
    using IPlcClient client = PlcClientFactory.Create(new PlcConnectionOptions
    {
        Protocol = protocol,
        Host = "127.0.0.1",
        TimeoutMilliseconds = 100
    });

    if (client.Protocol != protocol)
        throw new InvalidOperationException("Legacy plugin client mismatch: " + protocol);
}

static void VerifyMeterFriendlyFields()
{
    DeviceConfig dltDevice = new DeviceConfig
    {
        Id = "dlt-device",
        Name = "DLT645 meter",
        Protocol = PlcProtocol.Dlt6452007
    };
    dltDevice.Tags.Add(new TagConfig
    {
        Id = "dlt-tag",
        DeviceId = dltDevice.Id,
        Name = "Forward active energy",
        MeterAddress = "010203040506",
        MeterDataIdentifier = "00010000",
        MeterType = "Electric"
    });

    DeviceConfig cjtDevice = new DeviceConfig
    {
        Id = "cjt-device",
        Name = "CJT188 meter",
        Protocol = PlcProtocol.Cjt1882004
    };
    GroupConfig cjtGroup = new GroupConfig
    {
        Id = "cjt-group",
        DeviceId = cjtDevice.Id,
        Name = "Water meters"
    };
    cjtGroup.Tags.Add(new TagConfig
    {
        Id = "cjt-tag",
        DeviceId = cjtDevice.Id,
        GroupId = cjtGroup.Id,
        Name = "Accumulated flow",
        MeterAddress = "12345678",
        MeterDataIdentifier = "901F",
        MeterType = "Water"
    });
    cjtDevice.Groups.Add(cjtGroup);

    ProjectConfig project = new ProjectConfig
    {
        ProjectId = "meter-friendly-fields",
        Name = "Meter Friendly Fields"
    };
    project.Devices.Add(dltDevice);
    project.Devices.Add(cjtDevice);

    ProjectConfigurationDto dto = GatewayConfigurationContractMapper.ToDto(project);
    RequireFriendlyTag(dto.Devices[0].Tags[0], "Dlt6452007", "010203040506", "00010000", "Electric");
    RequireFriendlyTag(dto.Devices[1].Groups[0].Tags[0], "Cjt1882004", "12345678", "901F", "Water");

    ProjectConfig roundTrip = GatewayConfigurationContractMapper.ToConfig(dto);
    RequireMeterConfigFromTag(roundTrip.Devices[0].Tags[0], "010203040506", "00010000", "Electric");
    RequireMeterConfigFromTag(roundTrip.Devices[1].Groups[0].Tags[0], "12345678", "901F", "Water");
}

static void VerifyRuntimeStatePersistence()
{
    string databasePath = Path.Combine(Path.GetTempPath(), "ipc-gateway-runtime-state-" + Guid.NewGuid().ToString("N") + ".db");
    try
    {
        SqlSugarRuntimeStateRepository repository = new SqlSugarRuntimeStateRepository(new GatewayDatabaseOptions
        {
            Provider = "Sqlite",
            Database = databasePath,
            AutoCreateDatabase = true
        });

        string projectId = "runtime-state-persistence";
        GatewayRuntimeStateSnapshot snapshot = new GatewayRuntimeStateSnapshot
        {
            Devices = new List<DeviceRuntimeStatus>
            {
                new DeviceRuntimeStatus
                {
                    DeviceId = "device-1",
                    DeviceName = "virtual-device-1",
                    Protocol = PlcProtocol.VirtualPlc.ToString(),
                    Enabled = true,
                    IsConnected = true,
                    Status = "Online",
                    TotalReads = 10,
                    SuccessfulReads = 9,
                    FailedReads = 1,
                    SuccessRate = 90D,
                    LastSuccessTime = DateTime.Now,
                    LastError = "last transient error"
                }
            },
            Tags = new List<TagValueSnapshot>
            {
                new TagValueSnapshot
                {
                    DeviceId = "device-1",
                    DeviceName = "virtual-device-1",
                    TagId = "tag-1",
                    TagName = "temperature",
                    DataType = PlcDataType.Int16.ToString(),
                    RawValue = "42",
                    RawValueText = "42",
                    Value = "42",
                    ValueText = "42",
                    Quality = TagQuality.Good,
                    Timestamp = DateTime.Now,
                    Source = "RuntimeStatePersistenceTest"
                }
            },
            RecentErrors = new List<RuntimeErrorDetail>
            {
                new RuntimeErrorDetail
                {
                    Category = "TagRead",
                    DeviceName = "virtual-device-1",
                    TagName = "temperature",
                    Message = "read failed once",
                    Suggestion = "retry",
                    Source = "RuntimeStatePersistenceTest",
                    Timestamp = DateTime.Now
                }
            }
        };

        repository.Save(projectId, snapshot);
        GatewayRuntimeStateSnapshot loaded = repository.Load(projectId);

        DeviceRuntimeStatus? device = loaded.Devices.FirstOrDefault(item => item.DeviceId == "device-1");
        if (device == null || !device.IsConnected || device.SuccessRate != 90D || device.LastError != "last transient error")
            throw new InvalidOperationException("Runtime device status was not persisted.");

        TagValueSnapshot? tag = loaded.Tags.FirstOrDefault(item => item.TagId == "tag-1");
        if (tag == null || tag.ValueText != "42" || tag.Quality != TagQuality.Good)
            throw new InvalidOperationException("Runtime tag snapshot was not persisted.");

        RuntimeErrorDetail? error = loaded.RecentErrors.FirstOrDefault(item => item.Message == "read failed once");
        if (error == null || error.Category != "TagRead")
            throw new InvalidOperationException("Runtime recent error was not persisted.");
    }
    finally
    {
        try
        {
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
        catch
        {
        }
    }
}

static void VerifyConfigurationCrudSmokeTests()
{
    string tempDirectory = Path.Combine(Path.GetTempPath(), "ipc-gateway-crud-smoke-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDirectory);
    string databasePath = Path.Combine(tempDirectory, "crud-smoke.db");

    try
    {
        using GatewayCoreService gateway = new GatewayCoreService(
            new GatewayRuntimeOptions
            {
                AutoCreateDefaultProject = false,
                Database = new GatewayDatabaseOptions
                {
                    Provider = "Sqlite",
                    Database = databasePath,
                    AutoCreateDatabase = true
                }
            },
            new MqttGatewayOptions { Enabled = false },
            new OpcUaServerOptions { Enabled = false },
            new LocalHistoryOptions
            {
                Enabled = false,
                Directory = Path.Combine(tempDirectory, "history")
            },
            new StorageHealthThresholds());

        GatewayApplicationService app = new GatewayApplicationService(
            gateway,
            new GatewayProjectApplicationService(gateway),
            new GatewayDeviceConfigurationApplicationService(gateway),
            new GatewayRuleConfigurationApplicationService(gateway),
            new GatewayMqttConfigurationApplicationService(gateway),
            new GatewayOpcUaConfigurationApplicationService(gateway),
            new GatewayHistoryConfigurationApplicationService(gateway));

        DeviceConfigurationDto createdDevice = app.AddDevice(CreateDeviceCommand("crud-device", "VirtualPlc"));
        Require(createdDevice.Id.Length > 0, "Device create did not return an id.");
        Require(app.GetProject().Devices.Count == 1, "Device create did not persist.");

        SaveDeviceConfigurationCommand updateDevice = CreateDeviceCommand("crud-device-updated", "VirtualPlc");
        updateDevice.Enabled = false;
        updateDevice.DefaultScanRateMs = 1500;
        DeviceConfigurationDto updatedDevice = app.UpdateDevice(createdDevice.Id, updateDevice);
        Require(updatedDevice.Name == "crud-device-updated" && !updatedDevice.Enabled && updatedDevice.DefaultScanRateMs == 1500, "Device update did not persist.");

        GroupConfigurationDto createdGroup = app.AddGroup(createdDevice.Id, new SaveGroupConfigurationCommand
        {
            Name = "crud-group",
            Enabled = true,
            ScanRateMs = 700
        });
        Require(createdGroup.Id.Length > 0 && app.GetDeviceGroups(createdDevice.Id).Count == 1, "Group create did not persist.");

        GroupConfigurationDto updatedGroup = app.UpdateGroup(createdGroup.Id, new SaveGroupConfigurationCommand
        {
            Name = "crud-group-updated",
            Enabled = false,
            ScanRateMs = 900
        });
        Require(updatedGroup.Name == "crud-group-updated" && !updatedGroup.Enabled && updatedGroup.ScanRateMs == 900, "Group update did not persist.");

        TagConfigurationDto directTag = app.AddDeviceTag(createdDevice.Id, CreateTagCommand("direct-tag", "D100"));
        Require(directTag.Id.Length > 0 && app.GetDeviceTags(createdDevice.Id).Count == 1, "Device tag create did not persist.");

        TagConfigurationDto updatedDirectTag = app.UpdateTag(directTag.Id, CreateTagCommand("direct-tag-updated", "D101"));
        Require(updatedDirectTag.Name == "direct-tag-updated" && updatedDirectTag.Address == "D101", "Device tag update did not persist.");

        TagConfigurationDto groupTag = app.AddGroupTag(createdGroup.Id, CreateTagCommand("group-tag", "D200"));
        Require(groupTag.Id.Length > 0 && app.GetGroupTags(createdGroup.Id).Count == 1, "Group tag create did not persist.");

        app.DeleteTag(updatedDirectTag.Id);
        Require(app.GetDeviceTags(createdDevice.Id).Count == 0, "Tag delete did not persist.");

        app.DeleteGroup(createdGroup.Id);
        Require(app.GetDeviceGroups(createdDevice.Id).Count == 0, "Group delete did not persist.");
        Require(!app.GetStatus().Tags.Any(item => item.TagId == groupTag.Id), "Group delete left a runtime tag snapshot.");

        app.DeleteDevice(createdDevice.Id);
        Require(app.GetProject().Devices.Count == 0, "Device delete did not persist.");
    }
    finally
    {
        try
        {
            Directory.Delete(tempDirectory, true);
        }
        catch
        {
        }
    }
}

static SaveDeviceConfigurationCommand CreateDeviceCommand(string name, string protocol)
{
    return new SaveDeviceConfigurationCommand
    {
        Name = name,
        Enabled = true,
        Protocol = protocol,
        Connection = new PlcConnectionDto
        {
            Protocol = protocol,
            Host = "default",
            TimeoutMilliseconds = 3000,
            WordOrder = "HighWordFirst",
            Transport = "Tcp",
            DataBits = 8,
            SerialParity = "None",
            SerialStopBits = "One"
        },
        DefaultScanRateMs = 1000,
        FailureRetryDelayMs = 1000,
        MaxFailureRetryDelayMs = 30000
    };
}

static SaveTagConfigurationCommand CreateTagCommand(string name, string address)
{
    return new SaveTagConfigurationCommand
    {
        Name = name,
        Protocol = "VirtualPlc",
        Address = address,
        DataType = "Int16",
        ElementCount = 1,
        Enabled = true,
        AccessMode = "ReadWrite",
        ScanRateMs = 500,
        FailureRetryDelayMs = 300,
        Source = "CrudSmoke",
        PointCode = "crud." + name
    };
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void RequireFriendlyTag(TagConfigurationDto tag, string protocol, string meterAddress, string dataIdentifier, string meterType)
{
    if (!string.Equals(tag.Protocol, protocol, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Meter tag protocol was not preserved for UI.");
    RequireMeterConfigValues(tag.MeterAddress, tag.MeterDataIdentifier, tag.MeterType, meterAddress, dataIdentifier, meterType);
}

static void RequireMeterConfigFromTag(TagConfig tag, string meterAddress, string dataIdentifier, string meterType)
{
    RequireMeterConfigValues(tag.MeterAddress, tag.MeterDataIdentifier, tag.MeterType, meterAddress, dataIdentifier, meterType);
}

static void RequireMeterConfigValues(string actualAddress, string actualIdentifier, string actualType, string expectedAddress, string expectedIdentifier, string expectedType)
{
    if (!string.Equals(actualAddress, expectedAddress, StringComparison.Ordinal) ||
        !string.Equals(actualIdentifier, expectedIdentifier, StringComparison.Ordinal) ||
        !string.Equals(actualType, expectedType, StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Meter friendly fields were not preserved.");
    }
}

static string BuildPluginManifest(string version, string minGatewayVersion, string maxGatewayVersion)
{
    return "{" +
           "\"driverId\":\"test.virtual-plugin\"," +
           "\"displayName\":\"Test Virtual Protocol Plugin\"," +
           "\"version\":\"" + version + "\"," +
           "\"minGatewayVersion\":\"" + minGatewayVersion + "\"," +
           "\"maxGatewayVersion\":\"" + maxGatewayVersion + "\"," +
           "\"entryType\":\"IPC.Gateway.TestProtocolPlugin.TestVirtualProtocolDriver\"" +
           "}";
}

static string FindTestPluginAssembly()
{
    return FindProjectAssembly("IPC.Gateway.TestProtocolPlugin");
}

static string FindProjectAssembly(string projectName)
{
    string baseDirectory = AppContext.BaseDirectory;
    string configuration = new DirectoryInfo(Path.GetFullPath(Path.Combine(baseDirectory, ".."))).Name;
    DirectoryInfo? current = new DirectoryInfo(baseDirectory);
    while (current != null)
    {
        string candidate = Path.Combine(
            current.FullName,
            projectName,
            "bin",
            configuration,
            "net10.0",
            projectName + ".dll");
        if (File.Exists(candidate))
            return candidate;

        current = current.Parent;
    }

    throw new FileNotFoundException("Project assembly was not found: " + projectName);
}

static string FindWebHostDriverAssembly(string assemblyName)
{
    string baseDirectory = AppContext.BaseDirectory;
    string configuration = new DirectoryInfo(Path.GetFullPath(Path.Combine(baseDirectory, ".."))).Name;
    DirectoryInfo? current = new DirectoryInfo(baseDirectory);
    while (current != null)
    {
        string candidate = Path.Combine(
            current.FullName,
            "IPC.Gateway.WebHost",
            "bin",
            configuration,
            "net10.0",
            "Drivers",
            assemblyName + ".dll");
        if (File.Exists(candidate))
            return candidate;

        current = current.Parent;
    }

    throw new FileNotFoundException("WebHost driver assembly was not found: " + assemblyName);
}

static ProjectConfig CreateVirtualProject(LoadTestOptions options)
{
    ProjectConfig project = new ProjectConfig
    {
        Name = "Virtual PLC Load Test"
    };

    for (int deviceIndex = 0; deviceIndex < options.DeviceCount; deviceIndex++)
    {
        DeviceConfig device = new DeviceConfig
        {
            Name = "virtual-device-" + deviceIndex.ToString("D4"),
            Protocol = PlcProtocol.VirtualPlc,
            DefaultScanRateMs = options.DeviceScanRateMs,
            FailureRetryDelayMs = 100,
            MaxFailureRetryDelayMs = 1000,
            Connection = new PlcConnectionOptions
            {
                Protocol = PlcProtocol.VirtualPlc,
                Host = "load-test-" + deviceIndex.ToString("D4"),
                TimeoutMilliseconds = options.PollTimeoutMs
            }
        };

        for (int tagIndex = 0; tagIndex < options.TagsPerDevice; tagIndex++)
        {
            device.Tags.Add(new TagConfig
            {
                DeviceId = device.Id,
                Name = "tag-" + tagIndex.ToString("D4"),
                Address = "D" + tagIndex,
                DataType = PlcDataType.Int16,
                AccessMode = TagAccessMode.ReadWrite,
                ScanRateMs = options.TagScanRateMs,
                FailureRetryDelayMs = 100,
                Source = "VirtualPlcLoadTest",
                PointCode = device.Name + ".tag-" + tagIndex.ToString("D4")
            });
        }

        project.Devices.Add(device);
    }

    return project;
}

internal sealed class LoadTestOptions
{
    public int DeviceCount { get; private set; } = 64;
    public int TagsPerDevice { get; private set; } = 16;
    public int DurationSeconds { get; private set; } = 5;
    public int MaxConcurrentDevicePolls { get; private set; } = 16;
    public int SchedulerIntervalMs { get; private set; } = 50;
    public int QueueLimit { get; private set; } = 4096;
    public int DeviceScanRateMs { get; private set; } = 100;
    public int TagScanRateMs { get; private set; } = 100;
    public int SlowPollThresholdMs { get; private set; } = 1000;
    public int PollTimeoutMs { get; private set; } = 3000;
    public double MinimumSuccessRate { get; private set; } = 99D;
    public long MaximumRejectedTasks { get; private set; } = 0;
    public long MaximumPollTimeouts { get; private set; } = 0;
    public long MaximumReadTimeouts { get; private set; } = 0;
    public long MaximumSlowTasks { get; private set; } = 0;

    public static LoadTestOptions Parse(string[] args)
    {
        LoadTestOptions options = new LoadTestOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i].TrimStart('-', '/');
            string value = i + 1 < args.Length ? args[i + 1] : string.Empty;
            if (string.IsNullOrWhiteSpace(value) || value.StartsWith("-") || value.StartsWith("/"))
                continue;

            if (SetOption(options, key, value))
                i++;
        }

        options.DeviceCount = Clamp(options.DeviceCount, 1, 10000);
        options.TagsPerDevice = Clamp(options.TagsPerDevice, 1, 10000);
        options.DurationSeconds = Clamp(options.DurationSeconds, 1, 3600);
        options.MaxConcurrentDevicePolls = Clamp(options.MaxConcurrentDevicePolls, 1, 256);
        options.SchedulerIntervalMs = Clamp(options.SchedulerIntervalMs, 20, 60000);
        options.QueueLimit = Clamp(options.QueueLimit, 1, 100000);
        options.DeviceScanRateMs = Clamp(options.DeviceScanRateMs, 20, 86400000);
        options.TagScanRateMs = Clamp(options.TagScanRateMs, 20, 86400000);
        options.SlowPollThresholdMs = Clamp(options.SlowPollThresholdMs, 100, 86400000);
        options.PollTimeoutMs = Clamp(options.PollTimeoutMs, 100, 86400000);
        return options;
    }

    private static bool SetOption(LoadTestOptions options, string key, string value)
    {
        string normalized = key.Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
        switch (normalized)
        {
            case "devices":
            case "devicecount":
                options.DeviceCount = ReadInt(value, options.DeviceCount);
                return true;
            case "tags":
            case "tagsperdevice":
                options.TagsPerDevice = ReadInt(value, options.TagsPerDevice);
                return true;
            case "duration":
            case "durationseconds":
                options.DurationSeconds = ReadInt(value, options.DurationSeconds);
                return true;
            case "workers":
            case "maxconcurrentdevicepolls":
                options.MaxConcurrentDevicePolls = ReadInt(value, options.MaxConcurrentDevicePolls);
                return true;
            case "schedulerintervalms":
                options.SchedulerIntervalMs = ReadInt(value, options.SchedulerIntervalMs);
                return true;
            case "queuelimit":
                options.QueueLimit = ReadInt(value, options.QueueLimit);
                return true;
            case "devicescanratems":
                options.DeviceScanRateMs = ReadInt(value, options.DeviceScanRateMs);
                return true;
            case "tagscanratems":
                options.TagScanRateMs = ReadInt(value, options.TagScanRateMs);
                return true;
            case "slowpollthresholdms":
                options.SlowPollThresholdMs = ReadInt(value, options.SlowPollThresholdMs);
                return true;
            case "polltimeoutms":
                options.PollTimeoutMs = ReadInt(value, options.PollTimeoutMs);
                return true;
            case "minimumsuccessrate":
            case "minsuccessrate":
                options.MinimumSuccessRate = ReadDouble(value, options.MinimumSuccessRate);
                return true;
            case "maximumrejectedtasks":
            case "maxrejectedtasks":
                options.MaximumRejectedTasks = ReadLong(value, options.MaximumRejectedTasks);
                return true;
            case "maximumpolltimeouts":
            case "maxpolltimeouts":
                options.MaximumPollTimeouts = ReadLong(value, options.MaximumPollTimeouts);
                return true;
            case "maximumreadtimeouts":
            case "maxreadtimeouts":
                options.MaximumReadTimeouts = ReadLong(value, options.MaximumReadTimeouts);
                return true;
            case "maximumslowtasks":
            case "maxslowtasks":
                options.MaximumSlowTasks = ReadLong(value, options.MaximumSlowTasks);
                return true;
            default:
                return false;
        }
    }

    private static int ReadInt(string value, int defaultValue)
    {
        return int.TryParse(value, out int parsed) ? parsed : defaultValue;
    }

    private static long ReadLong(string value, long defaultValue)
    {
        return long.TryParse(value, out long parsed) ? parsed : defaultValue;
    }

    private static double ReadDouble(string value, double defaultValue)
    {
        return double.TryParse(value, out double parsed) ? parsed : defaultValue;
    }

    private static int Clamp(int value, int minValue, int maxValue)
    {
        if (value < minValue)
            return minValue;
        if (value > maxValue)
            return maxValue;
        return value;
    }
}

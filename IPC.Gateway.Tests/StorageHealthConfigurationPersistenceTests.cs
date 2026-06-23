/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：StorageHealthConfigurationPersistenceTests
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
using IPC.Gateway.Core.Application.Gateway.Contracts;
using IPC.Gateway.Core.Domain.Configuration;
using IPC.Gateway.Core.Gateway;

namespace IPC.Gateway.Tests;

public sealed class StorageHealthConfigurationPersistenceTests : IDisposable
{
    private readonly string _rootDirectory;
    private readonly string _databasePath;

    public StorageHealthConfigurationPersistenceTests()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), "ipc-gateway-tests", Guid.NewGuid().ToString("N"));
        _databasePath = Path.Combine(_rootDirectory, "gateway.db");
    }

    [Fact]
    public void ContractMapper_NormalizesStorageHealthCommand()
    {
        SaveStorageHealthConfigurationCommand command = new SaveStorageHealthConfigurationCommand
        {
            DegradedAvailableMegabytes = 128,
            UnhealthyAvailableMegabytes = 256,
            DegradedAvailablePercent = 1,
            UnhealthyAvailablePercent = 2
        };

        StorageHealthThresholds thresholds = GatewayConfigurationContractMapper.ToConfig(command);
        StorageHealthConfigurationDto dto = GatewayConfigurationContractMapper.ToDto(thresholds);

        Assert.Equal(256L * 1024L * 1024L, thresholds.DegradedAvailableBytes);
        Assert.Equal(256L * 1024L * 1024L, thresholds.UnhealthyAvailableBytes);
        Assert.Equal(2D, thresholds.DegradedAvailablePercent);
        Assert.Equal(2D, thresholds.UnhealthyAvailablePercent);
        Assert.Equal(256D, dto.DegradedAvailableMegabytes);
        Assert.Equal(256D, dto.UnhealthyAvailableMegabytes);
        Assert.Equal(2D, dto.DegradedAvailablePercent);
        Assert.Equal(2D, dto.UnhealthyAvailablePercent);
    }

    [Fact]
    public void GatewayCoreService_PersistsStorageHealthThresholdsAcrossInstances()
    {
        using (GatewayCoreService gateway = CreateGateway(new StorageHealthThresholds()))
        {
            gateway.UpdateStorageHealthThresholds(new StorageHealthThresholds
            {
                DegradedAvailableBytes = 2048L * 1024L * 1024L,
                UnhealthyAvailableBytes = 512L * 1024L * 1024L,
                DegradedAvailablePercent = 15D,
                UnhealthyAvailablePercent = 4D
            });

            StorageHealthThresholds current = gateway.CurrentStorageHealthThresholds;
            Assert.Equal(2048L * 1024L * 1024L, current.DegradedAvailableBytes);
            Assert.Equal(512L * 1024L * 1024L, current.UnhealthyAvailableBytes);
            Assert.Equal(15D, current.DegradedAvailablePercent);
            Assert.Equal(4D, current.UnhealthyAvailablePercent);

            IList<GatewayConfigurationVersionInfo> versions = gateway.GetConfigurationVersions(GatewayConfigurationType.StorageHealth, 10);
            Assert.Equal(2, versions.Count);
            Assert.Equal(2, versions[0].Version);
            Assert.True(versions[0].Active);
            Assert.Equal(1, versions[1].Version);
            Assert.False(versions[1].Active);
        }

        using (GatewayCoreService gateway = CreateGateway(new StorageHealthThresholds
        {
            DegradedAvailableBytes = 4096L * 1024L * 1024L,
            UnhealthyAvailableBytes = 1024L * 1024L * 1024L,
            DegradedAvailablePercent = 20D,
            UnhealthyAvailablePercent = 5D
        }))
        {
            StorageHealthThresholds reloaded = gateway.CurrentStorageHealthThresholds;
            Assert.Equal(2048L * 1024L * 1024L, reloaded.DegradedAvailableBytes);
            Assert.Equal(512L * 1024L * 1024L, reloaded.UnhealthyAvailableBytes);
            Assert.Equal(15D, reloaded.DegradedAvailablePercent);
            Assert.Equal(4D, reloaded.UnhealthyAvailablePercent);
        }
    }

    public void Dispose()
    {
        DeleteTemporaryDirectory();
    }

    private GatewayCoreService CreateGateway(StorageHealthThresholds storageHealthThresholds)
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
            storageHealthThresholds);
    }

    private void DeleteTemporaryDirectory()
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
}

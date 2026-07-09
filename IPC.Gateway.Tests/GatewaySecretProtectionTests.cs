/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：GatewaySecretProtectionTests
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
using IPC.Gateway.Core.Domain.Users;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Core.Infrastructure.Persistence;
using IPC.Gateway.WebHost;
using IPC.Plc.Communication.Core;
using IPC.Runtime.Configuration;
using Microsoft.AspNetCore.Http;

namespace IPC.Gateway.Tests;

public sealed class GatewaySecretProtectionTests : IDisposable
{
    private readonly string _rootDirectory;
    private readonly string _databasePath;

    public GatewaySecretProtectionTests()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), "ipc-gateway-secret-tests", Guid.NewGuid().ToString("N"));
        _databasePath = Path.Combine(_rootDirectory, "gateway.db");
    }

    [Fact]
    public void SecretProtector_RoundTripsEncryptedValue()
    {
        GatewaySecretProtector protector = new GatewaySecretProtector(new GatewaySecretProtectionOptions
        {
            Enabled = true,
            MasterKey = "unit-test-master-key"
        });

        string encrypted = protector.Protect("Device#12345");
        string decrypted = protector.Unprotect(encrypted);

        Assert.StartsWith(GatewaySecretProtector.Prefix, encrypted);
        Assert.NotEqual("Device#12345", encrypted);
        Assert.Equal("Device#12345", decrypted);
    }

    [Fact]
    public void ConfigurationRepository_StoresSecretsEncryptedAndLoadsPlainValues()
    {
        GatewayDatabaseOptions options = CreateOptions();
        SqlSugarGatewayConfigurationRepository repository = new SqlSugarGatewayConfigurationRepository(
            options,
            new GatewaySecretProtectionOptions
            {
                Enabled = true,
                MasterKey = "repository-test-master-key"
            });

        ProjectConfig project = CreateProject();
        MqttGatewayOptions mqtt = new MqttGatewayOptions
        {
            Password = "Mqtt#12345",
            ClientCertificatePassword = "MqttCert#12345",
            ClientCertificatePath = "Data/Certificates/mqtt-client.pfx"
        };

        repository.SaveProject(project, "Test", "Save encrypted project");
        repository.SaveMqtt(mqtt, "Test", "Save encrypted mqtt");

        using SqlSugar.ISqlSugarClient db = new SqlSugarConnectionFactory(options).Create();
        string projectPayload = db.Queryable<GatewayConfigurationEntity>()
            .Where(item => item.ConfigType == GatewayConfigurationType.Project && item.Active)
            .First()
            .Payload;
        string mqttPayload = db.Queryable<GatewayConfigurationEntity>()
            .Where(item => item.ConfigType == GatewayConfigurationType.Mqtt && item.Active)
            .First()
            .Payload;

        Assert.DoesNotContain("Device#12345", projectPayload);
        Assert.DoesNotContain("DeviceCert#12345", projectPayload);
        Assert.DoesNotContain("Mqtt#12345", mqttPayload);
        Assert.DoesNotContain("MqttCert#12345", mqttPayload);
        Assert.Contains(GatewaySecretProtector.Prefix, projectPayload);
        Assert.Contains(GatewaySecretProtector.Prefix, mqttPayload);

        ProjectConfig loadedProject = repository.LoadProject();
        MqttGatewayOptions loadedMqtt = repository.LoadOrCreateMqtt(new MqttGatewayOptions());

        Assert.Equal("Device#12345", loadedProject.Devices[0].Connection.Password);
        Assert.Equal("DeviceCert#12345", loadedProject.Devices[0].Connection.CertificatePassword);
        Assert.Equal("Mqtt#12345", loadedMqtt.Password);
        Assert.Equal("MqttCert#12345", loadedMqtt.ClientCertificatePassword);
    }

    [Fact]
    public void ConfigurationRepository_StoresOpcUaPasswordHashWithoutPlainText()
    {
        GatewayDatabaseOptions options = CreateOptions();
        SqlSugarGatewayConfigurationRepository repository = new SqlSugarGatewayConfigurationRepository(options);

        OpcUaServerOptions opcUa = new OpcUaServerOptions
        {
            Enabled = true,
            AllowAnonymous = true,
            UsernamePasswordEnabled = true,
            Username = "kepserver"
        };
        OpcUaPasswordHasher.SetPassword(opcUa, "OpcUa#12345");

        repository.SaveOpcUa(opcUa, "Test", "Save OPC UA credentials");

        using SqlSugar.ISqlSugarClient db = new SqlSugarConnectionFactory(options).Create();
        string payload = db.Queryable<GatewayConfigurationEntity>()
            .Where(item => item.ConfigType == GatewayConfigurationType.OpcUa && item.Active)
            .First()
            .Payload;

        Assert.DoesNotContain("OpcUa#12345", payload);
        Assert.Contains("UserPasswordHash", payload);
        Assert.Contains("UserPasswordSalt", payload);

        OpcUaServerOptions loaded = repository.LoadOrCreateOpcUa(new OpcUaServerOptions());
        OpcUaServerConfigurationDto dto = GatewayConfigurationContractMapper.ToDto(loaded);

        Assert.True(OpcUaPasswordHasher.VerifyPassword(loaded, "kepserver", "OpcUa#12345"));
        Assert.False(OpcUaPasswordHasher.VerifyPassword(loaded, "kepserver", "wrong"));
        Assert.True(dto.PasswordConfigured);
        Assert.Equal(string.Empty, dto.Password);
    }

    [Fact]
    public void ApiTokenService_ValidatesHashAndBuildsPermissionPrincipal()
    {
        GatewayApiTokenService service = new GatewayApiTokenService(new GatewayIndustrialSecurityOptions
        {
            ApiTokens = new GatewayApiTokenOptions
            {
                Enabled = true,
                Tokens = new List<GatewayApiTokenDefinition>
                {
                    new GatewayApiTokenDefinition
                    {
                        Name = "mes-readonly",
                        TokenHash = GatewayApiTokenService.HashToken("token-value"),
                        Permissions = new List<string> { GatewayPermissions.ViewRuntime, GatewayPermissions.ReadConfiguration }
                    }
                }
            }
        });
        DefaultHttpContext context = new DefaultHttpContext();
        context.Request.Headers["X-API-Token"] = "token-value";

        bool success = service.TryValidate(context.Request, out System.Security.Claims.ClaimsPrincipal principal, out string tokenName, out string errorMessage);

        Assert.True(success);
        Assert.Equal("mes-readonly", tokenName);
        Assert.True(string.IsNullOrWhiteSpace(errorMessage));
        Assert.True(principal.HasClaim("permission", GatewayPermissions.ViewRuntime));
        Assert.True(principal.HasClaim("permission", GatewayPermissions.ReadConfiguration));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_rootDirectory))
            Directory.Delete(_rootDirectory, true);
    }

    private ProjectConfig CreateProject()
    {
        DeviceConfig device = new DeviceConfig
        {
            Name = "Secure OPC",
            Protocol = PlcProtocol.OpcUa,
            Connection = new PlcConnectionOptions
            {
                Protocol = PlcProtocol.OpcUa,
                Host = "opc.tcp://127.0.0.1",
                Port = 4840,
                Username = "operator",
                Password = "Device#12345",
                CertificatePath = "Data/Certificates/device-client.pfx",
                CertificatePassword = "DeviceCert#12345"
            }
        };
        device.Tags.Add(new TagConfig
        {
            Name = "Temperature",
            Address = "ns=2;s=Temperature",
            Enabled = true
        });

        return new ProjectConfig
        {
            Name = "Gateway",
            Devices = new List<DeviceConfig> { device }
        };
    }

    private GatewayDatabaseOptions CreateOptions()
    {
        return new GatewayDatabaseOptions
        {
            Provider = "Sqlite",
            Database = _databasePath,
            AutoCreateDatabase = true
        };
    }
}

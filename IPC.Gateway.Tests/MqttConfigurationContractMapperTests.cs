/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：MqttConfigurationContractMapperTests
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

namespace IPC.Gateway.Tests;

public sealed class MqttConfigurationContractMapperTests
{
    [Fact]
    public void ToDto_PreservesTlsAndCertificateFields()
    {
        MqttGatewayOptions options = CreateTlsOptions();

        MqttConfigurationDto dto = GatewayConfigurationContractMapper.ToDto(options);

        Assert.True(dto.UseTls);
        Assert.True(dto.AllowUntrustedCertificates);
        Assert.Equal("mqtt-user", dto.Username);
        Assert.Equal("secret-value", dto.Password);
        Assert.Equal("certs/client.pfx", dto.ClientCertificatePath);
        Assert.Equal("cert-secret", dto.ClientCertificatePassword);
        Assert.Equal("client-thumbprint", dto.ClientCertificateThumbprint);
        Assert.Equal("server-thumbprint", dto.ServerCertificateThumbprint);
        Assert.Equal("certs/ca.pem", dto.CaCertificatePath);
    }

    [Fact]
    public void ToConfig_PreservesTlsAndCertificateFields()
    {
        MqttConfigurationDto dto = GatewayConfigurationContractMapper.ToDto(CreateTlsOptions());

        MqttGatewayOptions options = GatewayConfigurationContractMapper.ToConfig(dto);

        Assert.True(options.UseTls);
        Assert.True(options.AllowUntrustedCertificates);
        Assert.Equal("mqtt-user", options.Username);
        Assert.Equal("secret-value", options.Password);
        Assert.Equal("certs/client.pfx", options.ClientCertificatePath);
        Assert.Equal("cert-secret", options.ClientCertificatePassword);
        Assert.Equal("client-thumbprint", options.ClientCertificateThumbprint);
        Assert.Equal("server-thumbprint", options.ServerCertificateThumbprint);
        Assert.Equal("certs/ca.pem", options.CaCertificatePath);
    }

    private static MqttGatewayOptions CreateTlsOptions()
    {
        return new MqttGatewayOptions
        {
            UseTls = true,
            AllowUntrustedCertificates = true,
            Username = "mqtt-user",
            Password = "secret-value",
            ClientCertificatePath = "certs/client.pfx",
            ClientCertificatePassword = "cert-secret",
            ClientCertificateThumbprint = "client-thumbprint",
            ServerCertificateThumbprint = "server-thumbprint",
            CaCertificatePath = "certs/ca.pem"
        };
    }
}

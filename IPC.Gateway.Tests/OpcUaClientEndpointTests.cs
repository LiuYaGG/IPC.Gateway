using System.Reflection;
using System.Text.Json;
using IPC.Gateway.Core.Application.Gateway.Contracts;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.OpcUa;
using Opc.UaFx;

namespace IPC.Gateway.Tests;

public sealed class OpcUaClientEndpointTests
{
    [Theory]
    [InlineData("opc.tcp://127.0.0.1", 49320, "opc.tcp://127.0.0.1:49320/")]
    [InlineData("opc.tcp://127.0.0.1:49320", 4840, "opc.tcp://127.0.0.1:49320")]
    [InlineData("opc.tcp://localhost/UA", 49320, "opc.tcp://localhost:49320/UA")]
    [InlineData("127.0.0.1", 49320, "opc.tcp://127.0.0.1:49320/")]
    public void BuildEndpoint_UsesConfiguredPortWhenEndpointOmitsPort(string host, int port, string expected)
    {
        using OpcUaClient client = new OpcUaClient(new PlcConnectionOptions
        {
            Protocol = PlcProtocol.OpcUa,
            Host = host,
            Port = port
        });

        Assert.Equal(expected, InvokeBuildEndpoint(client));
    }

    [Theory]
    [InlineData("None", "None", OpcSecurityAlgorithm.None, OpcSecurityMode.None)]
    [InlineData("Basic128Rsa15", "Sign", OpcSecurityAlgorithm.Basic128Rsa15, OpcSecurityMode.Sign)]
    [InlineData("Basic256", "SignAndEncrypt", OpcSecurityAlgorithm.Basic256, OpcSecurityMode.SignAndEncrypt)]
    [InlineData("Basic256Sha256", "SignAndEncrypt", OpcSecurityAlgorithm.Basic256Sha256, OpcSecurityMode.SignAndEncrypt)]
    [InlineData("Aes128_Sha256_RsaOaep", "SignAndEncrypt", OpcSecurityAlgorithm.Aes128_Sha256_RsaOaep, OpcSecurityMode.SignAndEncrypt)]
    [InlineData("Aes256_Sha256_RsaPss", "SignAndEncrypt", OpcSecurityAlgorithm.Aes256_Sha256_RsaPss, OpcSecurityMode.SignAndEncrypt)]
    public void BuildSecurityPolicy_UsesConfiguredPolicyAndMode(
        string policy,
        string mode,
        OpcSecurityAlgorithm expectedAlgorithm,
        OpcSecurityMode expectedMode)
    {
        using OpcUaClient client = CreateClient(policy, mode);

        OpcSecurityPolicy result = InvokeBuildSecurityPolicy(client);

        Assert.Equal(expectedAlgorithm, result.Algorithm);
        Assert.Equal(expectedMode, result.Mode);
    }

    [Theory]
    [InlineData("Basic256", "None")]
    [InlineData("None", "Sign")]
    public void BuildSecurityPolicy_RejectsInvalidPolicyAndModeCombination(string policy, string mode)
    {
        using OpcUaClient client = CreateClient(policy, mode);

        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() => InvokeBuildSecurityPolicy(client));

        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public void ConnectionParameterCatalog_ExposesSecuritySettingsAndRemovesLegacyCertificateFields()
    {
        IList<PlcConnectionParameterDefinition> parameters = PlcConnectionParameterCatalog.ForProtocol(PlcProtocol.OpcUa);
        string[] keys = parameters.Select(item => item.Key).ToArray();

        Assert.Contains("opcUaSecurityPolicy", keys);
        Assert.Contains("opcUaMessageSecurityMode", keys);
        Assert.Contains("opcUaAutoTrustServerCertificate", keys);
        Assert.DoesNotContain("certificatePath", keys);
        Assert.DoesNotContain("certificatePassword", keys);
        Assert.DoesNotContain("certificateThumbprint", keys);
        Assert.DoesNotContain("trustStorePath", keys);
        Assert.DoesNotContain("validateServerCertificate", keys);
    }

    [Fact]
    public void ConnectionContract_RoundTripsSecuritySettingsWithoutLegacyCertificateFields()
    {
        PlcConnectionOptions options = new PlcConnectionOptions
        {
            Protocol = PlcProtocol.OpcUa,
            OpcUaSecurityPolicy = "Basic256",
            OpcUaMessageSecurityMode = "Sign",
            OpcUaAutoTrustServerCertificate = true
        };

        PlcConnectionDto dto = GatewayConfigurationContractMapper.ToDto(options);
        PlcConnectionOptions restored = GatewayConfigurationContractMapper.ToConfig(dto);
        string json = JsonSerializer.Serialize(dto);

        Assert.Equal("Basic256", restored.OpcUaSecurityPolicy);
        Assert.Equal("Sign", restored.OpcUaMessageSecurityMode);
        Assert.True(restored.OpcUaAutoTrustServerCertificate);
        Assert.DoesNotContain("CertificatePath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CertificatePassword", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CertificateThumbprint", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TrustStorePath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ValidateServerCertificate", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BatchReadExceptionClassifier_SplitsUnavailableErrorsWhenSessionIsConnected()
    {
        Exception exception = new InvalidOperationException("OPC UA node is unavailable.");

        Assert.True(InvokeShouldSplitBatchReadException(exception, true, 8));
        Assert.False(InvokeShouldTreatBatchReadExceptionAsCommunication(exception, true));
        Assert.Equal(PlcReadFailureScope.Batch, InvokeClassifyBatchReadExceptionScope(exception, true, 8));
        Assert.Equal(PlcReadFailureScope.Tag, InvokeClassifyBatchReadExceptionScope(exception, true, 1));
    }

    [Fact]
    public void BatchReadExceptionClassifier_TreatsUnavailableErrorsAsDownstreamWhenSessionStateIsStale()
    {
        Exception exception = new InvalidOperationException("OPC UA node is unavailable.");

        Assert.True(InvokeShouldSplitBatchReadException(exception, false, 8));
        Assert.False(InvokeShouldTreatBatchReadExceptionAsCommunication(exception, false));
        Assert.Equal(PlcReadFailureScope.Batch, InvokeClassifyBatchReadExceptionScope(exception, false, 8));
        Assert.Equal(PlcReadFailureScope.Tag, InvokeClassifyBatchReadExceptionScope(exception, false, 1));
    }

    [Fact]
    public void BatchReadExceptionClassifier_TreatsSessionErrorsAsSessionFailure()
    {
        Exception exception = new InvalidOperationException("OPC UA client is not connected.");

        Assert.False(InvokeShouldSplitBatchReadException(exception, false, 8));
        Assert.True(InvokeShouldTreatBatchReadExceptionAsCommunication(exception, false));
        Assert.Equal(PlcReadFailureScope.Session, InvokeClassifyBatchReadExceptionScope(exception, false, 8));
    }

    private static string InvokeBuildEndpoint(OpcUaClient client)
    {
        MethodInfo? method = typeof(OpcUaClient).GetMethod("BuildEndpoint", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (string)method!.Invoke(client, Array.Empty<object>())!;
    }

    private static OpcUaClient CreateClient(string policy, string mode)
    {
        return new OpcUaClient(new PlcConnectionOptions
        {
            Protocol = PlcProtocol.OpcUa,
            Host = "opc.tcp://127.0.0.1",
            Port = 4840,
            OpcUaSecurityPolicy = policy,
            OpcUaMessageSecurityMode = mode
        });
    }

    private static OpcSecurityPolicy InvokeBuildSecurityPolicy(OpcUaClient client)
    {
        MethodInfo? method = typeof(OpcUaClient).GetMethod("BuildSecurityPolicy", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (OpcSecurityPolicy)method!.Invoke(client, Array.Empty<object>())!;
    }

    private static bool InvokeShouldSplitBatchReadException(Exception exception, bool sessionConnected, int count)
    {
        MethodInfo? method = typeof(OpcUaClient).GetMethod("ShouldSplitBatchReadException", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (bool)method!.Invoke(null, new object[] { exception, sessionConnected, count })!;
    }

    private static bool InvokeShouldTreatBatchReadExceptionAsCommunication(Exception exception, bool sessionConnected)
    {
        MethodInfo? method = typeof(OpcUaClient).GetMethod("ShouldTreatBatchReadExceptionAsCommunication", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (bool)method!.Invoke(null, new object[] { exception, sessionConnected })!;
    }

    private static PlcReadFailureScope InvokeClassifyBatchReadExceptionScope(Exception exception, bool sessionConnected, int count)
    {
        MethodInfo? method = typeof(OpcUaClient).GetMethod("ClassifyBatchReadExceptionScope", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (PlcReadFailureScope)method!.Invoke(null, new object[] { exception, sessionConnected, count })!;
    }
}

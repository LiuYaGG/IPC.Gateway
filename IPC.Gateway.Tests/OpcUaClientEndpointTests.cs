using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using IPC.Gateway.Core.Application.Gateway.Contracts;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.OpcUa;
using Opc.Ua;

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
    [InlineData("None", "None", SecurityPolicies.None, MessageSecurityMode.None)]
    [InlineData("Basic128Rsa15", "Sign", SecurityPolicies.Basic128Rsa15, MessageSecurityMode.Sign)]
    [InlineData("Basic256", "SignAndEncrypt", SecurityPolicies.Basic256, MessageSecurityMode.SignAndEncrypt)]
    [InlineData("Basic256Sha256", "SignAndEncrypt", SecurityPolicies.Basic256Sha256, MessageSecurityMode.SignAndEncrypt)]
    [InlineData("Aes128_Sha256_RsaOaep", "SignAndEncrypt", SecurityPolicies.Aes128_Sha256_RsaOaep, MessageSecurityMode.SignAndEncrypt)]
    [InlineData("Aes256_Sha256_RsaPss", "SignAndEncrypt", SecurityPolicies.Aes256_Sha256_RsaPss, MessageSecurityMode.SignAndEncrypt)]
    public void BuildSecurityPolicy_UsesConfiguredPolicyAndMode(
        string policy,
        string mode,
        string expectedPolicyUri,
        MessageSecurityMode expectedMode)
    {
        using OpcUaClient client = CreateClient(policy, mode);

        EndpointDescription result = InvokeBuildSecurityPolicy(client);

        Assert.Equal(expectedPolicyUri, result.SecurityPolicyUri);
        Assert.Equal(expectedMode, result.SecurityMode);
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
    public void TrustServerCertificate_IsIdempotentWhenCertificateAlreadyExists()
    {
        string trustStorePath = Path.Combine(Path.GetTempPath(), "ipc-opcua-trust-" + Guid.NewGuid().ToString("N"));
        using RSA key = RSA.Create(2048);
        CertificateRequest request = new(
            "CN=IPC OPC UA Test Server",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using X509Certificate2 certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        ApplicationConfiguration configuration = new()
        {
            SecurityConfiguration = new SecurityConfiguration
            {
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = trustStorePath
                }
            }
        };
        EndpointDescription endpoint = new() { ServerCertificate = certificate.RawData };

        try
        {
            InvokeTrustServerCertificate(configuration, endpoint);
            InvokeTrustServerCertificate(configuration, endpoint);

            Assert.Single(Directory.GetFiles(trustStorePath, "*.der", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(trustStorePath))
                Directory.Delete(trustStorePath, true);
        }
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

    private static EndpointDescription InvokeBuildSecurityPolicy(OpcUaClient client)
    {
        MethodInfo? method = typeof(OpcUaClient).GetMethod("BuildSecurityPolicy", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (EndpointDescription)method!.Invoke(client, Array.Empty<object>())!;
    }

    private static void InvokeTrustServerCertificate(
        ApplicationConfiguration configuration,
        EndpointDescription endpoint)
    {
        Type factory = typeof(OpcUaClient).Assembly.GetType(
            "IPC.Plc.Communication.OpcUa.OpcUaFoundationSessionFactory",
            throwOnError: true)!;
        MethodInfo? method = factory.GetMethod("TrustServerCertificate", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(null, new object[] { configuration, endpoint });
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

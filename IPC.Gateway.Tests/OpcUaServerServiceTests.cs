/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：OpcUaServerServiceTests
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
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using IPC.EdgeGateway;
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;

namespace IPC.Gateway.Tests;

public sealed class OpcUaServerServiceTests
{
    private const string SecurityPolicyNone = "http://opcfoundation.org/UA/SecurityPolicy#None";
    private const string SecurityPolicyBasic256 = "http://opcfoundation.org/UA/SecurityPolicy#Basic256";
    private const string SecurityPolicyBasic256Sha256 = "http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256";

    [Fact]
    public void Start_WhenEnabled_CreatesCertificateAndRuns()
    {
        string certificateDirectory = Path.Combine(Path.GetTempPath(), "ipc-gateway-opcua-test-" + Guid.NewGuid().ToString("N"));
        int port = FindFreeTcpPort();

        try
        {
            using RuntimeEngine runtime = new RuntimeEngine();
            using OpcUaServerService service = new OpcUaServerService(runtime, CreateProject, new OpcUaServerOptions
            {
                Enabled = true,
                Host = "localhost",
                Port = port,
                EndpointPath = "IPC.Gateway.Tests",
                CertificateStorePath = certificateDirectory,
                AutoAcceptUntrustedCertificates = true
            });

            service.Start();
            OpcUaServerStatus status = service.GetStatus();

            Assert.True(status.IsRunning, status.LastError);
            Assert.True(Directory.Exists(certificateDirectory));
            Assert.Equal(string.Empty, status.LastError);
        }
        finally
        {
            if (Directory.Exists(certificateDirectory))
                Directory.Delete(certificateDirectory, recursive: true);
        }
    }

    [Fact]
    public void SecurityPolicies_WhenBasic256Selected_PublishOnlyBasic256()
    {
        OpcUaServerOptions options = new OpcUaServerOptions
        {
            AllowAnonymous = false,
            UsernamePasswordEnabled = true,
            Username = "kepserver",
            SecurityPolicy = OpcUaServerOptions.SecurityPolicyBasic256
        };
        OpcUaPasswordHasher.SetPassword(options, "OpcUa#12345");
        options = OpcUaServerOptions.Normalize(options);

        IEnumerable<object> securityPolicies = InvokeCreateSecurityPolicies(options);
        IEnumerable<object> userTokenPolicies = InvokeCreateUserTokenPolicies(options);

        Assert.DoesNotContain(securityPolicies, policy => ReadString(policy, "SecurityPolicyUri") == SecurityPolicyNone);
        Assert.Contains(securityPolicies, policy =>
            ReadString(policy, "SecurityMode") == "SignAndEncrypt" &&
            ReadString(policy, "SecurityPolicyUri") == SecurityPolicyBasic256);
        Assert.DoesNotContain(securityPolicies, policy => ReadString(policy, "SecurityPolicyUri") == SecurityPolicyBasic256Sha256);
        Assert.DoesNotContain(userTokenPolicies, policy => ReadString(policy, "TokenType") == "Anonymous");
        Assert.Contains(userTokenPolicies, policy =>
            ReadString(policy, "TokenType") == "UserName" &&
            ReadString(policy, "SecurityPolicyUri") == SecurityPolicyBasic256);
        Assert.DoesNotContain(userTokenPolicies, policy =>
            ReadString(policy, "TokenType") == "UserName" &&
            ReadString(policy, "SecurityPolicyUri") == SecurityPolicyBasic256Sha256);
    }

    [Fact]
    public void SecurityPolicies_WhenLegacyFlagsContainMultipleValues_NormalizeToSinglePolicy()
    {
        OpcUaServerOptions options = OpcUaServerOptions.Normalize(new OpcUaServerOptions
        {
            AllowAnonymous = false,
            UsernamePasswordEnabled = true,
            Username = "kepserver",
            AllowSecurityPolicyNone = false,
            EnableBasic256SignAndEncrypt = true,
            EnableBasic256Sha256SignAndEncrypt = true
        });

        Assert.Equal(OpcUaServerOptions.SecurityPolicyBasic256, options.SecurityPolicy);
        Assert.False(options.AllowSecurityPolicyNone);
        Assert.True(options.EnableBasic256SignAndEncrypt);
        Assert.False(options.EnableBasic256Sha256SignAndEncrypt);
    }

    private static ProjectConfig CreateProject()
    {
        return new ProjectConfig
        {
            Name = "OPC UA Test Project"
        };
    }

    private static int FindFreeTcpPort()
    {
        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static IEnumerable<object> InvokeCreateSecurityPolicies(OpcUaServerOptions options)
    {
        MethodInfo? method = typeof(OpcUaServerService).GetMethod("CreateSecurityPolicies", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return ToObjects(method!.Invoke(null, new object[] { options })!);
    }

    private static IEnumerable<object> InvokeCreateUserTokenPolicies(OpcUaServerOptions options)
    {
        MethodInfo? method = typeof(OpcUaServerService).GetMethod("CreateUserTokenPolicies", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return ToObjects(method!.Invoke(null, new object[] { options })!);
    }

    private static IEnumerable<object> ToObjects(object value)
    {
        return ((System.Collections.IEnumerable)value).Cast<object>();
    }

    private static string ReadString(object value, string propertyName)
    {
        return value.GetType().GetProperty(propertyName)?.GetValue(value)?.ToString() ?? string.Empty;
    }
}

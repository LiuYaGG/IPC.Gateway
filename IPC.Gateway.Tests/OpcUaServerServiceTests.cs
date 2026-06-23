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
using IPC.EdgeGateway;
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;

namespace IPC.Gateway.Tests;

public sealed class OpcUaServerServiceTests
{
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
}

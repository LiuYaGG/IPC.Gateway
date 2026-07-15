using System.Net;
using System.Net.Sockets;
using IPC.EdgeGateway;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.OpcUa;
using IPC.Plc.Communication.VirtualPlc;
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;
using IPC.Runtime.Values;

namespace IPC.Gateway.Tests;

public sealed class OpcUaClientIntegrationTests
{
    [Fact]
    public async Task FoundationClient_ConnectsWithBasic256Sha256AndUsername()
    {
        int port = FindFreeTcpPort();
        string storeKey = "opcua-client-secure-" + Guid.NewGuid().ToString("N");
        string certificateDirectory = Path.Combine(
            Path.GetTempPath(),
            "ipc-gateway-opcua-client-secure-test-" + Guid.NewGuid().ToString("N"));
        ProjectConfig project = CreateProject(storeKey);

        try
        {
            using (VirtualPlcClient seed = new VirtualPlcClient(new PlcConnectionOptions
            {
                Protocol = PlcProtocol.VirtualPlc,
                Host = storeKey
            }))
            {
                seed.Connect();
                seed.Write("D100", PlcDataType.Int32, "84", 0);
            }

            using RuntimeEngine runtime = new RuntimeEngine(new RuntimeSchedulerOptions
            {
                SchedulerIntervalMs = 25
            });
            runtime.Start(project);
            await WaitForGoodSnapshotAsync(runtime, "tag-1");

            OpcUaServerOptions serverOptions = new OpcUaServerOptions
            {
                Enabled = true,
                Host = "localhost",
                Port = port,
                EndpointPath = "IPC.Gateway.Tests.SecureClient",
                CertificateStorePath = certificateDirectory,
                SecurityPolicy = OpcUaServerOptions.SecurityPolicyBasic256Sha256,
                AutoAcceptUntrustedCertificates = true,
                AllowAnonymous = false,
                UsernamePasswordEnabled = true,
                Username = "gateway-client-test"
            };
            OpcUaPasswordHasher.SetPassword(serverOptions, "OpcUa#ClientTest");

            using OpcUaServerService server = new OpcUaServerService(runtime, () => project, serverOptions);
            server.Start();
            Assert.True(server.GetStatus().IsRunning, server.GetStatus().LastError);

            using OpcUaClient client = new OpcUaClient(new PlcConnectionOptions
            {
                Protocol = PlcProtocol.OpcUa,
                Host = $"opc.tcp://localhost:{port}/IPC.Gateway.Tests.SecureClient",
                Port = port,
                TimeoutMilliseconds = 10000,
                Username = "gateway-client-test",
                Password = "OpcUa#ClientTest",
                OpcUaSecurityPolicy = "Basic256Sha256",
                OpcUaMessageSecurityMode = "SignAndEncrypt",
                OpcUaAutoTrustServerCertificate = true
            });
            client.Connect();

            PlcReadResult result = client.Read(
                "ns=2;s=tag:channel-1/device-1/_/tag-1",
                PlcDataType.Int32,
                1,
                0);

            Assert.Equal(84, Assert.IsType<int>(result.Value));
            Assert.True(client.IsConnected);
        }
        finally
        {
            if (Directory.Exists(certificateDirectory))
                Directory.Delete(certificateDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task FoundationClient_ReadsAndIsolatesBadNodeAgainstGatewayServer()
    {
        int port = FindFreeTcpPort();
        string storeKey = "opcua-client-integration-" + Guid.NewGuid().ToString("N");
        string certificateDirectory = Path.Combine(
            Path.GetTempPath(),
            "ipc-gateway-opcua-client-test-" + Guid.NewGuid().ToString("N"));
        ProjectConfig project = CreateProject(storeKey);

        try
        {
            using (VirtualPlcClient seed = new VirtualPlcClient(new PlcConnectionOptions
            {
                Protocol = PlcProtocol.VirtualPlc,
                Host = storeKey
            }))
            {
                seed.Connect();
                seed.Write("D100", PlcDataType.Int32, "42", 0);
            }

            using RuntimeEngine runtime = new RuntimeEngine(new RuntimeSchedulerOptions
            {
                SchedulerIntervalMs = 25
            });
            runtime.Start(project);
            await WaitForGoodSnapshotAsync(runtime, "tag-1");

            using OpcUaServerService server = new OpcUaServerService(runtime, () => project, new OpcUaServerOptions
            {
                Enabled = true,
                Host = "localhost",
                Port = port,
                EndpointPath = "IPC.Gateway.Tests.Client",
                CertificateStorePath = certificateDirectory,
                SecurityPolicy = OpcUaServerOptions.SecurityPolicyNone,
                AllowAnonymous = true
            });
            server.Start();
            Assert.True(server.GetStatus().IsRunning, server.GetStatus().LastError);
            Assert.Equal(1, server.GetStatus().TagNodeCount);

            using OpcUaClient client = new OpcUaClient(new PlcConnectionOptions
            {
                Protocol = PlcProtocol.OpcUa,
                Host = $"opc.tcp://localhost:{port}/IPC.Gateway.Tests.Client",
                Port = port,
                TimeoutMilliseconds = 5000,
                OpcUaSecurityPolicy = "None",
                OpcUaMessageSecurityMode = "None"
            });
            client.Connect();

            const string validNode = "ns=2;s=tag:channel-1/device-1/_/tag-1";
            PlcReadResult single = client.Read(validNode, PlcDataType.Int32, 1, 0);
            Assert.Equal(42, Assert.IsType<int>(single.Value));

            IList<PlcBatchReadResult> batch = client.ReadMany(new List<PlcBatchReadRequest>
            {
                new PlcBatchReadRequest(validNode, PlcDataType.Int32, 1, 0),
                new PlcBatchReadRequest("ns=2;s=tag:missing", PlcDataType.Int32, 1, 0)
            });

            Assert.True(batch[0].Success, batch[0].ErrorMessage);
            Assert.Equal(42, Assert.IsType<int>(batch[0].Result!.Value));
            Assert.False(batch[1].Success);
            Assert.Equal(PlcReadFailureScope.Tag, batch[1].FailureScope);
            Assert.True(client.IsConnected);

            PlcProtocolException writeFailure = Assert.Throws<PlcProtocolException>(
                () => client.Write(validNode, PlcDataType.Int32, "99", 0));
            Assert.Equal(PlcReadFailureScope.Tag, writeFailure.FailureScope);
            Assert.True(client.IsConnected);

            TaskCompletionSource<PlcSubscriptionUpdate> changed =
                new TaskCompletionSource<PlcSubscriptionUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
            using IPlcSubscription subscription = await client.SubscribeAsync(
                new List<PlcSubscriptionRequest>
                {
                    new PlcSubscriptionRequest("value", validNode, PlcDataType.Int32, 1, 0, 100)
                },
                new PlcSubscriptionOptions
                {
                    PublishingIntervalMs = 100,
                    SamplingIntervalMs = 100,
                    QueueSize = 1
                },
                update =>
                {
                    if (update.Success && Convert.ToInt32(update.Result!.Value) == 43)
                        changed.TrySetResult(update);
                    return ValueTask.CompletedTask;
                },
                CancellationToken.None);
            Assert.True(subscription.IsActive);

            using (VirtualPlcClient writer = new VirtualPlcClient(new PlcConnectionOptions
            {
                Protocol = PlcProtocol.VirtualPlc,
                Host = storeKey
            }))
            {
                writer.Connect();
                writer.Write("D100", PlcDataType.Int32, "43", 0);
            }

            Task completed = await Task.WhenAny(changed.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(changed.Task, completed);
            Assert.Equal(43, Assert.IsType<int>((await changed.Task).Result!.Value));
        }
        finally
        {
            if (Directory.Exists(certificateDirectory))
                Directory.Delete(certificateDirectory, recursive: true);
        }
    }

    private static ProjectConfig CreateProject(string storeKey)
    {
        return new ProjectConfig
        {
            ProjectId = "project-1",
            Name = "OPC UA Client Integration",
            Channels = new List<ChannelConfig>
            {
                new ChannelConfig
                {
                    Id = "channel-1",
                    Name = "Virtual Channel",
                    Protocol = PlcProtocol.VirtualPlc
                }
            },
            Devices = new List<DeviceConfig>
            {
                new DeviceConfig
                {
                    Id = "device-1",
                    ChannelId = "channel-1",
                    Name = "Virtual Device",
                    Protocol = PlcProtocol.VirtualPlc,
                    Connection = new PlcConnectionOptions
                    {
                        Protocol = PlcProtocol.VirtualPlc,
                        Host = storeKey
                    },
                    DefaultScanRateMs = 25,
                    Tags = new List<TagConfig>
                    {
                        new TagConfig
                        {
                            Id = "tag-1",
                            DeviceId = "device-1",
                            Name = "Value",
                            Address = "D100",
                            DataType = PlcDataType.Int32,
                            ScanRateMs = 25
                        }
                    }
                }
            }
        };
    }

    private static async Task WaitForGoodSnapshotAsync(RuntimeEngine runtime, string tagId)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            TagValueSnapshot? snapshot = runtime.GetSnapshots()
                .FirstOrDefault(item => item.TagId == tagId && item.Quality == TagQuality.Good);
            if (snapshot != null)
                return;
            await Task.Delay(50);
        }

        throw new TimeoutException("Virtual PLC value was not collected before the OPC UA integration test started.");
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

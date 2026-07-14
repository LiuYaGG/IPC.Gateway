/*----------------------------------------------------------------
* 项目名称 ：IPC.EdgeGateway
* 项目描述 ：
* 类 名 称 ：OpcUaServerService
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.EdgeGateway
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
using System.IO;
using System.Collections.Generic;
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;
using IPC.Runtime.Values;
using Opc.Ua;
using Opc.Ua.Configuration;

namespace IPC.EdgeGateway
{
    public sealed class OpcUaServerService : IDisposable
    {
        private static readonly ITelemetryContext s_telemetry = DefaultTelemetry.Create(_ => { });

        private readonly object _syncRoot;
        private readonly IRuntimeService _runtime;
        private readonly Func<ProjectConfig> _projectProvider;
        private OpcUaServerOptions _options;
        private readonly OpcUaServerStatus _status;
        private ApplicationInstance? _application;
        private OpcUaGatewayServer? _server;
        private bool _disposed;

        public OpcUaServerService(IRuntimeService runtime, Func<ProjectConfig> projectProvider, OpcUaServerOptions options)
        {
            _syncRoot = new object();
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _projectProvider = projectProvider ?? throw new ArgumentNullException(nameof(projectProvider));
            _options = OpcUaServerOptions.Normalize(options);
            _status = new OpcUaServerStatus();
            ApplyOptionStatus(_status, _options);
        }

        public OpcUaServerOptions Options
        {
            get
            {
                lock (_syncRoot)
                    return _options.Clone();
            }
        }

        public void Start()
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                ApplyOptionStatus(_status, _options);
                if (!_options.Enabled)
                {
                    _status.IsRunning = false;
                    _status.LastMessage = "OPC UA Server is disabled.";
                    return;
                }

                if (_server != null)
                    return;

                try
                {
                    ApplicationConfiguration configuration = CreateApplicationConfiguration(_options);
                    configuration.ValidateAsync(ApplicationType.Server, CancellationToken.None).GetAwaiter().GetResult();

                    _server = new OpcUaGatewayServer(_runtime, _projectProvider, _options, _status);
                    _application = new ApplicationInstance(configuration, s_telemetry);
                    bool certificateRenewed = EnsureApplicationCertificate(_application, configuration);

                    _application.StartAsync(_server).GetAwaiter().GetResult();

                    _runtime.TagValueChanged -= OnRuntimeTagValueChanged;
                    _runtime.TagValueChanged += OnRuntimeTagValueChanged;
                    foreach (TagValueSnapshot snapshot in _runtime.GetSnapshots())
                        _server.UpdateValue(snapshot);

                    _status.IsRunning = true;
                    _status.StartedTime = DateTime.Now;
                    _status.LastReloadTime = _status.StartedTime;
                    _status.LastError = string.Empty;
                    _status.LastMessage = certificateRenewed
                        ? "OPC UA Server started after replacing an invalid application certificate."
                        : "OPC UA Server started.";
                }
                catch (Exception ex)
                {
                    _runtime.TagValueChanged -= OnRuntimeTagValueChanged;
                    TryStopUnlocked();
                    _status.IsRunning = false;
                    _status.LastError = FormatExceptionMessage(ex);
                    _status.LastMessage = "OPC UA Server failed to start.";
                }
            }
        }

        public void Stop()
        {
            lock (_syncRoot)
            {
                _runtime.TagValueChanged -= OnRuntimeTagValueChanged;
                TryStopUnlocked();
                _status.IsRunning = false;
                _status.LastMessage = "OPC UA Server stopped.";
            }
        }

        public void UpdateOptions(OpcUaServerOptions options)
        {
            lock (_syncRoot)
            {
                bool wasRunning = _server != null;
                if (wasRunning)
                    Stop();

                _options = OpcUaServerOptions.Normalize(options);
                ApplyOptionStatus(_status, _options);
                _status.LastReloadTime = DateTime.Now;

                if (wasRunning || _options.Enabled)
                    Start();
            }
        }

        public void RestartIfRunning()
        {
            lock (_syncRoot)
            {
                bool shouldRun = _options.Enabled && _server != null;
                if (!shouldRun)
                    return;

                Stop();
                Start();
            }
        }

        public OpcUaServerStatus GetStatus()
        {
            lock (_syncRoot)
            {
                OpcUaServerStatus status = _status.Clone();
                status.Enabled = _options.Enabled;
                status.ApplicationName = _options.ApplicationName;
                status.EndpointUrl = _options.EndpointUrl;
                status.NamespaceUri = _options.NamespaceUri;
                return status;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Stop();
        }

        private void OnRuntimeTagValueChanged(object? sender, TagValueChangedEventArgs e)
        {
            if (e?.Snapshot == null)
                return;

            lock (_syncRoot)
            {
                _server?.UpdateValue(e.Snapshot);
            }
        }

        private void TryStopUnlocked()
        {
            try
            {
                _application?.StopAsync().GetAwaiter().GetResult();
            }
            catch
            {
            }

            _application = null;
            _server = null;
        }

        private static ApplicationConfiguration CreateApplicationConfiguration(OpcUaServerOptions options)
        {
            OpcUaServerOptions normalized = OpcUaServerOptions.Normalize(options);
            string pkiRoot = ResolveCertificateStorePath(normalized.CertificateStorePath);
            Directory.CreateDirectory(pkiRoot);

            return new ApplicationConfiguration
            {
                ApplicationName = normalized.ApplicationName,
                ApplicationUri = normalized.ApplicationUri,
                ProductUri = normalized.ProductUri,
                ApplicationType = ApplicationType.Server,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = "Directory",
                        StorePath = Path.Combine(pkiRoot, "own"),
                        SubjectName = normalized.ApplicationName
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = "Directory",
                        StorePath = Path.Combine(pkiRoot, "issuers")
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = "Directory",
                        StorePath = Path.Combine(pkiRoot, "trusted")
                    },
                    RejectedCertificateStore = new CertificateStoreIdentifier
                    {
                        StoreType = "Directory",
                        StorePath = Path.Combine(pkiRoot, "rejected")
                    },
                    AutoAcceptUntrustedCertificates = normalized.AutoAcceptUntrustedCertificates,
                    AddAppCertToTrustedStore = true,
                    RejectSHA1SignedCertificates = false,
                    MinimumCertificateKeySize = 2048
                },
                TransportConfigurations = new TransportConfigurationCollection(),
                TransportQuotas = new TransportQuotas
                {
                    OperationTimeout = 15000,
                    MaxStringLength = 1024 * 1024,
                    MaxByteStringLength = 1024 * 1024,
                    MaxArrayLength = 65535,
                    MaxMessageSize = 4 * 1024 * 1024,
                    MaxBufferSize = 65535,
                    ChannelLifetime = 300000,
                    SecurityTokenLifetime = 3600000
                },
                ServerConfiguration = new ServerConfiguration
                {
                    BaseAddresses = new StringCollection { normalized.EndpointUrl },
                    SecurityPolicies = CreateSecurityPolicies(normalized),
                    UserTokenPolicies = CreateUserTokenPolicies(normalized),
                    DiagnosticsEnabled = normalized.PublishDiagnostics,
                    MinRequestThreadCount = 1,
                    MaxRequestThreadCount = 20,
                    MaxQueuedRequestCount = 200,
                    MaxSessionCount = 100,
                    MaxSubscriptionCount = 1000,
                    MaxPublishRequestCount = 100,
                    MaxMessageQueueSize = 100,
                    MaxNotificationQueueSize = 100,
                    MinPublishingInterval = OpcUaServerOptions.ClampSamplingInterval(normalized.MinimumSamplingIntervalMs),
                    MaxPublishingInterval = 60000,
                    PublishingResolution = OpcUaServerOptions.ClampSamplingInterval(normalized.MinimumSamplingIntervalMs),
                    MinMetadataSamplingInterval = OpcUaServerOptions.ClampSamplingInterval(normalized.MinimumSamplingIntervalMs)
                },
                DisableHiResClock = true
            };
        }

        private static string ResolveCertificateStorePath(string configuredPath)
        {
            string path = string.IsNullOrWhiteSpace(configuredPath)
                ? Path.Combine("Data", "OpcUa", "pki")
                : configuredPath.Trim();
            return Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
        }

        private static bool EnsureApplicationCertificate(
            ApplicationInstance application,
            ApplicationConfiguration configuration)
        {
            try
            {
                if (CheckApplicationCertificate(application))
                    return false;
            }
            catch (Exception ex) when (IsApplicationCertificateFailure(ex))
            {
                if (!ArchiveApplicationCertificate(configuration))
                    throw;

                if (!CheckApplicationCertificate(application))
                    throw new InvalidOperationException("OPC UA Server application certificate could not be recreated after archiving the invalid certificate.");
                return true;
            }

            if (!ArchiveApplicationCertificate(configuration))
                throw new InvalidOperationException("OPC UA Server application certificate could not be created or validated, and no existing certificate was available to archive.");
            if (!CheckApplicationCertificate(application))
                throw new InvalidOperationException("OPC UA Server application certificate could not be recreated after archiving the invalid certificate.");
            return true;
        }

        private static bool CheckApplicationCertificate(ApplicationInstance application)
        {
            return application
                .CheckApplicationInstanceCertificatesAsync(false, null, CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        private static bool IsApplicationCertificateFailure(Exception exception)
        {
            Exception root = exception.GetBaseException();
            return root is ServiceResultException &&
                   root.Message.IndexOf("certificate", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ArchiveApplicationCertificate(ApplicationConfiguration configuration)
        {
            string ownStorePath = configuration.SecurityConfiguration.ApplicationCertificate.StorePath;
            if (string.IsNullOrWhiteSpace(ownStorePath))
                return false;

            string ownCertificateDirectory = Path.Combine(ownStorePath, "certs");
            HashSet<string> ownCertificateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(ownCertificateDirectory))
            {
                foreach (string certificateFile in Directory.EnumerateFiles(ownCertificateDirectory, "*", SearchOption.TopDirectoryOnly))
                    ownCertificateNames.Add(Path.GetFileName(certificateFile));
            }

            string[] storeDirectories =
            {
                ownCertificateDirectory,
                Path.Combine(ownStorePath, "private")
            };
            string pkiRoot = Directory.GetParent(ownStorePath)?.FullName ?? ownStorePath;
            string archiveRoot = Path.Combine(
                pkiRoot,
                "archive",
                DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N"));
            bool archived = false;

            foreach (string storeDirectory in storeDirectories)
            {
                if (!Directory.Exists(storeDirectory))
                    continue;

                string targetDirectory = Path.Combine(archiveRoot, Path.GetFileName(storeDirectory));
                foreach (string file in Directory.EnumerateFiles(storeDirectory, "*", SearchOption.TopDirectoryOnly))
                {
                    Directory.CreateDirectory(targetDirectory);
                    File.Move(file, Path.Combine(targetDirectory, Path.GetFileName(file)));
                    archived = true;
                }
            }

            string trustedCertificateDirectory = Path.Combine(pkiRoot, "trusted", "certs");
            if (Directory.Exists(trustedCertificateDirectory) && ownCertificateNames.Count > 0)
            {
                string trustedArchiveDirectory = Path.Combine(archiveRoot, "trusted");
                foreach (string trustedFile in Directory.EnumerateFiles(trustedCertificateDirectory, "*", SearchOption.TopDirectoryOnly))
                {
                    if (!ownCertificateNames.Contains(Path.GetFileName(trustedFile)))
                        continue;

                    Directory.CreateDirectory(trustedArchiveDirectory);
                    File.Move(trustedFile, Path.Combine(trustedArchiveDirectory, Path.GetFileName(trustedFile)));
                    archived = true;
                }
            }

            return archived;
        }

        private static ServerSecurityPolicyCollection CreateSecurityPolicies(OpcUaServerOptions options)
        {
            ServerSecurityPolicyCollection policies = new ServerSecurityPolicyCollection();

            if (options.AllowSecurityPolicyNone)
            {
                policies.Add(new ServerSecurityPolicy
                {
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None
                });
            }

            if (options.EnableBasic256Sha256SignAndEncrypt)
            {
                policies.Add(new ServerSecurityPolicy
                {
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                });
            }

            if (options.EnableBasic256SignAndEncrypt)
            {
                policies.Add(new ServerSecurityPolicy
                {
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256
                });
            }

            if (policies.Count == 0)
            {
                policies.Add(new ServerSecurityPolicy
                {
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                });
            }

            return policies;
        }

        private static UserTokenPolicyCollection CreateUserTokenPolicies(OpcUaServerOptions options)
        {
            UserTokenPolicyCollection policies = new UserTokenPolicyCollection();

            if (options.AllowAnonymous)
            {
                policies.Add(new UserTokenPolicy
                {
                    PolicyId = "anonymous",
                    TokenType = UserTokenType.Anonymous
                });
            }

            if (options.UsernamePasswordEnabled && OpcUaPasswordHasher.IsPasswordConfigured(options))
            {
                if (options.EnableBasic256Sha256SignAndEncrypt)
                {
                    policies.Add(new UserTokenPolicy
                    {
                        PolicyId = "username_basic256sha256",
                        TokenType = UserTokenType.UserName,
                        SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                    });
                }

                if (options.EnableBasic256SignAndEncrypt)
                {
                    policies.Add(new UserTokenPolicy
                    {
                        PolicyId = "username_basic256",
                        TokenType = UserTokenType.UserName,
                        SecurityPolicyUri = SecurityPolicies.Basic256
                    });
                }

                if (!options.EnableBasic256Sha256SignAndEncrypt && !options.EnableBasic256SignAndEncrypt)
                {
                    policies.Add(new UserTokenPolicy
                    {
                        PolicyId = "username_basic256sha256",
                        TokenType = UserTokenType.UserName,
                        SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                    });
                }
            }

            if (policies.Count == 0)
            {
                policies.Add(new UserTokenPolicy
                {
                    PolicyId = "anonymous",
                    TokenType = UserTokenType.Anonymous
                });
            }

            return policies;
        }

        private static void ApplyOptionStatus(OpcUaServerStatus status, OpcUaServerOptions options)
        {
            status.Enabled = options.Enabled;
            status.ApplicationName = options.ApplicationName;
            status.EndpointUrl = options.EndpointUrl;
            status.NamespaceUri = options.NamespaceUri;
        }

        private static string FormatExceptionMessage(Exception exception)
        {
            Exception root = exception.GetBaseException();
            return root.GetType().Name + ": " + root.Message;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OpcUaServerService));
        }
    }
}

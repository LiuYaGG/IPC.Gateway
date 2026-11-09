using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using IPC.Plc.Communication.Core;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace IPC.Plc.Communication.OpcUa
{
    internal static class OpcUaFoundationSessionFactory
    {
        private const string ApplicationName = "IPC Gateway OPC UA Client";
        private static readonly object CertificateSyncRoot = new object();
        private static readonly ITelemetryContext Telemetry = DefaultTelemetry.Create(_ => { });

        public static ISession Connect(
            PlcConnectionOptions options,
            string endpointUrl,
            MessageSecurityMode securityMode,
            string securityPolicyUri)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            int timeout = Math.Max(1000, options.TimeoutMilliseconds);
            ApplicationConfiguration configuration = CreateConfiguration(options, timeout);
            configuration.ValidateAsync(ApplicationType.Client, CancellationToken.None).GetAwaiter().GetResult();

            if (securityMode != MessageSecurityMode.None)
                EnsureApplicationCertificate(configuration);

            EndpointDescription selectedEndpoint = SelectEndpoint(
                configuration,
                endpointUrl,
                securityMode,
                securityPolicyUri,
                timeout);

            if (options.OpcUaAutoTrustServerCertificate)
                TrustServerCertificate(configuration, selectedEndpoint);

            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(configuration);
            endpointConfiguration.OperationTimeout = timeout;
            ConfiguredEndpoint endpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfiguration);
            IUserIdentity identity = string.IsNullOrWhiteSpace(options.Username)
                ? new UserIdentity(new AnonymousIdentityToken())
                : new UserIdentity(options.Username, Encoding.UTF8.GetBytes(options.Password ?? string.Empty));

            uint sessionTimeout = (uint)Math.Clamp((long)timeout * 4L, 60000L, 3600000L);
            return new DefaultSessionFactory(Telemetry).CreateAsync(
                    configuration,
                    endpoint,
                    false,
                    !options.OpcUaAutoTrustServerCertificate,
                    ApplicationName,
                    sessionTimeout,
                    identity,
                    null,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        private static ApplicationConfiguration CreateConfiguration(PlcConnectionOptions options, int timeout)
        {
            string pkiRoot = Path.Combine(AppContext.BaseDirectory, "Data", "OpcUaClient", "pki");
            Directory.CreateDirectory(pkiRoot);

            return new ApplicationConfiguration
            {
                ApplicationName = ApplicationName,
                ApplicationUri = "urn:" + Dns.GetHostName() + ":IPC:Gateway:OpcUaClient",
                ProductUri = "urn:IPC:Gateway:OpcUaClient",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "own"),
                        SubjectName = ApplicationName
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "issuers")
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "trusted")
                    },
                    RejectedCertificateStore = new CertificateStoreIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "rejected")
                    },
                    AutoAcceptUntrustedCertificates = options.OpcUaAutoTrustServerCertificate,
                    AddAppCertToTrustedStore = true,
                    RejectSHA1SignedCertificates = false,
                    MinimumCertificateKeySize = 2048
                },
                TransportConfigurations = new TransportConfigurationCollection(),
                TransportQuotas = new TransportQuotas
                {
                    OperationTimeout = timeout,
                    MaxStringLength = 1024 * 1024,
                    MaxByteStringLength = 1024 * 1024,
                    MaxArrayLength = 65535,
                    MaxMessageSize = 4 * 1024 * 1024,
                    MaxBufferSize = 65535,
                    ChannelLifetime = 300000,
                    SecurityTokenLifetime = 3600000
                },
                ClientConfiguration = new ClientConfiguration
                {
                    DefaultSessionTimeout = 60000,
                    MinSubscriptionLifetime = 10000
                },
                DisableHiResClock = true
            };
        }

        private static void EnsureApplicationCertificate(ApplicationConfiguration configuration)
        {
            lock (CertificateSyncRoot)
            {
                ApplicationInstance application = new ApplicationInstance(configuration, Telemetry);
                bool valid = application
                    .CheckApplicationInstanceCertificatesAsync(false, null, CancellationToken.None)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
                if (!valid)
                    throw new InvalidOperationException("OPC UA client application certificate could not be created or validated.");
            }
        }

        private static EndpointDescription SelectEndpoint(
            ApplicationConfiguration configuration,
            string endpointUrl,
            MessageSecurityMode securityMode,
            string securityPolicyUri,
            int timeout)
        {
            Uri discoveryUri = new Uri(endpointUrl, UriKind.Absolute);
            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(configuration);
            endpointConfiguration.OperationTimeout = timeout;

            EndpointDescriptionCollection endpoints;
            using (DiscoveryClient discovery = DiscoveryClient.CreateAsync(
                       configuration,
                       discoveryUri,
                       endpointConfiguration,
                       DiagnosticsMasks.None,
                       CancellationToken.None)
                   .GetAwaiter()
                   .GetResult())
            {
                endpoints = discovery.GetEndpointsAsync(null, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }

            EndpointDescription selected = endpoints
                .Where(item => item.SecurityMode == securityMode)
                .Where(item => string.Equals(item.SecurityPolicyUri, securityPolicyUri, StringComparison.Ordinal))
                .OrderByDescending(item => item.SecurityLevel)
                .FirstOrDefault();

            if (selected == null)
            {
                string available = string.Join(", ", endpoints
                    .Select(item => item.SecurityMode + "/" + SecurityPolicies.GetDisplayName(item.SecurityPolicyUri))
                    .Distinct(StringComparer.OrdinalIgnoreCase));
                throw new InvalidOperationException(
                    "OPC UA endpoint does not support the configured security mode and policy. Available endpoints: " + available);
            }

            selected.EndpointUrl = RewriteEndpointUrl(selected.EndpointUrl, discoveryUri);
            return selected;
        }

        private static string RewriteEndpointUrl(string selectedEndpointUrl, Uri configuredUri)
        {
            if (!Uri.TryCreate(selectedEndpointUrl, UriKind.Absolute, out Uri selectedUri))
                return configuredUri.ToString();

            UriBuilder builder = new UriBuilder(selectedUri)
            {
                Scheme = configuredUri.Scheme,
                Host = configuredUri.Host,
                Port = configuredUri.Port
            };
            if (!string.IsNullOrWhiteSpace(configuredUri.AbsolutePath) && configuredUri.AbsolutePath != "/")
                builder.Path = configuredUri.AbsolutePath;
            return builder.Uri.ToString();
        }

        private static void TrustServerCertificate(
            ApplicationConfiguration configuration,
            EndpointDescription endpoint)
        {
            if (endpoint?.ServerCertificate == null || endpoint.ServerCertificate.Length == 0)
                return;

            using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(endpoint.ServerCertificate);
            lock (CertificateSyncRoot)
            {
                using ICertificateStore store = configuration.SecurityConfiguration.TrustedPeerCertificates.OpenStore(Telemetry);
                X509Certificate2Collection existing = store
                    .FindByThumbprintAsync(certificate.Thumbprint, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                try
                {
                    if (existing != null && existing.Count > 0)
                        return;

                    store.AddAsync(certificate, null, CancellationToken.None).GetAwaiter().GetResult();
                }
                finally
                {
                    if (existing != null)
                    {
                        foreach (X509Certificate2 item in existing)
                            item.Dispose();
                    }
                }
            }
        }
    }
}

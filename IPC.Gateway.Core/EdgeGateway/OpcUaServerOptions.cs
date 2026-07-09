/*----------------------------------------------------------------
* 项目名称 ：IPC.EdgeGateway
* 项目描述 ：
* 类 名 称 ：OpcUaServerOptions
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
using System;
using System.Globalization;

namespace IPC.EdgeGateway
{
    
    
    
    public sealed class OpcUaServerOptions
    {
        public const string SecurityPolicyNone = "None";
        public const string SecurityPolicyBasic256 = "Basic256";
        public const string SecurityPolicyBasic256Sha256 = "Basic256Sha256";

        public OpcUaServerOptions()
        {
            Enabled = false;
            ApplicationName = "IPC Gateway OPC UA Server";
            ApplicationUri = "urn:ipc-gateway:opcua";
            ProductUri = "urn:ipc-gateway";
            Host = "0.0.0.0";
            Port = 4840;
            EndpointPath = "IPC.Gateway";
            NamespaceUri = "urn:ipc-gateway:tags";
            CertificateStorePath = "Data/OpcUa/pki";
            AutoAcceptUntrustedCertificates = true;
            AllowAnonymous = true;
            UsernamePasswordEnabled = false;
            Username = string.Empty;
            UserPasswordHash = string.Empty;
            UserPasswordSalt = string.Empty;
            UserPasswordAlgorithm = OpcUaPasswordHasher.Algorithm;
            SecurityPolicy = string.Empty;
            AllowSecurityPolicyNone = true;
            EnableBasic256SignAndEncrypt = false;
            EnableBasic256Sha256SignAndEncrypt = false;
            MinimumSamplingIntervalMs = 250;
            PublishDiagnostics = true;
        }

        public bool Enabled { get; set; }
        public string ApplicationName { get; set; }
        public string ApplicationUri { get; set; }
        public string ProductUri { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public string EndpointPath { get; set; }
        public string NamespaceUri { get; set; }
        public string CertificateStorePath { get; set; }
        public bool AutoAcceptUntrustedCertificates { get; set; }
        public bool AllowAnonymous { get; set; }
        public bool UsernamePasswordEnabled { get; set; }
        public string Username { get; set; }
        public string UserPasswordHash { get; set; }
        public string UserPasswordSalt { get; set; }
        public string UserPasswordAlgorithm { get; set; }
        public string SecurityPolicy { get; set; }
        public bool AllowSecurityPolicyNone { get; set; }
        public bool EnableBasic256SignAndEncrypt { get; set; }
        public bool EnableBasic256Sha256SignAndEncrypt { get; set; }
        public int MinimumSamplingIntervalMs { get; set; }
        public bool PublishDiagnostics { get; set; }

        public string EndpointUrl
        {
            get
            {
                string host = string.IsNullOrWhiteSpace(Host) || Host.Trim() == "0.0.0.0" ? "localhost" : Host.Trim();
                string path = string.IsNullOrWhiteSpace(EndpointPath) ? "IPC.Gateway" : EndpointPath.Trim().Trim('/');
                return "opc.tcp://" + host + ":" + ClampPort(Port).ToString(CultureInfo.InvariantCulture) + "/" + path;
            }
        }

        public OpcUaServerOptions Clone()
        {
            return new OpcUaServerOptions
            {
                Enabled = Enabled,
                ApplicationName = ApplicationName,
                ApplicationUri = ApplicationUri,
                ProductUri = ProductUri,
                Host = Host,
                Port = Port,
                EndpointPath = EndpointPath,
                NamespaceUri = NamespaceUri,
                CertificateStorePath = CertificateStorePath,
                AutoAcceptUntrustedCertificates = AutoAcceptUntrustedCertificates,
                AllowAnonymous = AllowAnonymous,
                UsernamePasswordEnabled = UsernamePasswordEnabled,
                Username = Username,
                UserPasswordHash = UserPasswordHash,
                UserPasswordSalt = UserPasswordSalt,
                UserPasswordAlgorithm = UserPasswordAlgorithm,
                SecurityPolicy = SecurityPolicy,
                AllowSecurityPolicyNone = AllowSecurityPolicyNone,
                EnableBasic256SignAndEncrypt = EnableBasic256SignAndEncrypt,
                EnableBasic256Sha256SignAndEncrypt = EnableBasic256Sha256SignAndEncrypt,
                MinimumSamplingIntervalMs = MinimumSamplingIntervalMs,
                PublishDiagnostics = PublishDiagnostics
            };
        }

        public static OpcUaServerOptions Normalize(OpcUaServerOptions? options)
        {
            OpcUaServerOptions value = options == null ? new OpcUaServerOptions() : options.Clone();
            value.ApplicationName = string.IsNullOrWhiteSpace(value.ApplicationName) ? "IPC Gateway OPC UA Server" : value.ApplicationName.Trim();
            value.ApplicationUri = string.IsNullOrWhiteSpace(value.ApplicationUri) ? "urn:ipc-gateway:opcua" : value.ApplicationUri.Trim();
            value.ProductUri = string.IsNullOrWhiteSpace(value.ProductUri) ? "urn:ipc-gateway" : value.ProductUri.Trim();
            value.Host = string.IsNullOrWhiteSpace(value.Host) ? "0.0.0.0" : value.Host.Trim();
            value.Port = ClampPort(value.Port);
            value.EndpointPath = string.IsNullOrWhiteSpace(value.EndpointPath) ? "IPC.Gateway" : value.EndpointPath.Trim().Trim('/');
            value.NamespaceUri = string.IsNullOrWhiteSpace(value.NamespaceUri) ? "urn:ipc-gateway:tags" : value.NamespaceUri.Trim();
            value.CertificateStorePath = string.IsNullOrWhiteSpace(value.CertificateStorePath) ? "Data/OpcUa/pki" : value.CertificateStorePath.Trim();
            value.Username = value.Username == null ? string.Empty : value.Username.Trim();
            value.UserPasswordHash = value.UserPasswordHash == null ? string.Empty : value.UserPasswordHash.Trim();
            value.UserPasswordSalt = value.UserPasswordSalt == null ? string.Empty : value.UserPasswordSalt.Trim();
            value.UserPasswordAlgorithm = string.IsNullOrWhiteSpace(value.UserPasswordAlgorithm) ? OpcUaPasswordHasher.Algorithm : value.UserPasswordAlgorithm.Trim();
            value.UsernamePasswordEnabled = value.UsernamePasswordEnabled && !string.IsNullOrWhiteSpace(value.Username);
            if (!value.AllowAnonymous && !value.UsernamePasswordEnabled)
                value.AllowAnonymous = true;
            value.SecurityPolicy = NormalizeSecurityPolicy(value.SecurityPolicy, value);
            value.AllowSecurityPolicyNone = string.Equals(value.SecurityPolicy, SecurityPolicyNone, StringComparison.OrdinalIgnoreCase);
            value.EnableBasic256SignAndEncrypt = string.Equals(value.SecurityPolicy, SecurityPolicyBasic256, StringComparison.OrdinalIgnoreCase);
            value.EnableBasic256Sha256SignAndEncrypt = string.Equals(value.SecurityPolicy, SecurityPolicyBasic256Sha256, StringComparison.OrdinalIgnoreCase);
            value.MinimumSamplingIntervalMs = ClampSamplingInterval(value.MinimumSamplingIntervalMs);
            return value;
        }

        private static string NormalizeSecurityPolicy(string? policy, OpcUaServerOptions value)
        {
            string text = policy == null ? string.Empty : policy.Trim();
            if (string.Equals(text, SecurityPolicyNone, StringComparison.OrdinalIgnoreCase))
                return SecurityPolicyNone;
            if (string.Equals(text, SecurityPolicyBasic256, StringComparison.OrdinalIgnoreCase))
                return SecurityPolicyBasic256;
            if (string.Equals(text, SecurityPolicyBasic256Sha256, StringComparison.OrdinalIgnoreCase))
                return SecurityPolicyBasic256Sha256;

            if (value.UsernamePasswordEnabled && value.EnableBasic256SignAndEncrypt)
                return SecurityPolicyBasic256;
            if (value.UsernamePasswordEnabled && value.EnableBasic256Sha256SignAndEncrypt)
                return SecurityPolicyBasic256Sha256;
            if (value.AllowSecurityPolicyNone)
                return SecurityPolicyNone;
            if (value.EnableBasic256SignAndEncrypt)
                return SecurityPolicyBasic256;
            if (value.EnableBasic256Sha256SignAndEncrypt)
                return SecurityPolicyBasic256Sha256;
            return SecurityPolicyNone;
        }

        public static int ClampPort(int port)
        {
            if (port < 1)
                return 4840;
            if (port > 65535)
                return 65535;
            return port;
        }

        public static int ClampSamplingInterval(int value)
        {
            if (value < 100)
                return 100;
            if (value > 60000)
                return 60000;
            return value;
        }
    }
}

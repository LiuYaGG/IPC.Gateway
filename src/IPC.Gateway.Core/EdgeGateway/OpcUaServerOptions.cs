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
        public const string SecurityPolicyBasic128Rsa15 = "Basic128Rsa15";
        public const string SecurityPolicyBasic256 = "Basic256";
        public const string SecurityPolicyBasic256Sha256 = "Basic256Sha256";
        public const string SecurityPolicyAes128Sha256RsaOaep = "Aes128_Sha256_RsaOaep";
        public const string SecurityPolicyAes256Sha256RsaPss = "Aes256_Sha256_RsaPss";
        public const string SecurityModeNone = "None";
        public const string SecurityModeSign = "Sign";
        public const string SecurityModeSignAndEncrypt = "SignAndEncrypt";

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
            SecurityMode = string.Empty;
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
        // Empty in older configurations; infer the previous mode from the policy.
        public string SecurityMode { get; set; }
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
                SecurityMode = SecurityMode,
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
            value.SecurityMode = NormalizeSecurityMode(value.SecurityMode, value.SecurityPolicy);
            if ((value.SecurityMode == SecurityModeNone) != (value.SecurityPolicy == SecurityPolicyNone))
                throw new ArgumentException("OPC UA 安全模式 None 只能搭配安全策略 None；签名或签名与加密必须选择非 None 策略。");
            value.AllowSecurityPolicyNone = string.Equals(value.SecurityPolicy, SecurityPolicyNone, StringComparison.OrdinalIgnoreCase);
            value.EnableBasic256SignAndEncrypt = value.SecurityMode == SecurityModeSignAndEncrypt && value.SecurityPolicy == SecurityPolicyBasic256;
            value.EnableBasic256Sha256SignAndEncrypt = value.SecurityMode == SecurityModeSignAndEncrypt && value.SecurityPolicy == SecurityPolicyBasic256Sha256;
            value.MinimumSamplingIntervalMs = ClampSamplingInterval(value.MinimumSamplingIntervalMs);
            return value;
        }

        private static string NormalizeSecurityPolicy(string? policy, OpcUaServerOptions value)
        {
            string text = policy == null ? string.Empty : policy.Trim();
            if (text.Length > 0)
            {
                foreach (string supported in new[]
                {
                    SecurityPolicyNone, SecurityPolicyBasic128Rsa15, SecurityPolicyBasic256,
                    SecurityPolicyBasic256Sha256, SecurityPolicyAes128Sha256RsaOaep, SecurityPolicyAes256Sha256RsaPss
                })
                {
                    if (string.Equals(text, supported, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(text, "http://opcfoundation.org/UA/SecurityPolicy#" + supported, StringComparison.OrdinalIgnoreCase))
                        return supported;
                }
                throw new ArgumentException("不支持的 OPC UA 安全策略：" + text);
            }

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

        private static string NormalizeSecurityMode(string? mode, string policy)
        {
            if (string.IsNullOrWhiteSpace(mode))
                return policy == SecurityPolicyNone ? SecurityModeNone : SecurityModeSignAndEncrypt;
            foreach (string supported in new[] { SecurityModeNone, SecurityModeSign, SecurityModeSignAndEncrypt })
            {
                if (string.Equals(mode.Trim(), supported, StringComparison.OrdinalIgnoreCase))
                    return supported;
            }
            throw new ArgumentException("不支持的 OPC UA 安全模式：" + mode);
        }

        public static int ClampPort(int port)
        {
            if (port < 1)
                return 4840;
            if (port > 65535)
                return 65535;
            return port;
        }

        public static string ResolveCertificateStorePath(string? configuredPath)
        {
            string path = string.IsNullOrWhiteSpace(configuredPath)
                ? Path.Combine("Data", "OpcUa", "pki")
                : configuredPath.Trim();
            return Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
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

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
            value.MinimumSamplingIntervalMs = ClampSamplingInterval(value.MinimumSamplingIntervalMs);
            return value;
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

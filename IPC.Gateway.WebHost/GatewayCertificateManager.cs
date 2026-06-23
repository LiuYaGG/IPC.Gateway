/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.WebHost
* 项目描述 ：
* 类 名 称 ：GatewayCertificateManager
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.WebHost
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
using System.Security.Cryptography.X509Certificates;
using IPC.Gateway.Core.Gateway;
using IPC.Plc.Communication.Core;
using IPC.Runtime.Configuration;

namespace IPC.Gateway.WebHost;

public sealed class GatewayCertificateManager
{
    private readonly IConfiguration _configuration;
    private readonly GatewayIndustrialSecurityOptions _securityOptions;
    private readonly GatewayCoreService _gateway;

    public GatewayCertificateManager(IConfiguration configuration, GatewayIndustrialSecurityOptions securityOptions, GatewayCoreService gateway)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _securityOptions = securityOptions ?? new GatewayIndustrialSecurityOptions();
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    public GatewayCertificateInventory GetInventory()
    {
        GatewayCertificateInventory inventory = new GatewayCertificateInventory
        {
            ExpiringSoonDays = Math.Max(1, _securityOptions.Certificates.ExpiringSoonDays)
        };

        if (_securityOptions.Certificates.IncludeTlsCertificate)
            AddTlsCertificate(inventory);
        AddMqttClientCertificate(inventory);
        AddDeviceCertificates(inventory);
        if (_securityOptions.Certificates.IncludeOpcUaCertificateStore)
            AddCertificateDirectory(inventory, "OPC UA", _configuration["Gateway:OpcUa:CertificateStorePath"]);

        inventory.TotalCount = inventory.Certificates.Count;
        inventory.ExpiredCount = inventory.Certificates.Count(item => item.State.Equals("Expired", StringComparison.OrdinalIgnoreCase));
        inventory.ExpiringSoonCount = inventory.Certificates.Count(item => item.State.Equals("ExpiringSoon", StringComparison.OrdinalIgnoreCase));
        inventory.HealthyCount = inventory.Certificates.Count(item => item.State.Equals("Healthy", StringComparison.OrdinalIgnoreCase));
        return inventory;
    }

    private void AddMqttClientCertificate(GatewayCertificateInventory inventory)
    {
        try
        {
            IPC.EdgeGateway.MqttGatewayOptions mqtt = _gateway.CurrentMqttOptions;
            if (!mqtt.UseTls || string.IsNullOrWhiteSpace(mqtt.ClientCertificatePath))
                return;

            AddCertificateFile(inventory, "MQTT 客户端", ResolvePath(mqtt.ClientCertificatePath), mqtt.ClientCertificatePassword);
        }
        catch (Exception ex)
        {
            inventory.Certificates.Add(GatewayCertificateInfo.Error("MQTT 客户端", string.Empty, ex.Message));
        }
    }

    private void AddDeviceCertificates(GatewayCertificateInventory inventory)
    {
        try
        {
            ProjectConfig project = _gateway.CurrentProject;
            foreach (DeviceConfig device in project.Devices ?? new List<DeviceConfig>())
            {
                PlcConnectionOptions? connection = device.Connection;
                if (connection == null || string.IsNullOrWhiteSpace(connection.CertificatePath))
                    continue;

                string source = string.IsNullOrWhiteSpace(device.Name) ? "设备证书" : "设备证书：" + device.Name;
                AddCertificateFile(inventory, source, ResolvePath(connection.CertificatePath), connection.CertificatePassword);
            }
        }
        catch (Exception ex)
        {
            inventory.Certificates.Add(GatewayCertificateInfo.Error("设备证书", string.Empty, ex.Message));
        }
    }

    private void AddTlsCertificate(GatewayCertificateInventory inventory)
    {
        string path = _securityOptions.Tls.CertificatePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            inventory.Certificates.Add(GatewayCertificateInfo.Missing("TLS", "未配置 TLS 证书文件。"));
            return;
        }

        AddCertificateFile(inventory, "TLS", ResolvePath(path), _securityOptions.Tls.CertificatePassword);
    }

    private void AddCertificateDirectory(GatewayCertificateInventory inventory, string source, string? configuredPath)
    {
        string path = string.IsNullOrWhiteSpace(configuredPath) ? string.Empty : ResolvePath(configuredPath);
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            inventory.Certificates.Add(GatewayCertificateInfo.Missing(source, "证书目录不存在或未配置。"));
            return;
        }

        string[] extensions = { "*.pfx", "*.p12", "*.cer", "*.crt", "*.der", "*.pem" };
        foreach (string extension in extensions)
        {
            foreach (string file in Directory.EnumerateFiles(path, extension, SearchOption.AllDirectories))
                AddCertificateFile(inventory, source, file, string.Empty);
        }
    }

    private void AddCertificateFile(GatewayCertificateInventory inventory, string source, string path, string password)
    {
        try
        {
            if (!File.Exists(path))
            {
                inventory.Certificates.Add(GatewayCertificateInfo.Missing(source, "证书文件不存在：" + path));
                return;
            }

            using X509Certificate2 certificate = LoadCertificate(path, password);
            inventory.Certificates.Add(GatewayCertificateInfo.FromCertificate(source, path, certificate, inventory.ExpiringSoonDays));
        }
        catch (Exception ex)
        {
            inventory.Certificates.Add(GatewayCertificateInfo.Error(source, path, ex.Message));
        }
    }

    private static X509Certificate2 LoadCertificate(string path, string password)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".pfx" || extension == ".p12")
        {
            return X509CertificateLoader.LoadPkcs12FromFile(
                path,
                password ?? string.Empty,
                X509KeyStorageFlags.EphemeralKeySet);
        }

        return X509CertificateLoader.LoadCertificateFromFile(path);
    }

    private static string ResolvePath(string path)
    {
        string value = string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return Path.IsPathRooted(value) ? value : Path.Combine(AppContext.BaseDirectory, value);
    }
}

public sealed class GatewayCertificateInventory
{
    public int ExpiringSoonDays { get; set; }
    public int TotalCount { get; set; }
    public int HealthyCount { get; set; }
    public int ExpiringSoonCount { get; set; }
    public int ExpiredCount { get; set; }
    public IList<GatewayCertificateInfo> Certificates { get; set; } = new List<GatewayCertificateInfo>();
}

public sealed class GatewayCertificateInfo
{
    public string Source { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Thumbprint { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public DateTime NotBefore { get; set; }
    public DateTime NotAfter { get; set; }
    public int DaysRemaining { get; set; }
    public bool HasPrivateKey { get; set; }
    public string State { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;

    public static GatewayCertificateInfo FromCertificate(string source, string path, X509Certificate2 certificate, int expiringSoonDays)
    {
        DateTime now = DateTime.Now;
        int daysRemaining = (int)Math.Floor((certificate.NotAfter - now).TotalDays);
        string state = certificate.NotAfter <= now
            ? "Expired"
            : daysRemaining <= expiringSoonDays ? "ExpiringSoon" : "Healthy";

        return new GatewayCertificateInfo
        {
            Source = source,
            Path = path,
            Subject = certificate.Subject,
            Issuer = certificate.Issuer,
            Thumbprint = certificate.Thumbprint ?? string.Empty,
            SerialNumber = certificate.SerialNumber ?? string.Empty,
            NotBefore = certificate.NotBefore,
            NotAfter = certificate.NotAfter,
            DaysRemaining = daysRemaining,
            HasPrivateKey = certificate.HasPrivateKey,
            State = state
        };
    }

    public static GatewayCertificateInfo Missing(string source, string message)
    {
        return new GatewayCertificateInfo
        {
            Source = source,
            State = "Missing",
            ErrorMessage = message ?? string.Empty
        };
    }

    public static GatewayCertificateInfo Error(string source, string path, string message)
    {
        return new GatewayCertificateInfo
        {
            Source = source,
            Path = path,
            State = "Error",
            ErrorMessage = message ?? string.Empty
        };
    }
}

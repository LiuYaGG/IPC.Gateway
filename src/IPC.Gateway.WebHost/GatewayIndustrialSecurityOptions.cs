/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.WebHost
* 项目描述 ：
* 类 名 称 ：GatewayIndustrialSecurityOptions
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
using System.Security.Authentication;

namespace IPC.Gateway.WebHost;

public sealed class GatewayIndustrialSecurityOptions
{
    public GatewayTlsOptions Tls { get; set; } = new GatewayTlsOptions();
    public GatewayApiSecurityOptions Api { get; set; } = new GatewayApiSecurityOptions();
    public GatewayApiTokenOptions ApiTokens { get; set; } = new GatewayApiTokenOptions();
    public GatewayCertificateManagementOptions Certificates { get; set; } = new GatewayCertificateManagementOptions();

    public static GatewayIndustrialSecurityOptions FromConfiguration(IConfiguration configuration)
    {
        IConfigurationSection tls = configuration.GetSection("Gateway:Security:Tls");
        IConfigurationSection api = configuration.GetSection("Gateway:Security:Api");
        IConfigurationSection apiTokens = configuration.GetSection("Gateway:Security:ApiTokens");
        IConfigurationSection certs = configuration.GetSection("Gateway:Security:Certificates");
        return new GatewayIndustrialSecurityOptions
        {
            Tls = new GatewayTlsOptions
            {
                RequireHttps = GetBool(tls, "RequireHttps", false),
                EnableHttpsRedirection = GetBool(tls, "EnableHttpsRedirection", false),
                EnableHsts = GetBool(tls, "EnableHsts", false),
                HstsMaxAgeDays = GetInt(tls, "HstsMaxAgeDays", 180),
                CertificatePath = tls["CertificatePath"] ?? string.Empty,
                CertificatePassword = tls["CertificatePassword"] ?? string.Empty,
                HttpsPort = GetInt(tls, "HttpsPort", 0),
                MinimumProtocol = tls["MinimumProtocol"] ?? "Tls12"
            },
            Api = new GatewayApiSecurityOptions
            {
                RequireAuthenticationForHealth = GetBool(api, "RequireAuthenticationForHealth", false),
                AuditUnauthorizedRequests = GetBool(api, "AuditUnauthorizedRequests", true),
                AuditForbiddenRequests = GetBool(api, "AuditForbiddenRequests", true),
                AuditConfigurationRequestHash = GetBool(api, "AuditConfigurationRequestHash", true),
                MaxAuditedBodyBytes = GetInt(api, "MaxAuditedBodyBytes", 1024 * 1024)
            },
            ApiTokens = new GatewayApiTokenOptions
            {
                Enabled = GetBool(apiTokens, "Enabled", true),
                HeaderName = apiTokens["HeaderName"] ?? "X-API-Token",
                RequireHttps = GetBool(apiTokens, "RequireHttps", false),
                Tokens = apiTokens.GetSection("Tokens").Get<List<GatewayApiTokenDefinition>>() ?? new List<GatewayApiTokenDefinition>()
            },
            Certificates = new GatewayCertificateManagementOptions
            {
                IncludeTlsCertificate = GetBool(certs, "IncludeTlsCertificate", true),
                IncludeOpcUaCertificateStore = GetBool(certs, "IncludeOpcUaCertificateStore", true),
                ExpiringSoonDays = GetInt(certs, "ExpiringSoonDays", 30)
            }
        };
    }

    private static bool GetBool(IConfiguration configuration, string key, bool defaultValue)
    {
        return bool.TryParse(configuration[key], out bool parsed) ? parsed : defaultValue;
    }

    private static int GetInt(IConfiguration configuration, string key, int defaultValue)
    {
        return int.TryParse(configuration[key], out int parsed) ? parsed : defaultValue;
    }
}

public sealed class GatewayTlsOptions
{
    public bool RequireHttps { get; set; }
    public bool EnableHttpsRedirection { get; set; }
    public bool EnableHsts { get; set; }
    public int HstsMaxAgeDays { get; set; } = 180;
    public string CertificatePath { get; set; } = string.Empty;
    public string CertificatePassword { get; set; } = string.Empty;
    public int HttpsPort { get; set; }
    public string MinimumProtocol { get; set; } = "Tls12";

    public SslProtocols ResolveMinimumProtocol()
    {
        return MinimumProtocol.Equals("Tls13", StringComparison.OrdinalIgnoreCase)
            ? SslProtocols.Tls13
            : SslProtocols.Tls12;
    }
}

public sealed class GatewayApiSecurityOptions
{
    public bool RequireAuthenticationForHealth { get; set; }
    public bool AuditUnauthorizedRequests { get; set; } = true;
    public bool AuditForbiddenRequests { get; set; } = true;
    public bool AuditConfigurationRequestHash { get; set; } = true;
    public int MaxAuditedBodyBytes { get; set; } = 1024 * 1024;
}

public sealed class GatewayApiTokenOptions
{
    public bool Enabled { get; set; } = true;
    public string HeaderName { get; set; } = "X-API-Token";
    public bool RequireHttps { get; set; }
    public IList<GatewayApiTokenDefinition> Tokens { get; set; } = new List<GatewayApiTokenDefinition>();
}

public sealed class GatewayApiTokenDefinition
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string TokenHash { get; set; } = string.Empty;
    public string Role { get; set; } = "ApiToken";
    public IList<string> Permissions { get; set; } = new List<string>();
    public DateTime ExpiresUtc { get; set; }
}

public sealed class GatewayCertificateManagementOptions
{
    public bool IncludeTlsCertificate { get; set; } = true;
    public bool IncludeOpcUaCertificateStore { get; set; } = true;
    public int ExpiringSoonDays { get; set; } = 30;
}

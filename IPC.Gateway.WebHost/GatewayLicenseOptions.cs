using Microsoft.Extensions.Configuration;

namespace IPC.Gateway.WebHost;

public sealed class GatewayLicenseOptions
{
    public string ProductId { get; set; } = "IPC.Gateway";
    public string LicenseFile { get; set; } = string.Empty;
    public string LicenseText { get; set; } = string.Empty;
    public string TrustedPublicKeyPem { get; set; } = string.Empty;
    public string TrustedPublicKeyFile { get; set; } = string.Empty;
    public bool RequireValidLicense { get; set; }
    public bool RequireMachineBinding { get; set; } = true;
    public string MachineIdOverride { get; set; } = string.Empty;

    public static GatewayLicenseOptions FromConfiguration(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection("Gateway:License");
        return new GatewayLicenseOptions
        {
            ProductId = section["ProductId"] ?? "IPC.Gateway",
            LicenseFile = section["LicenseFile"] ?? string.Empty,
            LicenseText = section["LicenseText"] ?? string.Empty,
            TrustedPublicKeyPem = section["TrustedPublicKeyPem"] ?? string.Empty,
            TrustedPublicKeyFile = section["TrustedPublicKeyFile"] ?? string.Empty,
            RequireValidLicense = GetBool(section, "RequireValidLicense", false),
            RequireMachineBinding = GetBool(section, "RequireMachineBinding", true),
            MachineIdOverride = section["MachineIdOverride"] ?? string.Empty
        };
    }

    private static bool GetBool(IConfiguration section, string key, bool defaultValue)
    {
        string? value = section[key];
        return bool.TryParse(value, out bool parsed) ? parsed : defaultValue;
    }
}

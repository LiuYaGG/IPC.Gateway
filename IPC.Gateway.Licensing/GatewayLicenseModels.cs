namespace IPC.Gateway.Licensing;

public sealed class GatewayLicensePayload
{
    public int SchemaVersion { get; set; } = 2;
    public string ProductId { get; set; } = "IPC.Gateway";
    public string MachineCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string Edition { get; set; } = "Commercial";
    public string SerialNumber { get; set; } = string.Empty;
    public DateTime IssuedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public int MaxDevices { get; set; }
    public int MaxTags { get; set; }
    public IList<string> Features { get; set; } = new List<string>();
    public string Signature { get; set; } = string.Empty;
}

public sealed class GatewayLicenseRequest
{
    public int SchemaVersion { get; set; } = 1;
    public string ProductId { get; set; } = "IPC.Gateway";
    public string MachineCode { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string OperatingSystem { get; set; } = string.Empty;
    public DateTime GeneratedUtc { get; set; }
    public string Nonce { get; set; } = string.Empty;
}

public sealed class GatewayLicenseStatus
{
    public bool Configured { get; set; }
    public bool Valid { get; set; }
    public bool Operational { get; set; }
    public bool Expired { get; set; }
    public bool SignatureVerified { get; set; }
    public bool MachineMatched { get; set; }
    public bool RequireValidLicense { get; set; }
    public bool RequireMachineBinding { get; set; }
    public string ProductId { get; set; } = "IPC.Gateway";
    public string MachineCode { get; set; } = string.Empty;
    public string LicensedMachineCode { get; set; } = string.Empty;
    public string RequestCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string Edition { get; set; } = "Community";
    public string SerialNumber { get; set; } = string.Empty;
    public DateTime IssuedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public int MaxDevices { get; set; }
    public int MaxTags { get; set; }
    public IList<string> Features { get; set; } = new List<string>();
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

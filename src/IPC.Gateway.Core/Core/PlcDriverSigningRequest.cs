namespace IPC.Plc.Communication.Core;

public sealed class PlcDriverSigningInfo
{
    public string AssemblySha256 { get; set; } = string.Empty;
    public string PayloadBase64 { get; set; } = string.Empty;
    public string TrustedPublicKeyPem { get; set; } = string.Empty;
}

public sealed class PlcDriverSigningRequest
{
    public string AssemblySha256 { get; set; } = string.Empty;
    public string PublicKeyPem { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public string Signer { get; set; } = string.Empty;
}

public sealed class PlcDriverTrustPolicy
{
    public bool RequireSignature { get; set; }
    public bool PublicKeyConfigured { get; set; }
}

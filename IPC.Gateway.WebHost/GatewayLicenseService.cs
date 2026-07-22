using System.Security.Cryptography;
using IPC.Gateway.Licensing;

namespace IPC.Gateway.WebHost;

public sealed class GatewayLicenseService
{
    private const int MaxLicenseBytes = 256 * 1024;
    private readonly GatewayLicenseOptions _options;
    private readonly string _machineCode;

    public GatewayLicenseService(GatewayLicenseOptions options)
    {
        _options = options ?? new GatewayLicenseOptions();
        _machineCode = GatewayMachineIdentity.CreateMachineCode(_options.ProductId, _options.MachineIdOverride);
    }

    public GatewayLicenseStatus GetStatus()
    {
        string licenseText = ReadLicenseText();
        if (string.IsNullOrWhiteSpace(licenseText))
            return CreateMissingStatus();

        try
        {
            return Validate(GatewayLicenseCryptography.DeserializeLicense(licenseText));
        }
        catch (Exception ex)
        {
            return CreateInvalidStatus("License JSON is invalid: " + ex.Message);
        }
    }

    public string GetMachineCode() => _machineCode;

    public string GetRequestCode()
    {
        return GatewayLicenseCryptography.EncodeRequest(new GatewayLicenseRequest
        {
            ProductId = _options.ProductId,
            MachineCode = _machineCode,
            MachineName = Environment.MachineName,
            OperatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            GeneratedUtc = DateTime.UtcNow,
            Nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(8))
        });
    }

    public GatewayLicenseStatus Install(string licenseText)
    {
        if (string.IsNullOrWhiteSpace(licenseText))
            throw new ArgumentException("License content is empty.", nameof(licenseText));
        if (System.Text.Encoding.UTF8.GetByteCount(licenseText) > MaxLicenseBytes)
            throw new ArgumentException("License content is too large.", nameof(licenseText));
        if (!string.IsNullOrWhiteSpace(_options.LicenseText))
            throw new InvalidOperationException("LicenseText is configured and overrides file-based license installation.");
        if (string.IsNullOrWhiteSpace(_options.LicenseFile))
            throw new InvalidOperationException("LicenseFile is not configured.");

        GatewayLicensePayload payload = GatewayLicenseCryptography.DeserializeLicense(licenseText);
        GatewayLicenseStatus status = Validate(payload);
        if (!status.Valid || !status.Operational)
            throw new InvalidOperationException(status.Message);

        string path = ResolvePath(_options.LicenseFile);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        string temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporaryPath, GatewayLicenseCryptography.SerializeLicense(payload));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
        return GetStatus();
    }

    internal static string BuildSignaturePayload(GatewayLicensePayload payload) => GatewayLicenseCryptography.BuildSignaturePayload(payload);

    private GatewayLicenseStatus Validate(GatewayLicensePayload payload)
    {
        GatewayLicenseStatus status = new GatewayLicenseStatus
        {
            Configured = true,
            ProductId = payload.ProductId ?? string.Empty,
            MachineCode = _machineCode,
            LicensedMachineCode = GatewayMachineIdentity.NormalizeMachineCode(payload.MachineCode),
            CustomerName = payload.CustomerName ?? string.Empty,
            Edition = string.IsNullOrWhiteSpace(payload.Edition) ? "Commercial" : payload.Edition,
            SerialNumber = payload.SerialNumber ?? string.Empty,
            IssuedUtc = payload.IssuedUtc,
            ExpiresUtc = payload.ExpiresUtc,
            MaxDevices = payload.MaxDevices,
            MaxTags = payload.MaxTags,
            Features = (payload.Features ?? Array.Empty<string>()).ToList(),
            RequireValidLicense = _options.RequireValidLicense,
            RequireMachineBinding = _options.RequireMachineBinding,
            RequestCode = GetRequestCode()
        };

        if (!string.Equals(payload.ProductId, _options.ProductId, StringComparison.OrdinalIgnoreCase))
            return MarkInvalid(status, "License product does not match this gateway.");

        if (payload.IssuedUtc != DateTime.MinValue && payload.IssuedUtc.ToUniversalTime() > DateTime.UtcNow.AddMinutes(10))
            return MarkInvalid(status, "License issue time is in the future.");
        if (payload.ExpiresUtc != DateTime.MinValue && payload.IssuedUtc != DateTime.MinValue && payload.ExpiresUtc <= payload.IssuedUtc)
            return MarkInvalid(status, "License expiration time must be later than its issue time.");

        status.Expired = payload.ExpiresUtc != DateTime.MinValue && payload.ExpiresUtc.ToUniversalTime() < DateTime.UtcNow;
        if (status.Expired)
            return MarkInvalid(status, "License is expired.");

        if (string.IsNullOrWhiteSpace(payload.Signature))
        {
            status.SignatureVerified = false;
            status.Valid = !_options.RequireValidLicense;
            status.Operational = !_options.RequireValidLicense;
            status.Status = status.Valid ? "Unsigned" : "Invalid";
            status.Message = status.Valid ? "Unsigned license accepted because enforcement is disabled." : "License signature is required.";
            return status;
        }

        string trustedPublicKeyPem = ReadTrustedPublicKeyPem();
        if (string.IsNullOrWhiteSpace(trustedPublicKeyPem))
            return MarkInvalid(status, "Trusted license public key is not configured.");

        try
        {
            using RSA rsa = RSA.Create();
            rsa.ImportFromPem(trustedPublicKeyPem);
            status.SignatureVerified = GatewayLicenseCryptography.Verify(payload, rsa);
        }
        catch (Exception ex)
        {
            return MarkInvalid(status, "License signature verification failed: " + ex.Message);
        }

        if (!status.SignatureVerified)
            return MarkInvalid(status, "License signature verification failed.");

        status.MachineMatched = string.Equals(
            GatewayMachineIdentity.NormalizeMachineCode(payload.MachineCode),
            _machineCode,
            StringComparison.Ordinal);
        if (_options.RequireMachineBinding && !status.MachineMatched)
            return MarkInvalid(status, string.IsNullOrWhiteSpace(payload.MachineCode)
                ? "License is not bound to a machine."
                : "License is bound to a different machine.");

        status.Valid = true;
        status.Operational = true;
        status.Status = "Valid";
        status.Message = "License is valid.";
        return status;
    }

    private string ReadLicenseText()
    {
        if (!string.IsNullOrWhiteSpace(_options.LicenseText))
            return _options.LicenseText;
        string path = ResolvePath(_options.LicenseFile);
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            return File.ReadAllText(path);
        return string.Empty;
    }

    private string ReadTrustedPublicKeyPem()
    {
        if (!string.IsNullOrWhiteSpace(_options.TrustedPublicKeyPem))
            return _options.TrustedPublicKeyPem;
        string path = ResolvePath(_options.TrustedPublicKeyFile);
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    private static string ResolvePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        return Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(path, AppContext.BaseDirectory);
    }

    private GatewayLicenseStatus CreateMissingStatus()
    {
        return new GatewayLicenseStatus
        {
            ProductId = _options.ProductId,
            MachineCode = _machineCode,
            RequestCode = GetRequestCode(),
            RequireValidLicense = _options.RequireValidLicense,
            RequireMachineBinding = _options.RequireMachineBinding,
            Valid = !_options.RequireValidLicense,
            Operational = !_options.RequireValidLicense,
            Status = _options.RequireValidLicense ? "Missing" : "Community",
            Message = _options.RequireValidLicense ? "No license is configured." : "No commercial license is configured; enforcement is disabled."
        };
    }

    private GatewayLicenseStatus CreateInvalidStatus(string message)
    {
        return MarkInvalid(new GatewayLicenseStatus
        {
            ProductId = _options.ProductId,
            MachineCode = _machineCode,
            RequestCode = GetRequestCode(),
            Configured = true,
            RequireValidLicense = _options.RequireValidLicense,
            RequireMachineBinding = _options.RequireMachineBinding
        }, message);
    }

    private static GatewayLicenseStatus MarkInvalid(GatewayLicenseStatus status, string message)
    {
        status.Valid = false;
        status.Operational = false;
        status.Status = "Invalid";
        status.Message = message;
        return status;
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IPC.Gateway.Licensing;

public static class GatewayLicenseCryptography
{
    private const string RequestPrefix = "IPCGW-REQ1-";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string BuildSignaturePayload(GatewayLicensePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        IList<string> features = payload.Features ?? Array.Empty<string>();
        string featureText = string.Join(",", features
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase));

        if (payload.SchemaVersion <= 1)
        {
            return string.Join(
                "\n",
                payload.ProductId ?? string.Empty,
                payload.CustomerName ?? string.Empty,
                payload.Edition ?? string.Empty,
                payload.SerialNumber ?? string.Empty,
                ToUtcText(payload.IssuedUtc),
                ToUtcText(payload.ExpiresUtc),
                payload.MaxDevices.ToString(CultureInfo.InvariantCulture),
                payload.MaxTags.ToString(CultureInfo.InvariantCulture),
                featureText);
        }

        return string.Join(
            "\n",
            "IPC.Gateway.License/v2",
            payload.ProductId ?? string.Empty,
            GatewayMachineIdentity.NormalizeMachineCode(payload.MachineCode),
            payload.CustomerName ?? string.Empty,
            payload.Edition ?? string.Empty,
            payload.SerialNumber ?? string.Empty,
            ToUtcText(payload.IssuedUtc),
            ToUtcText(payload.ExpiresUtc),
            payload.MaxDevices.ToString(CultureInfo.InvariantCulture),
            payload.MaxTags.ToString(CultureInfo.InvariantCulture),
            featureText);
    }

    public static bool Verify(GatewayLicensePayload payload, RSA publicKey)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(publicKey);
        if (string.IsNullOrWhiteSpace(payload.Signature))
            return false;

        byte[] data = Encoding.UTF8.GetBytes(BuildSignaturePayload(payload));
        byte[] signature = Convert.FromBase64String(payload.Signature.Trim());
        RSASignaturePadding padding = payload.SchemaVersion <= 1
            ? RSASignaturePadding.Pkcs1
            : RSASignaturePadding.Pss;
        return publicKey.VerifyData(data, signature, HashAlgorithmName.SHA256, padding);
    }

    public static string SerializeLicense(GatewayLicensePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static GatewayLicensePayload DeserializeLicense(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("License content is empty.", nameof(json));
        return JsonSerializer.Deserialize<GatewayLicensePayload>(json, JsonOptions)
            ?? throw new InvalidDataException("License payload is empty.");
    }

    public static string EncodeRequest(GatewayLicenseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string json = JsonSerializer.Serialize(request, JsonOptions);
        return RequestPrefix + Base64UrlEncode(Encoding.UTF8.GetBytes(json));
    }

    public static GatewayLicenseRequest DecodeRequest(string requestCode)
    {
        string normalized = (requestCode ?? string.Empty).Trim();
        if (!normalized.StartsWith(RequestPrefix, StringComparison.OrdinalIgnoreCase))
            throw new FormatException("The request code format is not recognized.");

        byte[] json = Base64UrlDecode(normalized[RequestPrefix.Length..]);
        GatewayLicenseRequest? request = JsonSerializer.Deserialize<GatewayLicenseRequest>(json, JsonOptions);
        if (request == null || string.IsNullOrWhiteSpace(request.ProductId) || string.IsNullOrWhiteSpace(request.MachineCode))
            throw new FormatException("The request code does not contain a valid product and machine code.");
        request.MachineCode = GatewayMachineIdentity.NormalizeMachineCode(request.MachineCode);
        return request;
    }

    private static string ToUtcText(DateTime value)
    {
        return value == DateTime.MinValue ? DateTime.MinValue.ToString("O") : value.ToUniversalTime().ToString("O");
    }

    private static string Base64UrlEncode(byte[] value)
    {
        return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        return Convert.FromBase64String(padded);
    }
}

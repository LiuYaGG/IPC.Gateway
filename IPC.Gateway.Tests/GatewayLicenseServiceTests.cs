using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IPC.Gateway.Licensing;
using IPC.Gateway.WebHost;

namespace IPC.Gateway.Tests;

public sealed class GatewayLicenseServiceTests
{
    [Fact]
    public void GetStatus_AcceptsSignedLicense_WhenTrustedKeyMatches()
    {
        using RSA rsa = RSA.Create(2048);
        GatewayLicensePayload payload = new GatewayLicensePayload
        {
            ProductId = "IPC.Gateway",
            MachineCode = GatewayMachineIdentity.CreateMachineCode("IPC.Gateway", "factory-a-machine"),
            CustomerName = "Factory A",
            Edition = "Commercial",
            SerialNumber = "LIC-001",
            IssuedUtc = DateTime.UtcNow.AddDays(-1),
            ExpiresUtc = DateTime.UtcNow.AddDays(30),
            MaxDevices = 100,
            MaxTags = 10000,
            Features = new List<string> { "device-templates", "tag-bulk" }
        };
        Sign(payload, rsa);

        GatewayLicenseService service = new GatewayLicenseService(new GatewayLicenseOptions
        {
            ProductId = "IPC.Gateway",
            RequireValidLicense = true,
            RequireMachineBinding = true,
            MachineIdOverride = "factory-a-machine",
            TrustedPublicKeyPem = rsa.ExportSubjectPublicKeyInfoPem(),
            LicenseText = JsonSerializer.Serialize(payload)
        });

        GatewayLicenseStatus status = service.GetStatus();

        Assert.True(status.Valid);
        Assert.True(status.Operational);
        Assert.True(status.SignatureVerified);
        Assert.Equal("Commercial", status.Edition);
        Assert.True(status.MachineMatched);
        Assert.Equal(payload.MachineCode, status.MachineCode);
    }

    [Fact]
    public void GetStatus_RejectsMissingLicense_WhenRequired()
    {
        GatewayLicenseService service = new GatewayLicenseService(new GatewayLicenseOptions
        {
            RequireValidLicense = true
        });

        GatewayLicenseStatus status = service.GetStatus();

        Assert.False(status.Valid);
        Assert.False(status.Operational);
        Assert.Equal("Missing", status.Status);
    }

    [Fact]
    public void GetStatus_RejectsLicenseBoundToDifferentMachine()
    {
        using RSA rsa = RSA.Create(2048);
        GatewayLicensePayload payload = new GatewayLicensePayload
        {
            ProductId = "IPC.Gateway",
            MachineCode = GatewayMachineIdentity.CreateMachineCode("IPC.Gateway", "machine-a"),
            CustomerName = "Factory A",
            Edition = "Commercial",
            SerialNumber = "LIC-002",
            IssuedUtc = DateTime.UtcNow.AddMinutes(-1),
            ExpiresUtc = DateTime.UtcNow.AddDays(30)
        };
        Sign(payload, rsa);

        GatewayLicenseService service = new GatewayLicenseService(new GatewayLicenseOptions
        {
            ProductId = "IPC.Gateway",
            RequireValidLicense = true,
            RequireMachineBinding = true,
            MachineIdOverride = "machine-b",
            TrustedPublicKeyPem = rsa.ExportSubjectPublicKeyInfoPem(),
            LicenseText = GatewayLicenseCryptography.SerializeLicense(payload)
        });

        GatewayLicenseStatus status = service.GetStatus();

        Assert.False(status.Valid);
        Assert.False(status.Operational);
        Assert.True(status.SignatureVerified);
        Assert.False(status.MachineMatched);
        Assert.Contains("different machine", status.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequestCode_RoundTripsCurrentMachineIdentity()
    {
        GatewayLicenseService service = new GatewayLicenseService(new GatewayLicenseOptions
        {
            ProductId = "IPC.Gateway",
            MachineIdOverride = "request-machine"
        });

        GatewayLicenseRequest request = GatewayLicenseCryptography.DecodeRequest(service.GetRequestCode());

        Assert.Equal("IPC.Gateway", request.ProductId);
        Assert.Equal(service.GetMachineCode(), request.MachineCode);
        Assert.False(string.IsNullOrWhiteSpace(request.Nonce));
    }

    [Fact]
    public void Install_WritesMachineBoundLicenseAndReadsPublicKeyFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ipc-license-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using RSA rsa = RSA.Create(2048);
            string publicKeyPath = Path.Combine(directory, "public.pem");
            string licensePath = Path.Combine(directory, "license.json");
            File.WriteAllText(publicKeyPath, rsa.ExportSubjectPublicKeyInfoPem());
            GatewayLicensePayload payload = new()
            {
                ProductId = "IPC.Gateway",
                MachineCode = GatewayMachineIdentity.CreateMachineCode("IPC.Gateway", "install-machine"),
                CustomerName = "Install Test",
                Edition = "Commercial",
                SerialNumber = "LIC-INSTALL",
                IssuedUtc = DateTime.UtcNow.AddMinutes(-1),
                ExpiresUtc = DateTime.MinValue
            };
            Sign(payload, rsa);
            GatewayLicenseService service = new(new GatewayLicenseOptions
            {
                ProductId = "IPC.Gateway",
                RequireValidLicense = true,
                RequireMachineBinding = true,
                MachineIdOverride = "install-machine",
                LicenseFile = licensePath,
                TrustedPublicKeyFile = publicKeyPath
            });

            GatewayLicenseStatus status = service.Install(GatewayLicenseCryptography.SerializeLicense(payload));

            Assert.True(File.Exists(licensePath));
            Assert.True(status.Operational);
            Assert.Equal("LIC-INSTALL", service.GetStatus().SerialNumber);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void GetStatus_AcceptsLegacyVersionOneSignatureWhenBindingIsDisabled()
    {
        using RSA rsa = RSA.Create(2048);
        GatewayLicensePayload payload = new()
        {
            SchemaVersion = 1,
            ProductId = "IPC.Gateway",
            CustomerName = "Legacy Customer",
            Edition = "Commercial",
            SerialNumber = "LIC-V1",
            IssuedUtc = DateTime.UtcNow.AddDays(-1),
            ExpiresUtc = DateTime.UtcNow.AddDays(1)
        };
        byte[] data = Encoding.UTF8.GetBytes(GatewayLicenseCryptography.BuildSignaturePayload(payload));
        payload.Signature = Convert.ToBase64String(rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        GatewayLicenseService service = new(new GatewayLicenseOptions
        {
            ProductId = "IPC.Gateway",
            RequireValidLicense = true,
            RequireMachineBinding = false,
            TrustedPublicKeyPem = rsa.ExportSubjectPublicKeyInfoPem(),
            LicenseText = GatewayLicenseCryptography.SerializeLicense(payload)
        });

        GatewayLicenseStatus status = service.GetStatus();

        Assert.True(status.Operational);
        Assert.True(status.SignatureVerified);
        Assert.Equal("LIC-V1", status.SerialNumber);
    }

    private static void Sign(GatewayLicensePayload payload, RSA privateKey)
    {
        byte[] data = Encoding.UTF8.GetBytes(GatewayLicenseCryptography.BuildSignaturePayload(payload));
        payload.Signature = Convert.ToBase64String(
            privateKey.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
    }
}

/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：GatewayUpdatePackageServiceTests
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Tests
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
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using IPC.Gateway.WebHost;
using Microsoft.AspNetCore.Http;

namespace IPC.Gateway.Tests;

public sealed class GatewayUpdatePackageServiceTests : IDisposable
{
    private readonly string _root;

    public GatewayUpdatePackageServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ipc-gateway-update-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void ReadAndValidateManifest_AcceptsPackageWithPayload()
    {
        string packagePath = CreatePackage("upgrade-1", "1.0.1");
        GatewayUpdatePackageService service = CreateService();

        GatewayUpdatePackageManifest manifest = service.ReadAndValidateManifest(packagePath);

        Assert.Equal("IPC.Gateway", manifest.Product);
        Assert.Equal("Upgrade", manifest.PackageType);
        Assert.Equal("1.0.1", manifest.Version);
        Assert.Equal("payload", manifest.EntryDirectory);
    }

    [Fact]
    public async Task StoreAndPrepareUpgrade_CreatesPendingActionAndRollbackPoint()
    {
        string installDirectory = Path.Combine(_root, "install");
        Directory.CreateDirectory(installDirectory);
        File.WriteAllText(Path.Combine(installDirectory, "current.txt"), "current", Encoding.UTF8);
        Directory.CreateDirectory(Path.Combine(installDirectory, "Data"));
        File.WriteAllText(Path.Combine(installDirectory, "Data", "values.jsonl"), "runtime-data", Encoding.UTF8);

        string packagePath = CreatePackage("upgrade-2", "1.0.2");
        GatewayUpdatePackageService service = CreateService(installDirectory);

        await using FileStream stream = File.OpenRead(packagePath);
        FormFile file = new FormFile(stream, 0, stream.Length, "file", "upgrade.zip");
        GatewayUpdatePackageRecord record = await service.StorePackageAsync(file, CancellationToken.None);
        GatewayPrepareUpdateResult result = service.PrepareUpgrade(record.PackageId);
        GatewayUpdateStatus status = service.GetStatus();

        Assert.True(result.Prepared);
        Assert.Equal("Upgrade", result.PendingAction.ActionType);
        Assert.True(File.Exists(Path.Combine(result.PendingAction.SourceDirectory, "IPC.Gateway.WebHost.dll")));
        Assert.True(File.Exists(Path.Combine(result.PendingAction.RollbackDirectory, "payload", "current.txt")));
        Assert.False(File.Exists(Path.Combine(result.PendingAction.RollbackDirectory, "payload", "Data", "values.jsonl")));
        Assert.NotNull(status.PendingAction);
        Assert.Single(status.Packages);
        Assert.Single(status.RollbackPoints);
    }

    [Fact]
    public void ReadAndValidateManifest_RejectsWrongProduct()
    {
        string packagePath = CreatePackage("bad-product", "1.0.1", product: "Other.Product");
        GatewayUpdatePackageService service = CreateService();

        ArgumentException exception = Assert.Throws<ArgumentException>(() => service.ReadAndValidateManifest(packagePath));

        Assert.Contains("产品标识", exception.Message);
    }

    private GatewayUpdatePackageService CreateService(string? installDirectory = null)
    {
        return new GatewayUpdatePackageService(new GatewayUpdateMaintenanceOptions
        {
            ProductId = "IPC.Gateway",
            InstallDirectory = installDirectory ?? Path.Combine(_root, "install"),
            UpdateDirectory = Path.Combine(_root, "updates"),
            MaxPackageMegabytes = 16,
            KeepRollbackCount = 3
        });
    }

    private string CreatePackage(string packageId, string version, string product = "IPC.Gateway")
    {
        string packagePath = Path.Combine(_root, packageId + ".zip");
        using ZipArchive archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        GatewayUpdatePackageManifest manifest = new GatewayUpdatePackageManifest
        {
            PackageId = packageId,
            Product = product,
            PackageType = "Upgrade",
            Version = version,
            EntryDirectory = "payload",
            RequiresRestart = true,
            CreatedTime = DateTime.UtcNow
        };

        ZipArchiveEntry manifestEntry = archive.CreateEntry("ipc-gateway-package.json");
        using (StreamWriter writer = new StreamWriter(manifestEntry.Open(), new UTF8Encoding(false)))
        {
            writer.Write(JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
        }

        ZipArchiveEntry payloadEntry = archive.CreateEntry("payload/IPC.Gateway.WebHost.dll");
        using (StreamWriter writer = new StreamWriter(payloadEntry.Open(), new UTF8Encoding(false)))
        {
            writer.Write("binary");
        }

        return packagePath;
    }
}

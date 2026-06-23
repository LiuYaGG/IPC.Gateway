/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：GatewayWebRootTests
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
using IPC.Gateway.WebHost;

namespace IPC.Gateway.Tests;

public sealed class GatewayWebRootTests
{
    [Fact]
    public void Resolve_PrefersPublishedWwwrootWhenIndexExists()
    {
        string root = CreateTempRoot();
        try
        {
            string appRoot = Path.Combine(root, "app");
            string publishedWebRoot = Path.Combine(appRoot, "wwwroot");
            string developmentWebRoot = Path.Combine(root, "IPC.Gateway.Web", "dist");
            WriteIndex(publishedWebRoot);
            WriteIndex(developmentWebRoot);

            string resolved = GatewayWebRoot.Resolve(appRoot, appRoot);

            Assert.Equal(Path.GetFullPath(publishedWebRoot), resolved);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Resolve_FallsBackToDevelopmentDist()
    {
        string root = CreateTempRoot();
        try
        {
            string contentRoot = Path.Combine(root, "IPC.Gateway.WebHost");
            string appBase = Path.Combine(contentRoot, "bin", "Debug", "net10.0");
            string developmentWebRoot = Path.Combine(root, "IPC.Gateway.Web", "dist");
            WriteIndex(developmentWebRoot);

            string resolved = GatewayWebRoot.Resolve(contentRoot, appBase);

            Assert.Equal(Path.GetFullPath(developmentWebRoot), resolved);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Resolve_ReturnsPublishedWwwrootWhenAssetsAreMissing()
    {
        string root = CreateTempRoot();
        try
        {
            string appRoot = Path.Combine(root, "app");

            string resolved = GatewayWebRoot.Resolve(appRoot, appRoot);

            Assert.Equal(Path.GetFullPath(Path.Combine(appRoot, "wwwroot")), resolved);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static string CreateTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "ipc-gateway-tests", Guid.NewGuid().ToString("N"));
    }

    private static void WriteIndex(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "index.html"), "<!doctype html>");
    }

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}

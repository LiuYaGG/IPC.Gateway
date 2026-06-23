/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：GatewayAuditLogStoreTests
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
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Core.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;

namespace IPC.Gateway.Tests;

public sealed class GatewayAuditLogStoreTests : IDisposable
{
    private readonly string _rootDirectory;
    private readonly string _databasePath;

    public GatewayAuditLogStoreTests()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), "ipc-gateway-audit-tests", Guid.NewGuid().ToString("N"));
        _databasePath = Path.Combine(_rootDirectory, "gateway.db");
    }

    [Fact]
    public void AppendAndQuery_ReturnsNewestMatchingAuditEntries()
    {
        SqlSugarGatewayAuditLogStore store = CreateStore();
        store.Append(CreateEntry("config:mqtt", "success", "admin", "/api/config/mqtt", DateTime.Now.AddMinutes(-2)));
        store.Append(CreateEntry("config:storage-health", "success", "admin", "/api/config/storage-health", DateTime.Now.AddMinutes(-1)));
        store.Append(CreateEntry("config:storage-health", "bad_request", "operator1", "/api/config/storage-health", DateTime.Now));

        IReadOnlyList<GatewayAuditLogEntry> rows = store.Query(new GatewayAuditLogQuery
        {
            Limit = 10,
            Target = "storage",
            Outcome = "success",
            UserName = "ADM"
        });

        Assert.Single(rows);
        Assert.Equal("config:storage-health", rows[0].Target);
        Assert.Equal("success", rows[0].Outcome);
        Assert.Equal("admin", rows[0].UserName);
        Assert.Equal(HttpMethods.Put, rows[0].Method);
        Assert.Equal("/api/config/storage-health", rows[0].Path);
        Assert.False(string.IsNullOrWhiteSpace(rows[0].TraceId));
    }

    [Fact]
    public void Query_ClampsLimitAndOrdersNewestFirst()
    {
        SqlSugarGatewayAuditLogStore store = CreateStore();
        store.Append(CreateEntry("config:project", "success", "admin", "/api/config/project", DateTime.Now.AddMinutes(-2)));
        store.Append(CreateEntry("config:mqtt", "success", "admin", "/api/config/mqtt", DateTime.Now.AddMinutes(-1)));
        store.Append(CreateEntry("config:rules", "success", "admin", "/api/config/rules", DateTime.Now));

        IReadOnlyList<GatewayAuditLogEntry> rows = store.Query(new GatewayAuditLogQuery { Limit = 1, Offset = 1 });

        Assert.Single(rows);
        Assert.Equal("config:mqtt", rows[0].Target);
    }

    [Fact]
    public void Query_FiltersByTimeRange()
    {
        DateTime baseline = new DateTime(2026, 6, 20, 8, 0, 0, DateTimeKind.Local);
        SqlSugarGatewayAuditLogStore store = CreateStore();
        store.Append(CreateEntry("config:project", "success", "admin", "/api/config/project", baseline.AddMinutes(-10)));
        store.Append(CreateEntry("config:mqtt", "success", "admin", "/api/config/mqtt", baseline.AddMinutes(10)));
        store.Append(CreateEntry("config:rules", "success", "admin", "/api/config/rules", baseline.AddMinutes(30)));

        IReadOnlyList<GatewayAuditLogEntry> rows = store.Query(new GatewayAuditLogQuery
        {
            Limit = 10,
            FromTime = baseline,
            ToTime = baseline.AddMinutes(20)
        });

        Assert.Single(rows);
        Assert.Equal("config:mqtt", rows[0].Target);
    }

    [Fact]
    public void DeleteOlderThan_RemovesExpiredAuditRows()
    {
        DateTime baseline = new DateTime(2026, 6, 20, 8, 0, 0, DateTimeKind.Local);
        SqlSugarGatewayAuditLogStore store = CreateStore();
        store.Append(CreateEntry("config:old", "success", "admin", "/api/config/project", baseline.AddDays(-10)));
        store.Append(CreateEntry("config:recent", "success", "admin", "/api/config/mqtt", baseline.AddDays(-1)));

        int deleted = store.DeleteOlderThan(baseline.AddDays(-7));
        IReadOnlyList<GatewayAuditLogEntry> rows = store.Query(new GatewayAuditLogQuery { Limit = 10 });

        Assert.Equal(1, deleted);
        Assert.Single(rows);
        Assert.Equal("config:recent", rows[0].Target);
    }

    [Fact]
    public void CleanupExpired_UsesConfiguredRetentionDays()
    {
        DateTime now = DateTime.Today.AddDays(-10).AddHours(8);
        SqlSugarGatewayAuditLogStore store = CreateStore(new GatewayAuditLogOptions { RetentionDays = 7 });
        store.Append(CreateEntry("config:old", "success", "admin", "/api/config/project", now.AddDays(-8)));
        store.Append(CreateEntry("config:recent", "success", "admin", "/api/config/mqtt", now.AddDays(-6)));

        int deleted = store.CleanupExpired(now.AddDays(1));
        IReadOnlyList<GatewayAuditLogEntry> rows = store.Query(new GatewayAuditLogQuery { Limit = 10 });

        Assert.Equal(1, deleted);
        Assert.Single(rows);
        Assert.Equal("config:recent", rows[0].Target);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (!Directory.Exists(_rootDirectory))
            return;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(_rootDirectory, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(50);
            }
        }
    }

    private SqlSugarGatewayAuditLogStore CreateStore()
    {
        return CreateStore(new GatewayAuditLogOptions());
    }

    private SqlSugarGatewayAuditLogStore CreateStore(GatewayAuditLogOptions auditOptions)
    {
        return new SqlSugarGatewayAuditLogStore(new GatewayDatabaseOptions
        {
            Provider = "Sqlite",
            Database = _databasePath,
            AutoCreateDatabase = true
        }, auditOptions);
    }

    private static GatewayAuditLogEntry CreateEntry(string target, string outcome, string userName, string path, DateTime timestamp)
    {
        return GatewayAuditLog.CreateConfigurationEntry(new GatewayConfigurationAuditEvent
        {
            Outcome = outcome,
            Target = target,
            UserName = userName,
            Role = "Admin",
            RemoteIpAddress = "127.0.0.1",
            Method = HttpMethods.Put,
            Path = path,
            TraceId = Guid.NewGuid().ToString("N")
        }, timestamp);
    }
}

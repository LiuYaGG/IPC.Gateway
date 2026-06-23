/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：GatewayConfigurationAuditTests
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
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.WebHost;
using Microsoft.AspNetCore.Http;

namespace IPC.Gateway.Tests;

public sealed class GatewayConfigurationAuditTests
{
    [Fact]
    public void CreateConfigurationAuditEvent_ExtractsUserAndRequestContext()
    {
        DefaultHttpContext context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Put;
        context.Request.Path = "/api/config/storage-health";
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.1.2.3");
        context.TraceIdentifier = "trace-123";
        context.Items[GatewayApiSecurityControls.RequestBodySha256ItemKey] = "abc123";
        context.Items[GatewayApiSecurityControls.RequestBodyLengthItemKey] = 42L;
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "operator1"),
            new Claim(ClaimTypes.Role, "Operator")
        }, "GatewayCookie"));

        GatewayConfigurationAuditEvent auditEvent = GatewayConfigurationEndpoints.CreateConfigurationAuditEvent(
            context,
            "success",
            string.Empty);

        Assert.Equal("success", auditEvent.Outcome);
        Assert.Equal("config:storage-health", auditEvent.Target);
        Assert.Equal("operator1", auditEvent.UserName);
        Assert.Equal("Operator", auditEvent.Role);
        Assert.Equal("10.1.2.3", auditEvent.RemoteIpAddress);
        Assert.Equal(HttpMethods.Put, auditEvent.Method);
        Assert.Equal("/api/config/storage-health", auditEvent.Path);
        Assert.Equal("trace-123", auditEvent.TraceId);
        Assert.Equal("abc123", auditEvent.RequestBodySha256);
        Assert.Equal(42L, auditEvent.RequestContentLength);
    }

    [Fact]
    public void BuildConfigurationDetail_ReturnsSingleLineStructuredJson()
    {
        GatewayConfigurationAuditEvent auditEvent = new GatewayConfigurationAuditEvent
        {
            Outcome = "bad_request",
            Target = "config:project",
            UserName = "admin",
            Role = "Admin",
            RemoteIpAddress = "127.0.0.1",
            Method = "PUT",
            Path = "/api/config/project",
            TraceId = "trace-456",
            ErrorMessage = "invalid\r\nconfiguration",
            RequestBodySha256 = "abc123",
            RequestContentLength = 42L
        };

        string detail = GatewayAuditLog.BuildConfigurationDetail(auditEvent);

        Assert.DoesNotContain("\r", detail);
        Assert.DoesNotContain("\n", detail);

        using JsonDocument document = JsonDocument.Parse(detail);
        JsonElement root = document.RootElement;
        Assert.Equal("bad_request", root.GetProperty("outcome").GetString());
        Assert.Equal("admin", root.GetProperty("user").GetString());
        Assert.Equal("Admin", root.GetProperty("role").GetString());
        Assert.Equal("127.0.0.1", root.GetProperty("ip").GetString());
        Assert.Equal("PUT", root.GetProperty("method").GetString());
        Assert.Equal("/api/config/project", root.GetProperty("path").GetString());
        Assert.Equal("trace-456", root.GetProperty("traceId").GetString());
        Assert.Equal("invalid  configuration", root.GetProperty("error").GetString());
        Assert.Equal("abc123", root.GetProperty("requestBodySha256").GetString());
        Assert.Equal("42", root.GetProperty("requestContentLength").GetString());
    }

    [Fact]
    public void TryParseAuditLine_ExtractsStructuredFields()
    {
        string detail = GatewayAuditLog.BuildConfigurationDetail(new GatewayConfigurationAuditEvent
        {
            Outcome = "success",
            UserName = "admin",
            Role = "Admin",
            RemoteIpAddress = "127.0.0.1",
            Method = HttpMethods.Put,
            Path = "/api/config/mqtt",
            TraceId = "trace-789"
        });
        string line = $"2026-06-20 08:15:30.123 [AUDIT] action=configuration.write target=config:mqtt detail={detail}";

        bool parsed = GatewayAuditLog.TryParseAuditLine(line, out GatewayAuditLogEntry entry);

        Assert.True(parsed);
        Assert.Equal(new DateTime(2026, 6, 20, 8, 15, 30, 123), entry.Timestamp);
        Assert.Equal("AUDIT", entry.Level);
        Assert.Equal("configuration.write", entry.Action);
        Assert.Equal("config:mqtt", entry.Target);
        Assert.Equal("success", entry.Outcome);
        Assert.Equal("admin", entry.UserName);
        Assert.Equal("Admin", entry.Role);
        Assert.Equal("127.0.0.1", entry.RemoteIpAddress);
        Assert.Equal(HttpMethods.Put, entry.Method);
        Assert.Equal("/api/config/mqtt", entry.Path);
        Assert.Equal("trace-789", entry.TraceId);
        Assert.Equal(detail, entry.RawDetail);
    }

    [Fact]
    public void MatchesQuery_FiltersByTargetOutcomeAndUser()
    {
        GatewayAuditLogEntry entry = new GatewayAuditLogEntry
        {
            Target = "config:storage-health",
            Outcome = "bad_request",
            UserName = "operator1",
            Timestamp = new DateTime(2026, 6, 20, 8, 30, 0)
        };

        Assert.True(GatewayAuditLog.MatchesQuery(entry, new GatewayAuditLogQuery
        {
            Target = "storage",
            Outcome = "bad_request",
            UserName = "operator"
        }));
        Assert.False(GatewayAuditLog.MatchesQuery(entry, new GatewayAuditLogQuery
        {
            Target = "mqtt",
            Outcome = "bad_request",
            UserName = "operator"
        }));
        Assert.False(GatewayAuditLog.MatchesQuery(entry, new GatewayAuditLogQuery
        {
            Target = "storage",
            Outcome = "success",
            UserName = "operator"
        }));
        Assert.True(GatewayAuditLog.MatchesQuery(entry, new GatewayAuditLogQuery
        {
            FromTime = new DateTime(2026, 6, 20, 8, 0, 0),
            ToTime = new DateTime(2026, 6, 20, 9, 0, 0)
        }));
        Assert.False(GatewayAuditLog.MatchesQuery(entry, new GatewayAuditLogQuery
        {
            FromTime = new DateTime(2026, 6, 20, 9, 0, 0)
        }));
    }

    [Fact]
    public void ReadPage_ReturnsOffsetPageAndHasMore()
    {
        DateTime baseline = new DateTime(2026, 6, 20, 8, 0, 0);
        InMemoryAuditLogStore store = new InMemoryAuditLogStore();
        for (int index = 0; index < 5; index++)
        {
            store.Append(new GatewayAuditLogEntry
            {
                Timestamp = baseline.AddMinutes(index),
                Target = $"config:{index}",
                Outcome = "success",
                UserName = "admin",
                Source = "database"
            });
        }

        GatewayAuditLogQueryResult result = GatewayAuditLog.ReadPage(new GatewayAuditLogQuery
        {
            Limit = 2,
            Offset = 1
        }, store);

        Assert.Equal(2, result.Limit);
        Assert.Equal(1, result.Offset);
        Assert.Equal(2, result.Returned);
        Assert.True(result.HasMore);
        Assert.Equal(new[] { "config:3", "config:2" }, result.Items.Select(item => item.Target).ToArray());
    }

    [Fact]
    public void BuildAuditCsv_EscapesSpecialFields()
    {
        string csv = GatewayAuditCsv.Build(new[]
        {
            new GatewayAuditLogEntry
            {
                Timestamp = new DateTime(2026, 6, 20, 8, 15, 30),
                Source = "database",
                Outcome = "error",
                Target = "config:mqtt",
                UserName = "admin,ops",
                Role = "Admin",
                RemoteIpAddress = "127.0.0.1",
                Method = "PUT",
                Path = "/api/config/mqtt",
                TraceId = "trace-1",
                ErrorMessage = "bad \"payload\"\nretry"
            }
        });

        Assert.StartsWith(GatewayAuditCsv.Header, csv);
        Assert.Contains("2026-06-20 08:15:30,database,error,config:mqtt,\"admin,ops\"", csv);
        Assert.Contains("\"bad \"\"payload\"\"\nretry\"", csv);
    }

    [Fact]
    public void BuildAuditCsvUtf8Payload_IncludesBom()
    {
        byte[] payload = GatewayAuditCsv.BuildUtf8WithBom(Array.Empty<GatewayAuditLogEntry>());
        byte[] bom = Encoding.UTF8.GetPreamble();

        Assert.True(payload.AsSpan(0, bom.Length).SequenceEqual(bom));
        Assert.StartsWith(GatewayAuditCsv.Header, Encoding.UTF8.GetString(payload, bom.Length, payload.Length - bom.Length));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-1, 100)]
    [InlineData(50, 50)]
    [InlineData(999, 500)]
    public void ClampLimit_KeepsAuditQueriesBounded(int requested, int expected)
    {
        Assert.Equal(expected, GatewayAuditLog.ClampLimit(requested));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(200, 200)]
    [InlineData(20000, 10000)]
    public void ClampOffset_KeepsAuditQueriesBounded(int requested, int expected)
    {
        Assert.Equal(expected, GatewayAuditLog.ClampOffset(requested));
    }

    [Theory]
    [InlineData(0, 180)]
    [InlineData(-1, 180)]
    [InlineData(30, 30)]
    [InlineData(5000, 3650)]
    public void ClampRetentionDays_KeepsAuditRetentionBounded(int requested, int expected)
    {
        Assert.Equal(expected, GatewayAuditLogOptions.ClampRetentionDays(requested));
    }

    private sealed class InMemoryAuditLogStore : IGatewayAuditLogStore
    {
        private readonly List<GatewayAuditLogEntry> _entries = new List<GatewayAuditLogEntry>();

        public void Append(GatewayAuditLogEntry entry)
        {
            _entries.Add(entry);
        }

        public IReadOnlyList<GatewayAuditLogEntry> Query(GatewayAuditLogQuery query)
        {
            return _entries
                .Where(entry => GatewayAuditLog.MatchesQuery(entry, query))
                .OrderByDescending(entry => entry.Timestamp)
                .Skip(GatewayAuditLog.ClampOffset(query.Offset))
                .Take(GatewayAuditLog.ClampLimit(query.Limit))
                .ToList();
        }

        public int DeleteOlderThan(DateTime timestamp)
        {
            int removed = _entries.RemoveAll(entry => entry.Timestamp < timestamp);
            return removed;
        }
    }
}

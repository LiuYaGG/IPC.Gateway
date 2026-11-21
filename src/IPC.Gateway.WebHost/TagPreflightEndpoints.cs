using System.Security.Cryptography;
using System.Text;
using IPC.Gateway.Core.Gateway;
using IPC.Runtime.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace IPC.Gateway.WebHost;

internal sealed record TagPreflightRequest(string ChannelId, string DeviceId, string? GroupId, string? Csv,
    string? PreviewToken, string[]? TagIds);

internal static class TagPreflightEndpoints
{
    private static readonly SemaphoreSlim Slots = new(2);
    private static readonly byte[] PreviewKey = RandomNumberGenerator.GetBytes(32);
    private const int MaxCsvBytes = 8 * 1024 * 1024;
    public static void MapTagPreflightEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/commercial/tags/preflight", (HttpContext c, TagPreflightRequest r, GatewayCoreService g,
            IGatewayAuditLogStore log) => Check(c, r, g, log, false)).WithMetadata(new RequestSizeLimitAttribute(MaxCsvBytes * 3));
        app.MapPost("/api/commercial/tags/preflight/apply", (HttpContext c, TagPreflightRequest r, GatewayCoreService g,
            IGatewayAuditLogStore log) => Check(c, r, g, log, true)).WithMetadata(new RequestSizeLimitAttribute(MaxCsvBytes * 3));
        app.MapPost("/api/commercial/tags/preflight/sample", Sample).WithMetadata(new RequestSizeLimitAttribute(8192));
    }

    private static IResult Error(int status, string code, string text) =>
        Results.Json(new { success = false, errorCode = code, errorMessage = text }, statusCode: status);

    internal static string Token(TagPreflightRequest r, string revision) =>
        Convert.ToHexString(HMACSHA256.HashData(PreviewKey, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new[] { revision, r.ChannelId, r.DeviceId, r.GroupId, r.Csv })));

    private static async Task<IResult> Check(HttpContext context, TagPreflightRequest r, GatewayCoreService gateway,
        IGatewayAuditLogStore log, bool apply)
    {
        if (!GatewayAuthEndpoints.CanViewDevices(context.User) ||
            ((r.Csv != null || apply) && !GatewayAuthEndpoints.CanImportTags(context.User)))
            return Error(403, "forbidden", "没有点表检查或导入权限。");
        if (string.IsNullOrWhiteSpace(r.DeviceId) || string.IsNullOrWhiteSpace(r.ChannelId) ||
            (apply && (r.Csv == null || string.IsNullOrEmpty(r.PreviewToken))))
            return Error(400, "invalidScope", "请选择设备并先预览导入文件。");
        if (r.Csv != null && Encoding.UTF8.GetByteCount(r.Csv) > MaxCsvBytes)
            return Error(413, "fileSize", "CSV 文件最大为 8 MiB。");
        if (!Slots.Wait(0)) return Error(429, "busy", "点表检查正在运行，请稍后再试。");
        try
        {
            var cancel = context.RequestAborted;
            if (!apply)
            {
                var project = gateway.GetProjectWithRevision(out var revision);
                var report = TagPreflight.Run(project, r.ChannelId, r.DeviceId, r.GroupId, r.Csv, revision, cancel);
                return Results.Ok(new { success = true, data = new { report, previewToken = r.Csv == null ? null : Token(r, revision) } });
            }
            var saved = await gateway.MutateDeviceProjectAsync(project =>
            {
                cancel.ThrowIfCancellationRequested();
                string revision = gateway.ProjectRevision;
                if (!string.Equals(Token(r, revision), r.PreviewToken, StringComparison.Ordinal)) throw new StalePreview();
                var report = TagPreflight.Run(project, r.ChannelId, r.DeviceId, r.GroupId, r.Csv, revision, cancel);
                if (report.Errors != 0) throw new InvalidDataException();
                return report;
            });
            var audit = GatewayConfigurationEndpoints.CreateConfigurationAuditEvent(context, "Success", "");
            audit.Target = $"CSV import / {r.DeviceId} (+{saved.Added}, ~{saved.Updated})";
            await GatewayAuditLog.WriteConfigurationChangeAsync(audit, log);
            return Results.Ok(new { success = true, data = new { report = saved } });
        }
        catch (StalePreview) { return Error(409, "stalePreview", "配置或文件已变更，请重新检查后导入。"); }
        catch (KeyNotFoundException) { return Error(404, "invalidScope", "设备或分组已不存在。"); }
        catch (InvalidDataException) { return Error(422, "blocked", "点表存在阻断项，请修正后重新检查。"); }
        catch (ArgumentException ex) { return Error(400, "validation", ex.Message); }
        catch (InvalidOperationException ex) { return Error(409, "validation", ex.Message); }
        finally { Slots.Release(); }
    }

    private sealed class StalePreview : Exception { }

    private static async Task<IResult> Sample(HttpContext context, TagPreflightRequest r, GatewayCoreService gateway, IGatewayAuditLogStore log)
    {
        if (!GatewayAuthEndpoints.CanViewDevices(context.User) || !GatewayAuthEndpoints.CanRunProtocolDiagnostics(context.User))
            return Error(403, "forbidden", "没有协议诊断权限。");
        if (r.TagIds == null || r.TagIds.Length is < 1 or > 8 || r.Csv != null)
            return Error(400, "sampleSelection", "请选择 1 至 8 个现有可读点位。");
        var project = gateway.CurrentProject;
        var device = project.Devices.Find(d => d.Id == r.DeviceId && d.ChannelId == r.ChannelId);
        if (device == null || !device.Enabled || !project.Channels.Any(c => c.Id == r.ChannelId && c.Enabled))
            return Error(409, "disabled", "设备或通道已停用或不存在。");
        var readable = device.Groups.Where(g => g.Enabled && (r.GroupId == null || g.Id == r.GroupId))
            .SelectMany(g => g.Tags).Concat(r.GroupId == null ? device.Tags : [])
            .Where(t => t.Enabled && !t.IsVirtual && t.AccessMode != TagAccessMode.WriteOnly).ToDictionary(t => t.Id);
        var request = new ProtocolDiagnosticRequest { Operation = "batch", SampleOnly = true, Connection = device.Connection };
        request.Connection.TimeoutMilliseconds = Math.Clamp(request.Connection.TimeoutMilliseconds, 100, 10000);
        foreach (var id in r.TagIds.Distinct())
        {
            if (!readable.TryGetValue(id, out var t)) return Error(400, "sampleSelection", "抽读点位已变更或不可读。");
            request.Items.Add(new() { Address = ProjectConfigValidator.ResolveTagAddress(device, t), DataType = t.DataType,
                ElementCount = t.ElementCount, ElementOffset = t.ElementOffset });
        }
        return await ProtocolDiagnosticEndpoints.RunAsync(context, request, gateway, log);
    }
}

using System.Globalization;
using System.Security.Claims;
using IPC.EdgeGateway;
using IPC.Gateway.Core.Domain.Users;
using IPC.Gateway.Core.Gateway;

namespace IPC.Gateway.WebHost;

internal static class GatewayHistoryEndpoints
{
    public static void MapHistoryValueEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/config/history");
        group.MapGet("/values", (HttpContext ctx, GatewayHistoryQueries queries, GatewayCoreService gateway) =>
            queries.Run(ctx, queries.ExternalSlots, async ct =>
            {
                CheckKeys(ctx, "path", "from", "to", "size");
                var filter = Filter(ctx, external: true);
                var size = Integer(ctx, "size", 0);
                return await queries.PreparePublicResponseAsync(gateway.HistoryValueQueries, filter, size, ct);
            }));

        group.MapGet("/records", (HttpContext ctx, GatewayHistoryQueries queries, GatewayCoreService gateway) =>
            queries.Run(ctx, queries.UiSlots, async ct =>
            {
                CheckKeys(ctx, "from","to","path","channelId","deviceId","groupId","tagId","quality","page","pageSize","snapshot");
                var filter = Filter(ctx, external: false);
                long page = Integer(ctx, "page", 1), count = Integer(ctx, "pageSize", 20);
                if (count is not (20 or 50 or 100 or 200 or 500) || page < 1 || page > int.MaxValue / count)
                    throw new ArgumentException();
                var result = await gateway.HistoryValueQueries.QueryAsync(filter, (int)page, (int)count, Get(ctx,"snapshot"), ct);
                string? exportToken = CanExport(ctx.User) && result.Items.Count > 0
                    ? queries.RememberPage(ctx.User, filter, result, ct) : null;
                return Results.Ok(new { success = true, data = new { result.Page, result.PageSize, result.Total, result.Snapshot, result.Items, exportToken } });
            }));

        group.MapGet("/options", (HttpContext ctx, GatewayHistoryQueries queries, GatewayCoreService gateway) =>
            queries.Run(ctx, queries.UiSlots, async ct =>
            {
                CheckKeys(ctx, "level","channelId","deviceId","groupId","search");
                string level = Get(ctx,"level") ?? "";
                string? channel = Get(ctx,"channelId"), device = Get(ctx,"deviceId"), groupId = Get(ctx,"groupId"), search = SearchText(ctx);
                if (level is not ("channel" or "device" or "group" or "tag") ||
                    (level != "channel" && string.IsNullOrWhiteSpace(channel)) ||
                    (level is "group" or "tag" && string.IsNullOrWhiteSpace(device)) ||
                    (level == "tag" && groupId == null))
                    throw new ArgumentException();
                const int limit = 200;
                var archived = await gateway.HistoryValueQueries.OptionsAsync(level, channel, device, groupId, search, limit, ct);
                var current = gateway.GetConfiguredHistoryOptions(level, channel, device, groupId, search, limit, ct);
                var items = current.Concat(archived).DistinctBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Take(limit);
                return Results.Ok(new { success = true, data = items.ToArray() });
            }));

        group.MapGet("/export", (HttpContext ctx, GatewayHistoryQueries queries, GatewayCoreService gateway) =>
            queries.Run(ctx, queries.ExportSlots, async ct =>
            {
                CheckKeys(ctx, "token", "timeZone");
                if (!CanExport(ctx.User))
                    return Error(ctx, 403, "HISTORY_EXPORT_FORBIDDEN", "缺少点位历史导出权限。", "History export permission is required.");
                var rows = await queries.GetPageAsync(ctx.User, gateway.HistoryValueQueries, Get(ctx,"token"), ct);
                if (rows == null)
                    return Error(ctx, 409, "HISTORY_PAGE_EXPIRED", "当前页已过期，请重新查询后导出。", "This page has expired. Run the query again before exporting.");
                return queries.PrepareWorkbook(rows, English(ctx), ct, WorkbookTimeZone.FromRequest(ctx));
            }));
    }

    internal static bool English(HttpContext ctx) =>
        ctx.Request.Headers.AcceptLanguage.ToString().TrimStart().StartsWith("en", StringComparison.OrdinalIgnoreCase);

    internal static bool IsHistoryRead(HttpContext ctx) =>
        ctx.Request.Path.Value?.TrimEnd('/').ToLowerInvariant() is "/api/config/history/values" or "/api/config/history/records" or
            "/api/config/history/options" or "/api/config/history/export" or
            "/api/config/history/alarms" or "/api/config/history/alarms/options" or "/api/config/history/alarms/export";

    internal static IResult Error(HttpContext ctx, int status, string code, string chinese, string english) =>
        Results.Json(new { success = false, data = (object?)null, errorCode = code, errorMessage = English(ctx) ? english : chinese }, statusCode: status);

    internal static async Task Unauthorized(HttpContext ctx, string message)
    {
        if (IsHistoryRead(ctx))
        {
            await Error(ctx,401,"HISTORY_UNAUTHORIZED","请使用有效的登录会话或 API Token。","A valid session or API token is required.").ExecuteAsync(ctx);
            return;
        }
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await ctx.Response.WriteAsJsonAsync(new { success = false, errorMessage = message });
    }

    internal static bool CanExport(ClaimsPrincipal user) => GatewayAuthEndpoints.IsAdmin(user) ||
        GatewayAuthEndpoints.HasPermission(user, GatewayPermissions.ExportHistory);

    private static void CheckKeys(HttpContext ctx, params string[] allowed)
    {
        if (ctx.Request.Query.Any(pair => !allowed.Contains(pair.Key, StringComparer.OrdinalIgnoreCase) || pair.Value.Count != 1))
            throw new ArgumentException();
    }

    private static string? Get(HttpContext ctx, string key) =>
        ctx.Request.Query.TryGetValue(key, out var value) ? value.ToString() : null;

    private static string? SearchText(HttpContext ctx)
    {
        var text = Get(ctx,"search")?.Trim();
        return text?.Length > 256 ? throw new ArgumentException() : text;
    }

    private static long Integer(HttpContext ctx, string key, long fallback)
    {
        var text = Get(ctx,key);
        if (text == null) return fallback;
        return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : throw new ArgumentException();
    }

    private static HistoryValueFilter Filter(HttpContext ctx, bool external)
    {
        var from = Timestamp(ctx, "from");
        var to = Timestamp(ctx, "to");
        if (to <= from) throw new ArgumentException();
        var path = Get(ctx,"path");
        var channel = Get(ctx,"channelId");
        var device = Get(ctx,"deviceId");
        var groupId = Get(ctx,"groupId");
        var tag = Get(ctx,"tagId");
        if (path != null || external)
        {
            if (string.IsNullOrWhiteSpace(path) || !HistoryValuePath.IsValid(path) || path.Length > 4096 ||
                channel != null || device != null || groupId != null || tag != null)
                throw new ArgumentException();
        }
        else if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(device) || groupId == null || string.IsNullOrWhiteSpace(tag))
            throw new ArgumentException();
        return new HistoryValueFilter(path ?? "", from, to, Get(ctx,"quality"), channel, device, groupId, tag);
    }

    private static DateTimeOffset Timestamp(HttpContext ctx, string key)
    {
        var value = Get(ctx,key);
        if (value == null || (!value.EndsWith('Z') && !(value.Length >= 6 && (value[^6] == '-' || value[^6] == '+') && value[^3] == ':')) ||
            !DateTimeOffset.TryParseExact(value, ["yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", "yyyy-MM-dd'T'HH:mm:ssK","yyyy-MM-dd'T'HH:mmK"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            throw new ArgumentException();
        return parsed.ToUniversalTime();
    }
}

using System.Globalization;
using IPC.EdgeGateway;
using IPC.Gateway.Core.Gateway;

namespace IPC.Gateway.WebHost;

internal static partial class GatewayAlarmHistoryEndpoints
{
    internal static void MapAlarmHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/config/history/alarms");
        group.MapGet("", (HttpContext ctx, GatewayHistoryQueries queries, GatewayCoreService gateway) =>
            queries.Run(ctx, queries.UiSlots, async ct =>
            {
                CheckKeys(ctx, "from", "to", "channelId", "deviceId", "groupId", "tagId", "snapshot", "page", "pageSize");
                var filter = ParseFilter(ctx);
                var (page, size) = ParsePagination(ctx);
                var result = await Task.Run(() => gateway.AlarmJournal.ReadPage(page, size, query: filter, cancellationToken: ct), ct);
                return Results.Ok(new { success = true, data = new
                {
                    result.Total, result.Snapshot,
                    Items = result.Items.Select(record => new
                    {
                        record.EventId, record.Timestamp, record.ChannelId, record.ChannelName, record.DeviceId, record.DeviceName,
                        record.GroupId, record.GroupName, record.TagName, record.TagId, record.Source, record.RuleName,
                        record.EventType, record.PreviousState, record.State, record.Value, record.Threshold, record.Message, record.DeliveryStatus
                    })
                } });
            }));
        group.MapGet("/options", (HttpContext ctx, GatewayHistoryQueries queries, GatewayCoreService gateway) =>
            queries.Run(ctx, queries.UiSlots, async ct =>
            {
                CheckKeys(ctx, "level", "channelId", "deviceId", "groupId", "tagId", "search");
                string level = Get(ctx, "level") ?? "";
                if (level is not ("channel" or "device" or "group" or "tag")) throw new ArgumentException();
                var filter = ParseFilter(ctx);
                string? search = Get(ctx, "search")?.Trim();
                if (search?.Length > 256) throw new ArgumentException();
                const int limit = 200;
                var archived = await Task.Run(() => gateway.AlarmJournal.ReadOptions(level, filter, search, limit, cancellationToken: ct), ct);
                var current = gateway.GetConfiguredAlarmOptions(level, filter, search, limit, ct);
                var result = current.Concat(archived).DistinctBy(option => option.Id, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase).ThenBy(option => option.Id, StringComparer.OrdinalIgnoreCase).Take(limit).ToArray();
                return Results.Ok(new { success = true, data = result });
            }));
        MapExport(group);
    }

    internal static GatewayAlarmQuery ParseFilter(HttpContext ctx) => new()
    {
        From = Timestamp(ctx, "from"), To = Timestamp(ctx, "to"),
        ChannelId = Id(ctx, "channelId"), DeviceId = Id(ctx, "deviceId"),
        GroupId = Id(ctx, "groupId", allowEmpty: true), TagId = Id(ctx, "tagId"),
        Snapshot = Get(ctx, "snapshot") is null ? null : Integer(ctx, "snapshot", 0)
    };

    internal static (int Page, int PageSize) ParsePagination(HttpContext ctx)
    {
        long page = Integer(ctx, "page", 1), size = Integer(ctx, "pageSize", 20);
        if (page < 1 || page > int.MaxValue || size is not (20 or 50 or 100 or 200 or 500)) throw new ArgumentException();
        return ((int)page, (int)size);
    }

    internal static void CheckKeys(HttpContext ctx, params string[] allowed)
    {
        if (ctx.Request.Query.Any(pair => pair.Value.Count != 1 || !allowed.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)))
            throw new ArgumentException();
    }

    private static string? Get(HttpContext ctx, string name) => ctx.Request.Query.TryGetValue(name, out var values) ? values.ToString() : null;

    private static string? Id(HttpContext ctx, string name, bool allowEmpty = false)
    {
        string? value = Get(ctx, name);
        if (value?.Length > 256 || (value != null && !allowEmpty && string.IsNullOrWhiteSpace(value))) throw new ArgumentException();
        return value;
    }

    private static long Integer(HttpContext ctx, string name, long fallback)
    {
        string? value = Get(ctx, name);
        return value == null ? fallback : long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed) ? parsed : throw new ArgumentException();
    }

    private static DateTimeOffset? Timestamp(HttpContext ctx, string name)
    {
        string? value = Get(ctx, name);
        if (value == null) return null;
        if ((!value.EndsWith('Z') && !(value.Length >= 6 && (value[^6] == '+' || value[^6] == '-') && value[^3] == ':')) ||
            !DateTimeOffset.TryParseExact(value, ["yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", "yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mmK"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) throw new ArgumentException();
        return parsed.ToUniversalTime();
    }
}

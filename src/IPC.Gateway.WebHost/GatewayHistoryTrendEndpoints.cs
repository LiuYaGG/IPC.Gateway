using System.Text.Json;
using IPC.EdgeGateway;
using IPC.Gateway.Core.Gateway;

namespace IPC.Gateway.WebHost;

internal static class GatewayHistoryTrendEndpoints
{
    public static void MapHistoryTrendEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/config/history/trend");
        group.MapGet("", (HttpContext ctx, GatewayHistoryQueries queries, GatewayCoreService gateway) =>
            queries.Run(ctx, queries.UiSlots, async ct =>
            {
                GatewayHistoryEndpoints.CheckKeys(ctx, "from", "to", "points");
                var from = GatewayHistoryEndpoints.Timestamp(ctx, "from");
                var to = GatewayHistoryEndpoints.Timestamp(ctx, "to");
                var points = ReadPoints(ctx.Request.Query["points"].ToString());
                var result = await gateway.HistoryValueQueries.QueryTrendAsync(points, from, to, ct);
                return Results.Ok(new { success = true, data = result });
            }));

        group.MapGet("/options", (HttpContext ctx, GatewayHistoryQueries queries, GatewayCoreService gateway) =>
            queries.Run(ctx, queries.UiSlots, async ct =>
            {
                GatewayHistoryEndpoints.CheckKeys(ctx, "channelId", "deviceId", "groupId", "search");
                string? channel = GatewayHistoryEndpoints.Get(ctx, "channelId");
                string? device = GatewayHistoryEndpoints.Get(ctx, "deviceId");
                string? groupId = GatewayHistoryEndpoints.Get(ctx, "groupId");
                string? search = GatewayHistoryEndpoints.SearchText(ctx);
                if (!ValidId(channel) || !ValidId(device) || !ValidId(groupId, allowEmpty: true))
                    throw new ArgumentException();
                const int limit = 200;
                var current = gateway.GetConfiguredHistoryOptions("tag", channel, device, groupId, search, limit, ct, numericOnly: true);
                var archived = await gateway.HistoryValueQueries.OptionsAsync("tag", channel, device, groupId, search, limit, ct, numericOnly: true);
                var items = current.Concat(archived).Where(item => ValidId(item.Id))
                    .DistinctBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase).Take(limit).ToArray();
                return Results.Ok(new { success = true, data = items });
            }));
    }

    private static HistoryTrendPoint[] ReadPoints(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 7168) throw new ArgumentException();
        HistoryTrendPoint[]? points;
        try { points = JsonSerializer.Deserialize<HistoryTrendPoint[]>(json, JsonSerializerOptions.Web); }
        catch (JsonException ex) { throw new ArgumentException("Invalid trend points.", ex); }
        if (points == null || points.Length is < 1 or > HistoryValueIndex.MaxTrendPoints ||
            points.Any(point => point == null || !ValidId(point.ChannelId) || !ValidId(point.DeviceId) ||
                !ValidId(point.GroupId, allowEmpty: true) || !ValidId(point.TagId)))
            throw new ArgumentException();
        if (points.Select(point => (point.ChannelId.ToUpperInvariant(), point.DeviceId.ToUpperInvariant(),
                point.GroupId.ToUpperInvariant(), point.TagId.ToUpperInvariant())).Distinct().Count() != points.Length)
            throw new ArgumentException();
        return points;
    }

    private static bool ValidId(string? id, bool allowEmpty = false) =>
        id != null && id.Length <= 256 && (allowEmpty && id.Length == 0 || !string.IsNullOrWhiteSpace(id));
}

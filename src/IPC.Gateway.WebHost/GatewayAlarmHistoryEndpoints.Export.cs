using IPC.EdgeGateway;
using IPC.Gateway.Core.Gateway;

namespace IPC.Gateway.WebHost;

internal static partial class GatewayAlarmHistoryEndpoints
{
    private static void MapExport(RouteGroupBuilder group)
    {
        group.MapGet("/export", (HttpContext ctx, GatewayHistoryQueries queries, GatewayCoreService gateway) =>
            queries.Run(ctx, queries.ExportSlots, async ct =>
            {
                if (!GatewayHistoryEndpoints.CanExport(ctx.User))
                    return GatewayHistoryEndpoints.Error(ctx, 403, "HISTORY_EXPORT_FORBIDDEN",
                        "缺少历史导出权限。", "History export permission is required.");
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(TimeSpan.FromSeconds(20));
                try
                {
                    CheckKeys(ctx, "from", "to", "channelId", "deviceId", "groupId", "tagId", "snapshot", "page", "pageSize", "timeZone", "processingStatus");
                    var filter = ParseFilter(ctx);
                    var (page, pageSize) = ParsePagination(ctx);
                    var timeZone = WorkbookTimeZone.FromRequest(ctx);
                    return await Task.Run<IResult>(() =>
                    {
                        var records = gateway.AlarmJournal.ReadPage(page, pageSize, query: filter, cancellationToken: deadline.Token);
                        return queries.PrepareAlarmWorkbook(records.Items, GatewayHistoryEndpoints.English(ctx), deadline.Token, timeZone);
                    }, deadline.Token);
                }
                catch (Exception) when (!ct.IsCancellationRequested && deadline.IsCancellationRequested)
                {
                    return GatewayHistoryEndpoints.Error(ctx, 503, "HISTORY_QUERY_TIMEOUT",
                        "报警导出超时，请缩小查询范围后重试。", "Alarm export timed out. Narrow the query and retry.");
                }
            }));
    }
}

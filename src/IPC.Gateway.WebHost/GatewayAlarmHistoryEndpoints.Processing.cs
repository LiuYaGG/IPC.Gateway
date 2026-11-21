using System.Security.Claims;
using IPC.EdgeGateway;
using IPC.Gateway.Core.Gateway;

namespace IPC.Gateway.WebHost;

internal static partial class GatewayAlarmHistoryEndpoints
{
    private sealed record OperatorActionRequest(string? Note);

    private static void MapProcessing(RouteGroupBuilder group)
    {
        group.MapPost("/{eventId}/acknowledge",
            (HttpContext context, string eventId, OperatorActionRequest request, GatewayCoreService gateway, GatewayHistoryQueries queries) =>
                ApplyAction(context, eventId, request, gateway, queries, AlarmOperatorAction.Acknowledge));
        group.MapPost("/{eventId}/handle",
            (HttpContext context, string eventId, OperatorActionRequest request, GatewayCoreService gateway, GatewayHistoryQueries queries) =>
                ApplyAction(context, eventId, request, gateway, queries, AlarmOperatorAction.Handle));
    }

    private static Task<IResult> ApplyAction(HttpContext context, string eventId, OperatorActionRequest request,
        GatewayCoreService gateway, GatewayHistoryQueries queries, AlarmOperatorAction action)
    {
        bool handling = action == AlarmOperatorAction.Handle;
        string auditAction = handling ? "alarm.handle" : "alarm.acknowledge";
        string target = "alarm:" + eventId;
        Task Audit(string outcome) => GatewayAuthEndpoints.WriteSecurityAuditAsync(context,
            context.RequestServices.GetService<IGatewayAuditLogStore>(), auditAction, outcome, target, "", "", "");
        return queries.Run(context, queries.UiSlots, async ct =>
        {
            if (!(handling ? GatewayAuthEndpoints.CanHandleAlarm(context.User) : GatewayAuthEndpoints.CanAcknowledgeAlarm(context.User)))
            {
                await Audit("forbidden");
                return GatewayHistoryEndpoints.Error(context, 403, "ALARM_PROCESSING_FORBIDDEN",
                    "当前账号没有执行此操作的权限。", "Your account lacks permission for this alarm action.");
            }
            string? operatorId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(operatorId) || string.IsNullOrWhiteSpace(eventId) || eventId.Length > 256)
                return GatewayHistoryEndpoints.Error(context, 400, "ALARM_ACTION_INVALID", "报警记录或操作者无效。", "Invalid alarm or operator.");
            string note = request.Note?.Trim() ?? string.Empty;
            if (note.Length > 1000 || handling && note.Length == 0)
                return GatewayHistoryEndpoints.Error(context, 400, "ALARM_NOTE_INVALID",
                    "处理记录必填，备注最多 1000 个字符。", "A handling note is required. Notes may contain at most 1,000 characters.");

            var outcome = await Task.Run(() => gateway.AlarmJournal.ApplyOperatorAction(eventId, action, operatorId,
                context.User.Identity?.Name ?? "", note, ct), ct);
            if (outcome == AlarmOperatorOutcome.Updated) await Audit("success");
            return outcome switch
            {
                AlarmOperatorOutcome.Updated or AlarmOperatorOutcome.AlreadyRecorded => Results.Ok(new { success = true }),
                AlarmOperatorOutcome.NotFound => GatewayHistoryEndpoints.Error(context, 404, "ALARM_NOT_FOUND",
                    "报警记录已不存在。", "The alarm record is no longer available."),
                AlarmOperatorOutcome.AcknowledgeRequired => GatewayHistoryEndpoints.Error(context, 409, "ALARM_ACKNOWLEDGE_REQUIRED",
                    "请先确认报警。", "Acknowledge the alarm before recording its handling."),
                _ => GatewayHistoryEndpoints.Error(context, 409, "ALARM_ACTION_NOT_APPLICABLE",
                    "仅报警触发记录可办理。", "Only alarm activation records can be processed.")
            };
        });
    }
}

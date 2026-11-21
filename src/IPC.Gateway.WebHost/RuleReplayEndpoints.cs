using IPC.EdgeGateway;
using IPC.Gateway.Core.Application.Gateway.Contracts;
using IPC.Gateway.Core.Gateway;
using Microsoft.AspNetCore.Mvc;

namespace IPC.Gateway.WebHost;

internal static class RuleReplayEndpoints
{
    private static readonly SemaphoreSlim ReplaySlots = new(1, 1);

    internal sealed record ReplayRequest(string? RuleId, int? Version, FlowRuleDefinitionDto? Draft,
        DateTimeOffset From, DateTimeOffset To);

    public static void MapRuleReplayEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/diagnostics/rules/replay", Replay)
            .WithMetadata(new RequestSizeLimitAttribute(1_048_576));
    }

    private static async Task<IResult> Replay(HttpContext context, ReplayRequest request,
        GatewayCoreService gateway, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var user = context.User;
        bool draft = request.Draft != null;
        if (!GatewayAuthEndpoints.CanViewFlowRules(user) || !GatewayAuthEndpoints.CanDebugFlowRule(user) || !GatewayAuthEndpoints.CanViewHistory(user) ||
            (draft && !(GatewayAuthEndpoints.CanEditFlowRule(user) || GatewayAuthEndpoints.CanCreateFlowRule(user))))
            return Error("FORBIDDEN", 403, "需要流程调试、历史库查看及相应的草稿编辑权限。", "Rule debugging, historical data access and relevant draft editing permissions are required.");
        if (draft == !string.IsNullOrWhiteSpace(request.RuleId))
            return Error("INVALID_RULE", 400, "请选择一个已保存规则或编辑中的草稿。", "Select a saved rule or an in-editor draft.");

        if (!await ReplaySlots.WaitAsync(0, cancellationToken))
            return Error("BUSY", 429, "已有回放正在运行，请稍后重试。", "A replay is already running. Try again shortly.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var project = gateway.CurrentProject;
            var rule = draft ? GatewayConfigurationContractMapper.ToConfig(request.Draft!)
                : project.FlowRules.FirstOrDefault(rule => string.Equals(rule.Id, request.RuleId, StringComparison.OrdinalIgnoreCase));
            if (rule == null) return Error("RULE_NOT_FOUND", 404, "规则已删除或不存在。", "The rule has been deleted or is unavailable.");
            if (!draft && request.Version != null && request.Version != rule.Version)
                return Error("RULE_CHANGED", 409, "规则已更新，刷新列表后再试。", "The rule has changed. Refresh the rule list and try again.");
            var report = await FlowRuleReplay.Run(gateway.HistoryValueQueries, project, rule, draft,
                request.From, request.To, GatewayHistoryEndpoints.English(context), deadline.Token, gateway.CurrentMqttOptions);
            return Results.Ok(new { success = true, data = report });
        }
        catch (FlowReplayException ex)
        {
            return ex.Message switch
            {
                "INVALID_WINDOW" => Error(ex.Message, 400, "请选择有效的起止时间，单次最多回放 24 小时。", "Choose a valid time range of up to 24 hours."),
                "NO_BINDINGS" or "AMBIGUOUS_BINDING" => Error(ex.Message, 422, "规则引用的点位缺失或有歧义，请检查绑定。", "A rule input is missing or ambiguous. Check the tag bindings."),
                "UNSUPPORTED_RULE" => Error(ex.Message, 422, "包含模型推理或外部脚本的规则暂不支持历史回放。", "Historical replay currently excludes model inference and external script nodes."),
                "INPUT_LIMIT" => Error(ex.Message, 422, "一次回放最多使用 32 个输入点位。", "A replay supports up to 32 input tags."),
                "SAMPLE_LIMIT" => Error(ex.Message, 422, "历史数据量超过单次回放上限，请缩短时间范围。", "The historical data exceeds the replay limit. Use a shorter range."),
                _ => Error(ex.Message, 422, "规则存在配置错误或无法编译，请检查后重试。", "The rule has an invalid configuration or could not be compiled.")
            };
        }
        catch (HistoryIndexUnavailableException) { return Error("INDEX_UNAVAILABLE", 503, "历史索引尚未就绪，请稍后重试。", "The history index is not ready. Try again shortly."); }
        catch (HistorySnapshotExpiredException) { return Error("HISTORY_CHANGED", 409, "历史索引已变化，请重新回放。", "The history index changed. Run the replay again."); }
        catch (Exception) when (cancellationToken.IsCancellationRequested) { return Results.StatusCode(499); }
        catch (Exception) when (deadline.IsCancellationRequested) { return Error("TIMED_OUT", 504, "回放超时，请缩短时间范围。", "The replay timed out. Use a shorter range."); }
        catch (Exception error)
        {
            loggerFactory.CreateLogger("RuleReplay").LogError(error, "Historical rule replay failed.");
            return Error("UNAVAILABLE", 503, "规则回放暂时不可用。", "Historical rule replay is temporarily unavailable.");
        }
        finally { ReplaySlots.Release(); }

        IResult Error(string code, int status, string zh, string en) =>
            GatewayHistoryEndpoints.Error(context, status, "RULE_REPLAY_" + code, zh, en);
    }
}

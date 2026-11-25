using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using IPC.Gateway.Core.Domain.Users;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.HighAvailability;
using IPC.Gateway.HaSetup;

namespace IPC.Gateway.WebHost.HighAvailability;

internal static partial class HaManagementEndpoints
{
    internal static bool Allowed(ClaimsPrincipal user, string permission) =>
        GatewayAuthEndpoints.IsAdmin(user) || GatewayAuthEndpoints.HasPermission(user,permission);
    internal static IResult Error(int status, string code, string msg) =>
        Results.Json(new { success=false, errorCode=code, errorMessage=msg }, statusCode:status);
    private static IResult Ok(object data, int status=200) => Results.Json(new { success=true, data }, statusCode:status);
    private static RouteHandlerBuilder Needs(this RouteHandlerBuilder endpoint,string permission) =>
        endpoint.AddEndpointFilter(async (context,next) => Allowed(context.HttpContext.User,permission)
            ? await next(context) : Error(403,"FORBIDDEN","无权执行此操作。"));

    public static void MapHaManagement(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/health/ha-node", (GatewayHaClient client) =>
            Results.Json(new { client.ClusterId,client.NodeId,client.InstanceId })).AllowAnonymous();
        var group=routes.MapGroup("/api/ha");
        group.AddEndpointFilter(async (context,next) =>
        {
            var http=context.HttpContext;
            http.Response.Headers.CacheControl="no-store";
            if (http.User.Identity?.IsAuthenticated != true) return Error(401,"UNAUTHORIZED","请先登录。");
            object? result;
            try { result=await next(context); }
            catch (HaManagementError ex) { result=Error(ex.Status,ex.Code,ex.Message); }
            catch (HaRequestException ex) { result=Error(ex.StatusCode,ex.Code,ex.Message); }
            catch (HttpRequestException) { result=Error(503,"HA_NO_QUORUM","无法确认多数派。"); }
            catch (OperationCanceledException) when (!http.RequestAborted.IsCancellationRequested)
            { result=Error(503,"HA_TIMEOUT","等待仲裁回应超时，请查询原操作。"); }
            if (!HttpMethods.IsGet(http.Request.Method) && !http.Request.Path.Value!.EndsWith("/precheck",StringComparison.Ordinal))
            {
                string outcome=(result as IStatusCodeHttpResult)?.StatusCode is >= 400 ? "denied" : "accepted";
                await GatewayAuthEndpoints.WriteSecurityAuditAsync(http,http.RequestServices.GetService<IGatewayAuditLogStore>(),
                    "ha.manage",outcome,http.Request.Path,http.User.Identity?.Name ?? "",
                    http.User.FindFirst(ClaimTypes.Role)?.Value ?? "","");
            }
            return result;
        });
        group.MapGet("/capabilities",(HaManagementService service)=>Ok(service.Capabilities()));
        MapDisasterManagement(group);
        group.MapTransportManagement();
        MapProvisioning(group);
        group.MapGet("/status",async (HttpContext ctx, HaManagementService service)=>
            Ok(await service.StatusAsync(Allowed(ctx.User,GatewayPermissions.SwitchHa),ctx.RequestAborted))).Needs(GatewayPermissions.ViewHa);
        group.MapGet("/config",async (HaManagementService service,CancellationToken ct)=>Ok(await service.ConfigAsync(ct))).Needs(GatewayPermissions.ViewHa);
        group.MapPost("/cluster-switch",async (ClusterSwitchRequest req, HaManagementService service, ClaimsPrincipal actor,CancellationToken ct)=>
            Ok(await service.ChangeModeAsync(req,actor.Identity?.Name,ct),202)).Needs(GatewayPermissions.ManageHaConfig);
        group.MapGet("/cluster-switch/{id}",async (string id, GatewayHaClient client,CancellationToken ct)=>
        {
            HaManagementService.RequireId(id);
            return Ok(await client.QueryAsync("/v1/mode/"+id,ct));
        }).Needs(GatewayPermissions.ViewHa);
        group.MapPut("/config/draft",async (DraftRequest body,HaManagementService service,CancellationToken ct)=>
            Ok(await service.SaveDraftAsync(body,ct))).Needs(GatewayPermissions.ManageHaConfig);
        group.MapPost("/config/precheck",async (PrecheckRequest body,HaManagementService service,CancellationToken ct)=>
            Ok(await service.CheckDraftAsync(body,ct))).Needs(GatewayPermissions.ManageHaConfig);
        group.MapPost("/config/apply",async (ApplyRequest body,HaManagementService service,ClaimsPrincipal actor,CancellationToken ct)=>
            Ok(await service.ApplyAsync(body,actor.Identity?.Name,ct),202)).Needs(GatewayPermissions.ManageHaConfig);
        group.MapGet("/config/operations/{id}",async (string id,HaManagementService service,CancellationToken ct)=>
            Ok(await service.ReadChangeAsync(id,ct))).Needs(GatewayPermissions.ViewHa);
        group.MapPost("/switchovers/precheck",async (SwitchRequest req,GatewayHaClient client,CancellationToken ct)=>
            Ok(await CheckTransfer(req,client,ct))).Needs(GatewayPermissions.SwitchHa);
        group.MapPost("/switchovers", async (SwitchRequest req,GatewayHaClient client,HttpContext ctx)=>
        {
            HaManagementService.RequireId(req.OperationId);
            if (string.IsNullOrWhiteSpace(req.Reason) || req.Reason.Length > 512) return Error(400,"HA_REASON_REQUIRED","请填写切换原因。");
            if (req.Target is not ("A" or "B") || !client.IsCluster)
                return Error(409,"HA_INVALID_TARGET","请检查集群状态和切换目标。");
            // The state machine validates the current owner and target and resolves retransmissions after transfer.
            var result=await client.CommandAsync("/v1/transfer",new { clusterId=client.ClusterId,nodeId=client.NodeId,
                instanceId=client.InstanceId,epoch=req.ExpectedEpoch,target=req.Target,requestId=req.OperationId,
                reason=req.Reason,actor=ctx.User.Identity?.Name },ctx.RequestAborted);
            return Ok(new {operationId=req.OperationId,status="Submitted", decision=result},202);
        }).Needs(GatewayPermissions.SwitchHa);
        group.MapGet("/switchovers/{id}",async(string id,GatewayHaClient client,CancellationToken ct)=>
        {
            HaManagementService.RequireId(id);
            return Ok(await client.QueryAsync("/v1/switchovers/"+Uri.EscapeDataString(id),ct));
        }).Needs(GatewayPermissions.ViewHa);
        group.MapGet("/switchovers", (HttpContext ctx,GatewayHaClient client)=>ReadPage(ctx,client,"switchovers")).Needs(GatewayPermissions.ViewHa);
        group.MapGet("/write-attempts",(HttpContext ctx,GatewayHaClient client)=>ReadPage(ctx,client,"write-attempts")).Needs(GatewayPermissions.ViewHaWrites);
        group.MapPost("/maintenance",async (MaintenanceRequest req,GatewayHaClient client,HttpContext ctx)=>
        {
            HaManagementService.RequireId(req.OperationId);
            if (req.NodeId is not ("A" or "B" or "C") || string.IsNullOrWhiteSpace(req.Reason) || req.Reason.Length>512)
                return Error(400,"HA_INVALID_MAINTENANCE","请核对节点和维护原因。");
            return Ok(await client.CommandAsync("/v1/maintenance",new {clusterId=client.ClusterId,nodeId=client.NodeId,
                instanceId=client.InstanceId,epoch=req.ExpectedEpoch,target=req.NodeId,maintenance=req.Enabled,requestId=req.OperationId,
                reason=req.Reason,actor=ctx.User.Identity?.Name},ctx.RequestAborted));
        }).Needs(GatewayPermissions.MaintainHa);
    }

    private static void MapProvisioning(RouteGroupBuilder group)
    {
        var api = group.MapGroup("/setup");
        api.AddEndpointFilter(async (ctx, next) =>
        {
            try { return await next(ctx); }
            catch (UnauthorizedAccessException)
            {
                return Error(500, "HA_SETUP_FAILED", "网关服务账号无法访问本机热备配置目录。");
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException or IOException or JsonException or HttpRequestException)
            {
                return Error(400, "HA_SETUP_FAILED", ex is HttpRequestException ? "配对失败，请核对远端地址及配对码。" : ex.Message);
            }
        });
        api.MapGet("/status", (EmbeddedGatewayMember member) =>
        {
            var state = member.Setup?.Status();
            return Ok(new { member.Enabled, member.Role, roles = state?.Roles ?? [], clusterId = state?.ClusterId,
                configured = state?.Configured ?? false, restartRequired = state?.Configured == true && !member.Loaded,
                pairPort = state?.PairPort ?? 7381 });
        }).Needs(GatewayPermissions.ViewHa);
        static GatewaySetupCoordinator Setup(EmbeddedGatewayMember member) => member.Setup ??
            throw new InvalidOperationException("此安装未启用内置仲裁。");
        api.MapPost("/identity", (RoleSettings input, EmbeddedGatewayMember member) =>
            Ok(Setup(member).RegisterIdentity(input))).Needs(GatewayPermissions.ManageHaConfig);
        api.MapPost("/code", async (RoleChoice input, EmbeddedGatewayMember member, CancellationToken ct) =>
            Ok(await Setup(member).IssueCodeAsync(input, ct))).Needs(GatewayPermissions.ManageHaConfig);
        api.MapPost("/commit", async (CreateCluster input, EmbeddedGatewayMember member, CancellationToken ct) =>
            Ok(await Setup(member).CommitAsync(input, ct))).Needs(GatewayPermissions.ManageHaConfig);
    }

    private sealed record TransferCheck(bool Passed, HaCheck[] Checks);
    private static async Task<TransferCheck> CheckTransfer(SwitchRequest req,GatewayHaClient client,CancellationToken ct)
    {
        if (req.Target is not ("A" or "B")) throw new HaManagementError(400,"HA_INVALID_TARGET","目标必须是 A 或 B。");
        if (!client.IsCluster) throw new HaManagementError(409,"HA_NOT_CONFIGURED","尚未组网。");
        var s=await client.QueryAsync("/v1/status",ct);
        string? Field(string key)=>s.TryGetProperty(key,out var v)?v.ToString():null;
        string? code=!s.TryGetProperty("quorumAvailable",out var quorum) || quorum.ValueKind!=JsonValueKind.True ? "HA_NO_QUORUM" :
            Field("epoch")!=req.ExpectedEpoch ? "HA_STALE_EPOCH" :
            Field("owner")!=client.NodeId || !client.CanRunBusiness ? "HA_NOT_ACTIVE" :
            req.Target==client.NodeId ? "HA_INVALID_TARGET" : null;
        if (code==null && !HaManagementService.IsReady(s,req.Target))
            code="HA_TARGET_NOT_READY";
        return new(code==null,[new(req.Target,code==null,code ?? "目标具备接管条件。")]);
    }

    private static async Task<IResult> ReadPage(HttpContext ctx,GatewayHaClient client,string resource)
    {
        var q=ctx.Request.Query;
        if (!int.TryParse(q["page"].FirstOrDefault() ?? "1",out int page) || page<1 ||
            !int.TryParse(q["pageSize"].FirstOrDefault() ?? "20",out int size) || size is <1 or >500)
            return Error(400,"HA_INVALID_PAGE","分页参数无效。");
        var fields=q.Where(kv=>kv.Key is "page" or "pageSize" or "nodeId" or "result" or "operation" or "from" or "to")
            .ToArray();
        if (fields.Any(kv=>kv.Value.Count!=1 || kv.Value.ToString().Length>128))
            return Error(400,"HA_INVALID_FILTER","查询条件无效。");
        if (!TryRecordTime(q["from"],out var from) || !TryRecordTime(q["to"],out var to) ||
            from != null && to != null && from > to)
            return Error(400,"HA_INVALID_FILTER","查询时间无效，请使用包含时区的时间，且结束时间不得早于开始时间。");
        fields=fields.Select(kv=>kv.Key is "from" or "to"
            ? new KeyValuePair<string,Microsoft.Extensions.Primitives.StringValues>(kv.Key,
                (kv.Key == "from" ? from : to)?.ToString("O",CultureInfo.InvariantCulture)) : kv).ToArray();
        // The registered arbiters retain history while HA is disabled (managed standalone).
        // Runtime admission mode must not turn an existing audit trail into an empty page.
        if (client.Endpoints.Count == 0 && !client.IsCluster)
            return Ok(new {items=Array.Empty<object>(),total=0,page,pageSize=size});
        if (resource != "write-attempts")
            return Ok(await client.QueryAsync("/v1/"+resource+QueryString.Create(fields),ctx.RequestAborted));
        bool showTarget = GatewayAuthEndpoints.IsAdmin(ctx.User);
        if (q["evidence"] == "local")
        {
            if (q["commandId"].Count>1 || q["commandId"].ToString().Length>128) return Error(400,"HA_INVALID_FILTER","命令 ID 无效。");
            var entries = client.ReadWriteResults(page,size,q["nodeId"],q["result"],
                from?.ToString("O",CultureInfo.InvariantCulture),to?.ToString("O",CultureInfo.InvariantCulture),q["commandId"]);
            return Ok(new {entries.Total, items=entries.Items.Select(r => new {r.Timestamp,r.NodeId,r.Epoch,r.AttemptId,r.CommandId,
                r.InstanceId,r.Digest,r.CommitIndex,deviceResult=r.Result,grantResult=r.CommitIndex != null ? "Committed" : null,
                r.SourceType,r.SegmentIndex,r.TargetDigest,target=showTarget?r.Target:null,address=showTarget?r.Address:null})});
        }
        var audit = await client.QueryAsync("/v1/"+resource+QueryString.Create(fields),ctx.RequestAborted);
        var response = JsonNode.Parse(audit.GetRawText())!.AsObject();
        if (response["items"] is JsonArray items)
        {
            var local = client.FindWriteResults(items.Select(i=>i?["attemptId"]?.GetValue<string>() ?? "").ToArray());
            foreach (var entry in items.OfType<JsonObject>())
                if (entry["result"]?.GetValue<string>() == "Committed" && entry["attemptId"]?.GetValue<string>() is string attempt &&
                    local.TryGetValue(attempt,out var r) && r.NodeId == entry["nodeId"]?.GetValue<string>() && r.Epoch == entry["epoch"]?.GetValue<string>())
                {
                    entry["deviceResult"]=r.Result; entry["digest"]=r.Digest; entry["sourceType"]=r.SourceType;
                    entry["segmentIndex"]=r.SegmentIndex; entry["targetDigest"]=r.TargetDigest;
                    if (showTarget) { entry["target"]=r.Target; entry["address"]=r.Address; }
                }
        }
        return Ok(response);
    }

    private static bool TryRecordTime(string? text,out DateTimeOffset? utc)
    {
        utc=null;
        if (string.IsNullOrEmpty(text)) return true;
        // datetime-local belongs to the browser's zone; never reinterpret it in the server's zone.
        bool hasZone=text.EndsWith('Z') || text.Length>=6 && text[^3]==':' && text[^6] is '+' or '-';
        if (!hasZone || !DateTimeOffset.TryParse(text,CultureInfo.InvariantCulture,DateTimeStyles.None,out var value)) return false;
        utc=value.ToUniversalTime();
        return true;
    }
}

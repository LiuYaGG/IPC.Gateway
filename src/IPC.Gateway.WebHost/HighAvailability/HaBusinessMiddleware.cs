using System.Globalization;
using System.Text.Json;
using IPC.Gateway.Arbiter;
using IPC.Gateway.HighAvailability;
using IPC.Plc.Communication.Core;

namespace IPC.Gateway.WebHost.HighAvailability;

internal sealed class HaBusinessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context,GatewayHaClient client,HaDraftStore drafts,
        HaTransportManagementService transport,EmbeddedGatewayMember member)
    {
        var request=context.Request;
        string path=(request.Path.Value ?? "").TrimEnd('/');
        if (string.Equals(path,"/health/ha-active",StringComparison.OrdinalIgnoreCase))
        {
            bool available=client.BusinessReady && context.RequestServices.GetRequiredService<GatewayStartupState>().IsReady;
            context.Response.StatusCode=available ? 200 : 503;
            await context.Response.WriteAsJsonAsync(new {active=available},context.RequestAborted);
            return;
        }
        bool updateAction = HttpMethods.IsPost(request.Method) &&
            request.Path.StartsWithSegments("/api/maintenance/updates") &&
            (path.Equals("/api/maintenance/updates/apply",StringComparison.OrdinalIgnoreCase) ||
             path.EndsWith("/prepare",StringComparison.OrdinalIgnoreCase));
        JsonElement? updateClusterState = null;
        if (updateAction)
        {
            try
            {
                updateClusterState = await ValidateUpdateStateAsync(client,drafts,transport,member,context.RequestAborted);
            }
            catch (HaManagementError ex) { await HaManagementEndpoints.Error(ex.Status,ex.Code,ex.Message).ExecuteAsync(context); return; }
            catch (HaRequestException ex) { await HaManagementEndpoints.Error(503,ex.Code,"无法核实集群升级条件，请恢复仲裁通信后重试。").ExecuteAsync(context); return; }
            catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
            { await HaManagementEndpoints.Error(503,"HA_UPDATE_STATE_UNAVAILABLE","核实集群升级条件超时，请恢复仲裁通信后重试。").ExecuteAsync(context); return; }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or HttpRequestException)
            { await HaManagementEndpoints.Error(409,"HA_UPDATE_STATE_UNAVAILABLE","无法核实本机组网状态，不能准备或执行升级。请检查组网配置和持久目录。").ExecuteAsync(context); return; }
        }
        if (!client.IsCluster || !request.Path.StartsWithSegments("/api") ||
            request.Path.StartsWithSegments("/api/ha") || request.Path.StartsWithSegments("/api/auth") ||
            HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method))
        { await next(context); return; }
        // Driver signing updates only this node's manifest and trust store.
        // Endpoint authentication and certificate-management permissions still apply.
        var signingParts = path.Split('/');
        if (HttpMethods.IsPost(request.Method) && signingParts.Length == 6 &&
            signingParts[1].Equals("api", StringComparison.OrdinalIgnoreCase) &&
            signingParts[2].Equals("commercial", StringComparison.OrdinalIgnoreCase) &&
            signingParts[3].Equals("drivers", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(signingParts[4]) &&
            signingParts[5].Equals("signing", StringComparison.OrdinalIgnoreCase))
        { await next(context); return; }
        if (request.Path.StartsWithSegments("/api/maintenance/updates"))
        {
            if (path.Equals("/api/maintenance/updates/apply",StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var state=updateClusterState ?? await client.QueryAsync("/v1/status",context.RequestAborted);
                    ValidateApplyState(client,state);
                }
                catch (HaManagementError ex) { await HaManagementEndpoints.Error(ex.Status,ex.Code,ex.Message).ExecuteAsync(context); return; }
                catch (HaRequestException ex) { await HaManagementEndpoints.Error(503,ex.Code,ex.Message).ExecuteAsync(context); return; }
            }
            await next(context); return;
        }
        if (path.Equals("/api/commercial/tags/preflight",StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/diagnostics/rules/replay",StringComparison.OrdinalIgnoreCase))
        { await next(context); return; }

        if (!client.BusinessReady)
        { await HaManagementEndpoints.Error(409,"HA_NOT_ACTIVE","当前网关没有业务主身份。").ExecuteAsync(context); return; }
        long? expected=null;
        if (request.Headers.TryGetValue("X-IPC-Expected-Epoch",out var header))
        {
            if (header.Count!=1 || !long.TryParse(header[0],NumberStyles.None,CultureInfo.InvariantCulture,out long epoch) || epoch!=client.Epoch)
            { await HaManagementEndpoints.Error(409,"HA_STALE_EPOCH","业务任期已经变化。").ExecuteAsync(context); return; }
            expected=epoch;
        }
        string? commandId=request.Headers["X-IPC-Command-Id"].FirstOrDefault();
        if (commandId is { Length: > 0 } && !Guid.TryParse(commandId,out _))
        { await HaManagementEndpoints.Error(400,"HA_INVALID_COMMAND_ID","命令 ID 需要为 UUID。").ExecuteAsync(context); return; }
        commandId ??= Guid.NewGuid().ToString("N");
        context.Response.Headers["X-IPC-Command-Id"]=commandId;
        using (PlcWriteRequestContext.Push(commandId,expected ?? client.Epoch)) await next(context);
    }

    internal static async Task<JsonElement?> ValidateUpdateStateAsync(GatewayHaClient client,HaDraftStore drafts,
        HaTransportManagementService transport,EmbeddedGatewayMember member,CancellationToken cancellationToken)
    {
        // Emergency authority deliberately looks like a singleton. Always check
        // local and consensus migration state before the IsCluster shortcut.
        if (client.DisasterTakeover != null)
            throw new HaManagementError(409,"HA_UPDATE_DISASTER_ACTIVE","人工灾难接管期间不能准备或执行升级，请先恢复正常仲裁并退出人工接管。");
        if (transport.HasPending || drafts.Read().TransportPlanId != null ||
            member.MemberConfig != null && TransportMigrationFiles.HasPending(member.MemberConfig))
            throw new HaManagementError(409,"HA_UPDATE_TRANSPORT_PENDING","组网变更尚未完成，不能准备或执行升级，请先完成原变更计划。");
        if (client.Endpoints.Count == 0) return null;
        var state=await client.QueryAsync("/v1/status",cancellationToken);
        if (!state.TryGetProperty("quorumAvailable",out var quorum) || quorum.ValueKind != JsonValueKind.True)
            throw new HaManagementError(409,"HA_UPDATE_STATE_UNAVAILABLE","无法核实集群升级条件，请恢复仲裁通信后重试。");
        if (state.TryGetProperty("transportPending",out var pending) && pending.ValueKind == JsonValueKind.True)
            throw new HaManagementError(409,"HA_UPDATE_TRANSPORT_PENDING","组网变更尚未完成，不能准备或执行升级，请先完成原变更计划。");
        return state;
    }

    internal static void ValidateApplyState(GatewayHaClient client,JsonElement? snapshot)
    {
        if (!client.IsCluster) return;
        bool safe=snapshot is { } state &&
            state.TryGetProperty("quorumAvailable",out var quorum) && quorum.ValueKind==JsonValueKind.True &&
            state.TryGetProperty("reachableMembers",out var count) && count.TryGetInt32(out var n) && n==3 &&
            state.TryGetProperty("owner",out var owner) && !string.IsNullOrWhiteSpace(owner.ToString()) && owner.ToString()!=client.NodeId &&
            state.TryGetProperty("maintenance",out var maintenance) && maintenance.ValueKind==JsonValueKind.Array &&
            maintenance.EnumerateArray().Any(x=>x.ToString()==client.NodeId);
        if (!safe) throw new HaManagementError(409,"HA_MAINTENANCE_BLOCKED","请先交接业务，确认其他成员健康，并将本节点置于维护状态。");
    }
}

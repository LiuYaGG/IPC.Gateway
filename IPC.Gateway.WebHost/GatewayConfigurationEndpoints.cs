/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.WebHost
* 项目描述 ：
* 类 名 称 ：GatewayConfigurationEndpoints
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.WebHost
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
using IPC.Gateway.Core.Application.Gateway;
using IPC.Gateway.Core.Application.Gateway.Contracts;
using IPC.Gateway.Core.Gateway;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IPC.Gateway.WebHost;

public static class GatewayConfigurationEndpoints
{
    private const int RuntimeSnapshotTagBatchSize = 500;

    public static IEndpointRouteBuilder MapGatewayConfigurationEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/config");

        group.MapGet("/status", (ClaimsPrincipal user, IGatewayApplicationService gateway) =>
            ExecuteReadAuthorized(user, CanViewRuntimeStatus, "当前用户没有查看运行状态权限。", () => gateway.GetStatus()));

        group.MapGet("/status/events", StreamRuntimeEvents);

        group.MapGet("/status/tags", (
            ClaimsPrincipal user,
            IGatewayApplicationService gateway,
            string? deviceId,
            string? deviceName,
            string? groupId,
            string? groupName,
            string? tagId,
            string? tagName) =>
            ExecuteReadAuthorized(user, GatewayAuthEndpoints.CanViewDevices, "当前用户没有查看设备实时值权限。", () => gateway.GetTagSnapshots(new RuntimeTagSnapshotQuery
            {
                DeviceId = deviceId ?? string.Empty,
                DeviceName = deviceName ?? string.Empty,
                GroupId = groupId ?? string.Empty,
                GroupName = groupName ?? string.Empty,
                TagId = tagId ?? string.Empty,
                TagName = tagName ?? string.Empty
            })));

        group.MapGet("/protocols", (ClaimsPrincipal user, GatewayProtocolCatalogService protocols) =>
            ExecuteReadAuthorized(user, GatewayAuthEndpoints.CanViewDevices, "当前用户没有查看设备协议目录权限。", () => protocols.GetProtocols()));

        group.MapGet("/status/mqtt", (ClaimsPrincipal user, IGatewayApplicationService gateway) =>
            ExecuteReadAuthorized(user, GatewayAuthEndpoints.CanViewMqtt, "当前用户没有查看 MQTT 状态权限。", () => gateway.GetMqttStatus()));

        group.MapGet("/status/opcua", (ClaimsPrincipal user, IGatewayApplicationService gateway) =>
            ExecuteReadAuthorized(user, GatewayAuthEndpoints.CanViewOpcUa, "当前用户没有查看 OPC UA Server 状态权限。", () => gateway.GetOpcUaStatus()));

        group.MapGet("/status/history", (ClaimsPrincipal user, IGatewayApplicationService gateway) =>
            ExecuteReadAuthorized(user, GatewayAuthEndpoints.CanViewHistory, "当前用户没有查看历史库状态权限。", () => gateway.GetHistoryStatus()));

        group.MapGet("/sync", (ClaimsPrincipal user, IGatewayApplicationService gateway) =>
            ExecuteReadAuthorized(user, GatewayConfigurationSecurity.CanUseConfigurationSync, "当前用户没有同步网关数据权限。", () =>
                GatewayConfigurationSecurity.SanitizeSync(gateway.GetSync(), user)));

        group.MapGet("/versions", async (ClaimsPrincipal user, IGatewayApplicationService gateway, string? type, int? limit) =>
            await ExecuteReadAuthorizedAsync(user, GatewayAuthEndpoints.CanViewProject, "当前用户没有查看配置版本权限。", () => gateway.GetConfigurationVersionsAsync(new ConfigurationVersionsQuery
            {
                ConfigType = type ?? string.Empty,
                Limit = limit ?? 50
            })));

        group.MapGet("/audit", async (ClaimsPrincipal user, IGatewayAuditLogStore auditStore, int? limit, int? offset, string? target, string? outcome, string? username, DateTime? from, DateTime? to) =>
        {
            if (!GatewayAuthEndpoints.CanViewAudit(user))
                return Results.Json(ApiResult.Fail("当前用户没有查看审计日志权限。"), statusCode: StatusCodes.Status403Forbidden);

            return Ok(await GatewayAuditLog.ReadPageAsync(new GatewayAuditLogQuery
            {
                Limit = limit ?? 100,
                Offset = offset ?? 0,
                Target = target ?? string.Empty,
                Outcome = outcome ?? string.Empty,
                UserName = username ?? string.Empty,
                FromTime = from,
                ToTime = to
            }, auditStore));
        });

        group.MapGet("/audit/export", async (ClaimsPrincipal user, IGatewayAuditLogStore auditStore, int? limit, string? target, string? outcome, string? username, DateTime? from, DateTime? to) =>
        {
            if (!GatewayAuthEndpoints.CanExportAudit(user))
                return Results.Json(ApiResult.Fail("当前用户没有导出审计日志权限。"), statusCode: StatusCodes.Status403Forbidden);

            IReadOnlyList<GatewayAuditLogEntry> entries = await GatewayAuditLog.ReadRecentAsync(new GatewayAuditLogQuery
            {
                Limit = limit ?? 500,
                Offset = 0,
                Target = target ?? string.Empty,
                Outcome = outcome ?? string.Empty,
                UserName = username ?? string.Empty,
                FromTime = from,
                ToTime = to
            }, auditStore);

            byte[] payload = GatewayAuditCsv.BuildUtf8WithBom(entries);
            return Results.File(payload, "text/csv", $"gateway-audit-{DateTime.Now:yyyyMMddHHmmss}.csv");
        });

        group.MapPost("/rollback", async (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, RollbackConfigurationCommand command) =>
            await ExecuteAuthorizedAsync(context, user, GatewayAuthEndpoints.CanEditProject, "当前用户没有保存项目配置权限。", () => gateway.RollbackConfigurationAsync(command)));

        group.MapPost("/apply", async (IGatewayApplicationService gateway, HttpRequest request, ClaimsPrincipal user) =>
        {
            if (!GatewayAuthEndpoints.CanEditProject(user))
                return Results.Json(ApiResult.Fail("当前用户没有保存项目配置权限。"), statusCode: StatusCodes.Status403Forbidden);

            using StreamReader reader = new StreamReader(request.Body);
            string payload = await reader.ReadToEndAsync();
            return await ExecuteAuditedAsync(request.HttpContext, () => gateway.ApplyConfigurationCommandAsync(new RawConfigurationCommand
            {
                Source = "WebApi",
                Payload = payload
            }));
        });

        group.MapGet("/project", (ClaimsPrincipal user, IGatewayApplicationService gateway) =>
            ExecuteReadAuthorized(user, GatewayAuthEndpoints.CanViewProject, "当前用户没有查看项目配置权限。", () =>
                GatewayConfigurationSecurity.SanitizeProject(gateway.GetProject())));

        group.MapPut("/project", async (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, SaveProjectConfigurationCommand command) =>
            await ExecuteAuthorizedAsync(context, user, GatewayAuthEndpoints.CanEditProject, "当前用户没有保存项目配置权限。", () => gateway.SaveProjectAsync(command)));

        group.MapPost("/validate", (IGatewayApplicationService gateway, ValidateProjectConfigurationCommand command) =>
            Execute(() => gateway.ValidateProject(command)));

        group.MapGet("/channels", (ClaimsPrincipal user, IGatewayApplicationService gateway) =>
            ExecuteReadAuthorized(user, GatewayAuthEndpoints.CanViewDevices, "当前用户没有查看通道配置权限。", gateway.GetChannels));

        group.MapPost("/channels", async (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, SaveChannelConfigurationCommand command) =>
            await ExecuteAuthorizedAsync(context, user, GatewayAuthEndpoints.CanCreateDevice, "当前用户没有新增通道权限。", () => gateway.AddChannelAsync(command)));

        group.MapPut("/channels/{channelId}", async (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, string channelId, SaveChannelConfigurationCommand command) =>
            await ExecuteAuthorizedAsync(context, user, GatewayAuthEndpoints.CanEditDevice, "当前用户没有编辑通道权限。", () => gateway.UpdateChannelAsync(channelId, command)));

        group.MapDelete("/channels/{channelId}", async (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, string channelId) =>
            await ExecuteAuthorizedAsync(context, user, GatewayAuthEndpoints.CanDeleteDevice, "当前用户没有删除通道权限。", () => gateway.DeleteChannelAsync(channelId)));

        group.MapGet("/devices", (ClaimsPrincipal user, IGatewayApplicationService gateway) =>
            ExecuteReadAuthorized(user, GatewayAuthEndpoints.CanViewDevices, "当前用户没有查看设备配置权限。", () =>
                GatewayConfigurationSecurity.SanitizeDevices(gateway.GetDevices())));

        group.MapPost("/devices", async (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, SaveDeviceConfigurationCommand command) =>
            await ExecuteAuthorizedAsync(context, user, GatewayAuthEndpoints.CanCreateDevice, "当前用户没有新增设备权限。", () => gateway.AddDeviceAsync(command)));

        group.MapPut("/devices/{deviceId}", async (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, string deviceId, SaveDeviceConfigurationCommand command) =>
            await ExecuteAuthorizedAsync(context, user, GatewayAuthEndpoints.CanEditDevice, "当前用户没有编辑设备权限。", () => gateway.UpdateDeviceAsync(deviceId, command)));

        group.MapDelete("/devices/{deviceId}", async (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, string deviceId) =>
            await ExecuteAuthorizedAsync(context, user, GatewayAuthEndpoints.CanDeleteDevice, "当前用户没有删除设备权限。", () => gateway.DeleteDeviceAsync(deviceId)));

        group.MapGet("/devices/{deviceId}/groups", (ClaimsPrincipal user, IGatewayApplicationService gateway, string deviceId) =>
            ExecuteReadAuthorized(user, GatewayAuthEndpoints.CanViewDevices, "当前用户没有查看设备分组权限。", () => gateway.GetDeviceGroups(deviceId)));

        group.MapPost("/devices/{deviceId}/groups", async (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, string deviceId, SaveGroupConfigurationCommand command) =>
            await ExecuteAuthorizedAsync(context, user, GatewayAuthEndpoints.CanCreateGroup, "当前用户没有新增分组权限。", () => gateway.AddGroupAsync(deviceId, command)));

        group.MapPut("/groups/{groupId}", async (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, string groupId, SaveGroupConfigurationCommand command) =>
            await ExecuteAuthorizedAsync(context, user, GatewayAuthEndpoints.CanEditGroup, "当前用户没有编辑分组权限。", () => gateway.UpdateGroupAsync(groupId, command)));

        group.MapDelete("/groups/{groupId}", async (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, string groupId) =>
            await ExecuteAuthorizedAsync(context, user, GatewayAuthEndpoints.CanDeleteGroup, "当前用户没有删除分组权限。", () => gateway.DeleteGroupAsync(groupId)));

        group.MapGet("/devices/{deviceId}/tags", (ClaimsPrincipal user, IGatewayApplicationService gateway, string deviceId) =>
            ExecuteReadAuthorized(user, GatewayAuthEndpoints.CanViewDevices, "当前用户没有查看设备标签权限。", () => gateway.GetDeviceTags(deviceId)));

        group.MapPost("/devices/{deviceId}/tags", async (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, string deviceId, SaveTagConfigurationCommand command) =>
            await ExecuteAuthorizedAsync(context, user, GatewayAuthEndpoints.CanCreateTag, "当前用户没有新增标签权限。", () => gateway.AddDeviceTagAsync(deviceId, command)));

        group.MapGet("/groups/{groupId}/tags", (ClaimsPrincipal user, IGatewayApplicationService gateway, string groupId) =>
            ExecuteReadAuthorized(user, GatewayAuthEndpoints.CanViewDevices, "当前用户没有查看分组标签权限。", () => gateway.GetGroupTags(groupId)));

        group.MapPost("/groups/{groupId}/tags", async (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, string groupId, SaveTagConfigurationCommand command) =>
            await ExecuteAuthorizedAsync(context, user, GatewayAuthEndpoints.CanCreateTag, "当前用户没有新增标签权限。", () => gateway.AddGroupTagAsync(groupId, command)));

        group.MapPut("/tags/{tagId}", async (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, string tagId, SaveTagConfigurationCommand command) =>
            await ExecuteAuthorizedAsync(context, user, GatewayAuthEndpoints.CanEditTag, "当前用户没有编辑标签权限。", () => gateway.UpdateTagAsync(tagId, command)));

        group.MapDelete("/tags/{tagId}", async (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, string tagId) =>
            await ExecuteAuthorizedAsync(context, user, GatewayAuthEndpoints.CanDeleteTag, "当前用户没有删除标签权限。", () => gateway.DeleteTagAsync(tagId)));

        group.MapPost("/tags/write", async (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, WriteTagCommand command) =>
            await ExecuteAuthorizedAsync(context, user, GatewayAuthEndpoints.CanWriteTag, "当前用户没有标签写入权限。", () => gateway.WriteTagAsync(command)));

        group.MapGet("/flow-rules", (ClaimsPrincipal user, IGatewayApplicationService gateway) =>
            ExecuteReadAuthorized(user, GatewayAuthEndpoints.CanViewFlowRules, "当前用户没有查看规则引擎配置权限。", () =>
                GatewayConfigurationSecurity.SanitizeFlowRules(gateway.GetFlowRules())));

        group.MapGet("/flow-rules/status", (ClaimsPrincipal user, IGatewayApplicationService gateway) =>
            ExecuteReadAuthorized(user, GatewayAuthEndpoints.CanViewFlowRules, "当前用户没有查看规则引擎状态权限。", () => gateway.GetStatus().FlowRuleEngine));

        group.MapPost("/flow-rules", async (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, SaveFlowRuleDefinitionCommand command) =>
            await ExecuteAuthorizedAsync(context, user, GatewayAuthEndpoints.CanCreateFlowRule, "当前用户没有新增规则引擎权限。", () => gateway.AddFlowRuleAsync(command)));

        group.MapPut("/flow-rules/{ruleId}", async (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, string ruleId, SaveFlowRuleDefinitionCommand command) =>
            await ExecuteAuthorizedAsync(context, user, GatewayAuthEndpoints.CanEditFlowRule, "当前用户没有编辑规则引擎权限。", () => gateway.UpdateFlowRuleAsync(ruleId, command)));

        group.MapDelete("/flow-rules/{ruleId}", async (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, string ruleId) =>
            await ExecuteAuthorizedAsync(context, user, GatewayAuthEndpoints.CanDeleteFlowRule, "当前用户没有删除规则引擎权限。", () => gateway.DeleteFlowRuleAsync(ruleId)));

        group.MapGet("/mqtt", (ClaimsPrincipal user, IGatewayApplicationService gateway) =>
            ExecuteReadAuthorized(user, GatewayAuthEndpoints.CanViewMqtt, "当前用户没有查看 MQTT 配置权限。", () =>
                GatewayConfigurationSecurity.SanitizeMqtt(gateway.GetMqttOptions())));

        group.MapGet("/mqtt/status", (ClaimsPrincipal user, IGatewayApplicationService gateway) =>
            ExecuteReadAuthorized(user, GatewayAuthEndpoints.CanViewMqtt, "当前用户没有查看 MQTT 状态权限。", () => gateway.GetMqttStatus()));

        group.MapPut("/mqtt", async (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, SaveMqttConfigurationCommand command) =>
            await ExecuteAuthorizedAsync(context, user, GatewayAuthEndpoints.CanEditMqtt, "当前用户没有保存 MQTT 配置权限。", () => gateway.UpdateMqttOptionsAsync(command)));

        group.MapGet("/opcua", (ClaimsPrincipal user, IGatewayApplicationService gateway) =>
            ExecuteReadAuthorized(user, GatewayAuthEndpoints.CanViewOpcUa, "当前用户没有查看 OPC UA Server 配置权限。", () => gateway.GetOpcUaOptions()));

        group.MapGet("/opcua/status", (ClaimsPrincipal user, IGatewayApplicationService gateway) =>
            ExecuteReadAuthorized(user, GatewayAuthEndpoints.CanViewOpcUa, "当前用户没有查看 OPC UA Server 状态权限。", () => gateway.GetOpcUaStatus()));

        group.MapPut("/opcua", async (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, SaveOpcUaServerConfigurationCommand command) =>
            await ExecuteAuthorizedAsync(context, user, GatewayAuthEndpoints.CanEditOpcUa, "当前用户没有保存 OPC UA Server 配置权限。", () => gateway.UpdateOpcUaOptionsAsync(command)));

        group.MapGet("/history", (ClaimsPrincipal user, IGatewayApplicationService gateway) =>
            ExecuteReadAuthorized(user, GatewayAuthEndpoints.CanViewHistory, "当前用户没有查看历史库配置权限。", () => gateway.GetHistoryOptions()));

        group.MapGet("/history/status", (ClaimsPrincipal user, IGatewayApplicationService gateway) =>
            ExecuteReadAuthorized(user, GatewayAuthEndpoints.CanViewHistory, "当前用户没有查看历史库状态权限。", () => gateway.GetHistoryStatus()));

        group.MapPut("/history", async (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, SaveHistoryConfigurationCommand command) =>
            await ExecuteAuthorizedAsync(context, user, GatewayAuthEndpoints.CanEditHistory, "当前用户没有保存历史库配置权限。", () => gateway.UpdateHistoryOptionsAsync(command)));

        group.MapGet("/storage-health", (ClaimsPrincipal user, IGatewayApplicationService gateway) =>
            ExecuteReadAuthorized(user, CanViewStorageHealth, "当前用户没有查看历史库健康阈值权限。", () => gateway.GetStorageHealthOptions()));

        group.MapPut("/storage-health", async (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, SaveStorageHealthConfigurationCommand command) =>
            await ExecuteAuthorizedAsync(context, user, GatewayAuthEndpoints.CanEditHistory, "当前用户没有保存历史库健康阈值权限。", () => gateway.UpdateStorageHealthOptionsAsync(command)));

        return app;
    }

    private static readonly JsonSerializerOptions RuntimeEventJsonOptions = CreateRuntimeEventJsonOptions();

    private static async Task StreamRuntimeEvents(
        ClaimsPrincipal user,
        HttpContext context,
        IGatewayApplicationService gateway,
        GatewayRuntimeEventHub events)
    {
        if (!CanViewRuntimeStatus(user))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(ApiResult.Fail("Forbidden."));
            return;
        }

        context.Response.Headers.ContentType = "text/event-stream; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        await context.Response.StartAsync(context.RequestAborted);

        long lastEventId = ReadLastEventId(context.Request);
        long snapshotSequence = events.CurrentSequence;
        using GatewayRuntimeEventSubscription subscription = events.Subscribe(snapshotSequence);
        await WriteRuntimeEvent(
            context.Response,
            events.Create("hello", new
            {
                subscriberId = subscription.Id,
                lastEventId,
                snapshotSequence
            }, snapshotSequence),
            context.RequestAborted);
        await WriteRuntimeSnapshotEvents(context.Response, events, gateway, snapshotSequence, context.RequestAborted);

        try
        {
            while (!context.RequestAborted.IsCancellationRequested)
            {
                Task<bool> waitForEvent = subscription.Reader.WaitToReadAsync(context.RequestAborted).AsTask();
                Task heartbeat = Task.Delay(TimeSpan.FromSeconds(15), context.RequestAborted);
                Task completed = await Task.WhenAny(waitForEvent, heartbeat);

                if (completed == heartbeat)
                {
                    await WriteRuntimeEvent(context.Response, events.Create("heartbeat", new { subscribers = events.SubscriberCount }), context.RequestAborted);
                    continue;
                }

                if (!await waitForEvent)
                    break;

                while (subscription.Reader.TryRead(out GatewayRuntimeEventEnvelope? envelope))
                    await WriteRuntimeEvent(context.Response, envelope, context.RequestAborted);
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
    }

    private static async Task WriteRuntimeSnapshotEvents(
        HttpResponse response,
        GatewayRuntimeEventHub events,
        IGatewayApplicationService gateway,
        long snapshotSequence,
        CancellationToken cancellationToken)
    {
        GatewayRuntimeStatusDto status = gateway.GetStatus();
        if (status.Devices.Count > 0)
        {
            await WriteRuntimeEvent(
                response,
                events.Create("devices", new GatewayRuntimeDevicesChangedEvent
                {
                    Devices = status.Devices,
                    RemovedDeviceKeys = new List<string>()
                }, snapshotSequence),
                cancellationToken);
        }

        if (status.Tags.Count == 0)
            return;

        for (int offset = 0; offset < status.Tags.Count; offset += RuntimeSnapshotTagBatchSize)
        {
            List<TagValueSnapshotDto> tags = new List<TagValueSnapshotDto>();
            int count = Math.Min(RuntimeSnapshotTagBatchSize, status.Tags.Count - offset);
            for (int i = 0; i < count; i++)
                tags.Add(status.Tags[offset + i]);

            await WriteRuntimeEvent(
                response,
                events.Create("tags", new GatewayRuntimeTagsChangedEvent
                {
                    Tags = tags,
                    PendingCount = Math.Max(0, status.Tags.Count - offset - count)
                }, snapshotSequence),
                cancellationToken);
        }
    }

    private static long ReadLastEventId(HttpRequest request)
    {
        if (request == null)
            return 0;

        string value = request.Headers["Last-Event-ID"].FirstOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            value = request.Query["lastEventId"].FirstOrDefault() ?? string.Empty;

        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long sequence)
            ? Math.Max(0, sequence)
            : 0;
    }

    private static async Task WriteRuntimeEvent(
        HttpResponse response,
        GatewayRuntimeEventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        string payload = JsonSerializer.Serialize(envelope, RuntimeEventJsonOptions);
        await response.WriteAsync("id: " + envelope.Sequence.ToString(CultureInfo.InvariantCulture) + "\n", cancellationToken);
        await response.WriteAsync("event: " + envelope.Type + "\n", cancellationToken);
        await response.WriteAsync("data: " + payload + "\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    private static JsonSerializerOptions CreateRuntimeEventJsonOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static IResult Execute(Func<object?> action)
    {
        try
        {
            return Ok(action());
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(ApiResult.Fail(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(ApiResult.Fail(ex.Message));
        }
    }

    private static IResult ExecuteReadAuthorized(
        ClaimsPrincipal user,
        Func<ClaimsPrincipal, bool> authorize,
        string errorMessage,
        Func<object?> action)
    {
        if (!authorize(user))
            return Results.Json(ApiResult.Fail(errorMessage), statusCode: StatusCodes.Status403Forbidden);

        return Execute(action);
    }

    private static async Task<IResult> ExecuteReadAuthorizedAsync<T>(
        ClaimsPrincipal user,
        Func<ClaimsPrincipal, bool> authorize,
        string errorMessage,
        Func<Task<T>> action)
    {
        if (!authorize(user))
            return Results.Json(ApiResult.Fail(errorMessage), statusCode: StatusCodes.Status403Forbidden);

        return await ExecuteAsync(action);
    }

    private static bool CanViewRuntimeStatus(ClaimsPrincipal user)
    {
        return GatewayAuthEndpoints.CanViewDashboard(user) ||
               GatewayAuthEndpoints.CanViewBigScreen(user) ||
               GatewayAuthEndpoints.CanViewTopology(user) ||
               GatewayAuthEndpoints.CanViewDevices(user) ||
               GatewayAuthEndpoints.CanViewFlowRules(user) ||
               GatewayAuthEndpoints.CanViewMqtt(user) ||
               GatewayAuthEndpoints.CanViewOpcUa(user) ||
               GatewayAuthEndpoints.CanViewHistory(user);
    }

    private static bool CanViewStorageHealth(ClaimsPrincipal user)
    {
        return GatewayAuthEndpoints.CanViewDashboard(user) ||
               GatewayAuthEndpoints.CanViewBigScreen(user) ||
               GatewayAuthEndpoints.CanViewHistory(user);
    }

    private static async Task<IResult> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(ApiResult.Fail(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(ApiResult.Fail(ex.Message));
        }
    }

    private static async Task<IResult> ExecuteAuthorizedAsync<T>(
        HttpContext context,
        ClaimsPrincipal user,
        Func<ClaimsPrincipal, bool> authorize,
        string errorMessage,
        Func<Task<T>> action)
    {
        if (!authorize(user))
            return Results.Json(ApiResult.Fail(errorMessage), statusCode: StatusCodes.Status403Forbidden);

        return await ExecuteAuditedAsync(context, action);
    }

    private static async Task<IResult> ExecuteAuditedAsync<T>(HttpContext context, Func<Task<T>> action)
    {
        try
        {
            T data = await action();
            await WriteConfigurationAuditAsync(context, "success", string.Empty);
            return Ok(data);
        }
        catch (ArgumentException ex)
        {
            await WriteConfigurationAuditAsync(context, "bad_request", ex.Message);
            return Results.BadRequest(ApiResult.Fail(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            await WriteConfigurationAuditAsync(context, "not_found", ex.Message);
            return Results.NotFound(ApiResult.Fail(ex.Message));
        }
        catch (Exception ex)
        {
            await WriteConfigurationAuditAsync(context, "error", ex.Message);
            throw;
        }
    }

    private static async Task WriteConfigurationAuditAsync(HttpContext context, string outcome, string errorMessage)
    {
        try
        {
            await GatewayAuditLog.WriteConfigurationChangeAsync(
                CreateConfigurationAuditEvent(context, outcome, errorMessage),
                context.RequestServices.GetService<IGatewayAuditLogStore>());
        }
        catch
        {
        }
    }

    internal static GatewayConfigurationAuditEvent CreateConfigurationAuditEvent(HttpContext context, string outcome, string errorMessage)
    {
        HttpRequest? request = context?.Request;
        ClaimsPrincipal? user = context?.User;
        return new GatewayConfigurationAuditEvent
        {
            Outcome = outcome ?? string.Empty,
            Target = ResolveConfigurationAuditTarget(request?.Path.Value ?? string.Empty),
            UserName = user?.Identity?.Name ?? string.Empty,
            Role = user?.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty,
            RemoteIpAddress = context?.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            Method = request?.Method ?? string.Empty,
            Path = request?.Path.Value ?? string.Empty,
            TraceId = context?.TraceIdentifier ?? string.Empty,
            ErrorMessage = errorMessage ?? string.Empty,
            RequestBodySha256 = GetRequestBodySha256(context),
            RequestContentLength = GetRequestContentLength(context, request)
        };
    }

    private static string GetRequestBodySha256(HttpContext? context)
    {
        if (context == null)
            return string.Empty;
        return context.Items.TryGetValue(GatewayApiSecurityControls.RequestBodySha256ItemKey, out object? value)
            ? Convert.ToString(value) ?? string.Empty
            : string.Empty;
    }

    private static long GetRequestContentLength(HttpContext? context, HttpRequest? request)
    {
        if (context != null &&
            context.Items.TryGetValue(GatewayApiSecurityControls.RequestBodyLengthItemKey, out object? value) &&
            long.TryParse(Convert.ToString(value), out long parsed))
            return parsed;

        return request?.ContentLength ?? 0L;
    }

    private static string ResolveConfigurationAuditTarget(string path)
    {
        string value = string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim();
        const string prefix = "/api/config";
        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            value = value.Substring(prefix.Length);

        string[] segments = value
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length == 0)
            return "config";

        if (segments.Length >= 2 && segments[0].Equals("tags", StringComparison.OrdinalIgnoreCase) && segments[1].Equals("write", StringComparison.OrdinalIgnoreCase))
            return "config:tags/write";

        return "config:" + segments[0].ToLowerInvariant();
    }

    private static IResult Ok(object? data)
    {
        return Results.Ok(ApiResult.Ok(data));
    }

    private sealed class ApiResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public object? Data { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public static ApiResult Ok(object? data)
        {
            return new ApiResult { Success = true, Data = data };
        }

        public static ApiResult Fail(string message)
        {
            return new ApiResult { Success = false, ErrorMessage = message ?? string.Empty };
        }
    }
}

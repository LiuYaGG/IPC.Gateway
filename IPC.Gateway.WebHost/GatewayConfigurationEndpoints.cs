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
using System.Security.Claims;

namespace IPC.Gateway.WebHost;

public static class GatewayConfigurationEndpoints
{
    public static IEndpointRouteBuilder MapGatewayConfigurationEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/config");

        group.MapGet("/status", (IGatewayApplicationService gateway) =>
            Ok(gateway.GetStatus()));

        group.MapGet("/status/tags", (
            IGatewayApplicationService gateway,
            string? deviceId,
            string? deviceName,
            string? groupId,
            string? groupName,
            string? tagId,
            string? tagName) =>
            Ok(gateway.GetTagSnapshots(new RuntimeTagSnapshotQuery
            {
                DeviceId = deviceId ?? string.Empty,
                DeviceName = deviceName ?? string.Empty,
                GroupId = groupId ?? string.Empty,
                GroupName = groupName ?? string.Empty,
                TagId = tagId ?? string.Empty,
                TagName = tagName ?? string.Empty
            })));

        group.MapGet("/status/mqtt", (IGatewayApplicationService gateway) =>
            Ok(gateway.GetMqttStatus()));

        group.MapGet("/status/opcua", (IGatewayApplicationService gateway) =>
            Ok(gateway.GetOpcUaStatus()));

        group.MapGet("/status/rules", (IGatewayApplicationService gateway) =>
            Ok(gateway.GetRuleEngineStatus()));

        group.MapGet("/status/rule-engine", (IGatewayApplicationService gateway) =>
            Ok(gateway.GetRuleEngineStatus()));

        group.MapGet("/status/history", (IGatewayApplicationService gateway) =>
            Ok(gateway.GetHistoryStatus()));

        group.MapGet("/sync", (IGatewayApplicationService gateway) =>
            Ok(gateway.GetSync()));

        group.MapGet("/versions", (IGatewayApplicationService gateway, string? type, int? limit) =>
            Ok(gateway.GetConfigurationVersions(new ConfigurationVersionsQuery
            {
                ConfigType = type ?? string.Empty,
                Limit = limit ?? 50
            })));

        group.MapGet("/audit", (ClaimsPrincipal user, IGatewayAuditLogStore auditStore, int? limit, int? offset, string? target, string? outcome, string? username, DateTime? from, DateTime? to) =>
        {
            if (!GatewayAuthEndpoints.CanViewAudit(user))
                return Results.Json(ApiResult.Fail("当前用户没有查看审计日志权限。"), statusCode: StatusCodes.Status403Forbidden);

            return Ok(GatewayAuditLog.ReadPage(new GatewayAuditLogQuery
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

        group.MapGet("/audit/export", (ClaimsPrincipal user, IGatewayAuditLogStore auditStore, int? limit, string? target, string? outcome, string? username, DateTime? from, DateTime? to) =>
        {
            if (!GatewayAuthEndpoints.CanExportAudit(user))
                return Results.Json(ApiResult.Fail("当前用户没有导出审计日志权限。"), statusCode: StatusCodes.Status403Forbidden);

            IReadOnlyList<GatewayAuditLogEntry> entries = GatewayAuditLog.ReadRecent(new GatewayAuditLogQuery
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

        group.MapPost("/rollback", (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, RollbackConfigurationCommand command) =>
            ExecuteAuthorized(context, user, GatewayAuthEndpoints.CanEditProject, "当前用户没有保存项目配置权限。", () => gateway.RollbackConfiguration(command)));

        group.MapPost("/apply", async (IGatewayApplicationService gateway, HttpRequest request, ClaimsPrincipal user) =>
        {
            if (!GatewayAuthEndpoints.CanEditProject(user))
                return Results.Json(ApiResult.Fail("当前用户没有保存项目配置权限。"), statusCode: StatusCodes.Status403Forbidden);

            using StreamReader reader = new StreamReader(request.Body);
            string payload = await reader.ReadToEndAsync();
            return ExecuteAudited(request.HttpContext, () => gateway.ApplyConfigurationCommand(new RawConfigurationCommand
            {
                Source = "WebApi",
                Payload = payload
            }));
        });

        group.MapGet("/project", (IGatewayApplicationService gateway) =>
            Ok(gateway.GetProject()));

        group.MapPut("/project", (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, SaveProjectConfigurationCommand command) =>
            ExecuteAuthorized(context, user, GatewayAuthEndpoints.CanEditProject, "当前用户没有保存项目配置权限。", () => gateway.SaveProject(command)));

        group.MapPost("/validate", (IGatewayApplicationService gateway, ValidateProjectConfigurationCommand command) =>
            Execute(() => gateway.ValidateProject(command)));

        group.MapGet("/devices", (IGatewayApplicationService gateway) =>
            Ok(gateway.GetDevices()));

        group.MapPost("/devices", (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, SaveDeviceConfigurationCommand command) =>
            ExecuteAuthorized(context, user, GatewayAuthEndpoints.CanCreateDevice, "当前用户没有新增设备权限。", () => gateway.AddDevice(command)));

        group.MapPut("/devices/{deviceId}", (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, string deviceId, SaveDeviceConfigurationCommand command) =>
            ExecuteAuthorized(context, user, GatewayAuthEndpoints.CanEditDevice, "当前用户没有编辑设备权限。", () => gateway.UpdateDevice(deviceId, command)));

        group.MapDelete("/devices/{deviceId}", (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, string deviceId) =>
            ExecuteAuthorized(context, user, GatewayAuthEndpoints.CanDeleteDevice, "当前用户没有删除设备权限。", () => gateway.DeleteDevice(deviceId)));

        group.MapGet("/devices/{deviceId}/groups", (IGatewayApplicationService gateway, string deviceId) =>
            Execute(() => gateway.GetDeviceGroups(deviceId)));

        group.MapPost("/devices/{deviceId}/groups", (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, string deviceId, SaveGroupConfigurationCommand command) =>
            ExecuteAuthorized(context, user, GatewayAuthEndpoints.CanCreateGroup, "当前用户没有新增分组权限。", () => gateway.AddGroup(deviceId, command)));

        group.MapPut("/groups/{groupId}", (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, string groupId, SaveGroupConfigurationCommand command) =>
            ExecuteAuthorized(context, user, GatewayAuthEndpoints.CanEditGroup, "当前用户没有编辑分组权限。", () => gateway.UpdateGroup(groupId, command)));

        group.MapDelete("/groups/{groupId}", (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, string groupId) =>
            ExecuteAuthorized(context, user, GatewayAuthEndpoints.CanDeleteGroup, "当前用户没有删除分组权限。", () => gateway.DeleteGroup(groupId)));

        group.MapGet("/devices/{deviceId}/tags", (IGatewayApplicationService gateway, string deviceId) =>
            Execute(() => gateway.GetDeviceTags(deviceId)));

        group.MapPost("/devices/{deviceId}/tags", (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, string deviceId, SaveTagConfigurationCommand command) =>
            ExecuteAuthorized(context, user, GatewayAuthEndpoints.CanCreateTag, "当前用户没有新增标签权限。", () => gateway.AddDeviceTag(deviceId, command)));

        group.MapGet("/groups/{groupId}/tags", (IGatewayApplicationService gateway, string groupId) =>
            Execute(() => gateway.GetGroupTags(groupId)));

        group.MapPost("/groups/{groupId}/tags", (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, string groupId, SaveTagConfigurationCommand command) =>
            ExecuteAuthorized(context, user, GatewayAuthEndpoints.CanCreateTag, "当前用户没有新增标签权限。", () => gateway.AddGroupTag(groupId, command)));

        group.MapPut("/tags/{tagId}", (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, string tagId, SaveTagConfigurationCommand command) =>
            ExecuteAuthorized(context, user, GatewayAuthEndpoints.CanEditTag, "当前用户没有编辑标签权限。", () => gateway.UpdateTag(tagId, command)));

        group.MapDelete("/tags/{tagId}", (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, string tagId) =>
            ExecuteAuthorized(context, user, GatewayAuthEndpoints.CanDeleteTag, "当前用户没有删除标签权限。", () => gateway.DeleteTag(tagId)));

        group.MapPost("/tags/write", async (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, WriteTagCommand command) =>
            await ExecuteAuthorizedAsync(context, user, GatewayAuthEndpoints.CanWriteTag, "当前用户没有标签写入权限。", () => gateway.WriteTagAsync(command)));

        group.MapGet("/rules", (IGatewayApplicationService gateway) =>
            Ok(gateway.GetRules()));

        group.MapGet("/rules/status", (IGatewayApplicationService gateway) =>
            Ok(gateway.GetRuleEngineStatus()));

        group.MapPost("/rules", (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, SaveRuleConfigurationCommand command) =>
            ExecuteAuthorized(context, user, GatewayAuthEndpoints.CanCreateRule, "当前用户没有新增规则权限。", () => gateway.AddRule(command)));

        group.MapPut("/rules/{ruleId}", (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, string ruleId, SaveRuleConfigurationCommand command) =>
            ExecuteAuthorized(context, user, GatewayAuthEndpoints.CanEditRule, "当前用户没有编辑规则权限。", () => gateway.UpdateRule(ruleId, command)));

        group.MapDelete("/rules/{ruleId}", (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, string ruleId) =>
            ExecuteAuthorized(context, user, GatewayAuthEndpoints.CanDeleteRule, "当前用户没有删除规则权限。", () => gateway.DeleteRule(ruleId)));

        group.MapGet("/flow-rules", (IGatewayApplicationService gateway) =>
            Ok(gateway.GetFlowRules()));

        group.MapGet("/flow-rules/status", (IGatewayApplicationService gateway) =>
            Ok(gateway.GetStatus().FlowRuleEngine));

        group.MapPost("/flow-rules", (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, SaveFlowRuleDefinitionCommand command) =>
            ExecuteAuthorized(context, user, GatewayAuthEndpoints.CanCreateFlowRule, "当前用户没有新增流程规则权限。", () => gateway.AddFlowRule(command)));

        group.MapPut("/flow-rules/{ruleId}", (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, string ruleId, SaveFlowRuleDefinitionCommand command) =>
            ExecuteAuthorized(context, user, GatewayAuthEndpoints.CanEditFlowRule, "当前用户没有编辑流程规则权限。", () => gateway.UpdateFlowRule(ruleId, command)));

        group.MapDelete("/flow-rules/{ruleId}", (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, string ruleId) =>
            ExecuteAuthorized(context, user, GatewayAuthEndpoints.CanDeleteFlowRule, "当前用户没有删除流程规则权限。", () => gateway.DeleteFlowRule(ruleId)));

        group.MapGet("/mqtt", (IGatewayApplicationService gateway) =>
            Ok(gateway.GetMqttOptions()));

        group.MapGet("/mqtt/status", (IGatewayApplicationService gateway) =>
            Ok(gateway.GetMqttStatus()));

        group.MapPut("/mqtt", (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, SaveMqttConfigurationCommand command) =>
            ExecuteAuthorized(context, user, GatewayAuthEndpoints.CanEditMqtt, "当前用户没有保存 MQTT 配置权限。", () => gateway.UpdateMqttOptions(command)));

        group.MapGet("/opcua", (IGatewayApplicationService gateway) =>
            Ok(gateway.GetOpcUaOptions()));

        group.MapGet("/opcua/status", (IGatewayApplicationService gateway) =>
            Ok(gateway.GetOpcUaStatus()));

        group.MapPut("/opcua", (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, SaveOpcUaServerConfigurationCommand command) =>
            ExecuteAuthorized(context, user, GatewayAuthEndpoints.CanEditOpcUa, "当前用户没有保存 OPC UA Server 配置权限。", () => gateway.UpdateOpcUaOptions(command)));

        group.MapGet("/history", (IGatewayApplicationService gateway) =>
            Ok(gateway.GetHistoryOptions()));

        group.MapGet("/history/status", (IGatewayApplicationService gateway) =>
            Ok(gateway.GetHistoryStatus()));

        group.MapPut("/history", (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, SaveHistoryConfigurationCommand command) =>
            ExecuteAuthorized(context, user, GatewayAuthEndpoints.CanEditHistory, "当前用户没有保存历史库配置权限。", () => gateway.UpdateHistoryOptions(command)));

        group.MapGet("/storage-health", (IGatewayApplicationService gateway) =>
            Ok(gateway.GetStorageHealthOptions()));

        group.MapPut("/storage-health", (IGatewayApplicationService gateway, HttpContext context, ClaimsPrincipal user, SaveStorageHealthConfigurationCommand command) =>
            ExecuteAuthorized(context, user, GatewayAuthEndpoints.CanEditHistory, "当前用户没有保存历史库健康阈值权限。", () => gateway.UpdateStorageHealthOptions(command)));

        return app;
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

    private static IResult ExecuteAuthorized(
        HttpContext context,
        ClaimsPrincipal user,
        Func<ClaimsPrincipal, bool> authorize,
        string errorMessage,
        Func<object?> action)
    {
        if (!authorize(user))
            return Results.Json(ApiResult.Fail(errorMessage), statusCode: StatusCodes.Status403Forbidden);

        return ExecuteAudited(context, action);
    }

    private static IResult ExecuteAudited(HttpContext context, Func<object?> action)
    {
        try
        {
            object? data = action();
            WriteConfigurationAudit(context, "success", string.Empty);
            return Ok(data);
        }
        catch (ArgumentException ex)
        {
            WriteConfigurationAudit(context, "bad_request", ex.Message);
            return Results.BadRequest(ApiResult.Fail(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            WriteConfigurationAudit(context, "not_found", ex.Message);
            return Results.NotFound(ApiResult.Fail(ex.Message));
        }
        catch (Exception ex)
        {
            WriteConfigurationAudit(context, "error", ex.Message);
            throw;
        }
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
            WriteConfigurationAudit(context, "success", string.Empty);
            return Ok(data);
        }
        catch (ArgumentException ex)
        {
            WriteConfigurationAudit(context, "bad_request", ex.Message);
            return Results.BadRequest(ApiResult.Fail(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            WriteConfigurationAudit(context, "not_found", ex.Message);
            return Results.NotFound(ApiResult.Fail(ex.Message));
        }
        catch (Exception ex)
        {
            WriteConfigurationAudit(context, "error", ex.Message);
            throw;
        }
    }

    private static void WriteConfigurationAudit(HttpContext context, string outcome, string errorMessage)
    {
        try
        {
            GatewayAuditLog.WriteConfigurationChange(
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

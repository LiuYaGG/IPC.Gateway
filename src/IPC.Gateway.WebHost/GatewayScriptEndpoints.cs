using System.Security.Claims;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Scripting.Abstractions;
using IPC.Gateway.Scripting.Application;
using IPC.Gateway.Scripting.Models;
using Microsoft.AspNetCore.Mvc;

namespace IPC.Gateway.WebHost;

/// <summary>
/// 提供脚本中心、数据库连接、写入目标和脚本执行的受权 REST API。
/// </summary>
public static class GatewayScriptEndpoints
{
    /// <summary>
    /// 将脚本中心全部端点映射到 WebHost。
    /// </summary>
    public static IEndpointRouteBuilder MapGatewayScriptEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/scripts");

        group.MapGet("/overview", async (ClaimsPrincipal user, GatewayScriptManager manager, CancellationToken cancellationToken) =>
        {
            if (!GatewayAuthEndpoints.CanViewScripts(user))
                return Forbidden("当前用户没有查看脚本中心的权限。");
            return Results.Ok(ApiResult.Ok(await manager.GetOverviewAsync(cancellationToken)));
        });

        group.MapGet("/value-transforms/catalog", async (
            ClaimsPrincipal user,
            GatewayScriptManager manager,
            CancellationToken cancellationToken) =>
        {
            if (!GatewayAuthEndpoints.CanViewScripts(user) &&
                !GatewayAuthEndpoints.CanViewDevices(user) &&
                !GatewayAuthEndpoints.CanViewFlowRules(user))
                return Forbidden("当前用户没有查看值处理脚本目录的权限。");
            ScriptCenterOverview overview = await manager.GetOverviewAsync(cancellationToken);
            object[] catalog = overview.Scripts
                .Where(script =>
                    script.ScriptType == GatewayScriptType.ValueTransform &&
                    script.PublishedVersion > 0 &&
                    !string.IsNullOrWhiteSpace(script.PublishedSourceCode))
                .Select(script => new
                {
                    script.Id,
                    script.Name,
                    script.Description,
                    Scope = script.ValueTransformScope,
                    script.NodeCategory,
                    script.InputDataType,
                    script.OutputDataType,
                    Version = script.PublishedVersion,
                    script.PublishedUtc
                })
                .Cast<object>()
                .ToArray();
            return Results.Ok(ApiResult.Ok(catalog));
        });

        group.MapPost("/validate", async (ClaimsPrincipal user, [FromBody] ScriptValidationRequest request, IScriptRuntimeService runtime, CancellationToken cancellationToken) =>
        {
            if (!GatewayAuthEndpoints.CanExecuteScripts(user))
                return Forbidden("当前用户没有编译检查脚本的权限。");
            return Results.Ok(ApiResult.Ok(await runtime.ValidateAsync(request.SourceCode, request.ScriptType, cancellationToken)));
        });

        group.MapPut("/definitions", async (HttpContext context, ClaimsPrincipal user, [FromBody] GatewayScriptDefinition definition, GatewayScriptManager manager, IGatewayAuditLogStore audit, CancellationToken cancellationToken) =>
        {
            if (!GatewayAuthEndpoints.CanEditScripts(user))
                return Forbidden("当前用户没有编辑脚本的权限。");
            return await ExecuteAuditedAsync(context, audit, "scripts.save", definition.Id, async () =>
                ApiResult.Ok(await manager.SaveScriptAsync(definition, cancellationToken)));
        });

        group.MapDelete("/definitions/{id}", async (HttpContext context, ClaimsPrincipal user, string id, GatewayScriptManager manager, GatewayCoreService gateway, IGatewayAuditLogStore audit, CancellationToken cancellationToken) =>
        {
            if (!GatewayAuthEndpoints.CanEditScripts(user))
                return Forbidden("当前用户没有删除脚本的权限。");
            return await ExecuteAuditedAsync(context, audit, "scripts.delete", id, async () =>
            {
                EnsureValueScriptNotReferenced(gateway.CurrentProject, id);
                await manager.DeleteScriptAsync(id, cancellationToken);
                return ApiResult.Ok(null);
            });
        });

        group.MapPost("/definitions/{id}/publish", async (
            HttpContext context,
            ClaimsPrincipal user,
            string id,
            GatewayScriptManager manager,
            IGatewayAuditLogStore audit,
            CancellationToken cancellationToken) =>
        {
            if (!GatewayAuthEndpoints.CanEditScripts(user))
                return Forbidden("当前用户没有发布值处理脚本的权限。");
            return await ExecuteAuditedAsync(context, audit, "scripts.publish", id, async () =>
                ApiResult.Ok(await manager.PublishValueScriptAsync(id, cancellationToken)));
        });

        group.MapPost("/value-transforms/test", (
            ClaimsPrincipal user,
            [FromBody] ValueTransformScriptTestRequest request,
            GatewayScriptManager manager) =>
        {
            if (!GatewayAuthEndpoints.CanExecuteScripts(user))
                return Forbidden("当前用户没有测试值处理脚本的权限。");
            return Results.Ok(ApiResult.Ok(manager.TestValueScript(request)));
        });

        group.MapPost("/definitions/{id}/execute", async (HttpContext context, ClaimsPrincipal user, string id, IScriptRuntimeService runtime, IGatewayAuditLogStore audit, CancellationToken cancellationToken) =>
        {
            if (!GatewayAuthEndpoints.CanExecuteScripts(user))
                return Forbidden("当前用户没有执行脚本的权限。");
            return await ExecuteAuditedAsync(context, audit, "scripts.execute", id, async () =>
                ApiResult.Ok(await runtime.ExecuteManualAsync(id, cancellationToken)));
        });

        group.MapPut("/connections", async (HttpContext context, ClaimsPrincipal user, [FromBody] ScriptDatabaseConnectionDefinition connection, GatewayScriptManager manager, IGatewayAuditLogStore audit, CancellationToken cancellationToken) =>
        {
            if (!GatewayAuthEndpoints.CanManageScriptDatabases(user))
                return Forbidden("当前用户没有管理脚本数据库的权限。");
            return await ExecuteAuditedAsync(context, audit, "scripts.database.connection.save", connection.Id, async () =>
                ApiResult.Ok(await manager.SaveConnectionAsync(connection, cancellationToken)));
        });

        group.MapDelete("/connections/{id}", async (HttpContext context, ClaimsPrincipal user, string id, GatewayScriptManager manager, IGatewayAuditLogStore audit, CancellationToken cancellationToken) =>
        {
            if (!GatewayAuthEndpoints.CanManageScriptDatabases(user))
                return Forbidden("当前用户没有管理脚本数据库的权限。");
            return await ExecuteAuditedAsync(context, audit, "scripts.database.connection.delete", id, async () =>
            {
                await manager.DeleteConnectionAsync(id, cancellationToken);
                return ApiResult.Ok(null);
            });
        });

        group.MapPost("/connections/{id}/test", async (ClaimsPrincipal user, string id, GatewayScriptManager manager, CancellationToken cancellationToken) =>
        {
            if (!GatewayAuthEndpoints.CanManageScriptDatabases(user))
                return Forbidden("当前用户没有测试脚本数据库的权限。");
            try
            {
                await manager.TestConnectionAsync(id, cancellationToken);
                return Results.Ok(ApiResult.Ok(new { message = "数据库连接成功。" }));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.GetBaseException().Message));
            }
        });

        group.MapPut("/targets", async (HttpContext context, ClaimsPrincipal user, [FromBody] ScriptDatabaseWriteTarget target, GatewayScriptManager manager, IGatewayAuditLogStore audit, CancellationToken cancellationToken) =>
        {
            if (!GatewayAuthEndpoints.CanManageScriptDatabases(user))
                return Forbidden("当前用户没有管理数据库写入目标的权限。");
            return await ExecuteAuditedAsync(context, audit, "scripts.database.target.save", target.Id, async () =>
                ApiResult.Ok(await manager.SaveTargetAsync(target, cancellationToken)));
        });

        group.MapDelete("/targets/{id}", async (HttpContext context, ClaimsPrincipal user, string id, GatewayScriptManager manager, IGatewayAuditLogStore audit, CancellationToken cancellationToken) =>
        {
            if (!GatewayAuthEndpoints.CanManageScriptDatabases(user))
                return Forbidden("当前用户没有管理数据库写入目标的权限。");
            return await ExecuteAuditedAsync(context, audit, "scripts.database.target.delete", id, async () =>
            {
                await manager.DeleteTargetAsync(id, cancellationToken);
                return ApiResult.Ok(null);
            });
        });

        return app;
    }

    /// <summary>
    /// 阻止删除仍被规则节点或标签清洗配置引用的值处理脚本。
    /// </summary>
    private static void EnsureValueScriptNotReferenced(IPC.Runtime.Configuration.ProjectConfig project, string scriptId)
    {
        foreach (IPC.Runtime.Configuration.DeviceConfig device in project.Devices ?? [])
        {
            IEnumerable<IPC.Runtime.Configuration.TagConfig> tags =
                (device.Tags ?? []).Concat((device.Groups ?? []).SelectMany(group => group.Tags ?? []));
            if (tags.Any(tag =>
                    tag.Cleaning?.ValueScriptEnabled == true &&
                    string.Equals(tag.Cleaning.ValueScriptId, scriptId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("该值处理脚本仍被标签数据清洗配置引用，不能删除。");
            }
        }

        if ((project.FlowRules ?? []).SelectMany(rule => rule.Nodes ?? []).Any(node =>
                string.Equals(node.ValueScriptId, scriptId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("该值处理脚本仍被规则引擎节点引用，不能删除。");
        }
    }

    /// <summary>
    /// 创建统一的无权限响应。
    /// </summary>
    private static IResult Forbidden(string message)
    {
        return Results.Json(ApiResult.Fail(message), statusCode: StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// 执行变更操作并记录成功或失败的安全审计日志。
    /// </summary>
    private static async Task<IResult> ExecuteAuditedAsync(
        HttpContext context,
        IGatewayAuditLogStore audit,
        string action,
        string target,
        Func<Task<ApiResult>> operation)
    {
        try
        {
            ApiResult result = await operation();
            await GatewayAuthEndpoints.WriteSecurityAuditAsync(context, audit, action, "success", target, string.Empty, string.Empty, string.Empty);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            string message = ex.GetBaseException().Message;
            await GatewayAuthEndpoints.WriteSecurityAuditAsync(context, audit, action, "failed", target, string.Empty, string.Empty, message);
            return Results.BadRequest(ApiResult.Fail(message));
        }
    }

    /// <summary>
    /// 表示脚本编译检查请求。
    /// </summary>
    public sealed class ScriptValidationRequest
    {
        public string SourceCode { get; set; } = string.Empty;
        public GatewayScriptType ScriptType { get; set; } = GatewayScriptType.DatabaseWrite;
    }

    /// <summary>
    /// 表示脚本中心 API 的统一响应结构。
    /// </summary>
    private sealed class ApiResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public object? Data { get; set; }
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

        /// <summary>
        /// 创建成功响应。
        /// </summary>
        public static ApiResult Ok(object? data)
        {
            return new ApiResult { Success = true, Data = data };
        }

        /// <summary>
        /// 创建失败响应。
        /// </summary>
        public static ApiResult Fail(string message)
        {
            return new ApiResult { Success = false, ErrorMessage = message ?? string.Empty };
        }
    }
}

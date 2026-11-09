using System.Security.Claims;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Inference;

namespace IPC.Gateway.WebHost;

/// <summary>
/// 提供模型目录、版本上传发布、结构检查和安全测试 API。
/// </summary>
public static class GatewayModelEndpoints
{
    /// <summary>
    /// 映射模型中心全部端点。
    /// </summary>
    public static IEndpointRouteBuilder MapGatewayModelEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/models");

        group.MapGet("/", (ClaimsPrincipal user, OnnxModelCatalogService catalog) =>
            GatewayAuthEndpoints.CanViewModels(user)
                ? Results.Ok(ApiResult.Ok(catalog.GetModels()))
                : Forbidden("当前用户没有查看模型中心的权限。"));

        group.MapGet("/runtime", (ClaimsPrincipal user, OnnxModelCatalogService catalog) =>
            GatewayAuthEndpoints.CanViewModels(user)
                ? Results.Ok(ApiResult.Ok(catalog.GetRuntimeStats()))
                : Forbidden("当前用户没有查看模型运行状态的权限。"));

        group.MapPut("/", (ClaimsPrincipal user, SaveOnnxModelRequest request, OnnxModelCatalogService catalog) =>
            Execute(user, GatewayAuthEndpoints.CanEditModels, "当前用户没有编辑模型的权限。", () => catalog.SaveModel(request)));

        group.MapPost("/{id}/versions", async (
            ClaimsPrincipal user,
            string id,
            HttpRequest request,
            OnnxModelCatalogService catalog,
            CancellationToken cancellationToken) =>
        {
            if (!GatewayAuthEndpoints.CanUploadModels(user))
                return Forbidden("当前用户没有上传模型的权限。");
            try
            {
                IFormCollection form = await request.ReadFormAsync(cancellationToken);
                IFormFile? file = form.Files.GetFile("file");
                if (file == null)
                    return Results.BadRequest(ApiResult.Fail("请选择 ONNX 模型文件。"));
                await using Stream stream = file.OpenReadStream();
                OnnxModelVersion version = await catalog.UploadVersionAsync(
                    id,
                    stream,
                    file.FileName,
                    form["notes"].ToString(),
                    cancellationToken);
                return Results.Ok(ApiResult.Ok(version));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.GetBaseException().Message));
            }
        });

        group.MapPost("/{id}/versions/{version:int}/publish", (
            ClaimsPrincipal user,
            string id,
            int version,
            OnnxModelCatalogService catalog) =>
            Execute(user, GatewayAuthEndpoints.CanPublishModels, "当前用户没有发布模型的权限。", () => catalog.Publish(id, version)));

        group.MapPost("/{id}/test", (
            ClaimsPrincipal user,
            string id,
            OnnxModelTestRequest request,
            OnnxModelCatalogService catalog) =>
            Execute(user, GatewayAuthEndpoints.CanTestModels, "当前用户没有测试模型的权限。", () => catalog.Test(id, request)));

        group.MapDelete("/{id}/versions/{version:int}", (
            ClaimsPrincipal user,
            string id,
            int version,
            OnnxModelCatalogService catalog) =>
        {
            if (!GatewayAuthEndpoints.CanEditModels(user))
                return Forbidden("当前用户没有删除模型版本的权限。");
            try
            {
                catalog.DeleteVersion(id, version);
                return Results.Ok(ApiResult.Ok<object?>(null));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.GetBaseException().Message));
            }
        });

        return app;
    }

    /// <summary>
    /// 统一执行模型中心同步操作并转换业务异常。
    /// </summary>
    private static IResult Execute<T>(ClaimsPrincipal user, Func<ClaimsPrincipal, bool> authorize, string forbidden, Func<T> action)
    {
        if (!authorize(user))
            return Forbidden(forbidden);
        try
        {
            return Results.Ok(ApiResult.Ok(action()));
        }
        catch (Exception ex)
        {
            return Results.BadRequest(ApiResult.Fail(ex.GetBaseException().Message));
        }
    }

    /// <summary>
    /// 返回统一的无权限响应。
    /// </summary>
    private static IResult Forbidden(string message) =>
        Results.Json(ApiResult.Fail(message), statusCode: StatusCodes.Status403Forbidden);

    /// <summary>
    /// 表示模型中心 API 的统一响应。
    /// </summary>
    private sealed class ApiResult
    {
        public bool Success { get; set; }
        public object? Data { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;

        public static ApiResult Ok<T>(T data) => new() { Success = true, Data = data };
        public static ApiResult Fail(string message) => new() { ErrorMessage = message ?? string.Empty };
    }
}

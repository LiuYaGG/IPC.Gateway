namespace IPC.Gateway.WebHost;

public sealed class GatewayStartupState
{
    private string _state = "starting";
    private readonly TaskCompletionSource _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Listening => _listening.Task;
    public void MarkListening() => _listening.TrySetResult();

    public void MarkReady() => Interlocked.CompareExchange(ref _state, "ready", "starting");
    public void MarkFailed() => Interlocked.CompareExchange(ref _state, "failed", "starting");
    public void MarkStopping() => Interlocked.Exchange(ref _state, "stopping");

    public async Task<bool> TryHandleRequestAsync(HttpContext context, string metricsPath)
    {
        string state = Volatile.Read(ref _state);
        HttpRequest request = context.Request;
        bool get = HttpMethods.IsGet(request.Method);
        string path = (request.Path.Value ?? "").TrimEnd('/');
        if (get && path.Equals("/api/startup/status", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(new { state }, context.RequestAborted);
            return true;
        }
        if (state == "ready") return false;
        if (!(request.Path.StartsWithSegments("/api") || request.Path.StartsWithSegments("/health") ||
            path.Equals(metricsPath, StringComparison.OrdinalIgnoreCase))) return false;

        // These endpoints are independent of the PLC runtime. Their existing authentication still applies.
        if (get && (path.Equals("/health", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/health/live", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/health/live", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/maintenance/updates/progress", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/auth/me", StringComparison.OrdinalIgnoreCase))) return false;
        if (HttpMethods.IsPost(request.Method) && (path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/auth/logout", StringComparison.OrdinalIgnoreCase))) return false;

        string errorMessage = state switch
        {
            "failed" => "网关初始化失败，请查看服务启动日志。",
            "stopping" => "网关正在停止。",
            _ => "网关正在初始化。"
        };
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.RetryAfter = "2";
        if (GatewayHistoryEndpoints.IsHistoryRead(context))
        {
            await GatewayHistoryEndpoints.Error(context,503,"HISTORY_GATEWAY_UNAVAILABLE",errorMessage,"The gateway is not ready for history queries.").ExecuteAsync(context);
            return true;
        }
        await context.Response.WriteAsJsonAsync(new { success = false, state, errorMessage }, context.RequestAborted);
        return true;
    }
}

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IPC.Gateway.HaSetup;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
var root = builder.Configuration["configRoot"] ?? throw new ArgumentException("需要 --configRoot 指向本机专用配置目录。");
var localPort = builder.Configuration.GetValue("localPort", 7380);
var pairPort = builder.Configuration.GetValue("pairPort", 7381);
if (localPort == pairPort) throw new ArgumentException("配置管理与配对需要不同的端口。");
var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
await using var coordinator = new GatewaySetupCoordinator(root, pairPort);
builder.WebHost.ConfigureKestrel(k =>
{
    k.Limits.MaxRequestBodySize = 48 * 1024;
    k.Listen(IPAddress.Loopback, localPort);
});
var app = builder.Build();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    if (ctx.Connection.LocalPort != localPort || ctx.Request.Host.Host is not ("127.0.0.1" or "localhost"))
    {
        ctx.Response.StatusCode = 404;
        return;
    }
    var supplied = ctx.Request.Headers["X-Setup-Token"].ToString();
    if (ctx.Request.Path.StartsWithSegments("/setup") && (supplied.Length != token.Length ||
        !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(token))))
    {
        ctx.Response.StatusCode = 403;
        await ctx.Response.WriteAsJsonAsync(new { error = "请使用安装程序显示的本机配置链接。" });
        return;
    }
    try { await next(); }
    catch (Exception e) when (!ctx.Response.HasStarted && e is ArgumentException or InvalidOperationException or IOException
        or UnauthorizedAccessException or InvalidDataException or HttpRequestException or JsonException or TaskCanceledException)
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsJsonAsync(new { error = e is HttpRequestException or TaskCanceledException
            ? "未能完成配对，请检查地址和配对码并重试同一计划。" : e.Message });
    }
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/setup/status", () =>
{
    var status = coordinator.Status();
    return new { status.Roles, status.ClusterId, status.Configured, localPort, status.PairPort };
});
app.MapPost("/setup/identity", (RoleSettings input) => coordinator.RegisterIdentity(input));
app.MapPost("/setup/code", (RoleChoice input, CancellationToken ct) => coordinator.IssueCodeAsync(input, ct));
app.MapPost("/setup/commit", (CreateCluster input, CancellationToken ct) => coordinator.CommitAsync(input, ct));
Console.WriteLine($"本机组网配置链接（仅限安装人员） http://127.0.0.1:{localPort}/#token={token}");
await app.RunAsync();

using System.Security.Claims;

namespace IPC.Gateway.WebHost;

// Each stream keeps its original permission projection. A change closes it so that
// the next handshake obtains a new principal and an authoritative snapshot.
internal sealed class GatewayRuntimeStreamAccess : IAsyncDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);
    private readonly GatewayAuthService _auth;
    private readonly ClaimsPrincipal _principal;
    private readonly Func<Task<ClaimsPrincipal?>> _validate;
    private readonly CancellationTokenSource _lifetime;
    private readonly ILogger _logger;
    private readonly DateTimeOffset? _expiry;
    private Task _monitor = Task.CompletedTask;

    internal GatewayRuntimeStreamAccess(HttpContext context, GatewayAuthService auth, GatewayApiTokenService apiTokens,
        ILogger logger)
    {
        _auth = auth;
        _logger = logger;
        _principal = context.User;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        if (long.TryParse(_principal.FindFirst("exp")?.Value, out long seconds))
            _expiry = DateTimeOffset.FromUnixTimeSeconds(seconds);

        if (_principal.HasClaim("apiToken", "true"))
        {
            string token = apiTokens.ReadToken(context.Request);
            bool https = context.Request.IsHttps;
            _validate = () => Task.FromResult(apiTokens.TryValidate(token, https, out var current, out _, out _) ? current : null);
        }
        else
        {
            string token = GatewayAuthEndpoints.ReadToken(context.Request);
            _validate = async () =>
            {
                var result = await auth.ValidateTokenAsync(token);
                return result.Success ? result.Principal : null;
            };
            _auth.RuntimeAccessChanged += OnAccessChanged;
        }
        SetDeadline();
    }

    public CancellationToken Token => _lifetime.Token;

    public async Task<bool> StartAsync()
    {
        // Register for invalidation before re-reading the permissions: a change
        // between middleware authentication and this request cannot be missed.
        if (!await IsCurrentAsync() || Token.IsCancellationRequested) return false;
        _monitor = MonitorAsync();
        return true;
    }

    private void OnAccessChanged(string target)
    {
        if (target.Equals("user:" + _principal.Identity?.Name, StringComparison.OrdinalIgnoreCase) ||
            target.Equals("role:" + _principal.FindFirst(ClaimTypes.Role)?.Value, StringComparison.OrdinalIgnoreCase) ||
            target.Equals("session:" + _principal.FindFirst("jti")?.Value, StringComparison.Ordinal))
            _ = _lifetime.CancelAsync();
    }

    private void SetDeadline()
    {
        // Fail closed even when the identity store becomes unresponsive.
        TimeSpan delay = CheckInterval + CheckInterval;
        if (_expiry.HasValue && _expiry.Value - DateTimeOffset.UtcNow < delay)
            delay = _expiry.Value - DateTimeOffset.UtcNow;
        if (delay <= TimeSpan.Zero) _ = _lifetime.CancelAsync();
        else _lifetime.CancelAfter(delay);
    }

    private async Task<bool> IsCurrentAsync()
    {
        try
        {
            Token.ThrowIfCancellationRequested();
            var current = await _validate().WaitAsync(Token);
            return current?.Identity?.IsAuthenticated == true &&
                current.FindFirst(ClaimTypes.NameIdentifier)?.Value == _principal.FindFirst(ClaimTypes.NameIdentifier)?.Value &&
                current.FindFirst(ClaimTypes.Role)?.Value == _principal.FindFirst(ClaimTypes.Role)?.Value &&
                _principal.FindAll("permission").Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase)
                    .SetEquals(current.FindAll("permission").Select(c => c.Value));
        }
        catch (OperationCanceledException) when (Token.IsCancellationRequested) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not revalidate runtime stream authorization.");
            return false;
        }
    }

    private async Task MonitorAsync()
    {
        try
        {
            while (true)
            {
                await Task.Delay(CheckInterval, Token);
                if (!await IsCurrentAsync())
                {
                    await _lifetime.CancelAsync();
                    return;
                }
                SetDeadline();
            }
        }
        catch (OperationCanceledException) when (Token.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        _auth.RuntimeAccessChanged -= OnAccessChanged;
        await _lifetime.CancelAsync();
        await _monitor;
        _lifetime.Dispose();
    }
}

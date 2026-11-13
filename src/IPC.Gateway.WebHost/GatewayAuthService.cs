/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.WebHost
* 项目描述 ：
* 类 名 称 ：GatewayAuthService
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
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using IPC.Gateway.Core.Application.Users;
using IPC.Gateway.Core.Domain.Users;
using Microsoft.Extensions.Hosting;

namespace IPC.Gateway.WebHost;

public sealed class GatewayAuthService
{
    private readonly IGatewayUserApplicationService _users;
    private readonly IGatewayRoleApplicationService _roles;
    private readonly byte[] _secret;
    private readonly TimeSpan _tokenLifetime;

    public GatewayAuthService(IGatewayUserApplicationService users, IGatewayRoleApplicationService roles, IConfiguration configuration, IHostEnvironment environment)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _roles = roles ?? throw new ArgumentNullException(nameof(roles));
        string secret = ResolveSecret(configuration["Gateway:Auth:Secret"], environment);
        _secret = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        _tokenLifetime = TimeSpan.FromHours(configuration.GetValue("Gateway:Auth:TokenHours", 12));
    }

    public GatewayLoginResult Login(string username, string password)
    {
        GatewayUserAuthenticationResult authentication = _users.Authenticate(username, password);
        if (!authentication.Success || authentication.User == null)
            return GatewayLoginResult.Fail(authentication.ErrorMessage, authentication.Locked, authentication.LockoutEndTime);

        GatewayUserInfo user = authentication.User;
        if (_roles.FindByName(user.Role)?.Enabled != true)
            return GatewayLoginResult.Fail("账号所属角色不存在或已停用。");
        string token = CreateToken(user);
        return GatewayLoginResult.Ok(user, token, DateTimeOffset.UtcNow.Add(_tokenLifetime));
    }

    public async Task<GatewayLoginResult> LoginAsync(string username, string password)
    {
        GatewayUserAuthenticationResult authentication = await _users.AuthenticateAsync(username, password);
        if (!authentication.Success || authentication.User == null)
            return GatewayLoginResult.Fail(authentication.ErrorMessage, authentication.Locked, authentication.LockoutEndTime);

        GatewayUserInfo user = authentication.User;
        if ((await _roles.FindByNameAsync(user.Role))?.Enabled != true)
            return GatewayLoginResult.Fail("账号所属角色不存在或已停用。");
        string token = CreateToken(user);
        return GatewayLoginResult.Ok(user, token, DateTimeOffset.UtcNow.Add(_tokenLifetime));
    }

    private readonly object _accessLock = new();
    private Action<string>? _runtimeAccessChanged;

    internal event Action<string> RuntimeAccessChanged
    {
        add { lock (_accessLock) _runtimeAccessChanged += value; }
        remove { lock (_accessLock) _runtimeAccessChanged -= value; }
    }

    internal void InvalidateRuntimeAccess(string target)
    {
        int separator = target.IndexOf(':');
        if (separator < 0) return;
        string normalized = target[..(separator + 1)] + target[(separator + 1)..].Trim();
        lock (_accessLock) _runtimeAccessChanged?.Invoke(normalized);
    }

    public bool TryValidateToken(string token, out ClaimsPrincipal principal)
    {
        principal = new ClaimsPrincipal(new ClaimsIdentity());
        if (!TryReadClaims(token, out var fields)) return false;
        var user = _users.FindByUsername(fields[0]);
        if (user?.Enabled != true) return false;
        var role = _roles.FindByName(user.Role);
        if (role?.Enabled != true) return false;

        principal = CreatePrincipal(user, role, fields);
        return true;
    }

    public async Task<GatewayTokenValidationResult> ValidateTokenAsync(string token)
    {
        var empty = new ClaimsPrincipal(new ClaimsIdentity());
        if (!TryReadClaims(token, out var fields)) return GatewayTokenValidationResult.Fail(empty);
        var user = await _users.FindByUsernameAsync(fields[0]);
        if (user?.Enabled != true) return GatewayTokenValidationResult.Fail(empty);
        var role = await _roles.FindByNameAsync(user.Role);
        if (role?.Enabled != true) return GatewayTokenValidationResult.Fail(empty);

        return GatewayTokenValidationResult.Ok(CreatePrincipal(user, role, fields));
    }

    private bool TryReadClaims(string token, out string[] fields)
    {
        fields = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(token)) return false;
        string[] parts = token.Split('.');
        if (parts.Length != 2 || !CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Sign(parts[0])), Encoding.UTF8.GetBytes(parts[1]))) return false;
        try
        {
            fields = Encoding.UTF8.GetString(Base64UrlDecode(parts[0])).Split('|');
        }
        catch (FormatException) { return false; }
        return fields.Length == 4 && long.TryParse(fields[2], out long expiry) &&
               DateTimeOffset.UtcNow.ToUnixTimeSeconds() < expiry;
    }

    private static ClaimsPrincipal CreatePrincipal(GatewayUserInfo user, GatewayRoleInfo role, string[] fields)
    {
        ClaimsIdentity identity = new ClaimsIdentity("GatewayCookie");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id));
        identity.AddClaim(new Claim(ClaimTypes.Name, user.Username));
        identity.AddClaim(new Claim(ClaimTypes.Role, role.Name));
        identity.AddClaim(new Claim("displayName", user.DisplayName));
        identity.AddClaim(new Claim("exp", fields[2]));
        identity.AddClaim(new Claim("jti", fields[3]));
        foreach (string permission in GatewayPermissions.ExpandForRuntime(role.Name, role.Permissions))
            identity.AddClaim(new Claim("permission", permission));
        return new ClaimsPrincipal(identity);
    }

    public GatewayUserInfo? GetCurrentUser(ClaimsPrincipal principal)
    {
        string username = principal?.Identity?.Name ?? string.Empty;
        GatewayUserInfo? user = _users.FindByUsername(username);
        if (user == null)
            return null;
        user.PasswordHash = string.Empty;
        user.PasswordSalt = string.Empty;
        return user;
    }

    public async Task<GatewayUserInfo?> GetCurrentUserAsync(ClaimsPrincipal principal)
    {
        string username = principal?.Identity?.Name ?? string.Empty;
        GatewayUserInfo? user = await _users.FindByUsernameAsync(username);
        if (user == null)
            return null;
        user.PasswordHash = string.Empty;
        user.PasswordSalt = string.Empty;
        return user;
    }

    private string CreateToken(GatewayUserInfo user)
    {
        string payload = string.Join("|", new[]
        {
            user.Username,
            user.Role,
            DateTimeOffset.UtcNow.Add(_tokenLifetime).ToUnixTimeSeconds().ToString(),
            Guid.NewGuid().ToString("N")
        });
        string encodedPayload = Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
        return encodedPayload + "." + Sign(encodedPayload);
    }

    private string Sign(string encodedPayload)
    {
        using HMACSHA256 hmac = new HMACSHA256(_secret);
        return Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(encodedPayload)));
    }

    private static string ResolveSecret(string? configuredSecret, IHostEnvironment environment)
    {
        string secret = configuredSecret?.Trim() ?? string.Empty;
        bool isInsecurePlaceholder =
            string.IsNullOrWhiteSpace(secret) ||
            secret.Equals("ipc-gateway-web-auth-secret", StringComparison.OrdinalIgnoreCase) ||
            secret.Equals("ipc-gateway-web-auth-secret-change-me", StringComparison.OrdinalIgnoreCase) ||
            secret.Contains("change-me", StringComparison.OrdinalIgnoreCase);

        if (!isInsecurePlaceholder && secret.Length >= 32)
            return secret;

        if (environment.IsDevelopment())
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        throw new InvalidOperationException("Gateway:Auth:Secret must be set to a non-default value with at least 32 characters before running outside Development.");
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string text)
    {
        string base64 = text.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2:
                base64 += "==";
                break;
            case 3:
                base64 += "=";
                break;
        }
        return Convert.FromBase64String(base64);
    }
}

public sealed class GatewayLoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class GatewayLoginResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public GatewayUserInfo? User { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Locked { get; set; }
    public DateTime LockoutEndTime { get; set; }

    public static GatewayLoginResult Ok(GatewayUserInfo user, string token, DateTimeOffset expiresAt)
    {
        return new GatewayLoginResult
        {
            Success = true,
            User = user,
            Token = token,
            ExpiresAt = expiresAt
        };
    }

    public static GatewayLoginResult Fail(string message, bool locked = false, DateTime lockoutEndTime = default)
    {
        return new GatewayLoginResult
        {
            Success = false,
            ErrorMessage = message ?? string.Empty,
            Locked = locked,
            LockoutEndTime = lockoutEndTime
        };
    }
}

public sealed class GatewayTokenValidationResult
{
    public bool Success { get; set; }
    public ClaimsPrincipal Principal { get; set; } = new ClaimsPrincipal(new ClaimsIdentity());

    public static GatewayTokenValidationResult Ok(ClaimsPrincipal principal)
    {
        return new GatewayTokenValidationResult
        {
            Success = true,
            Principal = principal ?? new ClaimsPrincipal(new ClaimsIdentity())
        };
    }

    public static GatewayTokenValidationResult Fail(ClaimsPrincipal principal)
    {
        return new GatewayTokenValidationResult
        {
            Success = false,
            Principal = principal ?? new ClaimsPrincipal(new ClaimsIdentity())
        };
    }
}

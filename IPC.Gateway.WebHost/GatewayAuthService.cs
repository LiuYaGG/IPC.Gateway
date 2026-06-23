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
        string token = CreateToken(user);
        return GatewayLoginResult.Ok(user, token, DateTimeOffset.UtcNow.Add(_tokenLifetime));
    }

    public bool TryValidateToken(string token, out ClaimsPrincipal principal)
    {
        principal = new ClaimsPrincipal(new ClaimsIdentity());
        if (string.IsNullOrWhiteSpace(token))
            return false;

        string[] parts = token.Split('.');
        if (parts.Length != 2)
            return false;

        string payloadText;
        try
        {
            payloadText = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
        }
        catch
        {
            return false;
        }

        string expectedSignature = Sign(parts[0]);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expectedSignature), Encoding.UTF8.GetBytes(parts[1])))
            return false;

        string[] fields = payloadText.Split('|');
        if (fields.Length < 4)
            return false;

        if (!long.TryParse(fields[2], out long exp))
            return false;
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp)
            return false;

        GatewayUserInfo? user = _users.FindByUsername(fields[0]);
        if (user == null || !user.Enabled)
            return false;

        ClaimsIdentity identity = new ClaimsIdentity("GatewayCookie");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id));
        identity.AddClaim(new Claim(ClaimTypes.Name, user.Username));
        identity.AddClaim(new Claim(ClaimTypes.Role, user.Role));
        identity.AddClaim(new Claim("displayName", user.DisplayName));
        foreach (string permission in ResolvePermissions(user.Role))
        {
            identity.AddClaim(new Claim("permission", permission));
        }

        principal = new ClaimsPrincipal(identity);
        return true;
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

    private IReadOnlyList<string> ResolvePermissions(string roleName)
    {
        GatewayRoleInfo? role = _roles.FindByName(roleName);
        if (role == null || !role.Enabled)
            return GatewayPermissions.GetDefaultPermissionsForRole(roleName);
        return GatewayPermissions.ExpandForRuntime(role.Name, role.Permissions);
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

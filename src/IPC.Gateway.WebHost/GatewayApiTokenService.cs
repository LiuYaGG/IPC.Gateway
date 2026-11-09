/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.WebHost
* 项目描述 ：
* 类 名 称 ：GatewayApiTokenService
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
using IPC.Gateway.Core.Domain.Users;

namespace IPC.Gateway.WebHost;

public sealed class GatewayApiTokenService
{
    private readonly GatewayApiTokenOptions _options;

    public GatewayApiTokenService(GatewayIndustrialSecurityOptions securityOptions)
    {
        _options = securityOptions?.ApiTokens ?? new GatewayApiTokenOptions();
    }

    public bool TryValidate(HttpRequest request, out ClaimsPrincipal principal, out string tokenName, out string errorMessage)
    {
        principal = new ClaimsPrincipal(new ClaimsIdentity());
        tokenName = string.Empty;
        errorMessage = string.Empty;

        if (!_options.Enabled)
            return false;

        string token = ReadToken(request);
        if (string.IsNullOrWhiteSpace(token))
            return false;

        if (_options.RequireHttps && !request.IsHttps)
        {
            errorMessage = "API Token 要求通过 HTTPS/TLS 访问。";
            return false;
        }

        string tokenHash = HashToken(token);
        DateTime nowUtc = DateTime.UtcNow;
        GatewayApiTokenDefinition? matched = _options.Tokens
            .Where(item => item != null && item.Enabled)
            .FirstOrDefault(item =>
                !string.IsNullOrWhiteSpace(item.TokenHash) &&
                FixedTimeEquals(NormalizeHash(item.TokenHash), tokenHash) &&
                (item.ExpiresUtc == DateTime.MinValue || item.ExpiresUtc > nowUtc));

        if (matched == null)
        {
            errorMessage = "API Token 无效或已过期。";
            return false;
        }

        tokenName = string.IsNullOrWhiteSpace(matched.Name) ? "api-token" : matched.Name.Trim();
        ClaimsIdentity identity = new ClaimsIdentity("GatewayApiToken");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "api-token:" + tokenName));
        identity.AddClaim(new Claim(ClaimTypes.Name, tokenName));
        identity.AddClaim(new Claim(ClaimTypes.Role, string.IsNullOrWhiteSpace(matched.Role) ? "ApiToken" : matched.Role.Trim()));
        identity.AddClaim(new Claim("apiToken", "true"));

        foreach (string permission in NormalizeTokenPermissions(matched.Permissions))
            identity.AddClaim(new Claim("permission", permission));

        principal = new ClaimsPrincipal(identity);
        return true;
    }

    public static string HashToken(string token)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private string ReadToken(HttpRequest request)
    {
        string headerName = string.IsNullOrWhiteSpace(_options.HeaderName) ? "X-API-Token" : _options.HeaderName.Trim();
        string headerToken = request.Headers[headerName].ToString();
        if (!string.IsNullOrWhiteSpace(headerToken))
            return headerToken.Trim();

        string authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return authorization.Substring("Bearer ".Length).Trim();

        return string.Empty;
    }

    private static string NormalizeHash(string value)
    {
        return new string((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .ToArray())
            .ToLowerInvariant();
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        byte[] leftBytes = Encoding.UTF8.GetBytes(left ?? string.Empty);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right ?? string.Empty);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static IReadOnlyList<string> NormalizeTokenPermissions(IEnumerable<string>? permissions)
    {
        HashSet<string> values = GatewayPermissions.Normalize(permissions).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string permission in permissions ?? Array.Empty<string>())
        {
            string value = permission?.Trim() ?? string.Empty;
            if (IsCompatibilityPermission(value))
                values.Add(value);
        }

        return values.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsCompatibilityPermission(string permission)
    {
        return permission.Equals(GatewayPermissions.ViewRuntime, StringComparison.OrdinalIgnoreCase) ||
               permission.Equals(GatewayPermissions.WriteRuntime, StringComparison.OrdinalIgnoreCase) ||
               permission.Equals(GatewayPermissions.ReadConfiguration, StringComparison.OrdinalIgnoreCase) ||
               permission.Equals(GatewayPermissions.WriteConfiguration, StringComparison.OrdinalIgnoreCase) ||
               permission.Equals(GatewayPermissions.ManageUsers, StringComparison.OrdinalIgnoreCase) ||
               permission.Equals(GatewayPermissions.ManageRoles, StringComparison.OrdinalIgnoreCase);
    }
}

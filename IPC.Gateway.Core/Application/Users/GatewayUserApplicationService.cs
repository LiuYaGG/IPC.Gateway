/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Application.Users
* 项目描述 ：
* 类 名 称 ：GatewayUserApplicationService
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Core.Application.Users
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
using IPC.Gateway.Core.Domain.Users;

namespace IPC.Gateway.Core.Application.Users;

public sealed class GatewayUserApplicationService : IGatewayUserApplicationService
{
    private readonly IGatewayUserRepository _users;
    private readonly GatewayAccountSecurityOptions _securityOptions;

    public GatewayUserApplicationService(IGatewayUserRepository users)
        : this(users, new GatewayAccountSecurityOptions())
    {
    }

    public GatewayUserApplicationService(IGatewayUserRepository users, GatewayAccountSecurityOptions securityOptions)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _securityOptions = securityOptions ?? new GatewayAccountSecurityOptions();
    }

    public GatewayUserInfo? ValidatePassword(string username, string password)
    {
        return _users.ValidatePassword(username, password);
    }

    public GatewayUserAuthenticationResult Authenticate(string username, string password)
    {
        return _users.Authenticate(username, password, _securityOptions.Lockout);
    }

    public GatewayUserInfo? FindByUsername(string username)
    {
        GatewayUserInfo? user = _users.FindByUsername(username);
        if (user == null)
            return null;

        user.PasswordHash = string.Empty;
        user.PasswordSalt = string.Empty;
        return user;
    }

    public IList<GatewayUserInfo> GetUsers()
    {
        return _users.GetUsers();
    }

    public GatewayUserInfo SaveUser(string username, string displayName, string role, bool enabled, string password)
    {
        if (!string.IsNullOrWhiteSpace(password))
            GatewayPasswordPolicyValidator.Validate(username, password, _securityOptions.Password);
        return _users.UpsertUser(username, displayName, role, enabled, password);
    }

    public void ChangePassword(string username, string currentPassword, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("当前登录账号无效。", nameof(username));
        if (string.IsNullOrWhiteSpace(currentPassword))
            throw new ArgumentException("请输入当前密码。", nameof(currentPassword));
        if (string.IsNullOrWhiteSpace(newPassword))
            throw new ArgumentException("请输入新密码。", nameof(newPassword));

        string normalizedUsername = username.Trim();
        GatewayUserInfo? currentUser = _users.FindByUsername(normalizedUsername);
        if (currentUser == null || !currentUser.Enabled)
            throw new InvalidOperationException("当前登录账号无效。");
        if (_users.ValidatePassword(normalizedUsername, currentPassword) == null)
            throw new ArgumentException("当前密码不正确。", nameof(currentPassword));
        if (_users.ValidatePassword(normalizedUsername, newPassword) != null)
            throw new ArgumentException("新密码不能与当前密码相同。", nameof(newPassword));

        GatewayPasswordPolicyValidator.Validate(normalizedUsername, newPassword, _securityOptions.Password);
        _users.UpsertUser(currentUser.Username, currentUser.DisplayName, currentUser.Role, currentUser.Enabled, newPassword);
    }

    public GatewayUserInfo ResetPassword(string username, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("请输入账号。", nameof(username));
        if (string.IsNullOrWhiteSpace(newPassword))
            throw new ArgumentException("请输入新密码。", nameof(newPassword));

        string normalizedUsername = username.Trim();
        GatewayUserInfo? user = _users.FindByUsername(normalizedUsername);
        if (user == null)
            throw new InvalidOperationException("人员不存在：" + normalizedUsername);

        GatewayPasswordPolicyValidator.Validate(normalizedUsername, newPassword, _securityOptions.Password);
        return _users.UpsertUser(user.Username, user.DisplayName, user.Role, user.Enabled, newPassword);
    }

    public void DeleteUser(string username)
    {
        _users.DeleteUser(username);
    }
}

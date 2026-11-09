/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Application.Users
* 项目描述 ：
* 类 名 称 ：IGatewayUserApplicationService
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

public interface IGatewayUserApplicationService
{
    GatewayUserInfo? ValidatePassword(string username, string password);
    Task<GatewayUserInfo?> ValidatePasswordAsync(string username, string password) => Task.FromResult(ValidatePassword(username, password));
    GatewayUserAuthenticationResult Authenticate(string username, string password);
    Task<GatewayUserAuthenticationResult> AuthenticateAsync(string username, string password) => Task.FromResult(Authenticate(username, password));
    GatewayUserInfo? FindByUsername(string username);
    Task<GatewayUserInfo?> FindByUsernameAsync(string username) => Task.FromResult(FindByUsername(username));
    IList<GatewayUserInfo> GetUsers();
    Task<IList<GatewayUserInfo>> GetUsersAsync() => Task.FromResult(GetUsers());
    GatewayUserInfo SaveUser(string username, string displayName, string role, bool enabled, string password);
    Task<GatewayUserInfo> SaveUserAsync(string username, string displayName, string role, bool enabled, string password) => Task.FromResult(SaveUser(username, displayName, role, enabled, password));
    void ChangePassword(string username, string currentPassword, string newPassword);
    Task ChangePasswordAsync(string username, string currentPassword, string newPassword)
    {
        ChangePassword(username, currentPassword, newPassword);
        return Task.CompletedTask;
    }
    GatewayUserInfo ResetPassword(string username, string newPassword);
    Task<GatewayUserInfo> ResetPasswordAsync(string username, string newPassword) => Task.FromResult(ResetPassword(username, newPassword));
    void DeleteUser(string username);
    Task DeleteUserAsync(string username)
    {
        DeleteUser(username);
        return Task.CompletedTask;
    }
}

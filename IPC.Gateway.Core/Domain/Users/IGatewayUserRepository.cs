/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Domain.Users
* 项目描述 ：
* 类 名 称 ：IGatewayUserRepository
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Core.Domain.Users
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
namespace IPC.Gateway.Core.Domain.Users;

public interface IGatewayUserRepository
{
    GatewayUserInfo? ValidatePassword(string username, string password);
    Task<GatewayUserInfo?> ValidatePasswordAsync(string username, string password) => Task.FromResult(ValidatePassword(username, password));
    GatewayUserAuthenticationResult Authenticate(string username, string password, GatewayAccountLockoutOptions? lockoutOptions);
    Task<GatewayUserAuthenticationResult> AuthenticateAsync(string username, string password, GatewayAccountLockoutOptions? lockoutOptions) => Task.FromResult(Authenticate(username, password, lockoutOptions));
    GatewayUserInfo? FindByUsername(string username);
    Task<GatewayUserInfo?> FindByUsernameAsync(string username) => Task.FromResult(FindByUsername(username));
    IList<GatewayUserInfo> GetUsers();
    Task<IList<GatewayUserInfo>> GetUsersAsync() => Task.FromResult(GetUsers());
    GatewayUserInfo UpsertUser(string username, string displayName, string role, bool enabled, string password);
    Task<GatewayUserInfo> UpsertUserAsync(string username, string displayName, string role, bool enabled, string password) => Task.FromResult(UpsertUser(username, displayName, role, enabled, password));
    void DeleteUser(string username);
    Task DeleteUserAsync(string username)
    {
        DeleteUser(username);
        return Task.CompletedTask;
    }
}

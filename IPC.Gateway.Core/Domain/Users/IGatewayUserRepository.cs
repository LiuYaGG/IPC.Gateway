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
    GatewayUserAuthenticationResult Authenticate(string username, string password, GatewayAccountLockoutOptions? lockoutOptions);
    GatewayUserInfo? FindByUsername(string username);
    IList<GatewayUserInfo> GetUsers();
    GatewayUserInfo UpsertUser(string username, string displayName, string role, bool enabled, string password);
    void DeleteUser(string username);
}

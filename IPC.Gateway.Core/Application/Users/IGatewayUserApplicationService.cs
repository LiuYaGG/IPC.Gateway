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
    GatewayUserAuthenticationResult Authenticate(string username, string password);
    GatewayUserInfo? FindByUsername(string username);
    IList<GatewayUserInfo> GetUsers();
    GatewayUserInfo SaveUser(string username, string displayName, string role, bool enabled, string password);
    void ChangePassword(string username, string currentPassword, string newPassword);
    GatewayUserInfo ResetPassword(string username, string newPassword);
    void DeleteUser(string username);
}

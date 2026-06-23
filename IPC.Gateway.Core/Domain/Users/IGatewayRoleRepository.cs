/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Domain.Users
* 项目描述 ：
* 类 名 称 ：IGatewayRoleRepository
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

public interface IGatewayRoleRepository
{
    IList<GatewayRoleInfo> GetRoles();
    GatewayRoleInfo? FindByName(string roleName);
    GatewayRoleInfo UpsertRole(string roleName, string displayName, string description, bool enabled, IEnumerable<string> permissions);
    void DeleteRole(string roleName);
}

/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Application.Users
* 项目描述 ：
* 类 名 称 ：GatewayRoleApplicationService
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

public sealed class GatewayRoleApplicationService : IGatewayRoleApplicationService
{
    private readonly IGatewayRoleRepository _roles;

    public GatewayRoleApplicationService(IGatewayRoleRepository roles)
    {
        _roles = roles ?? throw new ArgumentNullException(nameof(roles));
    }

    public IList<GatewayRoleInfo> GetRoles()
    {
        return _roles.GetRoles();
    }

    public GatewayRoleInfo? FindByName(string roleName)
    {
        return _roles.FindByName(roleName);
    }

    public GatewayRoleInfo SaveRole(string roleName, string displayName, string description, bool enabled, IEnumerable<string> permissions)
    {
        return _roles.UpsertRole(roleName, displayName, description, enabled, GatewayPermissions.Normalize(permissions));
    }

    public void DeleteRole(string roleName)
    {
        _roles.DeleteRole(roleName);
    }

    public IReadOnlyList<GatewayPermissionInfo> GetPermissionCatalog()
    {
        return GatewayPermissions.GetCatalog();
    }
}

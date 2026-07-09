/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Domain.Users
* 项目描述 ：
* 类 名 称 ：GatewayPermissionInfo
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

public sealed class GatewayPermissionInfo
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string Page { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public static class GatewayPermissions
{
    public const string ViewRuntime = "runtime.view";
    public const string WriteRuntime = "runtime.write";
    public const string ReadConfiguration = "config.read";
    public const string WriteConfiguration = "config.write";
    public const string ManageUsers = "users.manage";
    public const string ManageRoles = "roles.manage";
    public const string ViewAudit = "audit.view";
    public const string ExportAudit = "audit.export";
    public const string ViewSecurity = "security.view";
    public const string ManageCertificates = "security.certificates.manage";
    public const string ViewMaintenance = "maintenance.view";
    public const string UploadUpdatePackage = "maintenance.packages.upload";
    public const string PrepareUpdate = "maintenance.update.prepare";
    public const string RollbackUpdate = "maintenance.rollback.prepare";
    public const string EditWatchdog = "maintenance.watchdog.edit";

    public const string ViewBigScreen = "bigScreen.view";
    public const string ViewTopology = "topology.view";
    public const string ViewDashboard = "dashboard.view";
    public const string EditDashboardStorageHealth = "dashboard.storageHealth.edit";

    public const string ViewDevices = "devices.view";
    public const string CreateDevice = "devices.create";
    public const string EditDevice = "devices.edit";
    public const string DeleteDevice = "devices.delete";
    public const string CreateGroup = "groups.create";
    public const string EditGroup = "groups.edit";
    public const string DeleteGroup = "groups.delete";
    public const string CreateTag = "tags.create";
    public const string EditTag = "tags.edit";
    public const string DeleteTag = "tags.delete";
    public const string WriteTag = "tags.write";

    public const string ViewFlowRules = "flowRules.view";
    public const string CreateFlowRule = "flowRules.create";
    public const string EditFlowRule = "flowRules.edit";
    public const string DeleteFlowRule = "flowRules.delete";
    public const string DebugFlowRule = "flowRules.debug";

    public const string ViewMqtt = "mqtt.view";
    public const string EditMqtt = "mqtt.edit";
    public const string ViewOpcUa = "opcUa.view";
    public const string EditOpcUa = "opcUa.edit";
    public const string ViewProject = "project.view";
    public const string EditProject = "project.edit";
    public const string ViewHistory = "history.view";
    public const string EditHistory = "history.edit";

    public const string ViewUsers = "users.view";
    public const string CreateUser = "users.create";
    public const string EditUser = "users.edit";
    public const string ResetUserPassword = "users.password.reset";
    public const string DeleteUser = "users.delete";
    public const string ViewRoles = "roles.view";
    public const string CreateRole = "roles.create";
    public const string EditRole = "roles.edit";
    public const string DeleteRole = "roles.delete";
    public const string ViewPermissions = "permissions.view";
    public const string EditPermissions = "permissions.edit";

    private static readonly GatewayPermissionInfo[] Catalog =
    {
        Page(ViewBigScreen, "大屏总览", "大屏总览", "查看大屏总览、系统资源、链路趋势和模块状态。"),
        Page(ViewTopology, "设备拓扑", "设备拓扑", "查看设备、协议、分组、标签和服务拓扑图。"),
        Page(ViewDashboard, "运行总览", "运行总览", "查看运行总览、设备状态、健康检查和实时值。"),
        Action(EditDashboardStorageHealth, "保存历史库健康阈值", "运行总览", "保存运行总览中的历史库健康阈值。"),

        Page(ViewDevices, "设备管理", "设备管理", "查看设备树、分组、标签和实时值。"),
        Action(CreateDevice, "新增设备", "设备管理", "在设备管理中新增设备。"),
        Action(EditDevice, "编辑设备", "设备管理", "编辑设备基础信息和连接参数。"),
        Action(DeleteDevice, "删除设备", "设备管理", "删除设备。"),
        Action(CreateGroup, "新增分组", "设备管理", "在设备下新增分组。"),
        Action(EditGroup, "编辑分组", "设备管理", "编辑分组名称、启用状态和采集周期。"),
        Action(DeleteGroup, "删除分组", "设备管理", "删除分组及分组下标签。"),
        Action(CreateTag, "新增标签", "设备管理", "新增设备直属标签或分组标签。"),
        Action(EditTag, "编辑标签", "设备管理", "编辑标签地址、数据类型、采集周期、清洗、缩放等配置。"),
        Action(DeleteTag, "删除标签", "设备管理", "删除标签。"),
        Action(WriteTag, "写入标签值", "设备管理", "在标签操作列执行写入。"),

        Page(ViewFlowRules, "流程规则", "流程规则", "查看流程规则列表和编辑器。"),
        Action(CreateFlowRule, "新增流程规则", "流程规则", "新增流程规则。"),
        Action(EditFlowRule, "编辑流程规则", "流程规则", "编辑流程规则画布、节点、连线和调试配置。"),
        Action(DeleteFlowRule, "删除流程规则", "流程规则", "删除流程规则。"),
        Action(DebugFlowRule, "流程调试", "流程规则", "开启流程调试高亮、调试节点和调试辅助操作。"),

        Page(ViewMqtt, "MQTT", "MQTT", "查看 MQTT 配置和运行状态。"),
        Action(EditMqtt, "保存 MQTT 配置", "MQTT", "编辑并保存 MQTT 连接、主题、发布策略和离线缓存。"),
        Page(ViewOpcUa, "OPC UA Server", "OPC UA Server", "查看 OPC UA Server 配置和运行状态。"),
        Action(EditOpcUa, "保存 OPC UA Server 配置", "OPC UA Server", "编辑并保存 OPC UA Server 监听地址、命名空间、证书和采样参数。"),
        Page(ViewProject, "项目配置", "项目配置", "查看项目 JSON 配置。"),
        Action(EditProject, "保存项目配置", "项目配置", "保存项目 JSON 配置、应用配置和回滚配置。"),
        Page(ViewHistory, "历史库", "历史库", "查看历史库配置和状态。"),
        Action(EditHistory, "保存历史库配置", "历史库", "编辑历史库配置和健康阈值。"),

        Page(ViewAudit, "审计日志", "审计日志", "查看审计日志。"),
        Action(ExportAudit, "导出审计日志", "审计日志", "导出审计日志 CSV。"),
        Page(ViewSecurity, "工业安全", "工业安全", "查看账号策略、TLS 和证书状态。"),
        Action(ManageCertificates, "查看证书状态", "工业安全", "查看 TLS 与 OPC UA 证书有效期、指纹和健康状态。"),
        Page(ViewMaintenance, "安装升级", "安装升级", "查看安装包、升级包、离线升级和版本回滚状态。"),
        Action(UploadUpdatePackage, "上传升级包", "安装升级", "上传离线安装包或升级包。"),
        Action(PrepareUpdate, "准备离线升级", "安装升级", "校验升级包、创建回滚点并生成离线升级动作。"),
        Action(RollbackUpdate, "准备版本回滚", "安装升级", "选择回滚点并生成离线回滚动作。"),
        Action(EditWatchdog, "保存看门狗配置", "安装升级", "编辑看门狗、自恢复和异常重启保护配置。"),

        Page(ViewUsers, "人员管理", "人员管理", "查看人员列表。"),
        Action(CreateUser, "新增人员", "人员管理", "新增 Web 登录人员。"),
        Action(EditUser, "编辑人员", "人员管理", "编辑人员姓名、角色、启用状态和密码。"),
        Action(ResetUserPassword, "重置密码", "人员管理", "重置人员登录密码。"),
        Action(DeleteUser, "删除人员", "人员管理", "删除 Web 登录人员。"),

        Page(ViewRoles, "角色管理", "角色管理", "查看角色列表。"),
        Action(CreateRole, "新增角色", "角色管理", "新增角色基础信息。"),
        Action(EditRole, "编辑角色", "角色管理", "编辑角色基础信息。"),
        Action(DeleteRole, "删除角色", "角色管理", "删除自定义角色。"),
        Page(ViewPermissions, "权限分配", "权限分配", "查看角色权限分配。"),
        Action(EditPermissions, "保存权限分配", "权限分配", "按角色分配页面和按钮级权限。")
    };

    private static GatewayPermissionInfo Page(string key, string name, string group, string description)
    {
        return new GatewayPermissionInfo
        {
            Key = key,
            Name = name,
            Group = group,
            Page = group,
            Action = "view",
            Description = description
        };
    }

    private static GatewayPermissionInfo Action(string key, string name, string group, string description)
    {
        return new GatewayPermissionInfo
        {
            Key = key,
            Name = name,
            Group = group,
            Page = group,
            Action = "action",
            Description = description
        };
    }

    public static IReadOnlyList<GatewayPermissionInfo> GetCatalog()
    {
        return Catalog;
    }

    public static IReadOnlySet<string> GetAllKeys()
    {
        return Catalog.Select(item => item.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> GetDefaultPermissionsForRole(string roleName)
    {
        if (string.Equals(roleName, "Admin", StringComparison.OrdinalIgnoreCase))
            return GetAllKeys().OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList();

        if (string.Equals(roleName, "Operator", StringComparison.OrdinalIgnoreCase))
            return new[]
            {
                ViewDashboard,
                ViewBigScreen,
                ViewTopology,
                EditDashboardStorageHealth,
                ViewDevices,
                CreateDevice,
                EditDevice,
                DeleteDevice,
                CreateGroup,
                EditGroup,
                DeleteGroup,
                CreateTag,
                EditTag,
                DeleteTag,
                WriteTag,
                ViewFlowRules,
                CreateFlowRule,
                EditFlowRule,
                DeleteFlowRule,
                DebugFlowRule,
                ViewMqtt,
                EditMqtt,
                ViewOpcUa,
                EditOpcUa,
                ViewProject,
                EditProject,
                ViewHistory,
                EditHistory,
                EditWatchdog
            };

        return new[]
        {
            ViewDashboard,
            ViewBigScreen,
            ViewTopology,
            ViewDevices,
            ViewFlowRules,
            ViewMqtt,
            ViewOpcUa,
            ViewProject,
            ViewHistory
        };
    }

    public static IReadOnlyList<string> Normalize(IEnumerable<string>? permissions)
    {
        HashSet<string> allowed = GetAllKeys().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ExpandLegacyPermissions(permissions)
            .Where(item => allowed.Contains(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<string> ExpandForRuntime(string roleName, IEnumerable<string>? permissions)
    {
        if (string.Equals(roleName, "Admin", StringComparison.OrdinalIgnoreCase))
            return GetDefaultPermissionsForRole("Admin");

        return Normalize(permissions);
    }

    private static IEnumerable<string> ExpandLegacyPermissions(IEnumerable<string>? permissions)
    {
        HashSet<string> input = (permissions ?? Array.Empty<string>())
            .Select(item => item?.Trim() ?? string.Empty)
            .Where(item => item.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string item in input)
            yield return item;

        if (input.Contains(ViewRuntime))
            foreach (string item in new[] { ViewDashboard, ViewBigScreen, ViewTopology, ViewDevices, ViewFlowRules, ViewMqtt, ViewOpcUa, ViewHistory })
                yield return item;

        if (input.Contains(WriteRuntime))
            yield return WriteTag;

        if (input.Contains(ReadConfiguration))
            foreach (string item in new[] { ViewDevices, ViewFlowRules, ViewMqtt, ViewOpcUa, ViewProject, ViewHistory })
                yield return item;

        if (input.Contains(WriteConfiguration))
            foreach (string item in new[]
            {
                EditDashboardStorageHealth,
                CreateDevice,
                EditDevice,
                DeleteDevice,
                CreateGroup,
                EditGroup,
                DeleteGroup,
                CreateTag,
                EditTag,
                DeleteTag,
                CreateFlowRule,
                EditFlowRule,
                DeleteFlowRule,
                DebugFlowRule,
                EditMqtt,
                EditOpcUa,
                EditProject,
                EditHistory,
                EditWatchdog
            })
                yield return item;

        if (input.Contains(ManageUsers))
            foreach (string item in new[] { ViewUsers, CreateUser, EditUser, ResetUserPassword, DeleteUser })
                yield return item;

        if (input.Contains(ManageRoles))
            foreach (string item in new[] { ViewRoles, CreateRole, EditRole, DeleteRole, ViewPermissions, EditPermissions })
                yield return item;

        if (input.Contains(ViewAudit))
            yield return ViewAudit;

        if (input.Contains(ExportAudit))
            yield return ExportAudit;
    }
}

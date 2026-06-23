/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Application.Gateway
* 项目描述 ：
* 类 名 称 ：GatewayProjectApplicationService
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Core.Application.Gateway
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
using IPC.Gateway.Core.Gateway;
using IPC.Runtime.Configuration;

namespace IPC.Gateway.Core.Application.Gateway;

public sealed class GatewayProjectApplicationService : IGatewayProjectApplicationService
{
    private readonly GatewayCoreService _gateway;

    public GatewayProjectApplicationService(GatewayCoreService gateway)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    public ProjectConfig GetProject() => _gateway.CurrentProject;

    public ProjectConfig SaveProject(ProjectConfig project)
    {
        _gateway.Reload(project);
        return _gateway.CurrentProject;
    }

    public ProjectConfigValidationResult ValidateProject(ProjectConfig project)
    {
        ProjectConfigStore.Normalize(project);
        return ProjectConfigValidator.Validate(project);
    }
}

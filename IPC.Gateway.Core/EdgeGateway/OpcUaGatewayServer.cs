/*----------------------------------------------------------------
* 项目名称 ：IPC.EdgeGateway
* 项目描述 ：
* 类 名 称 ：OpcUaGatewayServer
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.EdgeGateway
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
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;
using Opc.Ua;
using Opc.Ua.Server;

namespace IPC.EdgeGateway
{
    internal sealed class OpcUaGatewayServer : StandardServer
    {
        private readonly IRuntimeService _runtime;
        private readonly Func<ProjectConfig> _projectProvider;
        private readonly OpcUaServerOptions _options;
        private readonly OpcUaServerStatus _status;

        public OpcUaGatewayServer(
            IRuntimeService runtime,
            Func<ProjectConfig> projectProvider,
            OpcUaServerOptions options,
            OpcUaServerStatus status)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _projectProvider = projectProvider ?? throw new ArgumentNullException(nameof(projectProvider));
            _options = OpcUaServerOptions.Normalize(options);
            _status = status ?? throw new ArgumentNullException(nameof(status));
        }

        public OpcUaGatewayNodeManager? GatewayNodeManager { get; private set; }

        protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            GatewayNodeManager = new OpcUaGatewayNodeManager(server, configuration, _runtime, _projectProvider, _options, _status);
            return new MasterNodeManager(server, configuration, null, new INodeManager[] { GatewayNodeManager });
        }

        public void UpdateValue(IPC.Runtime.Values.TagValueSnapshot snapshot)
        {
            GatewayNodeManager?.UpdateValue(snapshot);
        }
    }
}

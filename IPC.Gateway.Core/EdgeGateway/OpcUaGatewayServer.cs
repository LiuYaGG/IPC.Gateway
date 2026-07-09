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
using System.Text;
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

        protected override void OnServerStarted(IServerInternal server)
        {
            base.OnServerStarted(server);
            if (server?.SessionManager != null)
                server.SessionManager.ImpersonateUser += OnImpersonateUser;
        }

        public void UpdateValue(IPC.Runtime.Values.TagValueSnapshot snapshot)
        {
            GatewayNodeManager?.UpdateValue(snapshot);
        }

        private void OnImpersonateUser(ISession session, ImpersonateEventArgs args)
        {
            OpcUaServerOptions options = OpcUaServerOptions.Normalize(_options);
            UserIdentityToken token = args.NewIdentity;

            if (token is AnonymousIdentityToken)
            {
                if (options.AllowAnonymous)
                    AcceptIdentity(args, token);
                else
                    RejectIdentity(args, "Anonymous OPC UA access is disabled.");
                return;
            }

            if (token is UserNameIdentityToken userNameToken &&
                options.UsernamePasswordEnabled &&
                OpcUaPasswordHasher.IsPasswordConfigured(options) &&
                OpcUaPasswordHasher.VerifyPassword(options, userNameToken.UserName, DecodePassword(userNameToken)))
            {
                AcceptIdentity(args, userNameToken);
                return;
            }

            RejectIdentity(args, "Invalid OPC UA username or password.");
        }

        private static string DecodePassword(UserNameIdentityToken token)
        {
            byte[] password = token.DecryptedPassword ?? Array.Empty<byte>();
            return Encoding.UTF8.GetString(password);
        }

        private static void AcceptIdentity(ImpersonateEventArgs args, UserIdentityToken token)
        {
            UserIdentity identity = new UserIdentity(token);
            args.Identity = identity;
            args.EffectiveIdentity = identity;
        }

        private static void RejectIdentity(ImpersonateEventArgs args, string message)
        {
            args.IdentityValidationError = ServiceResult.Create(StatusCodes.BadUserAccessDenied, message);
        }
    }
}

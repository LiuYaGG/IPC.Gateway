/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.Infrastructure
* 项目描述 ：
* 类 名 称 ：PlcDriverPluginLoadContext
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.Infrastructure
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
using System.Reflection;
using System.Runtime.Loader;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Infrastructure
{
    internal sealed class PlcDriverPluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public PlcDriverPluginLoadContext(string mainAssemblyPath)
            : base("IPC.Gateway.Plugin:" + mainAssemblyPath, true)
        {
            _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            AssemblyName coreAssemblyName = typeof(IProtocolDriver).Assembly.GetName();
            if (assemblyName.Name == coreAssemblyName.Name)
                return null;

            string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            if (assemblyPath == null)
                return null;
            return LoadFromAssemblyPath(assemblyPath);
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName)
        {
            string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (libraryPath == null)
                return 0;
            return LoadUnmanagedDllFromPath(libraryPath);
        }
    }
}

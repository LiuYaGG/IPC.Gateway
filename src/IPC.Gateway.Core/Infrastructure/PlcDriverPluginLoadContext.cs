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
using System;
using System.IO;
using System.Linq;
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

            if (IsDnp3ClrAssembly(assemblyName))
                return LoadDnp3ClrAssemblyInDefaultContext(assemblyName, assemblyPath);

            return LoadFromAssemblyPath(assemblyPath);
        }

        private static bool IsDnp3ClrAssembly(AssemblyName assemblyName)
        {
            return string.Equals(assemblyName.Name, "DNP3CLRInterface", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(assemblyName.Name, "DNP3CLRAdapter", StringComparison.OrdinalIgnoreCase);
        }

        private static Assembly LoadDnp3ClrAssemblyInDefaultContext(AssemblyName assemblyName, string assemblyPath)
        {
            if (string.Equals(assemblyName.Name, "DNP3CLRAdapter", StringComparison.OrdinalIgnoreCase))
            {
                string? directory = Path.GetDirectoryName(assemblyPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    string contractPath = Path.Combine(directory, "DNP3CLRInterface.dll");
                    if (File.Exists(contractPath))
                        LoadInDefaultContext("DNP3CLRInterface", contractPath);
                }
            }

            return LoadInDefaultContext(assemblyName.Name!, assemblyPath);
        }

        private static Assembly LoadInDefaultContext(string assemblyName, string assemblyPath)
        {
            Assembly? loaded = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(
                item => string.Equals(item.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));
            return loaded ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
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

/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.Infrastructure
* 项目描述 ：
* 类 名 称 ：PlcDriverPluginRegistry
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
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Infrastructure
{
    public static class PlcDriverPluginRegistry
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, PluginRegistration> Plugins = new Dictionary<string, PluginRegistration>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, PluginLoadSession> LoadedAssemblies = new Dictionary<string, PluginLoadSession>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> ProbeDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _defaultDirectoriesLoaded;

        static PlcDriverPluginRegistry()
        {
            RegisterBuiltIn(new ModbusTcpProtocolDriver());
            RegisterBuiltIn(new Dlt645ProtocolDriver());
            RegisterBuiltIn(new Cjt188ProtocolDriver());
            RegisterBuiltIn(new VirtualPlcProtocolDriver());
        }

        public static void Register(IPlcDriverPlugin? plugin)
        {
            if (plugin == null)
                return;
            Register(new LegacyPlcDriverPluginAdapter(plugin), string.Empty, true, false, new PlcDriverPluginManifest(), null);
        }

        public static void Register(IProtocolDriver? driver)
        {
            Register(driver, string.Empty, true, false, new PlcDriverPluginManifest(), null);
        }

        public static void RegisterBuiltIn(IProtocolDriver? driver)
        {
            PlcDriverPluginManifest manifest = new PlcDriverPluginManifest
            {
                Version = GetGatewayVersion().ToString()
            };
            Register(driver, string.Empty, true, true, manifest, null);
        }

        public static IList<PlcDriverPluginCandidate> DiscoverPlugins(string directory)
        {
            List<PlcDriverPluginCandidate> candidates = new List<PlcDriverPluginCandidate>();
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return candidates;

            string fullDirectory = Path.GetFullPath(directory);
            string[] files = Directory.GetFiles(fullDirectory, "*.dll", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++)
                candidates.Add(DiscoverPlugin(files[i]));

            return candidates;
        }

        public static PlcDriverPluginCandidate DiscoverPlugin(string assemblyPath)
        {
            PlcDriverPluginCandidate candidate = new PlcDriverPluginCandidate();
            if (string.IsNullOrWhiteSpace(assemblyPath))
            {
                candidate.Status = "Invalid";
                candidate.ErrorMessage = "Assembly path is empty.";
                return candidate;
            }

            string fullPath = Path.GetFullPath(assemblyPath);
            candidate.AssemblyPath = fullPath;
            candidate.ManifestPath = ResolveManifestPath(fullPath);

            try
            {
                AssemblyName assemblyName = AssemblyName.GetAssemblyName(fullPath);
                candidate.DriverId = assemblyName.Name ?? string.Empty;
                candidate.DisplayName = assemblyName.Name ?? string.Empty;
                candidate.Version = (assemblyName.Version ?? new Version(0, 0, 0, 0)).ToString();

                PlcDriverPluginManifest manifest = LoadManifest(candidate.ManifestPath);
                ApplyManifest(candidate, manifest);

                string error;
                candidate.IsVersionCompatible = ValidateVersion(manifest, out error);
                candidate.ErrorMessage = error;
                candidate.Status = candidate.IsVersionCompatible ? "Discovered" : "Incompatible";

                lock (SyncRoot)
                    candidate.IsLoaded = LoadedAssemblies.ContainsKey(fullPath);
            }
            catch (Exception ex)
            {
                candidate.Status = "Invalid";
                candidate.ErrorMessage = ex.Message;
            }

            return candidate;
        }

        public static bool ValidatePluginVersion(string assemblyPath, out string errorMessage)
        {
            PlcDriverPluginManifest manifest = LoadManifest(ResolveManifestPath(Path.GetFullPath(assemblyPath ?? string.Empty)));
            return ValidateVersion(manifest, out errorMessage);
        }

        public static void LoadDefaultPlugins()
        {
            lock (SyncRoot)
            {
                if (_defaultDirectoriesLoaded)
                    return;

                _defaultDirectoriesLoaded = true;
            }

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            LoadPluginsFromDirectory(Path.Combine(baseDirectory, "Drivers"));
            LoadPluginsFromDirectory(Path.Combine(baseDirectory, "Plugins", "Drivers"));
        }

        public static void RefreshDefaultPlugins()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            LoadPluginsFromDirectory(Path.Combine(baseDirectory, "Drivers"));
            LoadPluginsFromDirectory(Path.Combine(baseDirectory, "Plugins", "Drivers"));
            lock (SyncRoot)
                _defaultDirectoriesLoaded = true;
        }

        public static void LoadFromDirectory(string directory)
        {
            LoadPluginsFromDirectory(directory);
        }

        public static IList<PlcDriverPluginLoadResult> LoadPluginsFromDirectory(string directory)
        {
            List<PlcDriverPluginLoadResult> results = new List<PlcDriverPluginLoadResult>();
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return results;

            string fullDirectory = Path.GetFullPath(directory);
            lock (SyncRoot)
                ProbeDirectories.Add(fullDirectory);

            IList<PlcDriverPluginCandidate> candidates = DiscoverPlugins(fullDirectory);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(candidates[i].ManifestPath))
                    continue;
                results.Add(LoadPlugin(candidates[i].AssemblyPath));
            }

            return results;
        }

        public static PlcDriverPluginLoadResult LoadPlugin(string assemblyPath)
        {
            PlcDriverPluginLoadResult result = new PlcDriverPluginLoadResult();
            if (string.IsNullOrWhiteSpace(assemblyPath))
            {
                result.ErrorMessage = "Assembly path is empty.";
                return result;
            }

            string fullPath = Path.GetFullPath(assemblyPath);
            result.AssemblyPath = fullPath;
            result.ManifestPath = ResolveManifestPath(fullPath);

            lock (SyncRoot)
            {
                PluginLoadSession? existing;
                if (LoadedAssemblies.TryGetValue(fullPath, out existing) && existing != null)
                {
                    result.Success = true;
                    result.DriverIds = new List<string>(existing.DriverIds);
                    return result;
                }
            }

            PlcDriverPluginManifest manifest = LoadManifest(result.ManifestPath);
            string versionError;
            if (!ValidateVersion(manifest, out versionError))
            {
                result.ErrorMessage = versionError;
                return result;
            }

            PlcDriverPluginLoadContext loadContext = new PlcDriverPluginLoadContext(fullPath);
            PluginLoadSession session = new PluginLoadSession(fullPath, result.ManifestPath, loadContext, manifest);

            try
            {
                Assembly assembly = loadContext.LoadFromAssemblyPath(fullPath);
                IList<IProtocolDriver> drivers = CreateDrivers(assembly, manifest);
                if (drivers.Count == 0)
                    throw new InvalidOperationException("No IProtocolDriver or IPlcDriverPlugin implementation was found.");

                lock (SyncRoot)
                {
                    EnsureNoDuplicateDrivers(drivers, fullPath);
                    for (int i = 0; i < drivers.Count; i++)
                    {
                        IProtocolDriver driver = drivers[i];
                        Register(driver, fullPath, false, false, manifest, session);
                        session.DriverIds.Add(driver.DriverId);
                    }

                    LoadedAssemblies[fullPath] = session;
                }

                result.Success = true;
                result.DriverIds = new List<string>(session.DriverIds);
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                UnloadContext(loadContext);
                return result;
            }
        }

        public static bool UnloadPlugin(string driverId)
        {
            if (string.IsNullOrWhiteSpace(driverId))
                return false;

            PluginRegistration? registration;
            lock (SyncRoot)
            {
                if (!Plugins.TryGetValue(driverId.Trim(), out registration))
                    return false;
                if (registration.BuiltIn || registration.Session == null)
                    return false;
            }

            return UnloadPluginAssembly(registration.AssemblyPath);
        }

        public static bool UnloadPluginAssembly(string assemblyPath)
        {
            if (string.IsNullOrWhiteSpace(assemblyPath))
                return false;

            string fullPath = Path.GetFullPath(assemblyPath);
            PluginLoadSession? session;
            lock (SyncRoot)
            {
                if (!LoadedAssemblies.TryGetValue(fullPath, out session) || session == null)
                    return false;

                for (int i = 0; i < session.DriverIds.Count; i++)
                    Plugins.Remove(session.DriverIds[i]);

                LoadedAssemblies.Remove(fullPath);
            }

            UnloadContext(session.LoadContext);
            return true;
        }

        public static IList<PlcDriverPluginInfo> GetRegisteredDrivers()
        {
            LoadDefaultPlugins();
            lock (SyncRoot)
            {
                List<PlcDriverPluginInfo> drivers = new List<PlcDriverPluginInfo>();
                foreach (PluginRegistration registration in Plugins.Values)
                {
                    drivers.Add(new PlcDriverPluginInfo
                    {
                        DriverId = registration.Plugin.DriverId,
                        DisplayName = registration.Plugin.DisplayName,
                        Protocol = registration.Plugin.Protocol.ToString(),
                        Version = registration.Version,
                        MinGatewayVersion = registration.MinGatewayVersion,
                        MaxGatewayVersion = registration.MaxGatewayVersion,
                        AssemblyPath = registration.AssemblyPath,
                        LoadContextId = registration.LoadContextId,
                        BuiltIn = registration.BuiltIn
                    });
                }
                return drivers;
            }
        }

        public static bool TryCreateClient(PlcConnectionOptions options, out IPlcClient? client)
        {
            client = null;
            if (options == null)
                throw new ArgumentNullException("options");

            LoadDefaultPlugins();
            string driverId = options.DriverId;

            PluginRegistration? registration;
            lock (SyncRoot)
            {
                if (!string.IsNullOrWhiteSpace(driverId))
                {
                    if (!Plugins.TryGetValue(driverId.Trim(), out registration))
                        return false;
                }
                else
                {
                    registration = FindDriverByProtocol(options);
                    if (registration == null)
                        return false;
                }
            }

            if (!registration.Plugin.Supports(options))
                return false;

            client = registration.Plugin.CreateClient(options);
            if (client == null)
                throw new InvalidOperationException("PLC protocol driver returned an empty client: " + registration.Plugin.DriverId);
            return true;
        }

        private static void Register(IProtocolDriver? plugin, string assemblyPath, bool allowReplace, bool builtIn, PlcDriverPluginManifest? manifest, PluginLoadSession? session)
        {
            if (plugin == null)
                return;

            string driverId = plugin.DriverId;
            if (string.IsNullOrWhiteSpace(driverId))
                throw new InvalidOperationException("PLC protocol driver DriverId cannot be empty.");

            string key = driverId.Trim();
            if (!allowReplace && Plugins.ContainsKey(key))
                throw new InvalidOperationException("PLC protocol driver already exists: " + key);

            Plugins[key] = new PluginRegistration(
                plugin,
                assemblyPath ?? string.Empty,
                builtIn,
                manifest == null ? string.Empty : manifest.Version,
                manifest == null ? string.Empty : manifest.MinGatewayVersion,
                manifest == null ? string.Empty : manifest.MaxGatewayVersion,
                session == null ? string.Empty : session.Id,
                session);
        }

        private static PluginRegistration? FindDriverByProtocol(PlcConnectionOptions options)
        {
            foreach (PluginRegistration registration in Plugins.Values)
            {
                if (registration == null || registration.Plugin == null)
                    continue;
                if (registration.Plugin.Protocol == options.Protocol && registration.Plugin.Supports(options))
                    return registration;
            }

            return null;
        }

        private static IList<IProtocolDriver> CreateDrivers(Assembly assembly, PlcDriverPluginManifest manifest)
        {
            List<IProtocolDriver> drivers = new List<IProtocolDriver>();
            Type protocolDriverType = typeof(IProtocolDriver);
            Type pluginType = typeof(IPlcDriverPlugin);
            Type?[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }

            for (int i = 0; i < types.Length; i++)
            {
                Type? type = types[i];
                if (type == null || type.IsAbstract || type.IsInterface)
                    continue;
                if (type.GetConstructor(Type.EmptyTypes) == null)
                    continue;
                if (!string.IsNullOrWhiteSpace(manifest.EntryType) &&
                    !string.Equals(type.FullName, manifest.EntryType, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (protocolDriverType.IsAssignableFrom(type))
                {
                    IProtocolDriver? driver = Activator.CreateInstance(type) as IProtocolDriver;
                    if (driver == null)
                        throw new InvalidOperationException("PLC protocol driver could not be created: " + type.FullName);
                    drivers.Add(driver);
                    continue;
                }

                if (pluginType.IsAssignableFrom(type))
                {
                    IPlcDriverPlugin? plugin = Activator.CreateInstance(type) as IPlcDriverPlugin;
                    if (plugin == null)
                        throw new InvalidOperationException("Legacy PLC driver plugin could not be created: " + type.FullName);
                    drivers.Add(new LegacyPlcDriverPluginAdapter(plugin));
                }
            }

            return drivers;
        }

        private static void EnsureNoDuplicateDrivers(IList<IProtocolDriver> drivers, string assemblyPath)
        {
            for (int i = 0; i < drivers.Count; i++)
            {
                IProtocolDriver driver = drivers[i];
                if (driver == null || string.IsNullOrWhiteSpace(driver.DriverId))
                    throw new InvalidOperationException("PLC protocol driver DriverId cannot be empty.");

                PluginRegistration? existing;
                if (Plugins.TryGetValue(driver.DriverId.Trim(), out existing) && existing != null)
                {
                    if (!string.Equals(existing.AssemblyPath, assemblyPath, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("PLC protocol driver already exists: " + driver.DriverId);
                }
            }
        }

        private static string ResolveManifestPath(string assemblyPath)
        {
            if (string.IsNullOrWhiteSpace(assemblyPath))
                return string.Empty;

            string directory = Path.GetDirectoryName(assemblyPath) ?? string.Empty;
            string fileName = Path.GetFileNameWithoutExtension(assemblyPath);
            string[] candidates = new[]
            {
                Path.Combine(directory, fileName + ".ipc-driver.json"),
                Path.Combine(directory, "ipc-driver.json"),
                Path.Combine(directory, fileName + ".json")
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i]))
                    return Path.GetFullPath(candidates[i]);
            }

            return string.Empty;
        }

        private static PlcDriverPluginManifest LoadManifest(string manifestPath)
        {
            if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
                return new PlcDriverPluginManifest();

            try
            {
                string json = File.ReadAllText(manifestPath);
                PlcDriverPluginManifest? manifest = JsonSerializer.Deserialize<PlcDriverPluginManifest>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return manifest ?? new PlcDriverPluginManifest();
            }
            catch
            {
                return new PlcDriverPluginManifest();
            }
        }

        private static void ApplyManifest(PlcDriverPluginCandidate candidate, PlcDriverPluginManifest? manifest)
        {
            if (manifest == null)
                return;

            if (!string.IsNullOrWhiteSpace(manifest.DriverId))
                candidate.DriverId = manifest.DriverId;
            if (!string.IsNullOrWhiteSpace(manifest.DisplayName))
                candidate.DisplayName = manifest.DisplayName;
            if (!string.IsNullOrWhiteSpace(manifest.Version))
                candidate.Version = manifest.Version;
            candidate.MinGatewayVersion = manifest.MinGatewayVersion ?? string.Empty;
            candidate.MaxGatewayVersion = manifest.MaxGatewayVersion ?? string.Empty;
        }

        private static bool ValidateVersion(PlcDriverPluginManifest? manifest, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (manifest == null)
                return true;

            Version gatewayVersion = GetGatewayVersion();
            Version? minVersion;
            if (!string.IsNullOrWhiteSpace(manifest.MinGatewayVersion) &&
                Version.TryParse(manifest.MinGatewayVersion, out minVersion) &&
                gatewayVersion < minVersion)
            {
                errorMessage = "Gateway version " + gatewayVersion + " is lower than plugin minimum " + minVersion + ".";
                return false;
            }

            Version? maxVersion;
            if (!string.IsNullOrWhiteSpace(manifest.MaxGatewayVersion) &&
                Version.TryParse(manifest.MaxGatewayVersion, out maxVersion) &&
                gatewayVersion > maxVersion)
            {
                errorMessage = "Gateway version " + gatewayVersion + " is higher than plugin maximum " + maxVersion + ".";
                return false;
            }

            return true;
        }

        private static Version GetGatewayVersion()
        {
            return typeof(PlcDriverPluginRegistry).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
        }

        private static void UnloadContext(PlcDriverPluginLoadContext? loadContext)
        {
            if (loadContext == null)
                return;

            loadContext.Unload();
            for (int i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        private sealed class PluginRegistration
        {
            public PluginRegistration(
                IProtocolDriver plugin,
                string assemblyPath,
                bool builtIn,
                string version,
                string minGatewayVersion,
                string maxGatewayVersion,
                string loadContextId,
                PluginLoadSession? session)
            {
                Plugin = plugin;
                AssemblyPath = assemblyPath;
                BuiltIn = builtIn;
                Version = version ?? string.Empty;
                MinGatewayVersion = minGatewayVersion ?? string.Empty;
                MaxGatewayVersion = maxGatewayVersion ?? string.Empty;
                LoadContextId = loadContextId ?? string.Empty;
                Session = session;
            }

            public IProtocolDriver Plugin { get; private set; }
            public string AssemblyPath { get; private set; }
            public bool BuiltIn { get; private set; }
            public string Version { get; private set; }
            public string MinGatewayVersion { get; private set; }
            public string MaxGatewayVersion { get; private set; }
            public string LoadContextId { get; private set; }
            public PluginLoadSession? Session { get; private set; }
        }

        private sealed class PluginLoadSession
        {
            public PluginLoadSession(string assemblyPath, string manifestPath, PlcDriverPluginLoadContext loadContext, PlcDriverPluginManifest manifest)
            {
                Id = Guid.NewGuid().ToString("N");
                AssemblyPath = assemblyPath ?? string.Empty;
                ManifestPath = manifestPath ?? string.Empty;
                LoadContext = loadContext;
                Manifest = manifest ?? new PlcDriverPluginManifest();
                DriverIds = new List<string>();
            }

            public string Id { get; private set; }
            public string AssemblyPath { get; private set; }
            public string ManifestPath { get; private set; }
            public PlcDriverPluginLoadContext LoadContext { get; private set; }
            public PlcDriverPluginManifest Manifest { get; private set; }
            public List<string> DriverIds { get; private set; }
        }
    }
}

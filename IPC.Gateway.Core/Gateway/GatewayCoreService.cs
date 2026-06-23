/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Gateway
* 项目描述 ：
* 类 名 称 ：GatewayCoreService
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Core.Gateway
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
using System.Text.Json;
using System.Threading;
using IPC;
using IPC.EdgeGateway;
using IPC.Gateway.DataProcessing;
using IPC.Gateway.Core.Domain.Configuration;
using IPC.Gateway.Core.Infrastructure.Persistence;
using IPC.Plc.Communication.Core;
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;
using IPC.Runtime.Values;

namespace IPC.Gateway.Core.Gateway
{
    
    
    
    
    
    
    
    
    
    public sealed class GatewayCoreService : IDisposable
    {
        private readonly object _syncRoot;
        private readonly IGatewayConfigurationRepository _configurationStore;
        private readonly GatewayRuntimeStateCache _runtimeStateCache;
        private readonly GatewayRuntimeOptions _runtimeOptions;
        private readonly SystemResourceMonitor _systemResources;
        private LocalHistoryOptions _historyOptions;
        private StorageHealthThresholds _storageHealthThresholds;
        private LocalHistoryService _history;
        private MqttGatewayOptions _mqttOptions;
        private MqttGatewayService _mqtt;
        private OpcUaServerOptions _opcUaOptions;
        private OpcUaServerService _opcUa;
        private readonly IFlowRuleEngineFactory _flowRuleEngineFactory;
        private readonly IModelInferenceService _modelInference;
        private EdgeRuleEngineService _ruleEngine;
        private IFlowRuleEngineService _flowRuleEngine;
        private ProjectConfig _project;
        private ProjectConfigValidationResult _lastValidation;
        private DateTime _startedTime;
        private DateTime _lastReloadTime;
        private bool _disposed;

        public GatewayCoreService(
            GatewayRuntimeOptions runtimeOptions,
            MqttGatewayOptions mqttOptions,
            OpcUaServerOptions opcUaOptions,
            LocalHistoryOptions historyOptions,
            StorageHealthThresholds storageHealthThresholds,
            IFlowRuleEngineFactory? flowRuleEngineFactory = null,
            IModelInferenceService? modelInference = null)
        {
            _syncRoot = new object();
            _runtimeOptions = runtimeOptions == null ? new GatewayRuntimeOptions() : runtimeOptions;
            if (_runtimeOptions.Resilience == null)
                _runtimeOptions.Resilience = new GatewayResilienceOptions();
            _configurationStore = new SqlSugarGatewayConfigurationRepository(_runtimeOptions.Database, _runtimeOptions.SecretProtection);
            _runtimeStateCache = new GatewayRuntimeStateCache(new SqlSugarRuntimeStateRepository(_runtimeOptions.Database));
            _systemResources = new SystemResourceMonitor();
            _flowRuleEngineFactory = flowRuleEngineFactory ?? new NoopFlowRuleEngineFactory();
            _modelInference = modelInference ?? NoopModelInferenceService.Instance;
            Runtime = new RuntimeEngine(_runtimeOptions.Scheduler);
            _mqttOptions = _configurationStore.LoadOrCreateMqtt(mqttOptions);
            _opcUaOptions = _configurationStore.LoadOrCreateOpcUa(opcUaOptions);
            _historyOptions = _configurationStore.LoadOrCreateHistory(historyOptions);
            _storageHealthThresholds = _configurationStore.LoadOrCreateStorageHealth(storageHealthThresholds);
            _project = new ProjectConfig();
            _lastValidation = new ProjectConfigValidationResult();
            _history = new LocalHistoryService(Runtime, _historyOptions, _runtimeOptions.Resilience.History);
            _mqtt = CreateMqttService();
            _opcUa = CreateOpcUaService();
            _ruleEngine = CreateRuleEngine();
            _flowRuleEngine = CreateFlowRuleEngine();
        }

        public IRuntimeService Runtime { get; private set; }

        public bool IsRunning
        {
            get { return Runtime != null && Runtime.IsRunning; }
        }

        public ProjectConfig CurrentProject
        {
            get
            {
                lock (_syncRoot)
                    return ProjectConfigCloner.Clone(_project);
            }
        }

        public MqttGatewayOptions CurrentMqttOptions
        {
            get
            {
                lock (_syncRoot)
                    return _mqttOptions.Clone();
            }
        }

        public OpcUaServerOptions CurrentOpcUaOptions
        {
            get
            {
                lock (_syncRoot)
                    return _opcUaOptions.Clone();
            }
        }

        public LocalHistoryOptions CurrentHistoryOptions
        {
            get
            {
                lock (_syncRoot)
                    return _historyOptions.Clone();
            }
        }

        public StorageHealthThresholds CurrentStorageHealthThresholds
        {
            get
            {
                lock (_syncRoot)
                    return _storageHealthThresholds.Clone();
            }
        }

        public void Start()
        {
            lock (_syncRoot)
            {
                if (Runtime.IsRunning)
                    return;

                _project = LoadProject();
                _lastValidation = ProjectConfigValidator.Validate(_project);
                if (!_lastValidation.IsValid)
                    throw new InvalidOperationException("项目配置校验失败：" + string.Join("；", _lastValidation.Errors.ToArray()));

                Runtime.Start(_project);
                _runtimeStateCache.Start(Runtime, _project);
                _history.Start();
                _mqtt.Start();
                _opcUa.Start();
                ReplaceRuleEngine();
                _ruleEngine.Start();
                ReplaceFlowRuleEngine();
                _flowRuleEngine.Start();
                _startedTime = DateTime.Now;
                _lastReloadTime = _startedTime;
            }
        }

        public void Stop()
        {
            lock (_syncRoot)
            {
                _ruleEngine.Stop();
                _flowRuleEngine.Stop();
                _runtimeStateCache.Stop(markDevicesOffline: true);
                _opcUa.Stop();
                _mqtt.Stop();
                _history.Stop();
                Runtime.Stop();
            }
        }

        public void Reload(ProjectConfig project)
        {
            if (project == null)
                throw new ArgumentNullException("project");

            ProjectConfigStore.Normalize(project);
            ProjectConfigValidationResult validation = ProjectConfigValidator.Validate(project);
            if (!validation.IsValid)
                throw new InvalidOperationException("项目配置校验失败：" + string.Join("；", validation.Errors.ToArray()));

            lock (_syncRoot)
            {
                bool wasRunning = Runtime.IsRunning;
                if (wasRunning)
                {
                    _ruleEngine.Stop();
                    _flowRuleEngine.Stop();
                    _runtimeStateCache.Stop(markDevicesOffline: true);
                    _opcUa.Stop();
                    Runtime.Stop();
                }

                _project = ProjectConfigCloner.Clone(project);
                _lastValidation = validation;
                ReplaceRuleEngine();
                ReplaceFlowRuleEngine();
                _configurationStore.SaveProject(_project, "WebApi", "保存项目配置");

                if (wasRunning)
                {
                    Runtime.Start(_project);
                    _runtimeStateCache.Start(Runtime, _project);
                    _opcUa.Start();
                    _ruleEngine.Start();
                    _flowRuleEngine.Start();
                }

                _lastReloadTime = DateTime.Now;
            }
        }

        public void ReloadFromFile()
        {
            Reload(_configurationStore.LoadProject());
        }

        public void UpdateMqttOptions(MqttGatewayOptions options)
        {
            if (options == null)
                throw new ArgumentNullException("options");

            ApplyMqttOptions(options, true);
        }

        public void UpdateOpcUaOptions(OpcUaServerOptions options)
        {
            if (options == null)
                throw new ArgumentNullException("options");

            ApplyOpcUaOptions(options, true);
        }

        private void ApplyOpcUaOptions(OpcUaServerOptions options, bool save)
        {
            lock (_syncRoot)
            {
                _opcUaOptions = OpcUaServerOptions.Normalize(options);
                if (save)
                    _configurationStore.SaveOpcUa(_opcUaOptions, "WebApi", "Save OPC UA Server options");
                _opcUa.UpdateOptions(_opcUaOptions);
                _lastReloadTime = DateTime.Now;
            }
        }

        private void ApplyMqttOptions(MqttGatewayOptions options, bool save)
        {
            lock (_syncRoot)
            {
                bool wasRunning = Runtime.IsRunning;
                _ruleEngine.Stop();
                _flowRuleEngine.Stop();
                _mqtt.Stop();
                _mqtt.Dispose();
                _mqttOptions = options.Clone();
                if (save)
                    _configurationStore.SaveMqtt(_mqttOptions, "WebApi", "保存MQTT参数");
                _mqtt = CreateMqttService();
                ReplaceRuleEngine();
                ReplaceFlowRuleEngine();
                if (wasRunning)
                {
                    _mqtt.Start();
                    _ruleEngine.Start();
                    _flowRuleEngine.Start();
                }
                _lastReloadTime = DateTime.Now;
            }
        }

        public void UpdateHistoryOptions(LocalHistoryOptions options)
        {
            if (options == null)
                throw new ArgumentNullException("options");

            ApplyHistoryOptions(options, true);
        }

        public void UpdateStorageHealthThresholds(StorageHealthThresholds thresholds)
        {
            if (thresholds == null)
                throw new ArgumentNullException("thresholds");

            lock (_syncRoot)
            {
                _storageHealthThresholds = StorageHealthEvaluator.NormalizeThresholds(thresholds);
                _configurationStore.SaveStorageHealth(_storageHealthThresholds, "WebApi", "Save storage health thresholds");
                _lastReloadTime = DateTime.Now;
            }
        }

        private void ApplyHistoryOptions(LocalHistoryOptions options, bool save)
        {
            lock (_syncRoot)
            {
                bool wasRunning = Runtime.IsRunning;
                _ruleEngine.Stop();
                _flowRuleEngine.Stop();
                _mqtt.Stop();
                _history.Stop();
                _mqtt.Dispose();
                _history.Dispose();

                _historyOptions = NormalizeHistoryOptions(options);
                if (save)
                    _configurationStore.SaveHistory(_historyOptions, "WebApi", "Save local history options");

                _history = new LocalHistoryService(Runtime, _historyOptions, _runtimeOptions.Resilience.History);
                _mqtt = CreateMqttService();
                ReplaceRuleEngine();
                ReplaceFlowRuleEngine();
                if (wasRunning)
                {
                    _history.Start();
                    _mqtt.Start();
                    _ruleEngine.Start();
                    _flowRuleEngine.Start();
                }

                _lastReloadTime = DateTime.Now;
            }
        }

        public IList<GatewayConfigurationVersionInfo> GetConfigurationVersions(string configType, int maxCount)
        {
            return _configurationStore.GetVersions(configType, maxCount);
        }

        public void RollbackConfiguration(string configType, int version)
        {
            if (string.Equals(configType, GatewayConfigurationType.Mqtt, StringComparison.OrdinalIgnoreCase))
            {
                ApplyMqttOptions(_configurationStore.RollbackMqtt(version), false);
                return;
            }

            if (string.Equals(configType, GatewayConfigurationType.OpcUa, StringComparison.OrdinalIgnoreCase))
            {
                ApplyOpcUaOptions(_configurationStore.RollbackOpcUa(version), false);
                return;
            }

            if (string.Equals(configType, GatewayConfigurationType.History, StringComparison.OrdinalIgnoreCase))
            {
                ApplyHistoryOptions(_configurationStore.RollbackHistory(version), false);
                return;
            }

            if (string.Equals(configType, GatewayConfigurationType.StorageHealth, StringComparison.OrdinalIgnoreCase))
            {
                lock (_syncRoot)
                {
                    _storageHealthThresholds = _configurationStore.RollbackStorageHealth(version);
                    _lastReloadTime = DateTime.Now;
                }

                return;
            }

            ProjectConfig project = _configurationStore.RollbackProject(version);
            lock (_syncRoot)
            {
                bool wasRunning = Runtime.IsRunning;
                if (wasRunning)
                {
                    _ruleEngine.Stop();
                    _flowRuleEngine.Stop();
                    _runtimeStateCache.Stop(markDevicesOffline: true);
                    _opcUa.Stop();
                    Runtime.Stop();
                }

                _project = ProjectConfigCloner.Clone(project);
                _lastValidation = ProjectConfigValidator.Validate(_project);
                ReplaceRuleEngine();
                ReplaceFlowRuleEngine();
                if (wasRunning)
                {
                    Runtime.Start(_project);
                    _runtimeStateCache.Start(Runtime, _project);
                    _opcUa.Start();
                    _ruleEngine.Start();
                    _flowRuleEngine.Start();
                }
                _lastReloadTime = DateTime.Now;
            }
        }

        public object ApplyConfigurationCommand(string source, string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                throw new ArgumentException("配置下发内容不能为空。", "payload");

            using (JsonDocument document = JsonDocument.Parse(payload))
            {
                JsonElement root = document.RootElement;
                string action = ReadString(root, "action");
                if (string.IsNullOrWhiteSpace(action))
                    action = "replaceProject";
                string normalized = NormalizeAction(action);

                if (normalized == "replaceproject" || normalized == "applyproject" || normalized == "replaceall")
                {
                    ProjectConfig project = DeserializeConfig<ProjectConfig>(root, "project");
                    Reload(project);
                }
                else if (normalized == "upsertdevice")
                {
                    UpsertDevice(DeserializeConfig<DeviceConfig>(root, "device"));
                }
                else if (normalized == "deletedevice")
                {
                    DeleteDevice(ReadRequired(root, "deviceId", "deviceName"));
                }
                else if (normalized == "upsertgroup")
                {
                    UpsertGroup(ReadRequired(root, "deviceId", "deviceName"), DeserializeConfig<GroupConfig>(root, "group"));
                }
                else if (normalized == "deletegroup")
                {
                    DeleteGroup(ReadRequired(root, "groupId", "groupName"));
                }
                else if (normalized == "upserttag")
                {
                    UpsertTag(ReadString(root, "deviceId"), ReadString(root, "groupId"), DeserializeConfig<TagConfig>(root, "tag"));
                }
                else if (normalized == "deletetag")
                {
                    DeleteTag(ReadRequired(root, "tagId", "tagName"));
                }
                else if (normalized == "upsertrule")
                {
                    UpsertRule(DeserializeConfig<EdgeRuleConfig>(root, "rule"));
                }
                else if (normalized == "deleterule")
                {
                    DeleteRule(ReadRequired(root, "ruleId", "ruleName"));
                }
                else if (normalized == "upsertflowrule")
                {
                    UpsertFlowRule(DeserializeConfig<FlowRuleDefinition>(root, "flowRule"));
                }
                else if (normalized == "deleteflowrule")
                {
                    DeleteFlowRule(ReadRequired(root, "flowRuleId", "flowRuleName"));
                }
                else if (normalized == "updatemqtt" || normalized == "applymqtt")
                {
                    UpdateMqttOptions(DeserializeConfig<MqttGatewayOptions>(root, "mqtt"));
                }
                else if (normalized == "updateopcua" || normalized == "applyopcua" || normalized == "updateopcuaserver" || normalized == "applyopcuaserver")
                {
                    UpdateOpcUaOptions(DeserializeConfig<OpcUaServerOptions>(root, "opcUa"));
                }
                else if (normalized == "updatehistory" || normalized == "applyhistory")
                {
                    UpdateHistoryOptions(DeserializeConfig<LocalHistoryOptions>(root, "history"));
                }
                else if (normalized == "updatestoragehealth" || normalized == "applystoragehealth")
                {
                    UpdateStorageHealthThresholds(DeserializeConfig<StorageHealthThresholds>(root, "storageHealth"));
                }
                else if (normalized == "rollback" || normalized == "rollbackconfig" || normalized == "rollbackconfiguration")
                {
                    RollbackConfiguration(ReadRequired(root, "configType", "type"), ReadInt(root, "version"));
                }
                else
                {
                    throw new NotSupportedException("不支持的配置下发动作：" + action);
                }
            }

            return GetStatus();
        }

        public GatewayRuntimeStatus GetStatus()
        {
            lock (_syncRoot)
            {
                GatewayRuntimeStateSnapshot runtimeState = _runtimeStateCache.CaptureNow(Runtime);
                GatewayRuntimeStatus status = new GatewayRuntimeStatus
                {
                    IsRunning = Runtime.IsRunning,
                    ProjectId = _project == null ? string.Empty : _project.ProjectId,
                    ProjectName = _project == null ? string.Empty : _project.Name,
                    ProjectPath = string.Empty,
                    ConfigurationStore = "SqlSugar:" + _runtimeOptions.Database.Provider + ":" + _runtimeOptions.Database.Database,
                    StartedTime = _startedTime,
                    LastReloadTime = _lastReloadTime,
                    ConfigValidation = _lastValidation,
                    Devices = runtimeState.Devices,
                    Tags = runtimeState.Tags,
                    RecentErrors = runtimeState.RecentErrors.Take(20).ToList(),
                    Mqtt = _mqtt.GetStatus(),
                    OpcUa = _opcUa.GetStatus(),
                    History = _history.GetStats(),
                    RuleEngine = _ruleEngine.GetStatus(),
                    FlowRuleEngine = _flowRuleEngine.GetStatus(),
                    Scheduler = Runtime.GetSchedulerStatus(),
                    System = _systemResources.Capture()
                };

                FillProjectCounts(status, _project);
                FillRuntimeCounts(status, runtimeState.Tags);
                return status;
            }
        }

        public MqttGatewayStatus GetMqttStatus()
        {
            lock (_syncRoot)
                return _mqtt.GetStatus();
        }

        public OpcUaServerStatus GetOpcUaStatus()
        {
            lock (_syncRoot)
                return _opcUa.GetStatus();
        }

        public LocalHistoryStats GetHistoryStats()
        {
            lock (_syncRoot)
                return _history.GetStats();
        }

        public EdgeRuleEngineStatus GetRuleEngineStatus()
        {
            lock (_syncRoot)
                return _ruleEngine.GetStatus();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _runtimeStateCache.Dispose();
            _ruleEngine.Dispose();
            _flowRuleEngine.Dispose();
            _opcUa.Dispose();
            _mqtt.Dispose();
            _history.Dispose();
            Runtime.Dispose();
        }

        private ProjectConfig LoadProject()
        {
            if (_runtimeOptions.AutoCreateDefaultProject)
                return _configurationStore.LoadOrCreateProject(CreateDefaultProject);
            return _configurationStore.LoadProject();
        }

        private MqttGatewayService CreateMqttService()
        {
            return new MqttGatewayService(Runtime, _mqttOptions, _history, QueueRemoteConfiguration, _runtimeOptions.Resilience.Mqtt);
        }

        private OpcUaServerService CreateOpcUaService()
        {
            return new OpcUaServerService(Runtime, () => CurrentProject, _opcUaOptions);
        }

        private EdgeRuleEngineService CreateRuleEngine()
        {
            return new EdgeRuleEngineService(Runtime, _project, QueueRuleEventPublish, _mqttOptions, _runtimeOptions.Resilience.RuleEngine, _modelInference);
        }

        private IFlowRuleEngineService CreateFlowRuleEngine()
        {
            return _flowRuleEngineFactory.Create(Runtime, _project, QueueRuleEventPublish, _mqttOptions, _runtimeOptions.Resilience.RuleEngine, _modelInference);
        }

        private void ReplaceRuleEngine()
        {
            EdgeRuleEngineService oldRuleEngine = _ruleEngine;
            _ruleEngine = CreateRuleEngine();
            oldRuleEngine.Dispose();
        }

        private void ReplaceFlowRuleEngine()
        {
            IFlowRuleEngineService oldFlowRuleEngine = _flowRuleEngine;
            _flowRuleEngine = CreateFlowRuleEngine();
            oldFlowRuleEngine.Dispose();
        }

        private bool QueueRuleEventPublish(string topic, string payload, int qos)
        {
            MqttGatewayService mqtt = _mqtt;
            return mqtt != null && mqtt.QueueCustomPublish(topic, payload, qos);
        }

        private static LocalHistoryOptions NormalizeHistoryOptions(LocalHistoryOptions options)
        {
            LocalHistoryOptions normalized = options == null ? new LocalHistoryOptions() : options.Clone();
            normalized.Directory = string.IsNullOrWhiteSpace(normalized.Directory) ? "Data/History" : normalized.Directory.Trim();
            normalized.RetentionDays = LocalHistoryOptions.ClampRetentionDays(normalized.RetentionDays);
            normalized.MaxViewRecords = LocalHistoryOptions.ClampMaxViewRecords(normalized.MaxViewRecords);
            normalized.DataProcessing = EdgeDataProcessingOptions.Normalize(normalized.DataProcessing);
            normalized.Storage = LocalHistoryStorageOptions.Normalize(normalized.Storage, normalized.RetentionDays);
            return normalized;
        }

        private void QueueRemoteConfiguration(string topic, string payload)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    ApplyConfigurationCommand("MQTT:" + (topic ?? string.Empty), payload);
                }
                catch (Exception ex)
                {
                    IpcLogService.WriteError("MQTT remote configuration failed.", ex);
                }
            });
        }

        private void UpsertDevice(DeviceConfig input)
        {
            if (input == null)
                throw new ArgumentNullException("input");
            ProjectConfig project = CurrentProject;
            DeviceConfig? existing = FindDevice(project, input.Id, input.Name);
            if (existing == null)
            {
                if (string.IsNullOrWhiteSpace(input.Id))
                    input.Id = Guid.NewGuid().ToString("N");
                project.Devices.Add(input);
            }
            else
            {
                existing.Name = input.Name;
                existing.Enabled = input.Enabled;
                existing.Protocol = input.Protocol;
                existing.Connection = input.Connection;
                existing.DefaultScanRateMs = input.DefaultScanRateMs;
                existing.Tags = input.Tags ?? existing.Tags;
                existing.Groups = input.Groups ?? existing.Groups;
            }
            Reload(project);
        }

        private void DeleteDevice(string idOrName)
        {
            ProjectConfig project = CurrentProject;
            DeviceConfig? existing = FindDevice(project, idOrName, idOrName);
            if (existing == null)
                throw new InvalidOperationException("设备不存在：" + idOrName);
            project.Devices.Remove(existing);
            Reload(project);
        }

        private void UpsertGroup(string deviceIdOrName, GroupConfig input)
        {
            ProjectConfig project = CurrentProject;
            DeviceConfig? device = FindDevice(project, deviceIdOrName, deviceIdOrName);
            if (device == null)
                throw new InvalidOperationException("设备不存在：" + deviceIdOrName);
            GroupConfig? existing = FindGroup(project, input.Id, input.Name);
            if (existing == null)
            {
                if (string.IsNullOrWhiteSpace(input.Id))
                    input.Id = Guid.NewGuid().ToString("N");
                input.DeviceId = device.Id;
                device.Groups.Add(input);
            }
            else
            {
                existing.Name = input.Name;
                existing.Enabled = input.Enabled;
                existing.ScanRateMs = input.ScanRateMs;
                existing.Tags = input.Tags ?? existing.Tags;
            }
            Reload(project);
        }

        private void DeleteGroup(string idOrName)
        {
            ProjectConfig project = CurrentProject;
            for (int d = 0; d < project.Devices.Count; d++)
            {
                GroupConfig? group = FindGroup(project.Devices[d], idOrName, idOrName);
                if (group != null)
                {
                    project.Devices[d].Groups.Remove(group);
                    Reload(project);
                    return;
                }
            }
            throw new InvalidOperationException("分组不存在：" + idOrName);
        }

        private void UpsertTag(string deviceIdOrName, string groupIdOrName, TagConfig input)
        {
            ProjectConfig project = CurrentProject;
            DeviceConfig? device = string.IsNullOrWhiteSpace(deviceIdOrName) ? FindDeviceByTag(project, input.Id, input.Name) : FindDevice(project, deviceIdOrName, deviceIdOrName);
            if (device == null)
                throw new InvalidOperationException("标签所属设备不存在。");

            GroupConfig? group = string.IsNullOrWhiteSpace(groupIdOrName) ? null : FindGroup(device, groupIdOrName, groupIdOrName);
            TagConfig? existing = FindTag(project, input.Id, input.Name);
            if (existing == null)
            {
                if (string.IsNullOrWhiteSpace(input.Id))
                    input.Id = Guid.NewGuid().ToString("N");
                input.DeviceId = device.Id;
                input.GroupId = group == null ? string.Empty : group.Id;
                if (group == null)
                    device.Tags.Add(input);
                else
                    group.Tags.Add(input);
            }
            else
            {
                ApplyTag(existing, input);
            }
            Reload(project);
        }

        private void DeleteTag(string idOrName)
        {
            ProjectConfig project = CurrentProject;
            for (int d = 0; d < project.Devices.Count; d++)
            {
                TagConfig? tag = FindTag(project.Devices[d].Tags, idOrName, idOrName);
                if (tag != null)
                {
                    project.Devices[d].Tags.Remove(tag);
                    Reload(project);
                    return;
                }
                for (int g = 0; g < project.Devices[d].Groups.Count; g++)
                {
                    tag = FindTag(project.Devices[d].Groups[g].Tags, idOrName, idOrName);
                    if (tag != null)
                    {
                        project.Devices[d].Groups[g].Tags.Remove(tag);
                        Reload(project);
                        return;
                    }
                }
            }
            throw new InvalidOperationException("标签不存在：" + idOrName);
        }

        private void UpsertRule(EdgeRuleConfig input)
        {
            ProjectConfig project = CurrentProject;
            EdgeRuleConfig? existing = FindRule(project, input.Id, input.Name);
            if (existing == null)
            {
                if (string.IsNullOrWhiteSpace(input.Id))
                    input.Id = Guid.NewGuid().ToString("N");
                project.Rules.Add(input);
            }
            else
            {
                int index = project.Rules.IndexOf(existing);
                input.Id = existing.Id;
                project.Rules[index] = input;
            }
            Reload(project);
        }

        private void DeleteRule(string idOrName)
        {
            ProjectConfig project = CurrentProject;
            EdgeRuleConfig? existing = FindRule(project, idOrName, idOrName);
            if (existing == null)
                throw new InvalidOperationException("规则不存在：" + idOrName);
            project.Rules.Remove(existing);
            Reload(project);
        }

        private void UpsertFlowRule(FlowRuleDefinition input)
        {
            if (input == null)
                throw new ArgumentNullException("input");

            ProjectConfig project = CurrentProject;
            if (project.FlowRules == null)
                project.FlowRules = new List<FlowRuleDefinition>();

            FlowRuleDefinition? existing = FindFlowRule(project, input.Id, input.Name);
            string previousCompiledRuleId = existing == null ? string.Empty : existing.CompiledRuleId;
            if (existing == null)
            {
                if (string.IsNullOrWhiteSpace(input.Id))
                    input.Id = Guid.NewGuid().ToString("N");
                input.CreatedTime = input.CreatedTime == DateTime.MinValue ? DateTime.Now : input.CreatedTime;
                input.UpdatedTime = DateTime.Now;
                project.FlowRules.Add(input);
            }
            else
            {
                int index = project.FlowRules.IndexOf(existing);
                input.Id = existing.Id;
                input.CreatedTime = existing.CreatedTime == DateTime.MinValue ? DateTime.Now : existing.CreatedTime;
                input.UpdatedTime = DateTime.Now;
                project.FlowRules[index] = input;
            }

            FlowRuleCompiler.SyncCompiledRule(project, input, previousCompiledRuleId);
            Reload(project);
        }

        private void DeleteFlowRule(string idOrName)
        {
            ProjectConfig project = CurrentProject;
            FlowRuleDefinition? existing = FindFlowRule(project, idOrName, idOrName);
            if (existing == null)
                throw new InvalidOperationException("Flow rule was not found: " + idOrName);

            FlowRuleCompiler.RemoveCompiledRule(project, existing);
            project.FlowRules.Remove(existing);
            Reload(project);
        }

        private static ProjectConfig CreateDefaultProject()
        {
            DeviceConfig device = new DeviceConfig
            {
                Name = "虚拟PLC",
                Protocol = PlcProtocol.VirtualPlc,
                DefaultScanRateMs = 1000,
                Connection = new PlcConnectionOptions
                {
                    Protocol = PlcProtocol.VirtualPlc,
                    Host = "default",
                    TimeoutMilliseconds = 3000
                }
            };

            device.Tags.Add(new TagConfig
            {
                DeviceId = device.Id,
                Name = "tag",
                Address = "D100",
                DataType = PlcDataType.Int16,
                MqttPublishEnabled = true,
                PointCode = "virtual.tag",
                Source = "VirtualPlc"
            });

            ProjectConfig project = new ProjectConfig
            {
                Name = "IPC Gateway"
            };
            project.Devices.Add(device);
            return project;
        }

        private static DeviceConfig? FindDevice(ProjectConfig? project, string? id, string? name)
        {
            if (project == null || project.Devices == null)
                return null;
            for (int i = 0; i < project.Devices.Count; i++)
            {
                DeviceConfig device = project.Devices[i];
                if (device == null)
                    continue;
                if ((!string.IsNullOrWhiteSpace(id) && string.Equals(device.Id, id, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(name) && string.Equals(device.Name, name, StringComparison.OrdinalIgnoreCase)))
                    return device;
            }
            return null;
        }

        private static DeviceConfig? FindDeviceByTag(ProjectConfig? project, string? tagId, string? tagName)
        {
            if (project == null || project.Devices == null)
                return null;
            for (int i = 0; i < project.Devices.Count; i++)
            {
                if (FindTag(project.Devices[i].Tags, tagId, tagName) != null)
                    return project.Devices[i];
                for (int g = 0; g < project.Devices[i].Groups.Count; g++)
                {
                    if (FindTag(project.Devices[i].Groups[g].Tags, tagId, tagName) != null)
                        return project.Devices[i];
                }
            }
            return null;
        }

        private static GroupConfig? FindGroup(ProjectConfig? project, string? id, string? name)
        {
            if (project == null || project.Devices == null)
                return null;
            for (int i = 0; i < project.Devices.Count; i++)
            {
                GroupConfig? group = FindGroup(project.Devices[i], id, name);
                if (group != null)
                    return group;
            }
            return null;
        }

        private static GroupConfig? FindGroup(DeviceConfig? device, string? id, string? name)
        {
            if (device == null || device.Groups == null)
                return null;
            for (int i = 0; i < device.Groups.Count; i++)
            {
                GroupConfig group = device.Groups[i];
                if (group == null)
                    continue;
                if ((!string.IsNullOrWhiteSpace(id) && string.Equals(group.Id, id, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(name) && string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase)))
                    return group;
            }
            return null;
        }

        private static TagConfig? FindTag(ProjectConfig? project, string? id, string? name)
        {
            if (project == null || project.Devices == null)
                return null;
            for (int i = 0; i < project.Devices.Count; i++)
            {
                TagConfig? tag = FindTag(project.Devices[i].Tags, id, name);
                if (tag != null)
                    return tag;
                for (int g = 0; g < project.Devices[i].Groups.Count; g++)
                {
                    tag = FindTag(project.Devices[i].Groups[g].Tags, id, name);
                    if (tag != null)
                        return tag;
                }
            }
            return null;
        }

        private static TagConfig? FindTag(IList<TagConfig>? tags, string? id, string? name)
        {
            if (tags == null)
                return null;
            for (int i = 0; i < tags.Count; i++)
            {
                TagConfig tag = tags[i];
                if (tag == null)
                    continue;
                if ((!string.IsNullOrWhiteSpace(id) && string.Equals(tag.Id, id, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(name) && string.Equals(tag.Name, name, StringComparison.OrdinalIgnoreCase)))
                    return tag;
            }
            return null;
        }

        private static EdgeRuleConfig? FindRule(ProjectConfig? project, string? id, string? name)
        {
            if (project == null || project.Rules == null)
                return null;
            for (int i = 0; i < project.Rules.Count; i++)
            {
                EdgeRuleConfig rule = project.Rules[i];
                if (rule == null)
                    continue;
                if ((!string.IsNullOrWhiteSpace(id) && string.Equals(rule.Id, id, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(name) && string.Equals(rule.Name, name, StringComparison.OrdinalIgnoreCase)))
                    return rule;
            }
            return null;
        }

        private static FlowRuleDefinition? FindFlowRule(ProjectConfig? project, string? id, string? name)
        {
            if (project == null || project.FlowRules == null)
                return null;

            for (int i = 0; i < project.FlowRules.Count; i++)
            {
                FlowRuleDefinition rule = project.FlowRules[i];
                if (rule == null)
                    continue;
                if ((!string.IsNullOrWhiteSpace(id) && string.Equals(rule.Id, id, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(name) && string.Equals(rule.Name, name, StringComparison.OrdinalIgnoreCase)))
                    return rule;
            }

            return null;
        }

        private static void ApplyTag(TagConfig target, TagConfig input)
        {
            string deviceId = target.DeviceId;
            string groupId = target.GroupId;
            target.Name = input.Name;
            target.Address = input.Address;
            target.MeterAddress = input.MeterAddress;
            target.MeterDataIdentifier = input.MeterDataIdentifier;
            target.MeterType = input.MeterType;
            target.DataType = input.DataType;
            target.ElementCount = input.ElementCount;
            target.ElementOffset = input.ElementOffset;
            target.Enabled = input.Enabled;
            target.MqttPublishEnabled = input.MqttPublishEnabled;
            target.AccessMode = input.AccessMode;
            target.ScanRateMs = input.ScanRateMs;
            target.Unit = input.Unit;
            target.PointCode = input.PointCode;
            target.AssetPath = input.AssetPath;
            target.BusinessType = input.BusinessType;
            target.Source = input.Source;
            target.Precision = input.Precision;
            target.Scaling = input.Scaling;
            target.Cleaning = input.Cleaning;
            target.Alarm = input.Alarm;
            target.Description = input.Description;
            target.DeviceId = deviceId;
            target.GroupId = groupId;
        }

        private static T DeserializeConfig<T>(JsonElement root, string propertyName)
        {
            JsonElement element;
            if (root.TryGetProperty(propertyName, out element))
            {
                T? result = JsonSerializer.Deserialize<T>(element.GetRawText(), CreateJsonOptions());
                if (result == null)
                    throw new ArgumentException(propertyName + " configuration cannot be empty.");
                return result;
            }

            T? fallback = JsonSerializer.Deserialize<T>(root.GetRawText(), CreateJsonOptions());
            if (fallback == null)
                throw new ArgumentException(propertyName + " configuration cannot be empty.");
            return fallback;
        }

        private static JsonSerializerOptions CreateJsonOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            return options;
        }

        private static string ReadString(JsonElement root, string name)
        {
            JsonElement value;
            if (root.TryGetProperty(name, out value))
                return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
            return string.Empty;
        }

        private static string ReadRequired(JsonElement root, string firstName, string secondName)
        {
            string value = ReadString(root, firstName);
            if (string.IsNullOrWhiteSpace(value))
                value = ReadString(root, secondName);
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(firstName + "不能为空。");
            return value;
        }

        private static int ReadInt(JsonElement root, string name)
        {
            JsonElement value;
            if (root.TryGetProperty(name, out value) && value.TryGetInt32(out int result))
                return result;
            throw new ArgumentException(name + "必须是整数。");
        }

        private static string NormalizeAction(string action)
        {
            return (action ?? string.Empty).Replace("-", string.Empty).Replace("_", string.Empty).Trim().ToLowerInvariant();
        }

        private static void FillProjectCounts(GatewayRuntimeStatus status, ProjectConfig? project)
        {
            if (project == null || project.Devices == null)
                return;

            status.DeviceCount = project.Devices.Count;
            for (int d = 0; d < project.Devices.Count; d++)
            {
                DeviceConfig device = project.Devices[d];
                if (device == null)
                    continue;
                if (device.Enabled)
                    status.EnabledDeviceCount++;
                if (device.Tags != null)
                    status.TagCount += device.Tags.Count;
                if (device.Groups != null)
                {
                    status.GroupCount += device.Groups.Count;
                    for (int g = 0; g < device.Groups.Count; g++)
                    {
                        GroupConfig group = device.Groups[g];
                        if (group != null && group.Tags != null)
                            status.TagCount += group.Tags.Count;
                    }
                }
            }
        }

        private static void FillRuntimeCounts(GatewayRuntimeStatus status, IList<TagValueSnapshot>? snapshots)
        {
            if (snapshots == null)
                return;

            HashSet<string> onlineDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < snapshots.Count; i++)
            {
                TagValueSnapshot snapshot = snapshots[i];
                if (snapshot == null)
                    continue;

                if (snapshot.Quality == TagQuality.Good)
                {
                    status.GoodTagCount++;
                    onlineDevices.Add(string.IsNullOrWhiteSpace(snapshot.DeviceId) ? snapshot.DeviceName : snapshot.DeviceId);
                }
                else if (snapshot.Quality == TagQuality.Unknown)
                {
                    status.NoDataTagCount++;
                }
                else
                {
                    status.BadTagCount++;
                }
            }

            status.OnlineDeviceCount = onlineDevices.Count;
        }
    }
}

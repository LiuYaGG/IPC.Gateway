/*----------------------------------------------------------------
* 项目名称 ：IPC.EdgeGateway
* 项目描述 ：
* 类 名 称 ：FlowRuleEngineService
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
using System;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Core.Resilience;
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;
using IPC.Runtime.Values;

namespace IPC.EdgeGateway
{
    public sealed partial class FlowRuleEngineService : IFlowRuleEngineService, IDisposable
    {
        private readonly object _syncRoot;
        private readonly IRuntimeService _runtime;
        private readonly ProjectConfig _projectConfig;
        private readonly Func<string, string, int, bool> _mqttPublisher;
        private readonly MqttGatewayOptions _gatewayOptions;
        private readonly CircuitBreakerOptions _circuitBreakerOptions;
        private readonly IModelInferenceService _modelInference;
        private readonly TagValueChangedWorker _tagValueWorker;
        private EdgeRuleEngineService _engine;
        private bool _running;
        private bool _disposed;

        public FlowRuleEngineService(
            IRuntimeService runtime,
            ProjectConfig projectConfig,
            Func<string, string, int, bool> mqttPublisher,
            MqttGatewayOptions gatewayOptions)
            : this(runtime, projectConfig, mqttPublisher, gatewayOptions, new GatewayResilienceOptions().RuleEngine)
        {
        }

        public FlowRuleEngineService(
            IRuntimeService runtime,
            ProjectConfig projectConfig,
            Func<string, string, int, bool> mqttPublisher,
            MqttGatewayOptions gatewayOptions,
            CircuitBreakerOptions circuitBreakerOptions,
            IModelInferenceService? modelInference = null)
        {
            _runtime = runtime;
            _projectConfig = projectConfig;
            _mqttPublisher = mqttPublisher;
            _gatewayOptions = gatewayOptions == null ? new MqttGatewayOptions() : gatewayOptions.Clone();
            _circuitBreakerOptions = (circuitBreakerOptions ?? new GatewayResilienceOptions().RuleEngine).Normalize();
            _modelInference = modelInference ?? NoopModelInferenceService.Instance;
            _syncRoot = new object();
            _tagValueWorker = new TagValueChangedWorker(
                "IPC Flow Rule Tag Worker",
                100000,
                ProcessTagValueChanged);
            _engine = CreateInnerEngine();
        }

        public void Start()
        {
            lock (_syncRoot)
            {
                if (_running)
                    return;
                ReplaceInnerEngine();
                _engine.StartDetached();
                _running = true;
            }

            _tagValueWorker.Start();
            if (_runtime != null)
            {
                _runtime.TagValueChanged -= OnTagValueChanged;
                _runtime.TagValueChanged += OnTagValueChanged;
            }
        }

        public void Stop()
        {
            if (_runtime != null)
                _runtime.TagValueChanged -= OnTagValueChanged;
            _tagValueWorker.Stop(TimeSpan.FromSeconds(3));

            lock (_syncRoot)
            {
                _engine.Stop();
                _running = false;
            }
        }

        public EdgeRuleEngineStatus GetStatus()
        {
            _tagValueWorker.Drain(TimeSpan.FromMilliseconds(500));
            lock (_syncRoot)
                return _engine.GetStatus();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Stop();
            _tagValueWorker.Dispose();
            _engine.Dispose();
        }

        private void OnTagValueChanged(object? sender, TagValueChangedEventArgs e)
        {
            if (e == null || e.Snapshot == null)
                return;

            _tagValueWorker.Enqueue(e.Snapshot);
        }

        private void ProcessTagValueChanged(TagValueSnapshot snapshot)
        {
            EdgeRuleEngineService engine;
            lock (_syncRoot)
            {
                if (!_running)
                    return;
                engine = _engine;
            }

            engine.ProcessSnapshot(snapshot);
        }

        private EdgeRuleEngineService CreateInnerEngine()
        {
            return new EdgeRuleEngineService(_runtime, BuildRuntimeProject(), _mqttPublisher, _gatewayOptions, _circuitBreakerOptions, _modelInference);
        }

        private void ReplaceInnerEngine()
        {
            EdgeRuleEngineService oldEngine = _engine;
            _engine = CreateInnerEngine();
            oldEngine.Dispose();
        }
    }
}

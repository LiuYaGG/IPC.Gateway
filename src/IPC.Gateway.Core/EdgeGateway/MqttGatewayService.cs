/*----------------------------------------------------------------
* 项目名称 ：IPC.EdgeGateway
* 项目描述 ：
* 类 名 称 ：MqttGatewayService
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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Core.Resilience;
using IPC.Runtime.Api;
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;
using IPC.Runtime.Values;
using IPC.Gateway.Mqtt.Sparkplug;

namespace IPC.EdgeGateway
{
    
    
    
    
    
    
    
    
    
    public sealed partial class MqttGatewayService : IDisposable
    {
        private readonly object _syncRoot;
        private readonly IRuntimeService _runtime;
        private readonly MqttGatewayOptions _options;
        private readonly LocalHistoryService _history;
        private readonly Action<string, string>? _remoteConfigurationHandler;
        private readonly MqttGatewayStatus _status;
        private readonly CircuitBreaker _circuitBreaker;
        private readonly MqttOutboxStore _outboxStore;
        private readonly TagValueChangedWorker _tagValueWorker;
        private readonly Dictionary<string, MqttPublishValueState> _lastPublishedValues;
        private readonly Dictionary<string, string> _lastAlarmStates;
        private ManualResetEvent? _stopEvent;
        private Thread? _workerThread;
        private SimpleMqttClient? _currentClient;
        private DateTime _nextFlushUtc;
        private DateTime _nextHeartbeatUtc;
        private DateTime _nextStatusUtc;
        private DateTime _lastOutboxCleanupUtc;
        private DateTime _lastOutboxStatusRefreshUtc;
        private int _flushFailureCount;
        private int _currentSparkplugBirthSequence;
        private uint _sparkplugPayloadSequence;
        private bool _disposed;

        public MqttGatewayService(IRuntimeService runtime, MqttGatewayOptions options, LocalHistoryService history)
            : this(runtime, options, history, null, new GatewayResilienceOptions().Mqtt)
        {
        }

        public MqttGatewayService(IRuntimeService runtime, MqttGatewayOptions options, LocalHistoryService history, Action<string, string>? remoteConfigurationHandler)
            : this(runtime, options, history, remoteConfigurationHandler, new GatewayResilienceOptions().Mqtt)
        {
        }

        public MqttGatewayService(
            IRuntimeService runtime,
            MqttGatewayOptions options,
            LocalHistoryService history,
            Action<string, string>? remoteConfigurationHandler,
            CircuitBreakerOptions circuitBreakerOptions)
        {
            _runtime = runtime;
            _options = options == null ? new MqttGatewayOptions() : options.Clone();
            _history = history;
            _remoteConfigurationHandler = remoteConfigurationHandler;
            _status = new MqttGatewayStatus();
            _circuitBreaker = new CircuitBreaker("MQTT", circuitBreakerOptions ?? new GatewayResilienceOptions().Mqtt);
            _syncRoot = new object();
            _lastPublishedValues = new Dictionary<string, MqttPublishValueState>(StringComparer.Ordinal);
            _lastAlarmStates = new Dictionary<string, string>(StringComparer.Ordinal);
            _outboxStore = new MqttOutboxStore(ResolveOutboxDirectory(_options.OutboxDirectory));
            _tagValueWorker = new TagValueChangedWorker(
                "IPC MQTT Tag Worker",
                100000,
                ProcessRuntimeTagValueChanged,
                ex => IpcLogService.WriteError("MQTT tag queue failed.", ex));
            UpdateStatus(delegate(MqttGatewayStatus status)
            {
                status.Enabled = _options.Enabled;
                ApplyIdentityStatus(status);
                status.Broker = _options.BrokerAddress;
                status.SubscribeTopic = _options.SubscribeTopic;
                status.PublishEnabled = _options.PublishEnabled;
                status.PublishTopicTemplate = _options.PublishTopicTemplate;
                status.PublishQos = MqttGatewayOptions.ClampQos(_options.PublishQos);
                status.HeartbeatTopic = ResolveTopicTemplate(_options.HeartbeatTopic, string.Empty);
                status.StatusTopic = ResolveTopicTemplate(_options.StatusTopic, string.Empty);
                status.CommandReplyTopicTemplate = ResolveTopicTemplate(_options.CommandReplyTopicTemplate, "{requestId}");
                status.OutboxDirectory = _outboxStore.DirectoryPath;
                status.OutboxQuarantineDirectory = _outboxStore.QuarantineDirectoryPath;
                status.CircuitBreaker = _circuitBreaker.Snapshot();
                UpdateOutboxStatus(status);
            });
        }

        public bool IsRunning
        {
            get
            {
                lock (_syncRoot)
                    return _workerThread != null && _workerThread.IsAlive;
            }
        }

        public MqttGatewayStatus GetStatus()
        {
            lock (_syncRoot)
            {
                MqttGatewayStatus status = _status.Clone();
                status.CircuitBreaker = _circuitBreaker.Snapshot();
                return status;
            }
        }

        public bool QueueCustomPublish(string topic, string payload, int qos)
        {
            if (string.IsNullOrWhiteSpace(topic))
                return false;

            try
            {
                string normalizedTopic = topic.Trim('/');
                int normalizedQos = MqttGatewayOptions.ClampQos(qos);
                _outboxStore.Enqueue(normalizedTopic, payload ?? string.Empty, normalizedQos);
                RecordPublish("custom", normalizedTopic, normalizedQos, payload ?? string.Empty);
                MqttOutboxCleanupResult cleanup = CleanupOutbox();
                UpdateStatus(delegate(MqttGatewayStatus status)
                {
                    status.OutboxEnqueuedCount++;
                    ApplyOutboxCleanupResult(status, cleanup);
                    UpdateOutboxStatus(status);
                    status.LastPublishTime = DateTime.Now;
                    status.LastPublishResult = "Queued: " + normalizedTopic;
                    status.LastError = string.Empty;
                });
                return true;
            }
            catch (Exception ex)
            {
                _circuitBreaker.RecordFailure(ex.Message);
                UpdateStatus(delegate(MqttGatewayStatus status)
                {
                    status.FailedPublishes++;
                    status.LastPublishResult = "Failed: " + ex.Message;
                    status.LastError = ex.Message;
                });
                return false;
            }
        }

        public void Start()
        {
            if (!_options.Enabled)
                return;
            if (_runtime == null)
                throw new InvalidOperationException("Runtime service is not available.");

            lock (_syncRoot)
            {
                if (_workerThread != null && _workerThread.IsAlive)
                    return;

                _stopEvent = new ManualResetEvent(false);
                _workerThread = new Thread(WorkerLoop);
                _workerThread.IsBackground = true;
                _workerThread.Name = "IPC MQTT Gateway";
                _workerThread.Start();
                _tagValueWorker.Start();

                _runtime.TagValueChanged -= OnRuntimeTagValueChanged;
                _runtime.TagValueChanged += OnRuntimeTagValueChanged;
                _status.IsRunning = true;
                _status.Enabled = true;
                ApplyIdentityStatus(_status);
                _status.Broker = _options.BrokerAddress;
                _status.SubscribeTopic = _options.SubscribeTopic;
                _status.PublishEnabled = _options.PublishEnabled;
                _status.PublishTopicTemplate = _options.PublishTopicTemplate;
                _status.PublishQos = MqttGatewayOptions.ClampQos(_options.PublishQos);
                _status.HeartbeatTopic = ResolveTopicTemplate(_options.HeartbeatTopic, string.Empty);
                _status.StatusTopic = ResolveTopicTemplate(_options.StatusTopic, string.Empty);
                _status.CommandReplyTopicTemplate = ResolveTopicTemplate(_options.CommandReplyTopicTemplate, "{requestId}");
                _status.OutboxDirectory = _outboxStore.DirectoryPath;
                _status.OutboxQuarantineDirectory = _outboxStore.QuarantineDirectoryPath;
                UpdateOutboxStatus(_status);
                _status.LastError = string.Empty;
            }
        }

        public void Stop()
        {
            if (_runtime != null)
                _runtime.TagValueChanged -= OnRuntimeTagValueChanged;
            _tagValueWorker.Stop(TimeSpan.FromSeconds(3));

            SimpleMqttClient? connectedClient;
            lock (_syncRoot)
                connectedClient = _currentClient;
            TryQueueSparkplugDeath();
            if (connectedClient != null && connectedClient.IsConnected)
                FlushOutbox(connectedClient);

            ManualResetEvent? stopEvent;
            Thread? thread;
            lock (_syncRoot)
            {
                stopEvent = _stopEvent;
                thread = _workerThread;
                _stopEvent = null;
                _workerThread = null;
                _currentClient = null;
                _status.IsRunning = false;
                _status.IsConnected = false;
            }

            if (stopEvent != null)
                stopEvent.Set();
            if (thread != null && thread.IsAlive)
                thread.Join(3000);
            if (stopEvent != null)
                stopEvent.Close();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Stop();
        }

        private void WorkerLoop()
        {
            while (true)
            {
                ManualResetEvent? stopEvent;
                lock (_syncRoot)
                    stopEvent = _stopEvent;
                if (stopEvent == null || stopEvent.WaitOne(0))
                    break;

                if (!_circuitBreaker.CanExecute())
                {
                    UpdateStatus(delegate(MqttGatewayStatus status)
                    {
                        status.IsConnected = false;
                        status.LastError = "MQTT circuit breaker is open; degraded to outbox-only mode.";
                    });
                    if (stopEvent.WaitOne(TimeSpan.FromSeconds(MqttGatewayOptions.ClampReconnectSeconds(_options.ReconnectSeconds))))
                        break;
                    continue;
                }

                using (SimpleMqttClient client = new SimpleMqttClient(_options))
                {
                    client.Connected += OnClientConnected;
                    client.Disconnected += OnClientDisconnected;
                    client.MessageReceived += OnMessageReceived;

                    try
                    {
                        int birthSequence = BeginSparkplugSession();
                        MqttWillMessage? willMessage = BuildSparkplugNodeDeathWill(birthSequence);
                        client.ConnectAndReadLoop(BuildSubscribeTopics(), stopEvent, OnClientIdle, willMessage);
                    }
                    catch (Exception ex)
                    {
                        _circuitBreaker.RecordFailure(ex.Message);
                        UpdateStatus(delegate(MqttGatewayStatus status)
                        {
                            status.IsConnected = false;
                            status.LastError = ex.Message;
                        });
                    }
                    finally
                    {
                        try
                        {
                            client.Disconnect();
                        }
                        catch
                        {
                        }

                        lock (_syncRoot)
                        {
                            if (object.ReferenceEquals(_currentClient, client))
                                _currentClient = null;
                        }
                    }
                }

                lock (_syncRoot)
                {
                    if (_stopEvent == null)
                        break;
                    _status.ReconnectCount++;
                }

                if (stopEvent.WaitOne(TimeSpan.FromSeconds(MqttGatewayOptions.ClampReconnectSeconds(_options.ReconnectSeconds))))
                    break;
            }

            UpdateStatus(delegate(MqttGatewayStatus status)
            {
                status.IsRunning = false;
                status.IsConnected = false;
            });
        }

        private void OnClientConnected(object? sender, EventArgs e)
        {
            _circuitBreaker.RecordSuccess();
            UpdateStatus(delegate(MqttGatewayStatus status)
            {
                _currentClient = sender as SimpleMqttClient;
                status.IsConnected = true;
                status.LastConnectedTime = DateTime.Now;
                status.LastError = string.Empty;
            });

            TryQueueSparkplugBirth();
        }

        private void OnClientDisconnected(object? sender, EventArgs e)
        {
            UpdateStatus(delegate(MqttGatewayStatus status)
            {
                if (object.ReferenceEquals(_currentClient, sender))
                    _currentClient = null;
                status.IsConnected = false;
            });
        }

        private void OnMessageReceived(object? sender, MqttMessageEventArgs e)
        {
            UpdateStatus(delegate(MqttGatewayStatus status)
            {
                status.ReceivedCount++;
                status.LastMessageTime = DateTime.Now;
                status.LastMessage = e.Topic + " = " + e.Payload;
            });

            try
            {
                if (TryHandleSparkplugMessage(e))
                    return;

                if (IsRemoteConfigurationMessage(e.Topic, e.Payload))
                {
                    if (_remoteConfigurationHandler == null)
                    {
                        MarkWriteFailed("Remote configuration handler is not available.");
                        return;
                    }

                    _remoteConfigurationHandler(e.Topic, e.Payload);
                    UpdateStatus(delegate(MqttGatewayStatus status)
                    {
                        status.SuccessfulWrites++;
                        status.LastWriteResult = "Remote configuration queued.";
                        status.LastError = string.Empty;
                    });
                    return;
                }

                WriteTagRequest request;
                string parseError;
                if (!TryBuildWriteRequest(e.Topic, e.Payload, out request, out parseError))
                {
                    MarkWriteFailed(parseError);
                    return;
                }

                WriteTagResponse response = _runtime.WriteTag(request);
                if (response != null && response.Success)
                {
                    UpdateStatus(delegate(MqttGatewayStatus status)
                    {
                        status.SuccessfulWrites++;
                        status.LastWriteResult = "OK: " + request.DeviceName + "/" + request.GroupName + "/" + request.TagName;
                        status.LastError = string.Empty;
                    });
                }
                else
                {
                    MarkWriteFailed(response == null ? "Write failed." : response.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                MarkWriteFailed(ex.Message);
            }
        }

        private bool IsRemoteConfigurationMessage(string topic, string payload)
        {
            string relativeTopic;
            if (TryGetRelativeTopic(topic, out relativeTopic))
            {
                string normalized = relativeTopic.Trim('/').ToLowerInvariant();
                if (normalized == "_config" ||
                    normalized == "config" ||
                    normalized.StartsWith("_config/", StringComparison.Ordinal) ||
                    normalized.StartsWith("config/", StringComparison.Ordinal))
                    return true;
            }

            return IsRemoteConfigurationPayload(payload);
        }

        private static bool IsRemoteConfigurationPayload(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return false;

            string text = payload.Trim();
            if (!text.StartsWith("{", StringComparison.Ordinal) || !text.EndsWith("}", StringComparison.Ordinal))
                return false;

            string action = ExtractJsonValue(text, "action");
            if (string.IsNullOrWhiteSpace(action))
                return false;

            string normalized = action.Replace("-", string.Empty).Replace("_", string.Empty).Trim();
            string[] actions = new[]
            {
                "apply",
                "applyconfig",
                "rollback",
                "rollbackconfig",
                "rollbackconfiguration",
                "selfcheck",
                "gatewayselfcheck",
                "getdriverplugins",
                "driverplugins",
                "reloaddriverplugins",
                "refreshdriverplugins",
                "reloadplugins",
                "replaceall",
                "applypackage",
                "applygatewaypackage",
                "replaceproject",
                "applyproject",
                "replacedevices",
                "upsertdevice",
                "deletedevice",
                "replacegroups",
                "upsertgroup",
                "deletegroup",
                "replacetags",
                "upserttag",
                "deletetag",
                "replacerules",
                "upsertrule",
                "deleterule",
                "updatemqtt",
                "applymqtt",
                "applysettings",
                "updatesettings"
            };

            for (int i = 0; i < actions.Length; i++)
            {
                if (string.Equals(actions[i], normalized, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private void OnRuntimeTagValueChanged(object? sender, TagValueChangedEventArgs e)
        {
            if (e == null || e.Snapshot == null)
                return;

            _tagValueWorker.Enqueue(e.Snapshot);
        }

        private void ProcessRuntimeTagValueChanged(TagValueSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            TryQueueAlarmPublish(snapshot);

            if (!_options.PublishEnabled)
                return;
            if (_options.PublishSelectedTagsOnly && !snapshot.MqttPublishEnabled)
                return;

            try
            {
                string stateKey;
                string stateSignature;
                DateTime publishUtc = DateTime.UtcNow;
                if (!ShouldQueuePublish(snapshot, publishUtc, out stateKey, out stateSignature))
                    return;

                string topic;
                if (IsSparkplugMode())
                {
                    byte[] payload = BuildSparkplugDataPayload(snapshot);
                    topic = BuildSparkplugDeviceDataTopic(snapshot);
                    _outboxStore.Enqueue(topic, payload, _options.PublishQos);
                    RecordPublish("sparkplugData", topic, _options.PublishQos, payload);
                }
                else
                {
                    string payload = BuildPublishPayload(snapshot);
                    topic = BuildPublishTopic(snapshot);
                    _outboxStore.Enqueue(topic, payload, _options.PublishQos);
                    RecordPublish("value", topic, _options.PublishQos, payload);
                }
                MarkPublishQueued(stateKey, stateSignature, publishUtc);
                MqttOutboxCleanupResult cleanup = CleanupOutbox();
                UpdateStatus(delegate(MqttGatewayStatus status)
                {
                    status.OutboxEnqueuedCount++;
                    if (IsSparkplugMode())
                        status.SparkplugDataCount++;
                    ApplyOutboxCleanupResult(status, cleanup);
                    UpdateOutboxStatus(status);
                    status.LastPublishTime = DateTime.Now;
                    status.LastPublishResult = "Queued: " + topic;
                    status.LastError = string.Empty;
                });
            }
            catch (Exception ex)
            {
                _circuitBreaker.RecordFailure(ex.Message);
                UpdateStatus(delegate(MqttGatewayStatus status)
                {
                    status.FailedPublishes++;
                    status.LastPublishResult = "Failed: " + ex.Message;
                    status.LastError = ex.Message;
                });
            }
        }

        private void TryQueueAlarmPublish(TagValueSnapshot snapshot)
        {
            MqttAlarmEvaluation evaluation;
            if (!TryEvaluateAlarm(snapshot, out evaluation))
                return;

            string stateKey = BuildPublishStateKey(snapshot);
            string stateSignature = evaluation.State + "\u001F" + evaluation.Value.ToString("R", CultureInfo.InvariantCulture);
            lock (_syncRoot)
            {
                string? lastState;
                if (_lastAlarmStates.TryGetValue(stateKey, out lastState) &&
                    string.Equals(lastState, stateSignature, StringComparison.Ordinal))
                {
                    return;
                }

                _lastAlarmStates[stateKey] = stateSignature;
            }

            if (evaluation.IsNormal)
                return;

            try
            {
                string topic = BuildAlarmTopic(snapshot, evaluation);
                string payload = BuildAlarmPayload(snapshot, evaluation);
                _outboxStore.Enqueue(topic, payload, _options.PublishQos);
                RecordAlarm(snapshot, evaluation, topic);
                RecordPublish(evaluation.EventType, topic, _options.PublishQos, payload);
                MqttOutboxCleanupResult cleanup = CleanupOutbox();
                UpdateStatus(delegate(MqttGatewayStatus status)
                {
                    status.OutboxEnqueuedCount++;
                    ApplyOutboxCleanupResult(status, cleanup);
                    UpdateOutboxStatus(status);
                    status.LastPublishTime = DateTime.Now;
                    status.LastPublishResult = "Queued: " + topic;
                    status.LastError = string.Empty;
                });
            }
            catch (Exception ex)
            {
                _circuitBreaker.RecordFailure(ex.Message);
                UpdateStatus(delegate(MqttGatewayStatus status)
                {
                    status.FailedPublishes++;
                    status.LastPublishResult = "Failed: " + ex.Message;
                    status.LastError = ex.Message;
                });
            }
        }

        private static bool TryEvaluateAlarm(TagValueSnapshot snapshot, out MqttAlarmEvaluation evaluation)
        {
            evaluation = MqttAlarmEvaluation.Normal();
            if (snapshot == null || snapshot.Quality != TagQuality.Good)
                return false;

            TagAlarmConfig alarm = snapshot.Alarm;
            if (alarm == null || !alarm.Enabled)
                return false;

            double value;
            if (!TryGetNumericValue(snapshot, out value))
                return false;

            if (alarm.LowLimit > alarm.HighLimit)
                return false;

            if (value > alarm.HighLimit)
            {
                evaluation = MqttAlarmEvaluation.Active("HighAlarm", "alarm", "High", value, alarm.HighLimit, alarm.HighAlarmMessage, "Value is above high alarm limit.");
                return true;
            }

            if (value < alarm.LowLimit)
            {
                evaluation = MqttAlarmEvaluation.Active("LowAlarm", "alarm", "Low", value, alarm.LowLimit, alarm.LowAlarmMessage, "Value is below low alarm limit.");
                return true;
            }

            double deviation = Math.Max(0D, alarm.WarningDeviation);
            if (deviation > 0D && value >= alarm.HighLimit - deviation)
            {
                evaluation = MqttAlarmEvaluation.Active("HighWarning", "warning", "High", value, alarm.HighLimit - deviation, alarm.HighWarningMessage, "Value reached high warning deviation.");
                return true;
            }

            if (deviation > 0D && value <= alarm.LowLimit + deviation)
            {
                evaluation = MqttAlarmEvaluation.Active("LowWarning", "warning", "Low", value, alarm.LowLimit + deviation, alarm.LowWarningMessage, "Value reached low warning deviation.");
                return true;
            }

            evaluation = MqttAlarmEvaluation.Normal(value);
            return true;
        }

        private static bool TryGetNumericValue(TagValueSnapshot snapshot, out double value)
        {
            value = 0D;
            if (snapshot == null)
                return false;

            object current = snapshot.Value;
            if (current is byte || current is sbyte || current is short || current is ushort ||
                current is int || current is uint || current is long || current is ulong ||
                current is float || current is double || current is decimal)
            {
                value = Convert.ToDouble(current, CultureInfo.InvariantCulture);
                return true;
            }

            string text = snapshot.ValueText;
            if (string.IsNullOrWhiteSpace(text) && current != null)
                text = Convert.ToString(current, CultureInfo.InvariantCulture) ?? string.Empty;

            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                   double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private bool ShouldQueuePublish(TagValueSnapshot snapshot, DateTime publishUtc, out string stateKey, out string stateSignature)
        {
            stateKey = string.Empty;
            stateSignature = string.Empty;

            if (!_options.PublishChangedOnly)
                return true;

            stateKey = BuildPublishStateKey(snapshot);
            stateSignature = BuildPublishStateSignature(snapshot);

            lock (_syncRoot)
            {
                MqttPublishValueState? state;
                if (!_lastPublishedValues.TryGetValue(stateKey, out state) || state == null)
                    return true;

                if (!string.Equals(state.Signature, stateSignature, StringComparison.Ordinal))
                    return true;

                int heartbeatSeconds = MqttGatewayOptions.ClampPublishUnchangedHeartbeatSeconds(_options.PublishUnchangedHeartbeatSeconds);
                if (heartbeatSeconds <= 0)
                    return false;

                return publishUtc - state.LastQueuedUtc >= TimeSpan.FromSeconds(heartbeatSeconds);
            }
        }

        private void MarkPublishQueued(string stateKey, string stateSignature, DateTime publishUtc)
        {
            if (!_options.PublishChangedOnly || string.IsNullOrEmpty(stateKey))
                return;

            lock (_syncRoot)
            {
                _lastPublishedValues[stateKey] = new MqttPublishValueState(stateSignature, publishUtc);
            }
        }

        private void FlushOutbox(SimpleMqttClient client)
        {
            if (client == null || !client.IsConnected)
                return;

            if (DateTime.UtcNow < _nextFlushUtc)
                return;

            while (client.IsConnected)
            {
                MqttOutboxCleanupResult cleanup = CleanupOutbox();
                int batchSize = MqttGatewayOptions.ClampPublishFlushBatchSize(_options.PublishFlushBatchSize);
                IList<MqttOutboxEntry> entries = _outboxStore.ListPending(batchSize);
                UpdateStatus(delegate(MqttGatewayStatus status)
                {
                    ApplyOutboxCleanupResult(status, cleanup);
                    UpdateOutboxStatus(status);
                });

                if (entries.Count == 0)
                    return;

                bool stopFlush = false;
                for (int i = 0; i < entries.Count; i++)
                {
                    MqttOutboxEntry entry = entries[i];
                    MqttPublishResult result = client.Publish(
                        entry.Message.Topic,
                        entry.Message.GetPayloadBytes(),
                        entry.Message.Qos,
                        MqttGatewayOptions.ClampAckTimeoutMilliseconds(_options.PublishAckTimeoutMilliseconds));

                    if (result.Success)
                    {
                        _circuitBreaker.RecordSuccess();
                        _outboxStore.Delete(entry);
                        UpdateStatus(delegate(MqttGatewayStatus status)
                        {
                            status.PublishedCount++;
                            UpdateOutboxStatus(status);
                            status.LastPublishTime = DateTime.Now;
                            status.LastPublishResult = "OK: " + entry.Message.Topic;
                            status.LastError = string.Empty;
                            status.PublishRetryBackoffSeconds = 0;
                            status.PublishConsecutiveFailureCount = 0;
                            status.NextPublishRetryTime = DateTime.MinValue;
                        });
                        _flushFailureCount = 0;
                        _nextFlushUtc = DateTime.MinValue;
                    }
                    else
                    {
                        _circuitBreaker.RecordFailure(result.ErrorMessage);
                        int backoffSeconds = RegisterFlushFailure();
                        UpdateStatus(delegate(MqttGatewayStatus status)
                        {
                            status.FailedPublishes++;
                            UpdateOutboxStatus(status);
                            status.PublishRetryBackoffSeconds = backoffSeconds;
                            status.PublishConsecutiveFailureCount = _flushFailureCount;
                            status.LastPublishFailureTime = DateTime.Now;
                            status.NextPublishRetryTime = ToLocalTime(_nextFlushUtc);
                            status.LastPublishResult = "Failed: " + result.ErrorMessage;
                            status.LastError = result.ErrorMessage;
                        });
                        stopFlush = true;
                        break;
                    }
                }

                if (stopFlush)
                    return;

                if (entries.Count < batchSize)
                    return;
            }
        }

        private void OnClientIdle(SimpleMqttClient client)
        {
            TryQueueHeartbeat();
            TryQueueGatewayStatus();
            FlushOutbox(client);
        }

        private void TryQueueHeartbeat()
        {
            if (!_options.HeartbeatEnabled)
                return;

            int intervalSeconds = MqttGatewayOptions.ClampHeartbeatIntervalSeconds(_options.HeartbeatIntervalSeconds);
            DateTime nowUtc = DateTime.UtcNow;
            if (_nextHeartbeatUtc == DateTime.MinValue)
                _nextHeartbeatUtc = nowUtc;
            if (nowUtc < _nextHeartbeatUtc)
                return;

            try
            {
                string topic;
                if (IsSparkplugMode())
                {
                    topic = CreateSparkplugTopicBuilder().NodeData();
                    byte[] payload = BuildSparkplugNodeDataPayload(nowUtc);
                    _outboxStore.Enqueue(topic, payload, MqttGatewayOptions.ClampQos(_options.HeartbeatQos));
                    RecordPublish("sparkplugNodeData", topic, MqttGatewayOptions.ClampQos(_options.HeartbeatQos), payload);
                }
                else
                {
                    topic = ResolveTopicTemplate(string.IsNullOrWhiteSpace(_options.HeartbeatTopic) ? "gateway/{gatewayId}/heartbeat" : _options.HeartbeatTopic, string.Empty);
                    string payload = BuildHeartbeatPayload(nowUtc);
                    _outboxStore.Enqueue(topic, payload, MqttGatewayOptions.ClampQos(_options.HeartbeatQos));
                    RecordPublish("heartbeat", topic, MqttGatewayOptions.ClampQos(_options.HeartbeatQos), payload);
                }
                _nextHeartbeatUtc = nowUtc.AddSeconds(intervalSeconds);
                MqttOutboxCleanupResult cleanup = CleanupOutbox();
                UpdateStatus(delegate(MqttGatewayStatus status)
                {
                    status.OutboxEnqueuedCount++;
                    ApplyOutboxCleanupResult(status, cleanup);
                    UpdateOutboxStatus(status);
                    status.LastPublishTime = DateTime.Now;
                    status.LastPublishResult = "Queued: " + topic;
                    status.LastError = string.Empty;
                });
            }
            catch (Exception ex)
            {
                _circuitBreaker.RecordFailure(ex.Message);
                UpdateStatus(delegate(MqttGatewayStatus status)
                {
                    status.FailedPublishes++;
                    status.LastPublishResult = "Failed: " + ex.Message;
                    status.LastError = ex.Message;
                });
            }
        }

        private void TryQueueGatewayStatus()
        {
            if (!_options.HeartbeatEnabled)
                return;
            if (IsSparkplugMode())
                return;

            string topic = ResolveTopicTemplate(string.IsNullOrWhiteSpace(_options.StatusTopic) ? "gateway/{gatewayId}/status" : _options.StatusTopic, string.Empty);
            string heartbeatTopic = ResolveTopicTemplate(string.IsNullOrWhiteSpace(_options.HeartbeatTopic) ? "gateway/{gatewayId}/heartbeat" : _options.HeartbeatTopic, string.Empty);
            if (string.Equals(topic, heartbeatTopic, StringComparison.OrdinalIgnoreCase))
                return;

            int intervalSeconds = MqttGatewayOptions.ClampHeartbeatIntervalSeconds(_options.HeartbeatIntervalSeconds);
            DateTime nowUtc = DateTime.UtcNow;
            if (_nextStatusUtc == DateTime.MinValue)
                _nextStatusUtc = nowUtc;
            if (nowUtc < _nextStatusUtc)
                return;

            try
            {
                string payload = BuildGatewayStatusPayload(nowUtc);
                _outboxStore.Enqueue(topic, payload, MqttGatewayOptions.ClampQos(_options.HeartbeatQos));
                RecordPublish("gatewayStatus", topic, MqttGatewayOptions.ClampQos(_options.HeartbeatQos), payload);
                _nextStatusUtc = nowUtc.AddSeconds(intervalSeconds);
                MqttOutboxCleanupResult cleanup = CleanupOutbox();
                UpdateStatus(delegate(MqttGatewayStatus status)
                {
                    status.OutboxEnqueuedCount++;
                    ApplyOutboxCleanupResult(status, cleanup);
                    UpdateOutboxStatus(status);
                    status.LastPublishTime = DateTime.Now;
                    status.LastPublishResult = "Queued: " + topic;
                    status.LastError = string.Empty;
                });
            }
            catch (Exception ex)
            {
                _circuitBreaker.RecordFailure(ex.Message);
                UpdateStatus(delegate(MqttGatewayStatus status)
                {
                    status.FailedPublishes++;
                    status.LastPublishResult = "Failed: " + ex.Message;
                    status.LastError = ex.Message;
                });
            }
        }

        private bool TryBuildWriteRequest(string topic, string payload, out WriteTagRequest request, out string error)
        {
            request = new WriteTagRequest();
            error = string.Empty;

            string relativeTopic;
            if (!TryGetRelativeTopic(topic, out relativeTopic))
            {
                error = "MQTT topic does not match write topic prefix.";
                return false;
            }

            string[] parts = relativeTopic.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 &&
                (string.Equals(parts[0], "write", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(parts[0], "_write", StringComparison.OrdinalIgnoreCase)))
            {
                string[] trimmed = new string[parts.Length - 1];
                Array.Copy(parts, 1, trimmed, 0, trimmed.Length);
                parts = trimmed;
            }

            if (parts.Length != 3 && parts.Length != 4)
            {
                error = "MQTT write topic must be {prefix}/{channelId}/{deviceId}/{tagId} or {prefix}/{channelId}/{deviceId}/{groupId}/{tagId}.";
                return false;
            }

            string channelId = parts[0];
            string deviceId = parts[1];
            string groupId = parts.Length == 4 ? parts[2] : string.Empty;
            string tagId = parts.Length == 4 ? parts[3] : parts[2];
            string valueText;
            string dataType;
            ExtractPayload(payload, out valueText, out dataType);

            TagValueSnapshot? snapshot;
            if (!_runtime.TryGetSnapshotById(channelId, deviceId, groupId, tagId, out snapshot) || snapshot == null)
            {
                error = "Tag was not found for the supplied channel/device/group/tag IDs.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(dataType))
                dataType = snapshot.DataType;

            request = new WriteTagRequest
            {
                ChannelId = channelId,
                ChannelName = snapshot.ChannelName,
                DeviceId = deviceId,
                GroupId = groupId,
                TagId = tagId,
                DeviceName = snapshot.DeviceName,
                GroupName = snapshot.GroupName,
                TagName = snapshot.TagName,
                DataType = dataType,
                ValueText = valueText,
                Value = valueText
            };
            return true;
        }

        private bool TryGetRelativeTopic(string topic, out string relativeTopic)
        {
            relativeTopic = string.Empty;
            if (string.IsNullOrWhiteSpace(topic))
                return false;

            string prefix = GetWriteTopicPrefix(_options.SubscribeTopic);
            if (!topic.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            relativeTopic = topic.Substring(prefix.Length).Trim('/');
            return !string.IsNullOrWhiteSpace(relativeTopic);
        }

        private static string GetWriteTopicPrefix(string subscribeTopic)
        {
            string topic = string.IsNullOrWhiteSpace(subscribeTopic) ? "ipc/write/#" : subscribeTopic.Trim();
            int hashIndex = topic.IndexOf('#');
            if (hashIndex >= 0)
                topic = topic.Substring(0, hashIndex);
            int plusIndex = topic.IndexOf('+');
            if (plusIndex >= 0)
                topic = topic.Substring(0, plusIndex);
            topic = topic.Trim('/');
            if (topic.Length == 0)
                return string.Empty;
            return topic + "/";
        }

        private static void ExtractPayload(string payload, out string valueText, out string dataType)
        {
            valueText = payload == null ? string.Empty : payload.Trim();
            dataType = string.Empty;

            string text = valueText;
            if (text.StartsWith("{", StringComparison.Ordinal) && text.EndsWith("}", StringComparison.Ordinal))
            {
                string value = ExtractJsonValue(text, "valueText");
                if (string.IsNullOrEmpty(value))
                    value = ExtractJsonValue(text, "value");
                string type = ExtractJsonValue(text, "dataType");
                if (string.IsNullOrEmpty(type))
                    type = ExtractJsonValue(text, "DataType");
                if (!string.IsNullOrEmpty(value))
                    valueText = value;
                if (!string.IsNullOrEmpty(type))
                    dataType = type;
                return;
            }

            int colonIndex = text.IndexOf(':');
            if (colonIndex > 0 && colonIndex < text.Length - 1)
            {
                string possibleType = text.Substring(0, colonIndex).Trim();
                if (IsLikelyDataType(possibleType))
                {
                    dataType = possibleType;
                    valueText = text.Substring(colonIndex + 1).Trim();
                }
            }
        }

        private static string ExtractJsonValue(string json, string name)
        {
            string quoted = "\"" + name + "\"";
            int nameIndex = json.IndexOf(quoted, StringComparison.OrdinalIgnoreCase);
            if (nameIndex < 0)
                return string.Empty;

            int colonIndex = json.IndexOf(':', nameIndex + quoted.Length);
            if (colonIndex < 0)
                return string.Empty;

            int valueStart = colonIndex + 1;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
                valueStart++;
            if (valueStart >= json.Length)
                return string.Empty;

            if (json[valueStart] == '"')
            {
                valueStart++;
                int valueEnd = valueStart;
                while (valueEnd < json.Length)
                {
                    if (json[valueEnd] == '"' && (valueEnd == valueStart || json[valueEnd - 1] != '\\'))
                        break;
                    valueEnd++;
                }
                if (valueEnd <= json.Length)
                    return json.Substring(valueStart, valueEnd - valueStart).Replace("\\\"", "\"").Replace("\\\\", "\\");
                return string.Empty;
            }

            int end = valueStart;
            while (end < json.Length && json[end] != ',' && json[end] != '}')
                end++;
            return json.Substring(valueStart, end - valueStart).Trim();
        }

        private static bool IsLikelyDataType(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string normalized = value.Trim();
            string[] names = new[]
            {
                "Bool", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64", "String", "Float", "Double",
                "BoolArray", "Int16Array", "UInt16Array", "Int32Array", "UInt32Array", "Int64Array", "UInt64Array",
                "FloatArray", "DoubleArray", "Coil", "CoilArray", "DiscreteInput", "DiscreteInputArray"
            };

            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], normalized, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string BuildPublishStateKey(TagValueSnapshot snapshot)
        {
            return NormalizeStatePart(snapshot.ChannelId) + "\u001F" +
                   NormalizeStatePart(snapshot.DeviceId) + "\u001F" +
                   NormalizeStatePart(snapshot.GroupId) + "\u001F" +
                   NormalizeStatePart(snapshot.TagId);
        }

        private static string BuildPublishStateSignature(TagValueSnapshot snapshot)
        {
            return NormalizeStatePart(snapshot.ValueText) + "\u001F" +
                   NormalizeStatePart(snapshot.RawValueText) + "\u001F" +
                   NormalizeStatePart(snapshot.DataType) + "\u001F" +
                   NormalizeStatePart(snapshot.Unit) + "\u001F" +
                   NormalizeStatePart(GetPointCode(snapshot)) + "\u001F" +
                   NormalizeStatePart(snapshot.AssetPath) + "\u001F" +
                   NormalizeStatePart(snapshot.BusinessType) + "\u001F" +
                   NormalizeStatePart(snapshot.Source) + "\u001F" +
                   snapshot.Precision.ToString(CultureInfo.InvariantCulture) + "\u001F" +
                   snapshot.Quality.ToString() + "\u001F" +
                   NormalizeStatePart(snapshot.ErrorMessage);
        }

        private static string NormalizeStatePart(string value)
        {
            return value == null ? string.Empty : value;
        }

        private string BuildPublishTopic(TagValueSnapshot snapshot)
        {
            string template = string.IsNullOrWhiteSpace(_options.PublishTopicTemplate)
                ? "ipc/data/{channel}/{device}/{group}/{tag}"
                : _options.PublishTopicTemplate.Trim();

            string groupName = string.IsNullOrWhiteSpace(snapshot.GroupName) ? "_" : snapshot.GroupName.Trim();
            string pointCode = GetPointCode(snapshot);
            string topic = template
                .Replace("{gatewayId}", SanitizeTopicSegment(_options.GatewayId))
                .Replace("{gatewayName}", SanitizeTopicSegment(_options.GatewayName))
                .Replace("{site}", SanitizeTopicSegment(_options.SiteName))
                .Replace("{channelId}", SanitizeTopicSegment(snapshot.ChannelId))
                .Replace("{channel}", SanitizeTopicSegment(snapshot.ChannelName))
                .Replace("{deviceId}", SanitizeTopicSegment(snapshot.DeviceId))
                .Replace("{device}", SanitizeTopicSegment(snapshot.DeviceName))
                .Replace("{groupId}", SanitizeTopicSegment(string.IsNullOrWhiteSpace(snapshot.GroupId) ? "_" : snapshot.GroupId))
                .Replace("{group}", SanitizeTopicSegment(groupName))
                .Replace("{tagId}", SanitizeTopicSegment(snapshot.TagId))
                .Replace("{tag}", SanitizeTopicSegment(snapshot.TagName))
                .Replace("{pointCode}", SanitizeTopicSegment(pointCode))
                .Replace("{quality}", SanitizeTopicSegment(snapshot.Quality.ToString()))
                .Replace("{dataType}", SanitizeTopicSegment(snapshot.DataType));

            while (topic.IndexOf("//", StringComparison.Ordinal) >= 0)
                topic = topic.Replace("//", "/");
            return topic.Trim('/');
        }

        private string BuildPublishPayload(TagValueSnapshot snapshot)
        {
            return "{" +
                   "\"messageType\":\"telemetry\"," +
                   "\"protocolVersion\":\"" + JsonEscape(_options.CloudProtocolVersion) + "\"," +
                   "\"gatewayId\":\"" + JsonEscape(_options.GatewayId) + "\"," +
                   "\"gatewayName\":\"" + JsonEscape(_options.GatewayName) + "\"," +
                   "\"siteName\":\"" + JsonEscape(_options.SiteName) + "\"," +
                   "\"configVersion\":" + MqttGatewayOptions.ClampConfigVersion(_options.ConfigVersion).ToString(CultureInfo.InvariantCulture) + "," +
                   "\"channelId\":\"" + JsonEscape(snapshot.ChannelId) + "\"," +
                   "\"channel\":\"" + JsonEscape(snapshot.ChannelName) + "\"," +
                   "\"deviceId\":\"" + JsonEscape(snapshot.DeviceId) + "\"," +
                   "\"device\":\"" + JsonEscape(snapshot.DeviceName) + "\"," +
                   "\"groupId\":\"" + JsonEscape(snapshot.GroupId) + "\"," +
                   "\"group\":\"" + JsonEscape(snapshot.GroupName) + "\"," +
                   "\"tagId\":\"" + JsonEscape(snapshot.TagId) + "\"," +
                   "\"tag\":\"" + JsonEscape(snapshot.TagName) + "\"," +
                   "\"pointCode\":\"" + JsonEscape(GetPointCode(snapshot)) + "\"," +
                   "\"assetPath\":\"" + JsonEscape(snapshot.AssetPath) + "\"," +
                   "\"businessType\":\"" + JsonEscape(snapshot.BusinessType) + "\"," +
                   "\"source\":\"" + JsonEscape(snapshot.Source) + "\"," +
                   "\"precision\":" + snapshot.Precision.ToString(CultureInfo.InvariantCulture) + "," +
                   "\"valueText\":\"" + JsonEscape(snapshot.ValueText) + "\"," +
                   "\"rawValueText\":\"" + JsonEscape(snapshot.RawValueText) + "\"," +
                   "\"dataType\":\"" + JsonEscape(snapshot.DataType) + "\"," +
                   "\"quality\":\"" + JsonEscape(snapshot.Quality.ToString()) + "\"," +
                   "\"cleaningApplied\":" + (snapshot.CleaningApplied ? "true" : "false") + "," +
                   "\"cleaningAction\":\"" + JsonEscape(snapshot.CleaningAction) + "\"," +
                   "\"cleaningMessage\":\"" + JsonEscape(snapshot.CleaningMessage) + "\"," +
                   "\"unit\":\"" + JsonEscape(snapshot.Unit) + "\"," +
                   "\"timestamp\":\"" + JsonEscape(snapshot.Timestamp.ToString("o")) + "\"," +
                   "\"errorMessage\":\"" + JsonEscape(snapshot.ErrorMessage) + "\"" +
                   "}";
        }

        private string BuildHeartbeatPayload(DateTime timestampUtc)
        {
            MqttGatewayStatus status = GetStatus();
            return "{" +
                   "\"messageType\":\"heartbeat\"," +
                   "\"protocolVersion\":\"" + JsonEscape(_options.CloudProtocolVersion) + "\"," +
                   "\"gatewayId\":\"" + JsonEscape(_options.GatewayId) + "\"," +
                   "\"gatewayName\":\"" + JsonEscape(_options.GatewayName) + "\"," +
                   "\"siteName\":\"" + JsonEscape(_options.SiteName) + "\"," +
                   "\"clientId\":\"" + JsonEscape(_options.ClientId) + "\"," +
                   "\"configVersion\":" + MqttGatewayOptions.ClampConfigVersion(_options.ConfigVersion).ToString(CultureInfo.InvariantCulture) + "," +
                   "\"broker\":\"" + JsonEscape(_options.BrokerAddress) + "\"," +
                   "\"connected\":" + (status.IsConnected ? "true" : "false") + "," +
                   "\"outboxPending\":" + status.OutboxPendingCount.ToString(CultureInfo.InvariantCulture) + "," +
                   "\"outboxOldestPendingAgeSeconds\":" + status.OutboxOldestPendingAgeSeconds.ToString(CultureInfo.InvariantCulture) + "," +
                   "\"outboxInvalidMessageCount\":" + status.OutboxInvalidMessageCount.ToString(CultureInfo.InvariantCulture) + "," +
                   "\"outboxQuarantinedMessageCount\":" + status.OutboxQuarantinedMessageCount.ToString(CultureInfo.InvariantCulture) + "," +
                   "\"outboxQuarantineCount\":" + status.OutboxQuarantineCount.ToString(CultureInfo.InvariantCulture) + "," +
                   "\"outboxQuarantineBytes\":" + status.OutboxQuarantineBytes.ToString(CultureInfo.InvariantCulture) + "," +
                   "\"outboxQuarantineExpiredDeletedCount\":" + status.OutboxQuarantineExpiredDeletedCount.ToString(CultureInfo.InvariantCulture) + "," +
                   "\"publishedCount\":" + status.PublishedCount.ToString(CultureInfo.InvariantCulture) + "," +
                   "\"failedPublishes\":" + status.FailedPublishes.ToString(CultureInfo.InvariantCulture) + "," +
                   "\"publishConsecutiveFailureCount\":" + status.PublishConsecutiveFailureCount.ToString(CultureInfo.InvariantCulture) + "," +
                   "\"publishRetryBackoffSeconds\":" + status.PublishRetryBackoffSeconds.ToString(CultureInfo.InvariantCulture) + "," +
                   "\"timestamp\":\"" + JsonEscape(timestampUtc.ToString("o")) + "\"" +
                   "}";
        }

        private string BuildGatewayStatusPayload(DateTime timestampUtc)
        {
            MqttGatewayStatus status = GetStatus();
            IList<TagValueSnapshot> snapshots = _runtime == null ? new List<TagValueSnapshot>() : _runtime.GetSnapshots();
            Dictionary<string, DeviceStatusCounter> devices = BuildDeviceStatusCounters(snapshots);

            return "{" +
                   "\"messageType\":\"gatewayStatus\"," +
                   "\"protocolVersion\":\"" + JsonEscape(_options.CloudProtocolVersion) + "\"," +
                   "\"gatewayId\":\"" + JsonEscape(_options.GatewayId) + "\"," +
                   "\"gatewayName\":\"" + JsonEscape(_options.GatewayName) + "\"," +
                   "\"siteName\":\"" + JsonEscape(_options.SiteName) + "\"," +
                   "\"configVersion\":" + MqttGatewayOptions.ClampConfigVersion(_options.ConfigVersion).ToString(CultureInfo.InvariantCulture) + "," +
                   "\"timestamp\":\"" + JsonEscape(timestampUtc.ToString("o")) + "\"," +
                   "\"mqtt\":{\"connected\":" + (status.IsConnected ? "true" : "false") +
                   ",\"outboxPending\":" + status.OutboxPendingCount.ToString(CultureInfo.InvariantCulture) +
                   ",\"outboxOldestPendingAgeSeconds\":" + status.OutboxOldestPendingAgeSeconds.ToString(CultureInfo.InvariantCulture) +
                   ",\"outboxInvalidMessageCount\":" + status.OutboxInvalidMessageCount.ToString(CultureInfo.InvariantCulture) +
                   ",\"outboxQuarantinedMessageCount\":" + status.OutboxQuarantinedMessageCount.ToString(CultureInfo.InvariantCulture) +
                   ",\"outboxQuarantineCount\":" + status.OutboxQuarantineCount.ToString(CultureInfo.InvariantCulture) +
                   ",\"outboxQuarantineBytes\":" + status.OutboxQuarantineBytes.ToString(CultureInfo.InvariantCulture) +
                   ",\"outboxQuarantineExpiredDeletedCount\":" + status.OutboxQuarantineExpiredDeletedCount.ToString(CultureInfo.InvariantCulture) +
                   ",\"publishedCount\":" + status.PublishedCount.ToString(CultureInfo.InvariantCulture) +
                   ",\"failedPublishes\":" + status.FailedPublishes.ToString(CultureInfo.InvariantCulture) +
                   ",\"publishConsecutiveFailureCount\":" + status.PublishConsecutiveFailureCount.ToString(CultureInfo.InvariantCulture) +
                   ",\"publishRetryBackoffSeconds\":" + status.PublishRetryBackoffSeconds.ToString(CultureInfo.InvariantCulture) + "}," +
                   "\"channels\":" + BuildChannelStatusJson(devices) +
                   "}";
        }

        private string BuildAlarmTopic(TagValueSnapshot snapshot, MqttAlarmEvaluation evaluation)
        {
            string topic = BuildPublishTopic(snapshot);
            string suffix = evaluation.EventType == "warning" ? "warning" : "alarm";
            return (topic.Trim('/') + "/" + suffix).Trim('/');
        }

        private static string GetPointCode(TagValueSnapshot snapshot)
        {
            if (snapshot == null)
                return string.Empty;
            if (!string.IsNullOrWhiteSpace(snapshot.PointCode))
                return snapshot.PointCode.Trim();

            string group = string.IsNullOrWhiteSpace(snapshot.GroupName) ? "_" : snapshot.GroupName.Trim();
            return (NormalizeStatePart(snapshot.ChannelName).Trim() + "." + NormalizeStatePart(snapshot.DeviceName).Trim() + "." + group + "." + NormalizeStatePart(snapshot.TagName).Trim()).Trim('.');
        }

        private string BuildAlarmPayload(TagValueSnapshot snapshot, MqttAlarmEvaluation evaluation)
        {
            string valueText = evaluation.Value.ToString("R", CultureInfo.InvariantCulture);
            string thresholdText = evaluation.Threshold.ToString("R", CultureInfo.InvariantCulture);
            string valueField = evaluation.EventType == "warning" ? "warningValue" : "alarmValue";

            return "{" +
                   "\"messageType\":\"" + JsonEscape(evaluation.EventType) + "\"," +
                   "\"protocolVersion\":\"" + JsonEscape(_options.CloudProtocolVersion) + "\"," +
                   "\"gatewayId\":\"" + JsonEscape(_options.GatewayId) + "\"," +
                   "\"gatewayName\":\"" + JsonEscape(_options.GatewayName) + "\"," +
                   "\"siteName\":\"" + JsonEscape(_options.SiteName) + "\"," +
                   "\"configVersion\":" + MqttGatewayOptions.ClampConfigVersion(_options.ConfigVersion).ToString(CultureInfo.InvariantCulture) + "," +
                   "\"eventType\":\"" + JsonEscape(evaluation.EventType) + "\"," +
                   "\"state\":\"" + JsonEscape(evaluation.State) + "\"," +
                   "\"direction\":\"" + JsonEscape(evaluation.Direction) + "\"," +
                   "\"message\":\"" + JsonEscape(evaluation.Message) + "\"," +
                   "\"" + valueField + "\":" + valueText + "," +
                   "\"value\":" + valueText + "," +
                   "\"threshold\":" + thresholdText + "," +
                   "\"channelId\":\"" + JsonEscape(snapshot.ChannelId) + "\"," +
                   "\"channel\":\"" + JsonEscape(snapshot.ChannelName) + "\"," +
                   "\"deviceId\":\"" + JsonEscape(snapshot.DeviceId) + "\"," +
                   "\"device\":\"" + JsonEscape(snapshot.DeviceName) + "\"," +
                   "\"groupId\":\"" + JsonEscape(snapshot.GroupId) + "\"," +
                   "\"group\":\"" + JsonEscape(snapshot.GroupName) + "\"," +
                   "\"tagId\":\"" + JsonEscape(snapshot.TagId) + "\"," +
                   "\"tag\":\"" + JsonEscape(snapshot.TagName) + "\"," +
                   "\"pointCode\":\"" + JsonEscape(GetPointCode(snapshot)) + "\"," +
                   "\"assetPath\":\"" + JsonEscape(snapshot.AssetPath) + "\"," +
                   "\"businessType\":\"" + JsonEscape(snapshot.BusinessType) + "\"," +
                   "\"source\":\"" + JsonEscape(snapshot.Source) + "\"," +
                   "\"precision\":" + snapshot.Precision.ToString(CultureInfo.InvariantCulture) + "," +
                   "\"valueText\":\"" + JsonEscape(snapshot.ValueText) + "\"," +
                   "\"rawValueText\":\"" + JsonEscape(snapshot.RawValueText) + "\"," +
                   "\"dataType\":\"" + JsonEscape(snapshot.DataType) + "\"," +
                   "\"quality\":\"" + JsonEscape(snapshot.Quality.ToString()) + "\"," +
                   "\"cleaningApplied\":" + (snapshot.CleaningApplied ? "true" : "false") + "," +
                   "\"cleaningAction\":\"" + JsonEscape(snapshot.CleaningAction) + "\"," +
                   "\"cleaningMessage\":\"" + JsonEscape(snapshot.CleaningMessage) + "\"," +
                   "\"unit\":\"" + JsonEscape(snapshot.Unit) + "\"," +
                   "\"timestamp\":\"" + JsonEscape(snapshot.Timestamp.ToString("o")) + "\"" +
                   "}";
        }

        private static string SanitizeTopicSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "_";

            string text = value.Trim().Replace('\\', '/').Replace('+', '_').Replace('#', '_');
            while (text.IndexOf("//", StringComparison.Ordinal) >= 0)
                text = text.Replace("//", "/");
            return text.Trim('/');
        }

        private static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        private string ResolveTopicTemplate(string template, string requestId)
        {
            string topic = string.IsNullOrWhiteSpace(template) ? string.Empty : template.Trim();
            topic = topic
                .Replace("{gatewayId}", SanitizeTopicSegment(_options.GatewayId))
                .Replace("{gatewayName}", SanitizeTopicSegment(_options.GatewayName))
                .Replace("{site}", SanitizeTopicSegment(_options.SiteName))
                .Replace("{requestId}", SanitizeTopicSegment(requestId));

            while (topic.IndexOf("//", StringComparison.Ordinal) >= 0)
                topic = topic.Replace("//", "/");
            return topic.Trim('/');
        }

        private bool IsSparkplugMode()
        {
            return string.Equals(MqttGatewayOptions.NormalizePublishMode(_options.PublishMode), "SparkplugB", StringComparison.Ordinal);
        }

        private int BeginSparkplugSession()
        {
            if (!IsSparkplugMode())
                return 0;

            lock (_syncRoot)
            {
                _currentSparkplugBirthSequence++;
                if (_currentSparkplugBirthSequence > 255)
                    _currentSparkplugBirthSequence = 0;
                _sparkplugPayloadSequence = 255;
                return _currentSparkplugBirthSequence;
            }
        }

        private uint NextSparkplugPayloadSequence()
        {
            lock (_syncRoot)
            {
                _sparkplugPayloadSequence++;
                if (_sparkplugPayloadSequence > 255)
                    _sparkplugPayloadSequence = 0;
                return _sparkplugPayloadSequence;
            }
        }

        private SparkplugTopicBuilder CreateSparkplugTopicBuilder()
        {
            string groupId = MqttGatewayOptions.NormalizeText(_options.SparkplugGroupId, string.IsNullOrWhiteSpace(_options.GatewayId) ? "IPC-Gateway" : _options.GatewayId);
            string edgeNodeId = MqttGatewayOptions.NormalizeText(_options.SparkplugEdgeNodeId, string.IsNullOrWhiteSpace(_options.ClientId) ? "EdgeNode" : _options.ClientId);
            return new SparkplugTopicBuilder(_options.SparkplugNamespace, groupId, edgeNodeId);
        }

        private MqttWillMessage? BuildSparkplugNodeDeathWill(int birthSequence)
        {
            if (!IsSparkplugMode())
                return null;

            SparkplugTopicBuilder topicBuilder = CreateSparkplugTopicBuilder();
            byte[] payload = BuildSparkplugDeathPayload(birthSequence);
            return new MqttWillMessage(topicBuilder.NodeDeath(), payload, MqttGatewayOptions.ClampQos(_options.SparkplugDeathQos), false);
        }

        private void TryQueueSparkplugBirth()
        {
            if (!IsSparkplugMode())
                return;

            try
            {
                SparkplugTopicBuilder topicBuilder = CreateSparkplugTopicBuilder();
                _outboxStore.DeleteByTopicPrefix(topicBuilder.Namespace + "/" + topicBuilder.GroupId + "/");
                int birthSequence;
                lock (_syncRoot)
                    birthSequence = _currentSparkplugBirthSequence;

                if (_options.SparkplugPublishNodeBirth)
                    QueueSparkplugSystemPayload("sparkplugNodeBirth", topicBuilder.NodeBirth(), BuildSparkplugNodeBirthPayload(birthSequence), MqttGatewayOptions.ClampQos(_options.SparkplugBirthQos), true);

                if (_options.SparkplugPublishDeviceBirth)
                {
                    Dictionary<string, List<TagValueSnapshot>> devices = BuildSparkplugDeviceSnapshotMap();
                    foreach (KeyValuePair<string, List<TagValueSnapshot>> device in devices)
                        QueueSparkplugSystemPayload("sparkplugDeviceBirth", topicBuilder.DeviceBirth(device.Key), BuildSparkplugDeviceBirthPayload(device.Value), MqttGatewayOptions.ClampQos(_options.SparkplugBirthQos), true);
                }
            }
            catch (Exception ex)
            {
                _circuitBreaker.RecordFailure(ex.Message);
                UpdateStatus(delegate(MqttGatewayStatus status)
                {
                    status.FailedPublishes++;
                    status.LastPublishResult = "Failed: " + ex.Message;
                    status.LastError = ex.Message;
                });
            }
        }

        private void TryQueueSparkplugDeath()
        {
            if (!IsSparkplugMode())
                return;

            try
            {
                SparkplugTopicBuilder topicBuilder = CreateSparkplugTopicBuilder();
                int birthSequence;
                lock (_syncRoot)
                    birthSequence = _currentSparkplugBirthSequence;

                if (_options.SparkplugPublishDeviceDeath)
                {
                    Dictionary<string, List<TagValueSnapshot>> devices = BuildSparkplugDeviceSnapshotMap();
                    foreach (string deviceId in devices.Keys)
                        QueueSparkplugSystemPayload("sparkplugDeviceDeath", topicBuilder.DeviceDeath(deviceId), BuildSparkplugDeathPayload(birthSequence), MqttGatewayOptions.ClampQos(_options.SparkplugDeathQos), false);
                }

                QueueSparkplugSystemPayload("sparkplugNodeDeath", topicBuilder.NodeDeath(), BuildSparkplugDeathPayload(birthSequence), MqttGatewayOptions.ClampQos(_options.SparkplugDeathQos), false);
            }
            catch
            {
            }
        }

        private void QueueSparkplugSystemPayload(string source, string topic, byte[] payload, int qos, bool birth)
        {
            _outboxStore.Enqueue(topic, payload, qos);
            RecordPublish(source, topic, qos, payload);
            MqttOutboxCleanupResult cleanup = CleanupOutbox();
            UpdateStatus(delegate(MqttGatewayStatus status)
            {
                status.OutboxEnqueuedCount++;
                if (birth)
                {
                    status.SparkplugBirthCount++;
                    status.LastSparkplugBirthTime = DateTime.Now;
                }
                else
                {
                    status.SparkplugDeathCount++;
                    status.LastSparkplugDeathTime = DateTime.Now;
                }
                ApplyOutboxCleanupResult(status, cleanup);
                UpdateOutboxStatus(status);
                status.LastPublishTime = DateTime.Now;
                status.LastPublishResult = "Queued: " + topic;
                status.LastError = string.Empty;
            });
        }

        private byte[] BuildSparkplugNodeBirthPayload(int birthSequence)
        {
            SparkplugPayload payload = CreateSparkplugPayload();
            payload.Metrics.Add(SparkplugMetric.UInt64("bdSeq", (ulong)Math.Max(0, birthSequence)));
            payload.Metrics.Add(SparkplugMetric.Boolean("Node Control/Rebirth", false));
            payload.Metrics.Add(SparkplugMetric.String("Properties/GatewayId", _options.GatewayId));
            payload.Metrics.Add(SparkplugMetric.String("Properties/GatewayName", _options.GatewayName));
            payload.Metrics.Add(SparkplugMetric.String("Properties/SiteName", _options.SiteName));
            payload.Metrics.Add(SparkplugMetric.Int64("Properties/ConfigVersion", MqttGatewayOptions.ClampConfigVersion(_options.ConfigVersion)));
            return SparkplugPayloadEncoder.Encode(payload);
        }

        private byte[] BuildSparkplugNodeDataPayload(DateTime timestampUtc)
        {
            MqttGatewayStatus status = GetStatus();
            SparkplugPayload payload = CreateSparkplugPayload(timestampUtc);
            payload.Metrics.Add(SparkplugMetric.Boolean("Node Status/Connected", status.IsConnected));
            payload.Metrics.Add(SparkplugMetric.Int64("Node Status/OutboxPending", status.OutboxPendingCount));
            payload.Metrics.Add(SparkplugMetric.Int64("Node Status/PublishedCount", status.PublishedCount));
            payload.Metrics.Add(SparkplugMetric.Int64("Node Status/FailedPublishes", status.FailedPublishes));
            payload.Metrics.Add(SparkplugMetric.Int64("Node Status/ReconnectCount", status.ReconnectCount));
            return SparkplugPayloadEncoder.Encode(payload);
        }

        private byte[] BuildSparkplugDeviceBirthPayload(List<TagValueSnapshot> snapshots)
        {
            SparkplugPayload payload = CreateSparkplugPayload();
            if (snapshots != null && snapshots.Count > 0)
            {
                string deviceName = snapshots[0].DeviceName;
                payload.Metrics.Add(SparkplugMetric.Boolean("Device Status/Online", true));
                payload.Metrics.Add(SparkplugMetric.String("Device Status/Name", deviceName));
                payload.Metrics.Add(SparkplugMetric.String("Device Status/Protocol", snapshots[0].DeviceProtocol));
                for (int i = 0; i < snapshots.Count; i++)
                    payload.Metrics.Add(BuildSparkplugMetric(snapshots[i], true));
            }
            return SparkplugPayloadEncoder.Encode(payload);
        }

        private byte[] BuildSparkplugDataPayload(TagValueSnapshot snapshot)
        {
            TagValueSnapshot valueSnapshot = snapshot ?? new TagValueSnapshot();
            SparkplugPayload payload = CreateSparkplugPayload(valueSnapshot.Timestamp == DateTime.MinValue ? DateTime.UtcNow : valueSnapshot.Timestamp);
            payload.Metrics.Add(BuildSparkplugMetric(valueSnapshot, false));
            return SparkplugPayloadEncoder.Encode(payload);
        }

        private byte[] BuildSparkplugDeathPayload(int birthSequence)
        {
            SparkplugPayload payload = CreateSparkplugPayload();
            payload.Metrics.Add(SparkplugMetric.UInt64("bdSeq", (ulong)Math.Max(0, birthSequence)));
            return SparkplugPayloadEncoder.Encode(payload);
        }

        private SparkplugPayload CreateSparkplugPayload()
        {
            return CreateSparkplugPayload(DateTime.UtcNow);
        }

        private SparkplugPayload CreateSparkplugPayload(DateTime timestamp)
        {
            return new SparkplugPayload
            {
                Timestamp = ToSparkplugTimestamp(timestamp),
                Sequence = NextSparkplugPayloadSequence()
            };
        }

        private SparkplugMetric BuildSparkplugMetric(TagValueSnapshot snapshot, bool birth)
        {
            snapshot ??= new TagValueSnapshot();
            SparkplugMetric metric = SparkplugMetric.FromText(BuildSparkplugMetricName(snapshot), snapshot.DataType, snapshot.ValueText);
            metric.Timestamp = ToSparkplugTimestamp(snapshot.Timestamp);
            if (_options.SparkplugUseAliases)
            {
                metric.Alias = BuildSparkplugMetricAlias(snapshot);
                if (!birth)
                    metric.Name = string.Empty;
            }
            if (_options.SparkplugIncludeProperties)
                AddSparkplugMetricProperties(metric, snapshot, birth);
            return metric;
        }

        private void AddSparkplugMetricProperties(SparkplugMetric metric, TagValueSnapshot snapshot, bool birth)
        {
            metric.Properties["channelId"] = snapshot.ChannelId ?? string.Empty;
            metric.Properties["channelName"] = snapshot.ChannelName ?? string.Empty;
            metric.Properties["deviceId"] = snapshot.DeviceId ?? string.Empty;
            metric.Properties["deviceName"] = snapshot.DeviceName ?? string.Empty;
            metric.Properties["deviceProtocol"] = snapshot.DeviceProtocol ?? string.Empty;
            metric.Properties["groupId"] = snapshot.GroupId ?? string.Empty;
            metric.Properties["groupName"] = snapshot.GroupName ?? string.Empty;
            metric.Properties["tagId"] = snapshot.TagId ?? string.Empty;
            metric.Properties["tagName"] = snapshot.TagName ?? string.Empty;
            metric.Properties["pointCode"] = GetPointCode(snapshot);
            metric.Properties["assetPath"] = snapshot.AssetPath ?? string.Empty;
            metric.Properties["businessType"] = snapshot.BusinessType ?? string.Empty;
            metric.Properties["source"] = snapshot.Source ?? string.Empty;
            metric.Properties["unit"] = snapshot.Unit ?? string.Empty;
            metric.Properties["quality"] = snapshot.Quality.ToString();
            metric.Properties["rawValueText"] = snapshot.RawValueText ?? string.Empty;
            metric.Properties["cleaningApplied"] = snapshot.CleaningApplied ? "true" : "false";
            metric.Properties["cleaningAction"] = snapshot.CleaningAction ?? string.Empty;
            metric.Properties["cleaningMessage"] = snapshot.CleaningMessage ?? string.Empty;
            metric.Properties["birth"] = birth ? "true" : "false";
        }

        private string BuildSparkplugDeviceDataTopic(TagValueSnapshot snapshot)
        {
            return CreateSparkplugTopicBuilder().DeviceData(BuildSparkplugDeviceId(snapshot));
        }

        private string BuildSparkplugDeviceId(TagValueSnapshot snapshot)
        {
            if (snapshot == null)
                return "Device";

            string source = MqttGatewayOptions.NormalizeText(_options.SparkplugDeviceIdSource, "DeviceId");
            string value = source.Equals("DeviceId", StringComparison.OrdinalIgnoreCase)
                ? snapshot.DeviceId
                : snapshot.DeviceName;
            if (string.IsNullOrWhiteSpace(value))
                value = snapshot.DeviceName;
            return SparkplugTopicBuilder.Normalize(value, "Device");
        }

        private string BuildSparkplugMetricName(TagValueSnapshot snapshot)
        {
            string template = MqttGatewayOptions.NormalizeText(_options.SparkplugMetricNameTemplate, "{channel}/{group}/{tag}");
            string groupName = string.IsNullOrWhiteSpace(snapshot.GroupName) ? "_" : snapshot.GroupName.Trim();
            string metric = template
                .Replace("{channelId}", SanitizeTopicSegment(snapshot.ChannelId))
                .Replace("{channel}", SanitizeTopicSegment(snapshot.ChannelName))
                .Replace("{deviceId}", SanitizeTopicSegment(snapshot.DeviceId))
                .Replace("{device}", SanitizeTopicSegment(snapshot.DeviceName))
                .Replace("{groupId}", SanitizeTopicSegment(string.IsNullOrWhiteSpace(snapshot.GroupId) ? "_" : snapshot.GroupId))
                .Replace("{group}", SanitizeTopicSegment(groupName))
                .Replace("{tagId}", SanitizeTopicSegment(snapshot.TagId))
                .Replace("{tag}", SanitizeTopicSegment(snapshot.TagName))
                .Replace("{pointCode}", SanitizeTopicSegment(GetPointCode(snapshot)))
                .Replace("{dataType}", SanitizeTopicSegment(snapshot.DataType));
            while (metric.IndexOf("//", StringComparison.Ordinal) >= 0)
                metric = metric.Replace("//", "/");
            return metric.Trim('/');
        }

        private ulong BuildSparkplugMetricAlias(TagValueSnapshot snapshot)
        {
            string text = BuildPublishStateKey(snapshot);
            ulong hash = 1469598103934665603UL;
            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= 1099511628211UL;
            }
            return hash == 0 ? 1UL : hash;
        }

        private Dictionary<string, List<TagValueSnapshot>> BuildSparkplugDeviceSnapshotMap()
        {
            IList<TagValueSnapshot> snapshots = _runtime == null ? new List<TagValueSnapshot>() : _runtime.GetSnapshots();
            Dictionary<string, List<TagValueSnapshot>> devices = new Dictionary<string, List<TagValueSnapshot>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < snapshots.Count; i++)
            {
                TagValueSnapshot snapshot = snapshots[i];
                if (snapshot == null || string.Equals(snapshot.Source, "RuleEngine", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (_options.PublishSelectedTagsOnly && !snapshot.MqttPublishEnabled)
                    continue;

                string deviceId = BuildSparkplugDeviceId(snapshot);
                if (!devices.TryGetValue(deviceId, out List<TagValueSnapshot>? list))
                {
                    list = new List<TagValueSnapshot>();
                    devices[deviceId] = list;
                }
                list.Add(snapshot);
            }
            return devices;
        }

        private static DateTimeOffset ToSparkplugTimestamp(DateTime timestamp)
        {
            if (timestamp == DateTime.MinValue)
                return DateTimeOffset.UtcNow;
            if (timestamp.Kind == DateTimeKind.Utc)
                return new DateTimeOffset(timestamp, TimeSpan.Zero);
            if (timestamp.Kind == DateTimeKind.Local)
                return new DateTimeOffset(timestamp).ToUniversalTime();
            return new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Local)).ToUniversalTime();
        }

        private void ApplyIdentityStatus(MqttGatewayStatus status)
        {
            status.GatewayId = _options.GatewayId ?? string.Empty;
            status.GatewayName = _options.GatewayName ?? string.Empty;
            status.SiteName = _options.SiteName ?? string.Empty;
            status.CloudProtocolVersion = _options.CloudProtocolVersion ?? string.Empty;
            status.ConfigVersion = MqttGatewayOptions.ClampConfigVersion(_options.ConfigVersion);
            status.PublishMode = MqttGatewayOptions.NormalizePublishMode(_options.PublishMode);
            status.SparkplugEnabled = IsSparkplugMode();
            status.SparkplugNamespace = MqttGatewayOptions.NormalizeText(_options.SparkplugNamespace, "spBv1.0");
            status.SparkplugGroupId = MqttGatewayOptions.NormalizeText(_options.SparkplugGroupId, status.GatewayId.Length == 0 ? "IPC-Gateway" : status.GatewayId);
            string edgeFallback = !string.IsNullOrWhiteSpace(_options.ClientId)
                ? _options.ClientId
                : (!string.IsNullOrWhiteSpace(_options.GatewayId) ? _options.GatewayId : "EdgeNode");
            status.SparkplugEdgeNodeId = MqttGatewayOptions.NormalizeText(_options.SparkplugEdgeNodeId, edgeFallback);
            SparkplugTopicBuilder topicBuilder = CreateSparkplugTopicBuilder();
            status.SparkplugNodeBirthTopic = topicBuilder.NodeBirth();
            status.SparkplugNodeDeathTopic = topicBuilder.NodeDeath();
        }

        private static Dictionary<string, DeviceStatusCounter> BuildDeviceStatusCounters(IList<TagValueSnapshot> snapshots)
        {
            Dictionary<string, DeviceStatusCounter> devices = new Dictionary<string, DeviceStatusCounter>(StringComparer.OrdinalIgnoreCase);
            if (snapshots == null)
                return devices;

            for (int i = 0; i < snapshots.Count; i++)
            {
                TagValueSnapshot snapshot = snapshots[i];
                if (snapshot == null)
                    continue;

                string key = NormalizeStatePart(snapshot.ChannelId) + "\u001F" + NormalizeStatePart(snapshot.DeviceId);
                if (string.IsNullOrWhiteSpace(snapshot.ChannelId) || string.IsNullOrWhiteSpace(snapshot.DeviceId) ||
                    string.Equals(snapshot.Source, "RuleEngine", StringComparison.OrdinalIgnoreCase))
                    continue;

                DeviceStatusCounter? counter;
                if (!devices.TryGetValue(key, out counter) || counter == null)
                {
                    counter = new DeviceStatusCounter();
                    counter.ChannelId = snapshot.ChannelId;
                    counter.ChannelName = snapshot.ChannelName;
                    counter.DeviceId = snapshot.DeviceId;
                    counter.DeviceName = snapshot.DeviceName;
                    counter.Protocol = snapshot.DeviceProtocol;
                    devices[key] = counter;
                }

                counter.TotalTags++;
                AddTagStatus(counter, snapshot);
                if (snapshot.Quality == TagQuality.Good)
                    counter.GoodTags++;
                else if (snapshot.Quality == TagQuality.Unknown)
                    counter.NoDataTags++;
                else
                    counter.BadTags++;

                if (snapshot.Timestamp > counter.LastDataTime)
                    counter.LastDataTime = snapshot.Timestamp;
                if (!string.IsNullOrWhiteSpace(snapshot.ErrorMessage))
                    counter.LastError = snapshot.ErrorMessage;
            }

            return devices;
        }

        private static string BuildChannelStatusJson(Dictionary<string, DeviceStatusCounter> devices)
        {
            if (devices == null || devices.Count == 0)
                return "[]";

            List<IGrouping<string, DeviceStatusCounter>> channels = devices.Values
                .GroupBy(device => NormalizeStatePart(device.ChannelId) + "\u001F" + NormalizeStatePart(device.ChannelName), StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.First().ChannelName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            List<string> items = new List<string>();
            foreach (IGrouping<string, DeviceStatusCounter> channel in channels)
            {
                DeviceStatusCounter first = channel.First();
                Dictionary<string, DeviceStatusCounter> channelDevices = channel
                    .ToDictionary(device => device.DeviceId, StringComparer.OrdinalIgnoreCase);
                items.Add("{" +
                          "\"channelId\":\"" + JsonEscape(first.ChannelId) + "\"," +
                          "\"channelName\":\"" + JsonEscape(first.ChannelName) + "\"," +
                          "\"deviceCount\":" + channelDevices.Count.ToString(CultureInfo.InvariantCulture) + "," +
                          "\"devices\":" + BuildDeviceStatusJson(channelDevices) +
                          "}");
            }

            return "[" + string.Join(",", items.ToArray()) + "]";
        }

        private static string BuildDeviceStatusJson(Dictionary<string, DeviceStatusCounter> devices)
        {
            if (devices == null || devices.Count == 0)
                return "[]";

            List<DeviceStatusCounter> values = new List<DeviceStatusCounter>(devices.Values);
            values.Sort(delegate(DeviceStatusCounter left, DeviceStatusCounter right)
            {
                return string.Compare(left.DeviceName, right.DeviceName, StringComparison.OrdinalIgnoreCase);
            });

            List<string> items = new List<string>();
            for (int i = 0; i < values.Count; i++)
            {
                DeviceStatusCounter device = values[i];
                string state = device.GoodTags > 0 ? "Online" : (device.BadTags > 0 ? "Error" : "NoData");
                double successRate = device.TotalTags == 0 ? 0D : Math.Round(device.GoodTags * 100D / device.TotalTags, 2);
                items.Add("{" +
                          "\"channelId\":\"" + JsonEscape(device.ChannelId) + "\"," +
                          "\"channelName\":\"" + JsonEscape(device.ChannelName) + "\"," +
                          "\"deviceId\":\"" + JsonEscape(device.DeviceId) + "\"," +
                          "\"deviceName\":\"" + JsonEscape(device.DeviceName) + "\"," +
                          "\"protocol\":\"" + JsonEscape(device.Protocol) + "\"," +
                          "\"status\":\"" + state + "\"," +
                          "\"totalTags\":" + device.TotalTags.ToString(CultureInfo.InvariantCulture) + "," +
                          "\"goodTags\":" + device.GoodTags.ToString(CultureInfo.InvariantCulture) + "," +
                          "\"badTags\":" + device.BadTags.ToString(CultureInfo.InvariantCulture) + "," +
                          "\"noDataTags\":" + device.NoDataTags.ToString(CultureInfo.InvariantCulture) + "," +
                          "\"enabledTags\":" + device.TotalTags.ToString(CultureInfo.InvariantCulture) + "," +
                          "\"successRate\":" + successRate.ToString("0.##", CultureInfo.InvariantCulture) + "," +
                          "\"lastDataTime\":\"" + JsonEscape(device.LastDataTime == DateTime.MinValue ? string.Empty : device.LastDataTime.ToString("o")) + "\"," +
                          "\"lastError\":\"" + JsonEscape(device.LastError) + "\"," +
                          "\"tags\":" + BuildTagStatusJson(device.Tags) + "," +
                          "\"groups\":" + BuildGroupStatusJson(device.Groups) +
                          "}");
            }

            return "[" + string.Join(",", items.ToArray()) + "]";
        }

        private static void AddTagStatus(DeviceStatusCounter device, TagValueSnapshot snapshot)
        {
            TagStatusCounter tag = new TagStatusCounter();
            tag.ChannelId = snapshot.ChannelId;
            tag.TagId = snapshot.TagId;
            tag.DeviceId = snapshot.DeviceId;
            tag.GroupId = snapshot.GroupId;
            tag.TagName = snapshot.TagName;
            tag.PointCode = snapshot.PointCode;
            tag.DataType = snapshot.DataType;
            tag.Unit = snapshot.Unit;
            tag.AssetPath = snapshot.AssetPath;
            tag.BusinessType = snapshot.BusinessType;
            tag.Source = snapshot.Source;
            tag.MqttPublishEnabled = snapshot.MqttPublishEnabled;
            tag.Alarm = snapshot.Alarm;

            if (string.IsNullOrWhiteSpace(snapshot.GroupId))
            {
                device.Tags.Add(tag);
                return;
            }

            GroupStatusCounter? group = null;
            for (int i = 0; i < device.Groups.Count; i++)
            {
                if (string.Equals(device.Groups[i].GroupId, snapshot.GroupId, StringComparison.OrdinalIgnoreCase))
                {
                    group = device.Groups[i];
                    break;
                }
            }

            if (group == null)
            {
                group = new GroupStatusCounter();
                group.GroupId = snapshot.GroupId;
                group.GroupName = snapshot.GroupName;
                group.Enabled = true;
                device.Groups.Add(group);
            }

            group.Tags.Add(tag);
            group.TotalTags++;
            group.EnabledTags++;
            if (snapshot.Quality == TagQuality.Good)
                group.GoodTags++;
            else if (snapshot.Quality == TagQuality.Unknown)
                group.NoDataTags++;
            else
                group.BadTags++;
            if (snapshot.Timestamp > group.LastDataTime)
                group.LastDataTime = snapshot.Timestamp;
            if (!string.IsNullOrWhiteSpace(snapshot.ErrorMessage))
                group.LastError = snapshot.ErrorMessage;
        }

        private static string BuildGroupStatusJson(List<GroupStatusCounter> groups)
        {
            if (groups == null || groups.Count == 0)
                return "[]";

            List<string> items = new List<string>();
            for (int i = 0; i < groups.Count; i++)
            {
                GroupStatusCounter group = groups[i];
                string state = group.GoodTags > 0 ? "Online" : (group.BadTags > 0 ? "Error" : "NoData");
                double successRate = group.TotalTags == 0 ? 0D : Math.Round(group.GoodTags * 100D / group.TotalTags, 2);
                items.Add("{" +
                          "\"groupId\":\"" + JsonEscape(group.GroupId) + "\"," +
                          "\"groupName\":\"" + JsonEscape(group.GroupName) + "\"," +
                          "\"enabled\":" + (group.Enabled ? "true" : "false") + "," +
                          "\"status\":\"" + state + "\"," +
                          "\"totalTags\":" + group.TotalTags.ToString(CultureInfo.InvariantCulture) + "," +
                          "\"enabledTags\":" + group.EnabledTags.ToString(CultureInfo.InvariantCulture) + "," +
                          "\"goodTags\":" + group.GoodTags.ToString(CultureInfo.InvariantCulture) + "," +
                          "\"badTags\":" + group.BadTags.ToString(CultureInfo.InvariantCulture) + "," +
                          "\"noDataTags\":" + group.NoDataTags.ToString(CultureInfo.InvariantCulture) + "," +
                          "\"successRate\":" + successRate.ToString("0.##", CultureInfo.InvariantCulture) + "," +
                          "\"lastDataTime\":\"" + JsonEscape(group.LastDataTime == DateTime.MinValue ? string.Empty : group.LastDataTime.ToString("o")) + "\"," +
                          "\"lastError\":\"" + JsonEscape(group.LastError) + "\"," +
                          "\"tags\":" + BuildTagStatusJson(group.Tags) +
                          "}");
            }

            return "[" + string.Join(",", items.ToArray()) + "]";
        }

        private static string BuildTagStatusJson(List<TagStatusCounter> tags)
        {
            if (tags == null || tags.Count == 0)
                return "[]";

            List<string> items = new List<string>();
            for (int i = 0; i < tags.Count; i++)
            {
                TagStatusCounter tag = tags[i];
                items.Add("{" +
                          "\"channelId\":\"" + JsonEscape(tag.ChannelId) + "\"," +
                          "\"tagId\":\"" + JsonEscape(tag.TagId) + "\"," +
                          "\"deviceId\":\"" + JsonEscape(tag.DeviceId) + "\"," +
                          "\"groupId\":\"" + JsonEscape(tag.GroupId) + "\"," +
                          "\"tagName\":\"" + JsonEscape(tag.TagName) + "\"," +
                          "\"pointCode\":\"" + JsonEscape(tag.PointCode) + "\"," +
                          "\"dataType\":\"" + JsonEscape(tag.DataType) + "\"," +
                          "\"unit\":\"" + JsonEscape(tag.Unit) + "\"," +
                          "\"assetPath\":\"" + JsonEscape(tag.AssetPath) + "\"," +
                          "\"businessType\":\"" + JsonEscape(tag.BusinessType) + "\"," +
                          "\"source\":\"" + JsonEscape(tag.Source) + "\"," +
                          "\"mqttPublishEnabled\":" + (tag.MqttPublishEnabled ? "true" : "false") + "," +
                          "\"alarm\":" + BuildAlarmConfigJson(tag.Alarm) +
                          "}");
            }

            return "[" + string.Join(",", items.ToArray()) + "]";
        }

        private static string BuildAlarmConfigJson(TagAlarmConfig alarm)
        {
            if (alarm == null)
                alarm = TagAlarmConfig.Default();
            return "{" +
                   "\"enabled\":" + (alarm.Enabled ? "true" : "false") + "," +
                   "\"lowLimit\":" + alarm.LowLimit.ToString("R", CultureInfo.InvariantCulture) + "," +
                   "\"highLimit\":" + alarm.HighLimit.ToString("R", CultureInfo.InvariantCulture) + "," +
                   "\"lowAlarmMessage\":\"" + JsonEscape(alarm.LowAlarmMessage) + "\"," +
                   "\"highAlarmMessage\":\"" + JsonEscape(alarm.HighAlarmMessage) + "\"," +
                   "\"warningDeviation\":" + alarm.WarningDeviation.ToString("R", CultureInfo.InvariantCulture) + "," +
                   "\"lowWarningMessage\":\"" + JsonEscape(alarm.LowWarningMessage) + "\"," +
                   "\"highWarningMessage\":\"" + JsonEscape(alarm.HighWarningMessage) + "\"" +
                   "}";
        }

        private void MarkWriteFailed(string message)
        {
            UpdateStatus(delegate(MqttGatewayStatus status)
            {
                status.FailedWrites++;
                status.LastError = message ?? string.Empty;
                status.LastWriteResult = "Failed: " + status.LastError;
            });
        }

        private void UpdateStatus(Action<MqttGatewayStatus> update)
        {
            lock (_syncRoot)
            {
                update(_status);
                _status.CircuitBreaker = _circuitBreaker.Snapshot();
            }
        }

        private MqttOutboxCleanupResult CleanupOutbox()
        {
            DateTime nowUtc = DateTime.UtcNow;
            lock (_syncRoot)
            {
                if (_lastOutboxCleanupUtc != DateTime.MinValue && nowUtc - _lastOutboxCleanupUtc < TimeSpan.FromSeconds(5))
                    return new MqttOutboxCleanupResult();

                _lastOutboxCleanupUtc = nowUtc;
            }

            int maxMessages = MqttGatewayOptions.ClampOutboxMaxMessages(_options.OutboxMaxMessages);
            long maxBytes = (long)MqttGatewayOptions.ClampOutboxMaxMegabytes(_options.OutboxMaxMegabytes) * 1024L * 1024L;
            TimeSpan retention = TimeSpan.FromHours(MqttGatewayOptions.ClampOutboxRetentionHours(_options.OutboxRetentionHours));
            TimeSpan quarantineRetention = TimeSpan.FromHours(MqttGatewayOptions.ClampOutboxQuarantineRetentionHours(_options.OutboxQuarantineRetentionHours));
            return _outboxStore.Cleanup(maxMessages, maxBytes, retention, quarantineRetention);
        }

        private void RecordPublish(string source, string topic, int qos, string payload)
        {
            try
            {
                if (_history != null)
                    _history.RecordPublish(source, topic, MqttGatewayOptions.ClampQos(qos), payload);
            }
            catch (Exception ex)
            {
                IpcLogService.WriteError("Local history publish record failed.", ex);
            }
        }

        private void RecordPublish(string source, string topic, int qos, byte[] payload)
        {
            string preview = "base64:" + Convert.ToBase64String(payload ?? Array.Empty<byte>());
            RecordPublish(source, topic, qos, preview);
        }

        private void RecordAlarm(TagValueSnapshot snapshot, MqttAlarmEvaluation evaluation, string topic)
        {
            try
            {
                if (_history != null && evaluation != null)
                    _history.RecordAlarm(snapshot, evaluation.EventType, evaluation.State, evaluation.Message, evaluation.Value, evaluation.Threshold, topic);
            }
            catch (Exception ex)
            {
                IpcLogService.WriteError("Local history alarm record failed.", ex);
            }
        }

        private void UpdateOutboxStatus(MqttGatewayStatus status)
        {
            DateTime nowUtc = DateTime.UtcNow;
            if (_lastOutboxStatusRefreshUtc != DateTime.MinValue && nowUtc - _lastOutboxStatusRefreshUtc < TimeSpan.FromSeconds(1))
                return;

            _lastOutboxStatusRefreshUtc = nowUtc;
            MqttOutboxStats stats = _outboxStore.GetStats();
            MqttOutboxQuarantineStats quarantineStats = _outboxStore.GetQuarantineStats();
            status.OutboxPendingCount = stats.MessageCount;
            status.OutboxBytes = stats.TotalBytes;
            status.OutboxInvalidMessageCount = stats.InvalidMessageCount;
            status.OutboxOldestPendingTime = ToLocalTime(stats.OldestCreatedAt);
            status.OutboxNewestPendingTime = ToLocalTime(stats.NewestCreatedAt);
            status.OutboxOldestPendingAgeSeconds = CalculatePendingAgeSeconds(status.OutboxOldestPendingTime);
            status.OutboxQuarantineCount = quarantineStats.MessageCount;
            status.OutboxQuarantineBytes = quarantineStats.TotalBytes;
            status.OutboxOldestQuarantineTime = ToLocalTime(quarantineStats.OldestQuarantineTime);
            status.OutboxNewestQuarantineTime = ToLocalTime(quarantineStats.NewestQuarantineTime);
        }

        private static void ApplyOutboxCleanupResult(MqttGatewayStatus status, MqttOutboxCleanupResult cleanup)
        {
            if (status == null || cleanup == null)
                return;

            status.OutboxExpiredDeletedCount += cleanup.ExpiredDeleted;
            status.OutboxOverflowDeletedCount += cleanup.OverflowDeleted;
            status.OutboxQuarantinedMessageCount += cleanup.InvalidQuarantined;
            status.OutboxQuarantineExpiredDeletedCount += cleanup.QuarantineExpiredDeleted;
        }

        private int RegisterFlushFailure()
        {
            _flushFailureCount++;
            int minSeconds = MqttGatewayOptions.ClampRetrySeconds(_options.PublishRetryMinSeconds);
            int maxSeconds = MqttGatewayOptions.ClampRetrySeconds(_options.PublishRetryMaxSeconds);
            if (maxSeconds < minSeconds)
                maxSeconds = minSeconds;

            int exponent = Math.Min(_flushFailureCount - 1, 10);
            int delay = minSeconds;
            for (int i = 0; i < exponent; i++)
            {
                if (delay >= maxSeconds / 2)
                {
                    delay = maxSeconds;
                    break;
                }
                delay *= 2;
            }

            if (delay > maxSeconds)
                delay = maxSeconds;
            _nextFlushUtc = DateTime.UtcNow.AddSeconds(delay);
            return delay;
        }

        private static long CalculatePendingAgeSeconds(DateTime oldestPendingTime)
        {
            if (oldestPendingTime == DateTime.MinValue)
                return 0;

            DateTime oldest = oldestPendingTime.Kind == DateTimeKind.Utc ? oldestPendingTime.ToLocalTime() : oldestPendingTime;
            DateTime now = DateTime.Now;
            if (oldest > now)
                return 0;

            return (long)Math.Round((now - oldest).TotalSeconds, MidpointRounding.AwayFromZero);
        }

        private static DateTime ToLocalTime(DateTime value)
        {
            if (value == DateTime.MinValue)
                return DateTime.MinValue;
            return value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;
        }

        private static string ResolveOutboxDirectory(string configuredPath)
        {
            string path = string.IsNullOrWhiteSpace(configuredPath) ? "Data\\MqttOutbox" : configuredPath.Trim();
            if (!Path.IsPathRooted(path))
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
            return path;
        }

        
        
        
        
        
        
        
        
        
        private sealed class MqttPublishValueState
        {
            public MqttPublishValueState(string signature, DateTime lastQueuedUtc)
            {
                Signature = signature ?? string.Empty;
                LastQueuedUtc = lastQueuedUtc;
            }

            public string Signature { get; private set; }
            public DateTime LastQueuedUtc { get; private set; }
        }

        private sealed class DeviceStatusCounter
        {
            public DeviceStatusCounter()
            {
                ChannelId = string.Empty;
                ChannelName = string.Empty;
                DeviceId = string.Empty;
                DeviceName = string.Empty;
                Protocol = string.Empty;
                LastError = string.Empty;
                LastDataTime = DateTime.MinValue;
                Tags = new List<TagStatusCounter>();
                Groups = new List<GroupStatusCounter>();
            }

            public string ChannelId { get; set; }
            public string ChannelName { get; set; }
            public string DeviceId { get; set; }
            public string DeviceName { get; set; }
            public string Protocol { get; set; }
            public int TotalTags { get; set; }
            public int GoodTags { get; set; }
            public int BadTags { get; set; }
            public int NoDataTags { get; set; }
            public DateTime LastDataTime { get; set; }
            public string LastError { get; set; }
            public List<TagStatusCounter> Tags { get; set; }
            public List<GroupStatusCounter> Groups { get; set; }
        }

        private sealed class GroupStatusCounter
        {
            public GroupStatusCounter()
            {
                GroupId = string.Empty;
                GroupName = string.Empty;
                Enabled = true;
                LastError = string.Empty;
                LastDataTime = DateTime.MinValue;
                Tags = new List<TagStatusCounter>();
            }

            public string GroupId { get; set; }
            public string GroupName { get; set; }
            public bool Enabled { get; set; }
            public int TotalTags { get; set; }
            public int EnabledTags { get; set; }
            public int GoodTags { get; set; }
            public int BadTags { get; set; }
            public int NoDataTags { get; set; }
            public DateTime LastDataTime { get; set; }
            public string LastError { get; set; }
            public List<TagStatusCounter> Tags { get; set; }
        }

        private sealed class TagStatusCounter
        {
            public TagStatusCounter()
            {
                ChannelId = string.Empty;
                TagId = string.Empty;
                DeviceId = string.Empty;
                GroupId = string.Empty;
                TagName = string.Empty;
                PointCode = string.Empty;
                DataType = string.Empty;
                Unit = string.Empty;
                AssetPath = string.Empty;
                BusinessType = string.Empty;
                Source = string.Empty;
                Alarm = new TagAlarmConfig();
            }

            public string ChannelId { get; set; }
            public string TagId { get; set; }
            public string DeviceId { get; set; }
            public string GroupId { get; set; }
            public string TagName { get; set; }
            public string PointCode { get; set; }
            public string DataType { get; set; }
            public string Unit { get; set; }
            public string AssetPath { get; set; }
            public string BusinessType { get; set; }
            public string Source { get; set; }
            public bool MqttPublishEnabled { get; set; }
            public TagAlarmConfig Alarm { get; set; }
        }

        
        
        
        
        
        
        
        
        
        private sealed class MqttAlarmEvaluation
        {
            private MqttAlarmEvaluation()
            {
                State = string.Empty;
                EventType = string.Empty;
                Direction = string.Empty;
                Message = string.Empty;
            }

            public string State { get; private set; }
            public string EventType { get; private set; }
            public string Direction { get; private set; }
            public double Value { get; private set; }
            public double Threshold { get; private set; }
            public string Message { get; private set; }
            public bool IsNormal { get; private set; }

            public static MqttAlarmEvaluation Normal()
            {
                return Normal(0D);
            }

            public static MqttAlarmEvaluation Normal(double value)
            {
                return new MqttAlarmEvaluation
                {
                    State = "Normal",
                    EventType = "normal",
                    Direction = string.Empty,
                    Value = value,
                    Threshold = 0D,
                    Message = string.Empty,
                    IsNormal = true
                };
            }

            public static MqttAlarmEvaluation Active(string state, string eventType, string direction, double value, double threshold, string message, string fallbackMessage)
            {
                return new MqttAlarmEvaluation
                {
                    State = state ?? string.Empty,
                    EventType = eventType ?? string.Empty,
                    Direction = direction ?? string.Empty,
                    Value = value,
                    Threshold = threshold,
                    Message = string.IsNullOrWhiteSpace(message) ? fallbackMessage : message,
                    IsNormal = false
                };
            }
        }
    }
}

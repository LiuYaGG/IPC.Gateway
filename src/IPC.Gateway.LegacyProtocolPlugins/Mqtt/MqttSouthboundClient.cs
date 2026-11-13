using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IPC.EdgeGateway;
using IPC.Gateway.Mqtt.Sparkplug;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Mqtt
{
    public sealed class MqttSouthboundClient : IPlcClient, IPlcBatchReadClient, IAsyncPlcClient, IAsyncPlcBatchReadClient
    {
        private readonly PlcConnectionOptions _connection;
        private readonly MqttSouthboundOptions _options;
        private readonly ConcurrentDictionary<string, CachedValue> _values = new ConcurrentDictionary<string, CachedValue>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, string> _aliases = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        private readonly LinkedList<PendingWrite> _writes = new();
        private readonly HashSet<string> _offlineScopes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ulong> _birthSequence = new(StringComparer.Ordinal);
        private readonly object _cacheLock = new();
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);
        private readonly ManualResetEventSlim _connectedSignal = new ManualResetEventSlim(false);
        private SimpleMqttClient _client;
        private Thread _thread;
        private Exception _loopError;
        private volatile bool _connected;

        public MqttSouthboundClient(PlcConnectionOptions connection)
        {
            _connection = connection ?? new PlcConnectionOptions();
            _options = MqttSouthboundOptions.Parse(_connection);
        }

        public bool IsConnected => _connected && _client != null && _client.IsConnected;
        public PlcProtocol Protocol => PlcProtocol.MqttClient;

        public void Connect()
        {
            if (IsConnected)
                return;
            Disconnect();
            _stop.Reset();
            _connectedSignal.Reset();
            _loopError = null;
            _client = new SimpleMqttClient(_options.ToGatewayOptions(_connection));
            _client.Connected += OnConnected;
            _client.Disconnected += OnDisconnected;
            _client.MessageReceived += OnMessageReceived;
            var client = _client;
            _thread = new Thread(() => ReadLoop(client)) { IsBackground = true, Name = "MQTT-Southbound-" + _connection.Host };
            _thread.Start();

            int timeout = Math.Max(100, _connection.TimeoutMilliseconds);
            if (!_connectedSignal.Wait(timeout) || !IsConnected)
            {
                Exception error = _loopError;
                Disconnect();
                if (error != null)
                    throw new PlcCommunicationException("MQTT Broker 连接失败：" + error.Message, error);
                throw new System.TimeoutException("MQTT Broker 连接超时。");
            }
        }

        public ValueTask ConnectAsync(CancellationToken cancellationToken)
        {
            return new ValueTask(Task.Run(Connect, cancellationToken));
        }

        public void Disconnect()
        {
            _connected = false;
            _stop.Set();
            if (_client != null)
            {
                try { _client.Disconnect(); } catch { }
            }
            if (_thread != null && _thread != Thread.CurrentThread)
                _thread.Join(Math.Max(100, _connection.TimeoutMilliseconds));
            if (_client != null)
            {
                _client.Connected -= OnConnected;
                _client.Disconnected -= OnDisconnected;
                _client.MessageReceived -= OnMessageReceived;
                _client.Dispose();
            }
            _client = null;
            _thread = null;
            lock (_writes)
            {
                foreach (var pending in _writes)
                    pending.Completion.TrySetResult(MqttPublishResult.Fail("MQTT 客户端已断开。"));
                _writes.Clear();
            }
            lock (_cacheLock)
            {
                _values.Clear();
                _aliases.Clear();
                _offlineScopes.Clear();
                _birthSequence.Clear();
            }
        }

        public ValueTask DisconnectAsync(CancellationToken cancellationToken)
        {
            Disconnect();
            return ValueTask.CompletedTask;
        }

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            EnsureConnected();
            MqttTagAddress parsed = MqttTagAddress.Parse(address);
            string key = IsSparkplugMode() ? parsed.CacheKey : parsed.Topic;
            if (!_values.TryGetValue(key, out CachedValue cached))
                throw new MqttTagException("MQTT 主题尚未收到值：" + key + "。");
            if (_options.MaxValueAgeSeconds > 0 && DateTime.UtcNow - cached.TimestampUtc > TimeSpan.FromSeconds(_options.MaxValueAgeSeconds))
                throw new MqttTagException("MQTT 主题值已过期：" + key + "。");

            object value = IsSparkplugMode()
                ? MqttPayloadCodec.ConvertMetricValue(cached.Value, dataType)
                : MqttPayloadCodec.DecodeText(
                    Convert.ToString(cached.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                    parsed.Selector,
                    dataType,
                    elementCount,
                    elementOffset,
                    IsJsonMode());
            return new PlcReadResult(0, dataType.ToString(), value);
        }

        public ValueTask<PlcReadResult> ReadAsync(string address, PlcDataType dataType, int elementCount, int elementOffset, CancellationToken cancellationToken)
            => ValueTask.FromResult(Read(address, dataType, elementCount, elementOffset));

        public IList<PlcBatchReadResult> ReadMany(IList<PlcBatchReadRequest> requests)
        {
            List<PlcBatchReadResult> results = new List<PlcBatchReadResult>();
            if (requests == null)
                return results;
            for (int i = 0; i < requests.Count; i++)
            {
                PlcBatchReadRequest request = requests[i];
                try
                {
                    results.Add(PlcBatchReadResult.FromSuccess(request, Read(request.Address, request.DataType, request.ElementCount, request.ElementOffset)));
                }
                catch (Exception ex) when (IsTagError(ex))
                {
                    results.Add(PlcBatchReadResult.FromFailure(request, ex.Message, PlcReadFailureScope.Tag));
                }
                catch (PlcCommunicationException ex)
                {
                    results.Add(PlcBatchReadResult.FromFailure(request, ex.Message, PlcReadFailureScope.Transport));
                    for (int next = i + 1; next < requests.Count; next++)
                        results.Add(PlcBatchReadResult.FromDeferred(requests[next], ex.Message));
                    break;
                }
            }
            return results;
        }

        public ValueTask<IList<PlcBatchReadResult>> ReadManyAsync(IList<PlcBatchReadRequest> requests, CancellationToken cancellationToken)
            => ValueTask.FromResult(ReadMany(requests));

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
            => WriteAsync(address, dataType, valueText, elementOffset, CancellationToken.None).AsTask().GetAwaiter().GetResult();

        public async ValueTask WriteAsync(string address, PlcDataType dataType, string valueText, int elementOffset, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureConnected();
            if (elementOffset != 0)
                throw new NotSupportedException("MQTT 写入不使用元素偏移，请直接设置目标主题。");
            MqttTagAddress parsed = MqttTagAddress.Parse(address);
            byte[] payload;
            if (IsSparkplugMode())
            {
                if (string.IsNullOrWhiteSpace(parsed.Selector))
                    throw new FormatException("Sparkplug B 写入地址必须为 topic|metricName。");
                SparkplugPayload sparkplug = new SparkplugPayload();
                sparkplug.Metrics.Add(SparkplugMetric.FromText(parsed.Selector, dataType.ToString(), valueText));
                payload = SparkplugPayloadEncoder.Encode(sparkplug);
            }
            else
            {
                payload = System.Text.Encoding.UTF8.GetBytes(valueText ?? string.Empty);
            }

            int timeout = Math.Max(1500, _connection.TimeoutMilliseconds);
            var pending = new PendingWrite(parsed.Topic, payload, Environment.TickCount64 + timeout, cancellationToken);
            LinkedListNode<PendingWrite> entry;
            lock (_writes)
            {
                EnsureConnected();
                entry = _writes.AddLast(pending);
            }
            MqttPublishResult result;
            try
            {
                result = await pending.Completion.Task.WaitAsync(TimeSpan.FromMilliseconds(timeout), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
            {
                lock (_writes)
                {
                    if (entry.List != null) _writes.Remove(entry);
                    if (pending.Started)
                        throw new PlcCommunicationException("MQTT 发布已经开始，确认未收到，交付状态未知。", ex);
                }
                throw;
            }
            if (!result.Success) throw new PlcCommunicationException(result.ErrorMessage);
        }

        public void Dispose()
        {
            Disconnect();
            _stop.Dispose();
            _connectedSignal.Dispose();
        }

        private void ReadLoop(SimpleMqttClient client)
        {
            try
            {
                client.ConnectAndReadLoop(new[] { _options.SubscribeFilter }, _stop, DrainWrites, null, true);
            }
            catch (Exception ex)
            {
                if (!ReferenceEquals(client, _client)) return;
                _loopError = ex;
                _connected = false;
                _connectedSignal.Set();
            }
        }

        private void DrainWrites(SimpleMqttClient client)
        {
            while (ReferenceEquals(client, _client) && !_stop.WaitOne(0))
            {
                PendingWrite pending;
                lock (_writes)
                {
                    if (_writes.First == null) return;
                    pending = _writes.First.Value;
                    _writes.RemoveFirst();
                    if (pending.Token.IsCancellationRequested || Environment.TickCount64 >= pending.Deadline)
                    {
                        pending.Completion.TrySetException(new TimeoutException("MQTT 写请求在发送前已取消或过期。"));
                        continue;
                    }
                    pending.Started = true;
                }
                try
                {
                    MqttPublishResult result = client.Publish(
                        pending.Topic,
                        pending.Payload,
                        _options.Qos,
                        Math.Max(100, _connection.TimeoutMilliseconds));
                    pending.Completion.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    pending.Completion.TrySetResult(MqttPublishResult.Fail(ex.Message));
                }
            }
        }

        private void OnConnected(object sender, EventArgs args)
        {
            if (!ReferenceEquals(sender, _client)) return;
            _connected = true;
            _connectedSignal.Set();
        }

        private void OnDisconnected(object sender, EventArgs args)
        {
            if (!ReferenceEquals(sender, _client)) return;
            _connected = false;
        }

        private void OnMessageReceived(object sender, MqttMessageEventArgs args)
        {
            lock (_cacheLock)
            {
                if (!ReferenceEquals(sender, _client) || _stop.WaitOne(0)) return;
                ApplyMessage(args);
            }
        }

        private void ApplyMessage(MqttMessageEventArgs args)
        {
            DateTime now = DateTime.UtcNow;
            if (!IsSparkplugMode())
            {
                _values[args.Topic] = new CachedValue(args.Payload, now);
                return;
            }

            try
            {
                SparkplugPayload payload = SparkplugPayloadDecoder.Decode(args.PayloadBytes);
                string scope = GetSparkplugAliasScope(args.Topic);
                string[] parts = args.Topic.Split('/');
                if (parts.Length is not (4 or 5)) return;
                string kind = parts[2];
                string node = parts[0] + "/" + parts[1] + "/" + parts[3] + "/";
                bool nodeEvent = kind is "NBIRTH" or "NDEATH";
                if (kind is "NDEATH" or "DDEATH" or "NBIRTH" or "DBIRTH")
                {
                    ulong? sequence = null;
                    if (nodeEvent)
                        foreach (var item in payload.Metrics)
                            if (item.Name == "bdSeq" && ulong.TryParse(Convert.ToString(item.Value, CultureInfo.InvariantCulture), out ulong number))
                                sequence = number;
                    if (kind == "NDEATH" && sequence.HasValue && _birthSequence.TryGetValue(node, out var live) && sequence != live)
                        return;
                    bool Matches(string candidate) => nodeEvent ? candidate.StartsWith(node, StringComparison.Ordinal) : candidate == scope;
                    foreach (string key in _values.Keys)
                        if (Matches(GetSparkplugAliasScope(key.Split('|', 2)[0]))) _values.TryRemove(key, out _);
                    foreach (string key in _aliases.Keys)
                        if (Matches(key.Split('|', 2)[0])) _aliases.TryRemove(key, out _);
                    if (kind is "NDEATH" or "DDEATH")
                    {
                        _offlineScopes.Add(scope);
                        return;
                    }
                    _offlineScopes.Remove(scope);
                    if (kind == "NBIRTH")
                    {
                        _birthSequence.Remove(node);
                        if (sequence.HasValue) _birthSequence[node] = sequence.Value;
                    }
                }
                if (_offlineScopes.Contains(node) || _offlineScopes.Contains(scope)) return;
                foreach (SparkplugMetric metric in payload.Metrics)
                {
                    string name = metric.Name;
                    if (!string.IsNullOrWhiteSpace(name) && metric.Alias.HasValue)
                        _aliases[scope + "|" + metric.Alias.Value] = name;
                    if (string.IsNullOrWhiteSpace(name) && metric.Alias.HasValue)
                        _aliases.TryGetValue(scope + "|" + metric.Alias.Value, out name);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    string key = args.Topic + "|" + name;
                    if (metric.IsNull) _values.TryRemove(key, out _);
                    else _values[key] = new CachedValue(metric.Value, now);
                }
            }
            catch (FormatException)
            {
            }
        }

        private bool IsSparkplugMode() => _options.PayloadMode.Equals("SparkplugB", StringComparison.OrdinalIgnoreCase);
        private bool IsJsonMode() => _options.PayloadMode.Equals("Json", StringComparison.OrdinalIgnoreCase);

        private static string GetSparkplugAliasScope(string topic)
        {
            string[] parts = (topic ?? string.Empty).Split('/');
            if (parts.Length < 4)
                return topic ?? string.Empty;
            string device = parts.Length > 4 ? parts[4] : string.Empty;
            return parts[0] + "/" + parts[1] + "/" + parts[3] + "/" + device;
        }

        private static bool IsTagError(Exception exception)
            => exception is MqttTagException || exception is FormatException || exception is OverflowException || exception is NotSupportedException || exception is JsonException;

        private void EnsureConnected()
        {
            if (!IsConnected)
                throw new PlcCommunicationException("MQTT Broker 尚未连接。");
        }

        private sealed class CachedValue
        {
            public CachedValue(object value, DateTime timestampUtc) { Value = value; TimestampUtc = timestampUtc; }
            public object Value { get; }
            public DateTime TimestampUtc { get; }
        }

        private sealed record PendingWrite(string Topic, byte[] Payload, long Deadline, CancellationToken Token)
        {
            public TaskCompletionSource<MqttPublishResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public bool Started { get; set; }
        }
    }
}

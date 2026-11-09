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
        private readonly ConcurrentQueue<PendingWrite> _writes = new ConcurrentQueue<PendingWrite>();
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
            _thread = new Thread(ReadLoop) { IsBackground = true, Name = "MQTT-Southbound-" + _connection.Host };
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
            while (_writes.TryDequeue(out PendingWrite pending))
                pending.Complete(MqttPublishResult.Fail("MQTT 客户端已断开。"));
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
            foreach (PlcBatchReadRequest request in requests)
            {
                try
                {
                    results.Add(PlcBatchReadResult.FromSuccess(request, Read(request.Address, request.DataType, request.ElementCount, request.ElementOffset)));
                }
                catch (Exception ex) when (IsTagError(ex))
                {
                    results.Add(PlcBatchReadResult.FromFailure(request, ex.Message, PlcReadFailureScope.Tag));
                }
            }
            return results;
        }

        public ValueTask<IList<PlcBatchReadResult>> ReadManyAsync(IList<PlcBatchReadRequest> requests, CancellationToken cancellationToken)
            => ValueTask.FromResult(ReadMany(requests));

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
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

            PendingWrite pending = new PendingWrite(parsed.Topic, payload);
            _writes.Enqueue(pending);
            int timeout = Math.Max(1500, _connection.TimeoutMilliseconds);
            if (!pending.Wait(timeout))
                throw new System.TimeoutException("MQTT 写入等待 I/O 线程超时。");
            MqttPublishResult result = pending.Result;
            pending.Dispose();
            if (!result.Success)
                throw new PlcCommunicationException(result.ErrorMessage);
        }

        public ValueTask WriteAsync(string address, PlcDataType dataType, string valueText, int elementOffset, CancellationToken cancellationToken)
            => new ValueTask(Task.Run(() => Write(address, dataType, valueText, elementOffset), cancellationToken));

        public void Dispose()
        {
            Disconnect();
            _stop.Dispose();
            _connectedSignal.Dispose();
        }

        private void ReadLoop()
        {
            try
            {
                _client.ConnectAndReadLoop(_options.SubscribeFilter, _stop, DrainWrites);
            }
            catch (Exception ex)
            {
                _loopError = ex;
                _connected = false;
                _connectedSignal.Set();
            }
        }

        private void DrainWrites(SimpleMqttClient client)
        {
            while (_writes.TryDequeue(out PendingWrite pending))
            {
                try
                {
                    MqttPublishResult result = client.Publish(
                        pending.Topic,
                        pending.Payload,
                        _options.Qos,
                        Math.Max(100, _connection.TimeoutMilliseconds));
                    pending.Complete(result);
                }
                catch (Exception ex)
                {
                    pending.Complete(MqttPublishResult.Fail(ex.Message));
                }
            }
        }

        private void OnConnected(object sender, EventArgs args)
        {
            _connected = true;
            _connectedSignal.Set();
        }

        private void OnDisconnected(object sender, EventArgs args)
        {
            _connected = false;
        }

        private void OnMessageReceived(object sender, MqttMessageEventArgs args)
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
                foreach (SparkplugMetric metric in payload.Metrics)
                {
                    string name = metric.Name;
                    if (!string.IsNullOrWhiteSpace(name) && metric.Alias.HasValue)
                        _aliases[scope + "|" + metric.Alias.Value] = name;
                    if (string.IsNullOrWhiteSpace(name) && metric.Alias.HasValue)
                        _aliases.TryGetValue(scope + "|" + metric.Alias.Value, out name);
                    if (!string.IsNullOrWhiteSpace(name))
                        _values[args.Topic + "|" + name] = new CachedValue(metric.Value, now);
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

        private sealed class PendingWrite : IDisposable
        {
            private readonly ManualResetEventSlim _completed = new ManualResetEventSlim(false);
            public PendingWrite(string topic, byte[] payload) { Topic = topic; Payload = payload; Result = MqttPublishResult.Fail("尚未发送。"); }
            public string Topic { get; }
            public byte[] Payload { get; }
            public MqttPublishResult Result { get; private set; }
            public void Complete(MqttPublishResult result) { Result = result; _completed.Set(); }
            public bool Wait(int milliseconds) => _completed.Wait(milliseconds);
            public void Dispose() => _completed.Dispose();
        }
    }
}

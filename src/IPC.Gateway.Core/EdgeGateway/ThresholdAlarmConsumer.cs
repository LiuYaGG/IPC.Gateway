using System.Globalization;
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;
using IPC.Runtime.Indexing;
using IPC.Runtime.Values;

namespace IPC.EdgeGateway;

internal sealed class ThresholdAlarmConsumer : IDisposable
{
    private readonly RuntimeEngine _runtime;
    private readonly GatewayAlarmJournal _journal;
    private readonly Func<MqttGatewayOptions> _mqttOptions;
    private readonly GatewayMessageLanguage _messageLanguage;
    private readonly object _gate = new();
    private readonly OverflowQueue<Transition> _queue = new(4096);
    private Dictionary<string, AlarmTagState> _states = new(StringComparer.Ordinal);
    private IReadOnlySet<string>? _configuredTags;
    private MqttGatewayOptions? _lastOptions, _publicRoute;
    private Thread? _writer;
    private bool _stopping;

    public ThresholdAlarmConsumer(RuntimeEngine runtime, GatewayAlarmJournal journal, Func<MqttGatewayOptions> mqttOptions, GatewayMessageLanguage? messageLanguage = null)
    {
        _runtime = runtime;
        _journal = journal;
        _mqttOptions = mqttOptions;
        _messageLanguage = messageLanguage ?? new GatewayMessageLanguage();
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_writer != null) return;
            _states = _journal.ReadStates();
            ForgetRemovedThresholds();
            _stopping = false;
            _writer = new Thread(WriteLoop) { Name = "IPC threshold alarm journal", IsBackground = true };
            _writer.Start();
            _runtime.AlarmSampleCommitted += Observe;
        }
    }

    public void Stop()
    {
        Thread? writer;
        lock (_gate)
        {
            writer = _writer;
            if (writer == null) return;
            _runtime.AlarmSampleCommitted -= Observe;
            _stopping = true;
            Monitor.PulseAll(_gate);
        }
        if (!writer.Join(TimeSpan.FromSeconds(15)))
            throw new IOException("The alarm journal could not finish saving pending transitions within 15 seconds.");
        lock (_gate) _writer = null;
    }

    public void Dispose() => Stop();

    public void SetConfiguredTags(IReadOnlySet<string> tags)
    {
        lock (_gate)
        {
            _configuredTags = tags;
            if (_writer != null && !_stopping) ForgetRemovedThresholds();
        }
    }

    // Remove the previous active state in FIFO order with already-observed events.
    // Disabling an alarm is configuration, and must not manufacture a recovery event.
    private void ForgetRemovedThresholds()
    {
        if (_configuredTags == null) return;
        foreach (string key in _states.Keys.Where(key => !_configuredTags.Contains(key)).ToArray())
        {
            _queue.Enqueue(new Transition { Key = key });
            _states.Remove(key);
        }
        if (_queue.Count != 0) Monitor.Pulse(_gate);
    }

    // Runtime calls this synchronously at cache commit, before other subscribers. Only
    // edges enter the writer queue. The normal sampling path never waits on SQLite/MQTT.
    private void Observe(TagValueSnapshot snapshot)
    {
        TagAlarmConfig alarm = snapshot.Alarm;
        if (!double.IsFinite(alarm.LowLimit) || !double.IsFinite(alarm.HighLimit) || alarm.LowLimit > alarm.HighLimit ||
            !TryNumber(snapshot, out double value)) return;
        var (state, threshold, message) = Evaluate(alarm, value);
        string key = TagPath.BuildIdentity(snapshot.ChannelId, snapshot.DeviceId, snapshot.GroupId, snapshot.TagId);
        lock (_gate)
        {
            if (_stopping || (_configuredTags != null && !_configuredTags.Contains(key))) return;
            bool hadState = _states.TryGetValue(key, out var previous);
            string from = hadState ? previous.State : "Normal";
            if (state == from) return;
            if (state == "Normal") threshold = previous.Threshold;
            var transition = new Transition
            {
                Key = key, Snapshot = snapshot, Previous = from, State = state, Value = value,
                Threshold = threshold, Message = message, Route = GetPublishRoute(), English = _messageLanguage.English,
            };
            _queue.Enqueue(transition);
            if (state == "Normal") _states.Remove(key);
            else _states[key] = new(state, threshold);
            Monitor.Pulse(_gate);
        }
    }

    // Queued transitions may spill to disk. Store only routing fields; connection
    // credentials and certificate passwords must never enter that spool.
    private MqttGatewayOptions? GetPublishRoute()
    {
        var options = _mqttOptions();
        if (!options.Enabled || !options.PublishEnabled) return null;
        if (!ReferenceEquals(options, _lastOptions))
        {
            _publicRoute = new MqttGatewayOptions
            {
                GatewayId = options.GatewayId, GatewayName = options.GatewayName, SiteName = options.SiteName,
                PublishTopicTemplate = options.PublishTopicTemplate, CloudProtocolVersion = options.CloudProtocolVersion,
                PublishQos = Math.Clamp(options.PublishQos, 1, 2)
            };
            _lastOptions = options;
        }
        return _publicRoute;
    }

    private void WriteLoop()
    {
        var batch = new List<Transition>(256);
        DateTime nextErrorLog = DateTime.MinValue;
        while (true)
        {
            try
            {
                lock (_gate)
                {
                    if (batch.Count == 0 && _queue.Count == 0 && !_stopping) Monitor.Wait(_gate, TimeSpan.FromMinutes(1));
                    if (batch.Count == 0 && _queue.Count == 0 && _stopping) return;
                    while (_queue.Count != 0 && batch.Count < 256) batch.Add(_queue.Dequeue());
                }
                var events = new List<GatewayAlarmRecord>(batch.Count);
                var states = new Dictionary<string, AlarmTagState?>(StringComparer.Ordinal);
                foreach (var item in batch)
                {
                    if (item.Snapshot == null)
                    {
                        states[item.Key] = null;
                        continue;
                    }
                    var entry = item.ToRecord();
                    events.Add(entry);
                    states[item.Key] = entry.State == "Normal" ? null : new(entry.State, item.Threshold);
                }
                _journal.SaveTransitions(events, states);
                batch.Clear();
                _journal.CleanupIfDue();
            }
            catch (Exception ex)
            {
                if (DateTime.UtcNow >= nextErrorLog)
                {
                    IpcLogService.WriteError("Alarm journal write failed. Retrying the same events in order.", ex);
                    nextErrorLog = DateTime.UtcNow.AddSeconds(30);
                }
                Thread.Sleep(250);
            }
        }
    }

    private static (string State, double Threshold, string Message) Evaluate(TagAlarmConfig alarm, double value)
    {
        static string Text(string message, string fallback) => string.IsNullOrWhiteSpace(message) ? fallback : message;
        if (value > alarm.HighLimit)
            return ("HighAlarm", alarm.HighLimit, Text(alarm.HighAlarmMessage, "Value is above high alarm limit."));
        if (value < alarm.LowLimit)
            return ("LowAlarm", alarm.LowLimit, Text(alarm.LowAlarmMessage, "Value is below low alarm limit."));
        double deviation = alarm.WarningDeviation;
        if (deviation > 0 && double.IsFinite(deviation))
        {
            double high = alarm.HighLimit - deviation, low = alarm.LowLimit + deviation;
            if (double.IsFinite(high) && value >= high)
                return ("HighWarning", high, Text(alarm.HighWarningMessage, "Value reached high warning deviation."));
            if (double.IsFinite(low) && value <= low)
                return ("LowWarning", low, Text(alarm.LowWarningMessage, "Value reached low warning deviation."));
        }
        return ("Normal", 0, "Value returned to the normal range.");
    }

    private static bool TryNumber(TagValueSnapshot snapshot, out double number)
    {
        if (snapshot.Value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
        {
            number = Convert.ToDouble(snapshot.Value, CultureInfo.InvariantCulture);
            return double.IsFinite(number);
        }
        string text = string.IsNullOrWhiteSpace(snapshot.ValueText) ? snapshot.Value as string ?? "" : snapshot.ValueText;
        return (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out number)) && double.IsFinite(number);
    }

    private sealed class Transition
    {
        public string EventId { get; set; } = Guid.NewGuid().ToString("N");
        public string Key { get; set; } = string.Empty;
        public TagValueSnapshot? Snapshot { get; set; }
        public string Previous { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public double Value { get; set; }
        public double Threshold { get; set; }
        public MqttGatewayOptions? Route { get; set; }
        public bool English { get; set; }

        public GatewayAlarmRecord ToRecord()
        {
            bool clear = State == "Normal";
            string active = clear ? Previous : State;
            string severity = active.EndsWith("Warning", StringComparison.Ordinal) ? "warning" : "alarm";
            var s = Snapshot!;
            var item = new GatewayAlarmRecord
            {
                EventId = EventId, Timestamp = s.Timestamp.ToUniversalTime(), Value = Value, Threshold = Threshold,
                AlarmCorrelationKey = "threshold:" + Key,
                ChannelId = s.ChannelId, ChannelName = s.ChannelName, DeviceId = s.DeviceId, DeviceName = s.DeviceName,
                GroupId = s.GroupId, GroupName = s.GroupName, TagId = s.TagId, TagName = s.TagName,
                PreviousState = Previous, State = State, EventType = clear ? "recovery" : severity,
                Direction = active.StartsWith("High", StringComparison.Ordinal) ? "High" : "Low",
                PreviousStateText = RuleMessageText.Label(Previous, English), StateText = RuleMessageText.Label(State, English),
                Message = RuleMessageText.Message(Message, English),
            };
            if (Route != null)
            {
                (item.Topic, item.Payload) = MqttGatewayService.BuildAlarmMessage(Route, s, EventId, severity,
                    State, item.Direction, Value, Threshold, Message, clear ? "clear" : "active", Previous, English);
                item.DeliveryStatus = "Pending";
                item.Qos = Route.PublishQos;
            }
            return item;
        }
    }
}

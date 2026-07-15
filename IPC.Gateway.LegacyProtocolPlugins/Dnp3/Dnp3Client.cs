using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Automatak.DNP3.Adapter;
using Automatak.DNP3.Interface;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Dnp3
{
    public sealed class Dnp3Client : IPlcClient, IPlcBatchReadClient, IAsyncPlcClient, IAsyncPlcBatchReadClient
    {
        private readonly PlcConnectionOptions _connection;
        private readonly Dnp3DriverOptions _options;
        private readonly Dnp3SoeHandler _handler = new Dnp3SoeHandler();
        private readonly Dnp3ChannelListener _listener = new Dnp3ChannelListener();
        private IDNP3Manager _manager;
        private IChannel _channel;
        private IMaster _master;

        public Dnp3Client(PlcConnectionOptions connection)
        {
            _connection = connection ?? new PlcConnectionOptions();
            _options = Dnp3DriverOptions.Parse(_connection);
        }

        public bool IsConnected => _master != null && _listener.State == ChannelState.OPEN;
        public PlcProtocol Protocol => PlcProtocol.Dnp3;

        public void Connect()
        {
            if (IsConnected) return;
            Disconnect();
            _handler.Clear();
            try
            {
                _manager = DNP3ManagerFactory.CreateManager(1, NullLogHandler.Instance);
                ChannelRetry retry = new ChannelRetry(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));
                _channel = _manager.AddTCPClient(
                    "ipc-gateway-dnp3",
                    LogLevels.NORMAL,
                    retry,
                    new List<IPEndpoint> { new IPEndpoint(_connection.Host, checked((ushort)_connection.Port)) },
                    _listener);
                MasterStackConfig config = new MasterStackConfig();
                config.link.localAddr = _options.LocalAddress;
                config.link.remoteAddr = _options.RemoteAddress;
                config.link.responseTimeout = Timeout;
                config.master.responseTimeout = Timeout;
                config.master.taskStartTimeout = Timeout;
                config.master.startupIntegrityClassMask = _options.StartupIntegrity ? ClassField.AllClasses : ClassField.None;
                config.master.disableUnsolOnStartup = !_options.EnableUnsolicited;
                config.master.unsolClassMask = _options.EnableUnsolicited ? ClassField.AllEventClasses : ClassField.None;
                config.master.eventScanOnEventsAvailableClassMask = ClassField.AllEventClasses;
                config.master.integrityOnEventOverflowIIN = true;
                config.master.timeSyncMode = ParseTimeSyncMode(_options.TimeSyncMode);
                _master = _channel.AddMaster("ipc-gateway-master", _handler, DefaultMasterApplication.Instance, config);
                if (_options.EventScanIntervalSeconds > 0)
                    _master.AddClassScan(
                        ClassField.AllEventClasses,
                        TimeSpan.FromSeconds(_options.EventScanIntervalSeconds),
                        _handler,
                        TaskConfig.Default);
                if (_options.IntegrityScanIntervalSeconds > 0)
                    _master.AddClassScan(
                        ClassField.AllClasses,
                        TimeSpan.FromSeconds(_options.IntegrityScanIntervalSeconds),
                        _handler,
                        TaskConfig.Default);
                _master.Enable();
                if (!_listener.WaitOpen(_connection.TimeoutMilliseconds))
                    throw new TimeoutException("DNP3 通道连接超时。");
            }
            catch
            {
                Disconnect();
                throw;
            }
        }

        public ValueTask ConnectAsync(CancellationToken cancellationToken) => new ValueTask(Task.Run(Connect, cancellationToken));

        public void Disconnect()
        {
            try { _master?.Shutdown(); } catch { }
            try { _channel?.Shutdown(); } catch { }
            try { _manager?.Shutdown(); } catch { }
            _master = null;
            _channel = null;
            _manager = null;
            _listener.Reset();
            _handler.Clear();
        }

        public ValueTask DisconnectAsync(CancellationToken cancellationToken)
        {
            Disconnect();
            return ValueTask.CompletedTask;
        }

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            PlcBatchReadRequest request = new PlcBatchReadRequest(address, dataType, elementCount, elementOffset);
            PlcBatchReadResult result = ReadMany(new List<PlcBatchReadRequest> { request })[0];
            if (!result.Success)
                throw new Dnp3TagException(result.ErrorMessage);
            return result.Result;
        }

        public ValueTask<PlcReadResult> ReadAsync(string address, PlcDataType dataType, int elementCount, int elementOffset, CancellationToken cancellationToken)
            => new ValueTask<PlcReadResult>(Task.Run(() => Read(address, dataType, elementCount, elementOffset), cancellationToken));

        public IList<PlcBatchReadResult> ReadMany(IList<PlcBatchReadRequest> requests)
        {
            EnsureConnected();
            if (requests == null || requests.Count == 0)
                return new List<PlcBatchReadResult>();

            List<ReadItem> items = new List<ReadItem>(requests.Count);
            for (int i = 0; i < requests.Count; i++)
            {
                PlcBatchReadRequest request = requests[i];
                try
                {
                    if (request.ElementCount != 1 || request.ElementOffset != 0)
                        throw new Dnp3TagException("DNP3 点位不使用元素数量或偏移。");
                    items.Add(new ReadItem(i, request, Dnp3Address.Parse(request.Address)));
                }
                catch (Exception ex) when (ex is FormatException || ex is Dnp3TagException)
                {
                    items.Add(new ReadItem(i, request, ex.Message));
                }
            }

            DateTime scanStartedUtc = DateTime.UtcNow;
            HashSet<string> scannedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IEnumerable<ReadItem> refreshItems = items.Where(item => item.Address != null && NeedsRefresh(item.Address, scanStartedUtc));
            foreach (IGrouping<Dnp3PointType, ReadItem> group in refreshItems.GroupBy(item => item.Address.PointType))
            {
                foreach (ReadItem item in group)
                    scannedKeys.Add(item.Address.ToString());
                foreach ((ushort start, ushort stop) in BuildRanges(group.Select(item => item.Address.Index)))
                    ExecuteScan(group.Key, start, stop);
            }

            PlcBatchReadResult[] results = new PlcBatchReadResult[requests.Count];
            foreach (ReadItem item in items)
            {
                if (item.Address == null)
                {
                    results[item.Index] = PlcBatchReadResult.FromFailure(item.Request, item.Error, PlcReadFailureScope.Tag);
                    continue;
                }
                if (!_handler.TryGet(item.Address, out Dnp3CachedValue cached) ||
                    (scannedKeys.Contains(item.Address.ToString()) && cached.TimestampUtc < scanStartedUtc))
                {
                    results[item.Index] = PlcBatchReadResult.FromFailure(item.Request, "DNP3 响应中不存在点位 " + item.Address + "。", PlcReadFailureScope.Tag);
                    continue;
                }
                if (!cached.Online)
                {
                    string quality = cached.Quality.ToString("X2", CultureInfo.InvariantCulture);
                    results[item.Index] = PlcBatchReadResult.FromFailure(item.Request, "DNP3 点位质量无效，Flags=0x" + quality + "。", PlcReadFailureScope.Tag);
                    continue;
                }
                try
                {
                    object value = Dnp3ValueCodec.ConvertValue(cached.Value, item.Request.DataType);
                    results[item.Index] = PlcBatchReadResult.FromSuccess(item.Request, new PlcReadResult(0, item.Request.DataType.ToString(), value));
                }
                catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is NotSupportedException)
                {
                    results[item.Index] = PlcBatchReadResult.FromFailure(item.Request, ex.Message, PlcReadFailureScope.Tag);
                }
            }
            return results;
        }

        public ValueTask<IList<PlcBatchReadResult>> ReadManyAsync(IList<PlcBatchReadRequest> requests, CancellationToken cancellationToken)
            => new ValueTask<IList<PlcBatchReadResult>>(Task.Run(() => ReadMany(requests), cancellationToken));

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
            EnsureConnected();
            if (elementOffset != 0) throw new NotSupportedException("DNP3 命令不使用元素偏移。");
            Dnp3Address parsed = Dnp3Address.Parse(address);
            Task<CommandTaskResult> task;
            if (parsed.PointType == Dnp3PointType.BinaryOutput)
            {
                bool value = (bool)Dnp3ValueCodec.ParseCommand(valueText, PlcDataType.Bool);
                ControlRelayOutputBlock command = new ControlRelayOutputBlock(
                    value ? OperationType.LATCH_ON : OperationType.LATCH_OFF, TripCloseCode.NUL, false, 1, 0, 0);
                task = _options.SelectBeforeOperate
                    ? _master.SelectAndOperate(command, parsed.Index, TaskConfig.Default)
                    : _master.DirectOperate(command, parsed.Index, TaskConfig.Default);
            }
            else if (parsed.PointType == Dnp3PointType.AnalogOutput)
            {
                task = CreateAnalogCommand(parsed.Index, dataType, valueText);
            }
            else
            {
                throw new NotSupportedException("DNP3 仅 BinaryOutput 和 AnalogOutput 点位支持写命令。");
            }
            CommandTaskResult result = Wait(task);
            if (result.TaskSummary != TaskCompletion.SUCCESS || result.Results.Any(item => item.Status != CommandStatus.SUCCESS))
                throw new PlcCommunicationException("DNP3 命令失败：" + result);
        }

        public ValueTask WriteAsync(string address, PlcDataType dataType, string valueText, int elementOffset, CancellationToken cancellationToken)
            => new ValueTask(Task.Run(() => Write(address, dataType, valueText, elementOffset), cancellationToken));

        public void Dispose() => Disconnect();

        private void ExecuteScan(Dnp3PointType pointType, ushort start, ushort stop)
        {
            (byte group, byte variation) = GetStaticVariation(pointType);
            TaskCompletion completion = Wait(_master.ScanRange(group, variation, start, stop, _handler, TaskConfig.Default));
            if (completion != TaskCompletion.SUCCESS)
                throw new PlcCommunicationException("DNP3 范围扫描失败：" + completion + "。");
        }

        private Task<CommandTaskResult> CreateAnalogCommand(ushort index, PlcDataType dataType, string valueText)
        {
            if (dataType == PlcDataType.Int16)
            {
                AnalogOutputInt16 command = new AnalogOutputInt16((short)Dnp3ValueCodec.ParseCommand(valueText, dataType));
                return _options.SelectBeforeOperate ? _master.SelectAndOperate(command, index, TaskConfig.Default) : _master.DirectOperate(command, index, TaskConfig.Default);
            }
            if (dataType == PlcDataType.Int32)
            {
                AnalogOutputInt32 command = new AnalogOutputInt32((int)Dnp3ValueCodec.ParseCommand(valueText, dataType));
                return _options.SelectBeforeOperate ? _master.SelectAndOperate(command, index, TaskConfig.Default) : _master.DirectOperate(command, index, TaskConfig.Default);
            }
            if (dataType == PlcDataType.Float)
            {
                AnalogOutputFloat32 command = new AnalogOutputFloat32((float)Dnp3ValueCodec.ParseCommand(valueText, dataType));
                return _options.SelectBeforeOperate ? _master.SelectAndOperate(command, index, TaskConfig.Default) : _master.DirectOperate(command, index, TaskConfig.Default);
            }
            AnalogOutputDouble64 doubleCommand = new AnalogOutputDouble64(Convert.ToDouble(Dnp3ValueCodec.ParseCommand(valueText, dataType), CultureInfo.InvariantCulture));
            return _options.SelectBeforeOperate ? _master.SelectAndOperate(doubleCommand, index, TaskConfig.Default) : _master.DirectOperate(doubleCommand, index, TaskConfig.Default);
        }

        private IEnumerable<(ushort Start, ushort Stop)> BuildRanges(IEnumerable<ushort> indexes)
        {
            ushort[] sorted = indexes.Distinct().OrderBy(value => value).ToArray();
            if (sorted.Length == 0) yield break;
            ushort start = sorted[0], previous = sorted[0];
            for (int i = 1; i < sorted.Length; i++)
            {
                if (sorted[i] - previous > _options.ScanGapLimit + 1)
                {
                    yield return (start, previous);
                    start = sorted[i];
                }
                previous = sorted[i];
            }
            yield return (start, previous);
        }

        private bool NeedsRefresh(Dnp3Address address, DateTime nowUtc)
        {
            if (!_handler.TryGet(address, out Dnp3CachedValue cached))
                return true;
            return _options.CacheMaxAgeMilliseconds > 0 &&
                   nowUtc - cached.TimestampUtc > TimeSpan.FromMilliseconds(_options.CacheMaxAgeMilliseconds);
        }

        private static TimeSyncMode ParseTimeSyncMode(string value)
        {
            return Enum.TryParse(value, true, out TimeSyncMode mode) ? mode : TimeSyncMode.None;
        }

        private T Wait<T>(Task<T> task)
        {
            if (!task.Wait(_connection.TimeoutMilliseconds)) throw new TimeoutException("DNP3 操作超时。");
            return task.GetAwaiter().GetResult();
        }

        private void EnsureConnected()
        {
            if (!IsConnected) throw new PlcCommunicationException("DNP3 通道尚未连接。");
        }

        private TimeSpan Timeout => TimeSpan.FromMilliseconds(Math.Max(100, _connection.TimeoutMilliseconds));

        private static (byte Group, byte Variation) GetStaticVariation(Dnp3PointType type) => type switch
        {
            Dnp3PointType.Binary => (1, 2), Dnp3PointType.DoubleBitBinary => (3, 2),
            Dnp3PointType.Analog => (30, 6), Dnp3PointType.Counter => (20, 1),
            Dnp3PointType.FrozenCounter => (21, 1), Dnp3PointType.BinaryOutput => (10, 2),
            Dnp3PointType.AnalogOutput => (40, 4), _ => throw new NotSupportedException()
        };

        private sealed record ReadItem(int Index, PlcBatchReadRequest Request, Dnp3Address Address, string Error)
        {
            public ReadItem(int index, PlcBatchReadRequest request, Dnp3Address address) : this(index, request, address, string.Empty) { }
            public ReadItem(int index, PlcBatchReadRequest request, string error) : this(index, request, null, error) { }
        }

        private sealed class NullLogHandler : ILogHandler
        {
            public static readonly NullLogHandler Instance = new NullLogHandler();
            public void Log(LogEntry entry) { }
        }

        private sealed class Dnp3ChannelListener : IChannelListener
        {
            private readonly ManualResetEventSlim _open = new ManualResetEventSlim(false);
            public ChannelState State { get; private set; } = ChannelState.CLOSED;
            public void OnStateChange(ChannelState state) { State = state; if (state == ChannelState.OPEN) _open.Set(); }
            public bool WaitOpen(int milliseconds) => _open.Wait(Math.Max(100, milliseconds));
            public void Reset() { State = ChannelState.CLOSED; _open.Reset(); }
        }
    }
}

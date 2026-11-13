#nullable enable

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.CanOpen
{
    internal sealed class SharedCanOpenChannelLease : IDisposable
    {
        private SharedCanOpenChannel? _channel;

        internal SharedCanOpenChannelLease(SharedCanOpenChannel channel)
        {
            _channel = channel;
        }

        public bool IsOpen => _channel?.IsOpen == true;

        public byte[] Upload(CanOpenObjectAddress address)
        {
            try
            {
                return GetChannel().Execute(sdo => sdo.Upload(address));
            }
            catch (TimeoutException ex)
            {
                throw NodeUnavailable(address.NodeId, ex);
            }
        }

        public void Download(CanOpenObjectAddress address, byte[] data)
        {
            try
            {
                GetChannel().Execute(sdo =>
                {
                    sdo.Download(address, data);
                    return true;
                });
            }
            catch (TimeoutException ex)
            {
                throw NodeUnavailable(address.NodeId, ex);
            }
        }

        public void ProbeNode(int nodeId)
        {
            Upload(CanOpenObjectAddress.Parse(
                nodeId.ToString(CultureInfo.InvariantCulture) + ":1000:0",
                nodeId));
        }

        public void SendNmt(byte command, int nodeId)
        {
            GetChannel().SendNmt(command, nodeId);
        }

        public CanOpenPdoValue ReadTpdo(int pdoNumber, int nodeId, int maxAgeMilliseconds)
        {
            return GetChannel().ReadTpdo(pdoNumber, nodeId, maxAgeMilliseconds);
        }

        public void WriteRpdo(int pdoNumber, int nodeId, int byteOffset, int? bitOffset, byte[] data, bool bitValue)
        {
            GetChannel().WriteRpdo(pdoNumber, nodeId, byteOffset, bitOffset, data, bitValue);
        }

        public CanOpenHeartbeatState ReadHeartbeat(int nodeId, int timeoutMilliseconds)
        {
            return GetChannel().ReadHeartbeat(nodeId, timeoutMilliseconds);
        }

        public bool TryReadEmergency(int nodeId, out CanOpenEmergencyState? state)
        {
            return GetChannel().TryReadEmergency(nodeId, out state);
        }

        public void SendSync()
        {
            GetChannel().SendSync();
        }

        public void SendTime(DateTime utc)
        {
            GetChannel().SendTime(utc);
        }

        public void ConfigureSync(int intervalMilliseconds)
        {
            GetChannel().ConfigureSync(intervalMilliseconds);
        }

        public void Dispose()
        {
            SharedCanOpenChannel? channel = Interlocked.Exchange(ref _channel, null);
            if (channel != null)
                SharedCanOpenChannelRegistry.Release(channel);
        }

        private SharedCanOpenChannel GetChannel()
        {
            return _channel ?? throw new ObjectDisposedException(nameof(SharedCanOpenChannelLease));
        }

        private static PlcProtocolException NodeUnavailable(int nodeId, Exception inner)
        {
            return new PlcProtocolException(
                PlcReadFailureScope.Device,
                "CANopen node " + nodeId.ToString(CultureInfo.InvariantCulture) + " did not respond.",
                "CANOPEN-NODE-TIMEOUT",
                inner);
        }
    }

    internal sealed class SharedCanOpenChannel : IDisposable
    {
        private readonly SlcanAdapter _adapter;
        private readonly CanOpenSdoClient _sdo;
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<int, CanOpenHeartbeatState> _heartbeats = new ConcurrentDictionary<int, CanOpenHeartbeatState>();
        private readonly ConcurrentDictionary<int, CanOpenEmergencyState> _emergencies = new ConcurrentDictionary<int, CanOpenEmergencyState>();
        private readonly ConcurrentDictionary<int, CanOpenPdoValue> _tpdos = new ConcurrentDictionary<int, CanOpenPdoValue>();
        private Timer? _syncTimer;
        private int _syncIntervalMilliseconds;

        public SharedCanOpenChannel(string key, PlcConnectionOptions options, int canBitRate)
        {
            Key = key;
            _adapter = new SlcanAdapter(options, canBitRate);
            _adapter.FrameReceived += OnFrameReceived;
            _adapter.Open();
            _sdo = new CanOpenSdoClient(_adapter);
        }

        public string Key { get; }
        public int ReferenceCount { get; set; }
        public bool IsOpen => _adapter.IsOpen;

        public T Execute<T>(Func<CanOpenSdoClient, T> action)
        {
            _operationLock.Wait();
            try
            {
                return action(_sdo);
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public CanOpenPdoValue ReadTpdo(int pdoNumber, int nodeId, int maxAgeMilliseconds)
        {
            ValidatePdo(pdoNumber, nodeId);
            int identifier = GetPdoIdentifier(true, pdoNumber, nodeId);
            if (!_tpdos.TryGetValue(identifier, out CanOpenPdoValue? value))
                throw new PlcProtocolException(PlcReadFailureScope.Tag, "尚未收到 TPDO" + pdoNumber + "（Node " + nodeId + "）。", "CANOPEN-TPDO-MISSING");
            if (maxAgeMilliseconds > 0 && DateTime.UtcNow - value.TimestampUtc > TimeSpan.FromMilliseconds(maxAgeMilliseconds))
                throw new PlcProtocolException(PlcReadFailureScope.Tag, "TPDO" + pdoNumber + " 数据已过期（Node " + nodeId + "）。", "CANOPEN-TPDO-STALE");
            return value;
        }

        public void WriteRpdo(int pdoNumber, int nodeId, int byteOffset, int? bitOffset, byte[] data, bool bitValue)
        {
            ValidatePdo(pdoNumber, nodeId);
            if (byteOffset < 0 || bitOffset is < 0 or > 7)
                throw new ArgumentOutOfRangeException(nameof(byteOffset));
            int start = checked(byteOffset * 8 + bitOffset.GetValueOrDefault());
            int width = bitOffset.HasValue ? 1 : checked(data.Length * 8);
            if (width <= 0 || start > 64 - width)
                throw new ArgumentOutOfRangeException(nameof(byteOffset));

            Execute(sdo =>
            {
                // The mapping defines both DLC and the neighbouring process outputs.
                // Read only neighbours that this write must preserve.
                byte[] ReadEntry(ushort index, byte sub) => sdo.Upload(new CanOpenObjectAddress(nodeId, index, sub));
                ushort comm = (ushort)(0x1400 + pdoNumber - 1);
                uint cobId = ReadMappingWord(ReadEntry(comm, 1));
                if ((cobId & 0xA0000000) != 0 || (cobId & 0x1FFFFFFF) > 0x7FF)
                    throw new NotSupportedException("该 RPDO 未启用或使用了不支持的扩展 CAN ID。");
                ushort map = (ushort)(0x1600 + pdoNumber - 1);
                byte[] size = ReadEntry(map, 0);
                if (size.Length == 0 || size[0] is 0 or > 64)
                    throw new InvalidOperationException("RPDO 映射为空或无效。");
                uint[] entries = new uint[size[0]];
                for (int i = 0; i < entries.Length; i++)
                    entries[i] = ReadMappingWord(ReadEntry(map, (byte)(i + 1)));
                byte[] frame = ComposeRpdo(entries, start, width, bitOffset.HasValue ? new[] { (byte)(bitValue ? 1 : 0) } : data, ReadEntry);
                _adapter.SendFrame(new CanFrame((int)(cobId & 0x7FF), frame));
                return true;
            });
        }

        private static uint ReadMappingWord(byte[] bytes)
        {
            if (bytes.Length != 4) throw new FormatException("CANopen PDO 映射项长度无效。");
            return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        }

        internal static byte[] ComposeRpdo(uint[] entries, int start, int width, byte[] update,
            Func<ushort, byte, byte[]> read)
        {
            int totalBits = 0;
            foreach (uint entry in entries)
            {
                int length = (byte)entry;
                if (length == 0 || length > 64 - totalBits)
                    throw new FormatException("RPDO 映射长度超出经典 CAN 帧。");
                totalBits += length;
            }
            if (start < 0 || width <= 0 || start > totalBits - width)
                throw new ArgumentOutOfRangeException(nameof(start), "写入范围超过设备 RPDO 映射。");

            byte[] frame = new byte[(totalBits + 7) / 8];
            int position = 0;
            foreach (uint entry in entries)
            {
                int length = (byte)entry;
                byte[] current = Array.Empty<byte>();
                if (position < start || position + length > start + width)
                {
                    ushort index = (ushort)(entry >> 16);
                    current = read(index, (byte)(entry >> 8));
                    if (current.Length * 8 < length)
                        throw new FormatException("无法读取完整的 RPDO 相邻映射值。");
                }
                for (int bit = 0; bit < length; bit++, position++)
                {
                    bool replacing = position >= start && position < start + width;
                    int sourceBit = replacing ? position - start : bit;
                    byte[] source = replacing ? update : current;
                    if ((source[sourceBit / 8] & (1 << (sourceBit % 8))) != 0)
                        frame[position / 8] |= (byte)(1 << (position % 8));
                }
            }
            return frame;
        }

        public CanOpenHeartbeatState ReadHeartbeat(int nodeId, int timeoutMilliseconds)
        {
            if (!_heartbeats.TryGetValue(nodeId, out CanOpenHeartbeatState? heartbeat))
                throw NodeOffline(nodeId, "尚未收到 Heartbeat/Boot-up。");
            if (timeoutMilliseconds > 0 && DateTime.UtcNow - heartbeat.TimestampUtc > TimeSpan.FromMilliseconds(timeoutMilliseconds))
                throw NodeOffline(nodeId, "Heartbeat 已超时。");
            return heartbeat;
        }

        public bool TryReadEmergency(int nodeId, out CanOpenEmergencyState? state)
        {
            return _emergencies.TryGetValue(nodeId, out state);
        }

        public void SendSync()
        {
            _adapter.SendFrame(new CanFrame(0x080, Array.Empty<byte>()));
        }

        public void SendTime(DateTime utc)
        {
            DateTime value = utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime();
            DateTime epoch = new DateTime(1984, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            TimeSpan elapsed = value - epoch;
            uint milliseconds = (uint)Math.Max(0, Math.Min(uint.MaxValue, elapsed.TotalMilliseconds % TimeSpan.FromDays(1).TotalMilliseconds));
            ushort days = (ushort)Math.Max(0, Math.Min(ushort.MaxValue, Math.Floor(elapsed.TotalDays)));
            byte[] data = new byte[6];
            Buffer.BlockCopy(BitConverter.GetBytes(milliseconds), 0, data, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(days), 0, data, 4, 2);
            _adapter.SendFrame(new CanFrame(0x100, data));
        }

        public void ConfigureSync(int intervalMilliseconds)
        {
            if (intervalMilliseconds <= 0)
                return;
            lock (_operationLock)
            {
                if (_syncIntervalMilliseconds > 0 && _syncIntervalMilliseconds <= intervalMilliseconds)
                    return;
                _syncIntervalMilliseconds = intervalMilliseconds;
                _syncTimer?.Dispose();
                _syncTimer = new Timer(_ =>
                {
                    try { SendSync(); } catch { }
                }, null, intervalMilliseconds, intervalMilliseconds);
            }
        }

        private void OnFrameReceived(CanFrame frame)
        {
            int identifier = frame.Identifier;
            DateTime timestamp = DateTime.UtcNow;
            if (identifier >= 0x701 && identifier <= 0x77F && frame.Data.Length >= 1)
            {
                int nodeId = identifier - 0x700;
                byte rawState = frame.Data[0];
                CanOpenNodeState state = rawState switch
                {
                    0 => CanOpenNodeState.BootUp,
                    4 => CanOpenNodeState.Stopped,
                    5 => CanOpenNodeState.Operational,
                    127 => CanOpenNodeState.PreOperational,
                    _ => CanOpenNodeState.Unknown
                };
                _heartbeats[nodeId] = new CanOpenHeartbeatState(nodeId, state, rawState, timestamp);
                return;
            }

            if (identifier >= 0x081 && identifier <= 0x0FF && frame.Data.Length >= 3)
            {
                int nodeId = identifier - 0x080;
                ushort errorCode = (ushort)(frame.Data[0] | frame.Data[1] << 8);
                byte[] manufacturer = new byte[Math.Max(0, frame.Data.Length - 3)];
                if (manufacturer.Length > 0)
                    Buffer.BlockCopy(frame.Data, 3, manufacturer, 0, manufacturer.Length);
                _emergencies[nodeId] = new CanOpenEmergencyState(nodeId, errorCode, frame.Data[2], manufacturer, timestamp);
                return;
            }

            for (int pdo = 1; pdo <= 4; pdo++)
            {
                int baseIdentifier = 0x180 + (pdo - 1) * 0x100;
                if (identifier <= baseIdentifier || identifier > baseIdentifier + 0x7F)
                    continue;
                int nodeId = identifier - baseIdentifier;
                byte[] data = new byte[frame.Data.Length];
                Buffer.BlockCopy(frame.Data, 0, data, 0, data.Length);
                _tpdos[identifier] = new CanOpenPdoValue(pdo, nodeId, data, timestamp);
                return;
            }
        }

        private static int GetPdoIdentifier(bool transmit, int pdoNumber, int nodeId)
        {
            int firstBase = transmit ? 0x180 : 0x200;
            return firstBase + (pdoNumber - 1) * 0x100 + nodeId;
        }

        private static void ValidatePdo(int pdoNumber, int nodeId)
        {
            if (pdoNumber is < 1 or > 4)
                throw new ArgumentOutOfRangeException(nameof(pdoNumber));
            if (nodeId is < 1 or > 127)
                throw new ArgumentOutOfRangeException(nameof(nodeId));
        }

        private static PlcProtocolException NodeOffline(int nodeId, string reason)
        {
            return new PlcProtocolException(
                PlcReadFailureScope.Device,
                "CANopen Node " + nodeId.ToString(CultureInfo.InvariantCulture) + " 离线：" + reason,
                "CANOPEN-HEARTBEAT-TIMEOUT");
        }

        public void SendNmt(byte command, int nodeId)
        {
            if (nodeId < 0 || nodeId > 127)
                throw new ArgumentOutOfRangeException(nameof(nodeId));
            _operationLock.Wait();
            try
            {
                _adapter.SendFrame(new CanFrame(0, new[] { command, (byte)nodeId }));
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public void Dispose()
        {
            _syncTimer?.Dispose();
            _adapter.FrameReceived -= OnFrameReceived;
            _adapter.Dispose();
            _operationLock.Dispose();
        }
    }

    internal static class SharedCanOpenChannelRegistry
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, SharedCanOpenChannel> Channels =
            new Dictionary<string, SharedCanOpenChannel>(StringComparer.OrdinalIgnoreCase);

        public static SharedCanOpenChannelLease Acquire(PlcConnectionOptions options, int canBitRate)
        {
            string key = BuildKey(options, canBitRate);
            lock (SyncRoot)
            {
                if (!Channels.TryGetValue(key, out SharedCanOpenChannel? channel))
                {
                    channel = new SharedCanOpenChannel(key, options, canBitRate);
                    Channels.Add(key, channel);
                }

                channel.ReferenceCount++;
                return new SharedCanOpenChannelLease(channel);
            }
        }

        public static void Release(SharedCanOpenChannel channel)
        {
            lock (SyncRoot)
            {
                if (--channel.ReferenceCount > 0)
                    return;
                Channels.Remove(channel.Key);
                channel.Dispose();
            }
        }

        private static string BuildKey(PlcConnectionOptions options, int canBitRate)
        {
            return string.Join("|", new[]
            {
                (options.Host ?? "COM1").Trim().ToUpperInvariant(),
                (options.Port > 0 ? options.Port : 115200).ToString(CultureInfo.InvariantCulture),
                (options.DataBits > 0 ? options.DataBits : 8).ToString(CultureInfo.InvariantCulture),
                options.SerialParity.ToString(),
                options.SerialStopBits.ToString(),
                canBitRate.ToString(CultureInfo.InvariantCulture)
            });
        }
    }
}

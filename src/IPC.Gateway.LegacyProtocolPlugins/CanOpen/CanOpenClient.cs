using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.CanOpen
{
    public sealed class CanOpenClient : IPlcClient, IPlcBatchReadClient
    {
        private readonly PlcConnectionOptions _options;
        private readonly CanOpenDriverOptions _driverOptions;
        private SharedCanOpenChannelLease _channelLease;

        public CanOpenClient(PlcConnectionOptions options)
        {
            _options = options ?? throw new ArgumentNullException("options");
            _driverOptions = CanOpenDriverOptions.Parse(_options.DriverOptionsJson);
            if (string.IsNullOrWhiteSpace(_options.Host))
                _options.Host = "COM1";
            if (_options.Port <= 0)
                _options.Port = 115200;
            if (_options.DataBits <= 0)
                _options.DataBits = 8;
        }

        public bool IsConnected
        {
            get { return _channelLease != null && _channelLease.IsOpen; }
        }

        public PlcProtocol Protocol
        {
            get { return PlcProtocol.CanOpen; }
        }

        public void Connect()
        {
            if (IsConnected)
                return;

            _channelLease = SharedCanOpenChannelRegistry.Acquire(_options, _driverOptions.CanBitRate);
            _channelLease.ConfigureSync(_driverOptions.SyncIntervalMilliseconds);
            if (_driverOptions.ResetCommunicationOnConnect)
                _channelLease.SendNmt(0x82, _driverOptions.DefaultNodeId);
            if (_driverOptions.StartNodeOnConnect)
                _channelLease.SendNmt(0x01, _driverOptions.DefaultNodeId);
            if (_driverOptions.ProbeNodeOnConnect)
                _channelLease.ProbeNode(_driverOptions.DefaultNodeId);
        }

        public void Disconnect()
        {
            SharedCanOpenChannelLease channelLease = _channelLease;
            _channelLease = null;
            if (channelLease != null)
                channelLease.Dispose();
        }

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            EnsureConnected();
            CanOpenServiceAddress service = CanOpenServiceAddress.Parse(address, _driverOptions.DefaultNodeId);
            object value;
            if (service.Kind == CanOpenServiceKind.Sdo)
            {
                CanOpenObjectAddress objectAddress = service.ObjectAddress!.AddSubIndexOffset(elementOffset);
                value = ReadValue(objectAddress, dataType, elementCount);
            }
            else if (service.Kind == CanOpenServiceKind.Tpdo)
            {
                CanOpenPdoValue pdo = _channelLease.ReadTpdo(
                    service.PdoNumber,
                    service.NodeId,
                    _driverOptions.PdoMaxAgeMilliseconds);
                value = DecodePdo(pdo.Data, service, dataType, elementCount, elementOffset);
            }
            else if (service.Kind == CanOpenServiceKind.Heartbeat)
            {
                CanOpenHeartbeatState heartbeat = _channelLease.ReadHeartbeat(service.NodeId, _driverOptions.HeartbeatTimeoutMilliseconds);
                value = dataType == PlcDataType.String
                    ? heartbeat.State.ToString()
                    : CanOpenDataCodec.Decode(dataType, new[] { heartbeat.RawState });
            }
            else if (service.Kind == CanOpenServiceKind.Emergency)
            {
                value = ReadEmergency(service.NodeId, dataType);
            }
            else
            {
                throw new NotSupportedException("CANopen " + service.Kind + " 地址不支持读取。");
            }
            return new PlcReadResult(0, CanOpenDataCodec.GetTypeName(dataType), value);
        }

        public IList<PlcBatchReadResult> ReadMany(IList<PlcBatchReadRequest> requests)
        {
            List<PlcBatchReadResult> results = new List<PlcBatchReadResult>();
            if (requests == null || requests.Count == 0)
                return results;

            if (!IsConnected)
                Connect();

            int maxBatchItems = Math.Max(1, _driverOptions.MaxBatchItems);
            for (int i = 0; i < requests.Count; i++)
            {
                PlcBatchReadRequest request = requests[i] ?? new PlcBatchReadRequest(string.Empty, PlcDataType.Int16, 1, 0);
                try
                {
                    if ((i % maxBatchItems) == 0 && !IsConnected)
                        Connect();

                    PlcReadResult readResult = Read(request.Address, request.DataType, request.ElementCount, request.ElementOffset);
                    results.Add(PlcBatchReadResult.FromSuccess(request, readResult));
                }
                catch (Exception ex)
                {
                    PlcReadFailureScope failureScope = PlcFailureClassifier.Classify(
                        ex,
                        IsCommunicationException(ex) ? PlcReadFailureScope.Transport : PlcReadFailureScope.Tag);
                    if (failureScope == PlcReadFailureScope.Device ||
                        PlcBatchReadResult.IsConnectionFailureScope(failureScope))
                        throw;
                    results.Add(PlcBatchReadResult.FromFailure(
                        request,
                        ex.Message,
                        failureScope));
                }
            }

            return results;
        }

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
            EnsureConnected();
            CanOpenServiceAddress service = CanOpenServiceAddress.Parse(address, _driverOptions.DefaultNodeId);
            if (service.Kind == CanOpenServiceKind.Sdo)
            {
                CanOpenObjectAddress objectAddress = service.ObjectAddress!.AddSubIndexOffset(elementOffset);
                _channelLease.Download(objectAddress, CanOpenDataCodec.Encode(dataType, valueText));
                return;
            }
            if (service.Kind == CanOpenServiceKind.Rpdo)
            {
                bool bitValue = ParseBoolean(valueText);
                byte[] data = service.BitOffset.HasValue ? Array.Empty<byte>() : CanOpenDataCodec.Encode(dataType, valueText);
                int bitIndex = service.BitOffset.GetValueOrDefault() + (service.BitOffset.HasValue ? elementOffset : 0);
                int targetByteOffset = service.BitOffset.HasValue
                    ? checked(service.ByteOffset + bitIndex / 8)
                    : checked(service.ByteOffset + elementOffset * CanOpenDataCodec.GetScalarByteCount(dataType));
                int? targetBitOffset = service.BitOffset.HasValue ? bitIndex % 8 : null;
                _channelLease.WriteRpdo(
                    service.PdoNumber,
                    service.NodeId,
                    targetByteOffset,
                    targetBitOffset,
                    data,
                    bitValue);
                return;
            }
            if (service.Kind == CanOpenServiceKind.Nmt)
            {
                _channelLease.SendNmt(ParseNmtCommand(valueText), service.NodeId);
                return;
            }
            if (service.Kind == CanOpenServiceKind.Sync)
            {
                _channelLease.SendSync();
                return;
            }
            if (service.Kind == CanOpenServiceKind.Time)
            {
                DateTime utc = DateTime.TryParse(valueText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTime parsed)
                    ? parsed.ToUniversalTime()
                    : DateTime.UtcNow;
                _channelLease.SendTime(utc);
                return;
            }
            throw new NotSupportedException("CANopen " + service.Kind + " 地址为只读。");
        }

        public void Dispose()
        {
            Disconnect();
        }

        private object ReadValue(CanOpenObjectAddress address, PlcDataType dataType, int elementCount)
        {
            if (PlcDataTypeHelper.IsArray(dataType))
            {
                int count = Math.Max(1, elementCount);
                Array values = CanOpenDataCodec.CreateArray(dataType, count);
                for (int i = 0; i < count; i++)
                {
                    byte[] raw = _channelLease.Upload(address.AddSubIndexOffset(i));
                    values.SetValue(CanOpenDataCodec.Decode(dataType, raw), i);
                }
                return values;
            }

            byte[] data = _channelLease.Upload(address);
            return CanOpenDataCodec.Decode(dataType, data);
        }

        private static object DecodePdo(
            byte[] data,
            CanOpenServiceAddress address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset)
        {
            data ??= Array.Empty<byte>();
            if (address.BitOffset.HasValue)
            {
                if (dataType != PlcDataType.Bool && dataType != PlcDataType.BoolArray)
                    throw new NotSupportedException("带位偏移的 PDO 地址只支持 Bool 或 BoolArray。");
                int count = dataType == PlcDataType.BoolArray ? Math.Max(1, elementCount) : 1;
                bool[] values = new bool[count];
                for (int i = 0; i < count; i++)
                {
                    int bit = address.BitOffset.Value + elementOffset + i;
                    int byteIndex = address.ByteOffset + bit / 8;
                    EnsurePdoRange(data, byteIndex, 1);
                    values[i] = (data[byteIndex] & (1 << (bit % 8))) != 0;
                }
                return dataType == PlcDataType.Bool ? values[0] : values;
            }

            if (dataType == PlcDataType.String)
            {
                int length = Math.Max(1, elementCount);
                int offset = checked(address.ByteOffset + elementOffset);
                EnsurePdoRange(data, offset, length);
                return System.Text.Encoding.ASCII.GetString(data, offset, length).TrimEnd('\0');
            }

            int scalarSize = CanOpenDataCodec.GetScalarByteCount(dataType);
            int countValues = PlcDataTypeHelper.IsArray(dataType) ? Math.Max(1, elementCount) : 1;
            int start = checked(address.ByteOffset + elementOffset * scalarSize);
            EnsurePdoRange(data, start, checked(countValues * scalarSize));
            if (!PlcDataTypeHelper.IsArray(dataType))
            {
                byte[] raw = new byte[scalarSize];
                Buffer.BlockCopy(data, start, raw, 0, raw.Length);
                return CanOpenDataCodec.Decode(dataType, raw);
            }

            Array valuesArray = CanOpenDataCodec.CreateArray(dataType, countValues);
            for (int i = 0; i < countValues; i++)
            {
                byte[] raw = new byte[scalarSize];
                Buffer.BlockCopy(data, start + i * scalarSize, raw, 0, raw.Length);
                valuesArray.SetValue(CanOpenDataCodec.Decode(dataType, raw), i);
            }
            return valuesArray;
        }

        private object ReadEmergency(int nodeId, PlcDataType dataType)
        {
            if (!_channelLease.TryReadEmergency(nodeId, out CanOpenEmergencyState emergency) || emergency == null)
                return dataType == PlcDataType.String ? "No EMCY received" : CanOpenDataCodec.Decode(dataType, new byte[] { 0, 0 });
            if (dataType == PlcDataType.String)
            {
                return "0x" + emergency.ErrorCode.ToString("X4", CultureInfo.InvariantCulture) +
                       ", register=0x" + emergency.ErrorRegister.ToString("X2", CultureInfo.InvariantCulture) +
                       (emergency.ManufacturerData.Length == 0 ? string.Empty : ", manufacturer=" + Convert.ToHexString(emergency.ManufacturerData));
            }
            return CanOpenDataCodec.Decode(dataType, BitConverter.GetBytes(emergency.ErrorCode));
        }

        private static byte ParseNmtCommand(string value)
        {
            string command = (value ?? string.Empty).Trim();
            if (byte.TryParse(command, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte numeric))
                return numeric;
            return command.ToLowerInvariant() switch
            {
                "start" or "operational" => 0x01,
                "stop" or "stopped" => 0x02,
                "preoperational" or "pre-operational" => 0x80,
                "resetnode" or "reset-node" => 0x81,
                "resetcommunication" or "reset-communication" => 0x82,
                _ => throw new FormatException("NMT 命令应为 Start、Stop、PreOperational、ResetNode 或 ResetCommunication。")
            };
        }

        private static bool ParseBoolean(string value)
        {
            string text = (value ?? string.Empty).Trim();
            return text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   text.Equals("on", StringComparison.OrdinalIgnoreCase) ||
                   text == "1";
        }

        private static void EnsurePdoRange(byte[] data, int offset, int count)
        {
            if (offset < 0 || count < 0 || offset > data.Length || data.Length - offset < count)
                throw new InvalidOperationException("PDO 数据长度不足，请检查 PDO 映射和标签偏移。");
        }

        private void EnsureConnected()
        {
            if (!IsConnected)
                Connect();
            if (_channelLease == null)
                throw new InvalidOperationException("CANopen client is not connected.");
        }

        private static bool IsCommunicationException(Exception exception)
        {
            Exception current = exception;
            while (current != null)
            {
                if (current is TimeoutException ||
                    current is IOException ||
                    current is ObjectDisposedException ||
                    current is InvalidOperationException && (current.Message ?? string.Empty).IndexOf("SLCAN", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                string text = (current.Message ?? string.Empty).ToLowerInvariant();
                if (text.IndexOf("timeout", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("timed out", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("closed", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("not connected", StringComparison.Ordinal) >= 0)
                    return true;

                current = current.InnerException;
            }

            return false;
        }
    }
}

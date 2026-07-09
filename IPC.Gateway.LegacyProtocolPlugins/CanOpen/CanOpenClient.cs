using System;
using System.Collections.Generic;
using System.IO;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.CanOpen
{
    public sealed class CanOpenClient : IPlcClient, IPlcBatchReadClient
    {
        private readonly PlcConnectionOptions _options;
        private readonly CanOpenDriverOptions _driverOptions;
        private SlcanAdapter _adapter;
        private CanOpenSdoClient _sdo;

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
            get { return _adapter != null && _adapter.IsOpen; }
        }

        public PlcProtocol Protocol
        {
            get { return PlcProtocol.CanOpen; }
        }

        public void Connect()
        {
            if (IsConnected)
                return;

            _adapter = new SlcanAdapter(_options, _driverOptions.CanBitRate);
            _adapter.Open();
            _sdo = new CanOpenSdoClient(_adapter);
        }

        public void Disconnect()
        {
            SlcanAdapter adapter = _adapter;
            _adapter = null;
            _sdo = null;
            if (adapter != null)
                adapter.Dispose();
        }

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            EnsureConnected();
            CanOpenObjectAddress objectAddress = CanOpenObjectAddress
                .Parse(address, _driverOptions.DefaultNodeId)
                .AddSubIndexOffset(elementOffset);

            object value = ReadValue(objectAddress, dataType, elementCount);
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
                    results.Add(PlcBatchReadResult.FromFailure(
                        request,
                        ex.Message,
                        IsCommunicationException(ex) ? PlcReadFailureScope.Transport : PlcReadFailureScope.Tag));
                }
            }

            return results;
        }

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
            EnsureConnected();
            CanOpenObjectAddress objectAddress = CanOpenObjectAddress
                .Parse(address, _driverOptions.DefaultNodeId)
                .AddSubIndexOffset(elementOffset);
            byte[] data = CanOpenDataCodec.Encode(dataType, valueText);
            _sdo.Download(objectAddress, data);
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
                    byte[] raw = _sdo.Upload(address.AddSubIndexOffset(i));
                    values.SetValue(CanOpenDataCodec.Decode(dataType, raw), i);
                }
                return values;
            }

            byte[] data = _sdo.Upload(address);
            return CanOpenDataCodec.Decode(dataType, data);
        }

        private void EnsureConnected()
        {
            if (!IsConnected)
                Connect();
            if (_sdo == null)
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

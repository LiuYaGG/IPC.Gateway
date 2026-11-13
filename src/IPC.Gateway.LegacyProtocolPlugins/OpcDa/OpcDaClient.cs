/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.OpcDa
* 项目描述 ：
* 类 名 称 ：OpcDaClient
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.OpcDa
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
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.OpcDa
{
    
    
    
    
    
    
    
    
    
    
    public sealed class OpcDaClient : IPlcClient, IPlcBatchReadClient, IAsyncPlcClient, IAsyncPlcBatchReadClient
    {
        private const int OpcQualityGood = 0xC0;

        private readonly PlcConnectionOptions _options;
        private readonly object _syncRoot;
        private readonly Dictionary<string, OpcDaItemHandle> _itemsByAddress;
        private readonly BoundedSynchronousIoExecutor _executor;
        private readonly OpcDaDataSource _readSource;
        private readonly int _requestedUpdateRate;

        private IOPCServer _server;
        private IOPCItemMgt _itemMgt;
        private IOPCSyncIO _syncIO;
        private object _serverObject;
        private object _groupObject;
        private int _serverGroupHandle;
        private bool _connected;

        public OpcDaClient(PlcConnectionOptions options)
        {
            _options = options ?? new PlcConnectionOptions();
            _syncRoot = new object();
            _itemsByAddress = new Dictionary<string, OpcDaItemHandle>(StringComparer.OrdinalIgnoreCase);
            _executor = new BoundedSynchronousIoExecutor(1, 32, "OPC DA COM", true);
            _readSource = ReadDriverString("opcDaReadSource", "Cache").Equals("Device", StringComparison.OrdinalIgnoreCase)
                ? OpcDaDataSource.Device
                : OpcDaDataSource.Cache;
            _requestedUpdateRate = ReadDriverInt("opcDaUpdateRateMilliseconds", 1000, 50, 60000);
        }

        public bool IsConnected
        {
            get { return _connected && _server != null && _syncIO != null; }
        }

        public PlcProtocol Protocol
        {
            get { return PlcProtocol.OpcDa; }
        }

        public void Connect()
        {
            lock (_syncRoot)
            {
                if (IsConnected)
                    return;

                string progId = GetServerProgId();
                Type serverType = CreateServerType(progId);
                _serverObject = Activator.CreateInstance(serverType);
                _server = (IOPCServer)_serverObject;

                object group;
                AddGroupWithFallback(out group);

                _groupObject = group;
                _itemMgt = (IOPCItemMgt)group;
                _syncIO = QuerySyncIo(group);
                _itemsByAddress.Clear();
                _connected = true;
            }
        }

        public void Disconnect()
        {
            lock (_syncRoot)
            {
                _connected = false;
                _itemsByAddress.Clear();

                ReleaseComObject(ref _syncIO);
                ReleaseComObject(ref _itemMgt);
                ReleaseComObject(ref _groupObject);
                ReleaseComObject(ref _server);
                ReleaseComObject(ref _serverObject);
                _serverGroupHandle = 0;
            }
        }

        public ValueTask ConnectAsync(CancellationToken cancellationToken)
        {
            return _executor.InvokeAsync(Connect, cancellationToken);
        }

        public ValueTask DisconnectAsync(CancellationToken cancellationToken)
        {
            return _executor.InvokeAsync(Disconnect, cancellationToken);
        }

        public ValueTask<PlcReadResult> ReadAsync(
            string address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            return _executor.InvokeAsync(
                () => Read(address, dataType, elementCount, elementOffset),
                cancellationToken);
        }

        public ValueTask<IList<PlcBatchReadResult>> ReadManyAsync(
            IList<PlcBatchReadRequest> requests,
            CancellationToken cancellationToken)
        {
            return _executor.InvokeAsync(() => ReadMany(requests), cancellationToken);
        }

        public ValueTask WriteAsync(
            string address,
            PlcDataType dataType,
            string valueText,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            return _executor.InvokeAsync(
                () => Write(address, dataType, valueText, elementOffset),
                cancellationToken);
        }

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            lock (_syncRoot)
            {
                EnsureConnected();
                OpcDaItemHandle item = GetOrAddItem(address);
                int[] handles = new[] { item.ServerHandle };
                IntPtr valuesPointer;
                IntPtr errorsPointer;
                _syncIO.Read(_readSource, 1, handles, out valuesPointer, out errorsPointer);

                try
                {
                    int error = ReadError(errorsPointer, 0);
                    if (error != 0)
                        Marshal.ThrowExceptionForHR(error);

                    OpcDaItemState state = (OpcDaItemState)Marshal.PtrToStructure(valuesPointer, typeof(OpcDaItemState));
                    return DecodeItemValue(item, state, dataType, elementCount, elementOffset);
                }
                finally
                {
                    DestroyItemStates(valuesPointer, 1);
                    FreeCoTaskMemory(errorsPointer);
                }
            }
        }

        public IList<PlcBatchReadResult> ReadMany(IList<PlcBatchReadRequest> requests)
        {
            if (requests == null || requests.Count == 0) return Array.Empty<PlcBatchReadResult>();
            lock (_syncRoot)
            {
                EnsureConnected();
                var ordered = new PlcBatchReadResult[requests.Count];
                var indexes = Enumerable.Range(0, requests.Count).ToArray();
                try
                {
                    PlcBatchReadRecovery.Start(this, "OPC DA").ReadRange(indexes, 0, indexes.Length,
                        i => EnsureRequest(requests[i]), i => i, ReadRange, ordered, "SyncIO");
                }
                catch (Exception ex) { PlcBatchReadResult.DeferPending(requests, ordered, ex.Message); }
                return ordered;

                void ReadRange(int start, int end)
                {
                    var range = new List<PlcBatchReadRequest>(end - start);
                    for (int i = start; i < end; i++) range.Add(EnsureRequest(requests[i]));
                    var errors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    try { PreloadItems(range, errors); }
                    catch (Exception ex)
                    {
                        var scope = IsCommunicationException(ex) ? PlcReadFailureScope.Transport
                            : range.Count == 1 ? PlcReadFailureScope.Tag : PlcReadFailureScope.Batch;
                        if (scope == PlcReadFailureScope.Transport)
                            for (int i = start; i < end; i++)
                                if (_itemsByAddress.ContainsKey((range[i - start].Address ?? string.Empty).Trim()))
                                    ordered[i] = PlcBatchReadResult.FromDeferred(range[i - start], ex.Message);
                        throw new PlcProtocolException(scope, ex.Message);
                    }
                    var pending = new List<PendingRead>(range.Count);
                    for (int i = start; i < end; i++)
                    {
                        var request = range[i - start];
                        string id = (request.Address ?? string.Empty).Trim();
                        if (_itemsByAddress.TryGetValue(id, out var item))
                            pending.Add(new PendingRead(i, request, item));
                        else
                        {
                            string message = errors.TryGetValue(id, out int hr)
                                ? Marshal.GetExceptionForHR(hr)?.Message ?? $"OPC DA AddItems returned 0x{hr:X8}"
                                : "OPC DA ItemID is empty or missing: " + id;
                            ordered[i] = PlcBatchReadResult.FromFailure(request, message, PlcReadFailureScope.Tag);
                        }
                    }
                    if (pending.Count > 0) ReadPendingItems(pending, ordered);
                }
            }
        }

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
            lock (_syncRoot)
            {
                if (elementOffset != 0)
                    throw new NotSupportedException("OPC DA SyncIO cannot write an element range. Configure an individually addressable ItemID.");
                EnsureConnected();
                OpcDaItemHandle item = GetOrAddItem(address);
                int[] handles = new[] { item.ServerHandle };
                object parsed = ParseValue(dataType, valueText);
                bool arrayItem = (item.CanonicalDataType & (int)VarEnum.VT_ARRAY) != 0;
                if (arrayItem && parsed is not Array)
                    throw new NotSupportedException("OPC DA cannot replace an array Item with a scalar.");
                bool dynamicType = item.CanonicalDataType == (int)VarEnum.VT_EMPTY || item.CanonicalDataType == (int)VarEnum.VT_VARIANT;
                if (parsed is Array || dynamicType)
                {
                    // SyncIO writes the complete Item. Never shrink an array
                    // when the tag represents only its first few elements.
                    IntPtr states, errors;
                    _syncIO.Read(OpcDaDataSource.Device, 1, handles, out states, out errors);
                    try
                    {
                        Marshal.ThrowExceptionForHR(ReadError(errors, 0));
                        var current = Marshal.PtrToStructure<OpcDaItemState>(states);
                        if (parsed is Array replacement ? current.Value is not Array existing || existing.Rank != 1 ||
                            existing.Length != replacement.Length : current.Value is Array)
                            throw new NotSupportedException("OPC DA array writes require the complete array at offset zero.");
                    }
                    finally { DestroyItemStates(states, 1); FreeCoTaskMemory(errors); }
                }
                object[] values = new[] { parsed };
                IntPtr errorsPointer;
                _syncIO.Write(1, handles, values, out errorsPointer);

                try
                {
                    int error = ReadError(errorsPointer, 0);
                    if (error != 0)
                        Marshal.ThrowExceptionForHR(error);
                }
                finally
                {
                    FreeCoTaskMemory(errorsPointer);
                }
            }
        }

        public void Dispose()
        {
            Disconnect();
            _executor.Dispose();
        }

        private string GetServerProgId()
        {
            string progId = _options.OpcDaServerProgId;
            if (string.IsNullOrWhiteSpace(progId))
                progId = _options.DriverId;
            if (string.IsNullOrWhiteSpace(progId))
                throw new InvalidOperationException("OPC DA Server ProgID cannot be empty.");
            return progId.Trim();
        }

        private string ReadDriverString(string name, string fallback)
        {
            if (string.IsNullOrWhiteSpace(_options.DriverOptionsJson))
                return fallback;
            try
            {
                using JsonDocument document = JsonDocument.Parse(_options.DriverOptionsJson);
                return document.RootElement.TryGetProperty(name, out JsonElement value) &&
                       value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? fallback
                    : fallback;
            }
            catch (JsonException)
            {
                return fallback;
            }
        }

        private int ReadDriverInt(string name, int fallback, int min, int max)
        {
            if (string.IsNullOrWhiteSpace(_options.DriverOptionsJson))
                return fallback;
            try
            {
                using JsonDocument document = JsonDocument.Parse(_options.DriverOptionsJson);
                return document.RootElement.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int number)
                    ? Math.Clamp(number, min, max)
                    : fallback;
            }
            catch (JsonException)
            {
                return fallback;
            }
        }

        private string GetGroupName()
        {
            return string.IsNullOrWhiteSpace(_options.OpcDaGroupName) ? "IPC" : _options.OpcDaGroupName.Trim();
        }

        private void AddGroupWithFallback(out object group)
        {
            Guid itemMgtId = typeof(IOPCItemMgt).GUID;
            int revisedUpdateRate;
            Exception lastException = null;

            int[] localeIds = BuildLocaleCandidates();
            int[] updateRates = BuildUpdateRateCandidates();
            for (int r = 0; r < updateRates.Length; r++)
            {
                for (int l = 0; l < localeIds.Length; l++)
                {
                    try
                    {
                        _server.AddGroup(
                            GetGroupName(),
                            1,
                            updateRates[r],
                            1,
                            IntPtr.Zero,
                            IntPtr.Zero,
                            localeIds[l],
                            out _serverGroupHandle,
                            out revisedUpdateRate,
                            ref itemMgtId,
                            out group);
                        return;
                    }
                    catch (COMException ex)
                    {
                        lastException = ex;
                    }
                    catch (ArgumentException ex)
                    {
                        lastException = ex;
                    }
                }
            }

            throw new PlcCommunicationException("OPC DA AddGroup failed. Please check KepServerEX OPC DA settings and DCOM permissions.", lastException);
        }

        private static int[] BuildLocaleCandidates()
        {
            List<int> values = new List<int>();
            AddDistinct(values, 0);
            AddDistinct(values, 1033);
            AddDistinct(values, CultureInfo.CurrentCulture.LCID);
            AddDistinct(values, CultureInfo.InstalledUICulture.LCID);
            return values.ToArray();
        }

        private int[] BuildUpdateRateCandidates()
        {
            List<int> values = new List<int>();
            AddDistinct(values, _requestedUpdateRate);
            AddDistinct(values, Math.Max(100, Math.Min(60000, _options.TimeoutMilliseconds)));
            AddDistinct(values, 500);
            return values.ToArray();
        }

        private static void AddDistinct(List<int> values, int value)
        {
            if (!values.Contains(value))
                values.Add(value);
        }

        private Type CreateServerType(string progId)
        {
            string host = string.IsNullOrWhiteSpace(_options.Host) ? string.Empty : _options.Host.Trim();
            Type type = string.IsNullOrWhiteSpace(host) ||
                        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                ? Type.GetTypeFromProgID(progId, true)
                : Type.GetTypeFromProgID(progId, host, true);

            if (type == null)
                throw new InvalidOperationException("OPC DA server was not found: " + progId);
            return type;
        }

        private static IOPCSyncIO QuerySyncIo(object group)
        {
            IntPtr unknown = IntPtr.Zero;
            IntPtr syncIoPointer = IntPtr.Zero;
            try
            {
                unknown = Marshal.GetIUnknownForObject(group);
                Guid syncIoId = typeof(IOPCSyncIO).GUID;
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, syncIoId, out syncIoPointer));
                return (IOPCSyncIO)Marshal.GetObjectForIUnknown(syncIoPointer);
            }
            finally
            {
                if (syncIoPointer != IntPtr.Zero)
                    Marshal.Release(syncIoPointer);
                if (unknown != IntPtr.Zero)
                    Marshal.Release(unknown);
            }
        }

        private OpcDaItemHandle GetOrAddItem(string address)
        {
            string itemId = NormalizeItemId(address);
            OpcDaItemHandle item;
            if (_itemsByAddress.TryGetValue(itemId, out item))
                return item;

            OpcDaItemDef[] definitions = new[]
            {
                new OpcDaItemDef
                {
                    AccessPath = string.Empty,
                    ItemId = itemId,
                    Active = 1,
                    ClientHandle = _itemsByAddress.Count + 1,
                    BlobSize = 0,
                    Blob = IntPtr.Zero,
                    RequestedDataType = 0,
                    Reserved = 0
                }
            };

            IntPtr resultsPointer;
            IntPtr errorsPointer;
            _itemMgt.AddItems(1, definitions, out resultsPointer, out errorsPointer);

            try
            {
                int error = ReadError(errorsPointer, 0);
                if (error != 0)
                    Marshal.ThrowExceptionForHR(error);

                OpcDaItemResult result = (OpcDaItemResult)Marshal.PtrToStructure(resultsPointer, typeof(OpcDaItemResult));
                item = new OpcDaItemHandle(itemId, result.ServerHandle, result.CanonicalDataType);
                _itemsByAddress[itemId] = item;
                return item;
            }
            finally
            {
                DestroyItemResults(resultsPointer, 1);
                FreeCoTaskMemory(errorsPointer);
            }
        }

        private void PreloadItems(IList<PlcBatchReadRequest> requests, Dictionary<string, int> errors)
        {
            List<string> missing = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < requests.Count; index++)
            {
                string itemId = (requests[index]?.Address ?? string.Empty).Trim();
                if (itemId.Length > 0 && !_itemsByAddress.ContainsKey(itemId) && seen.Add(itemId))
                    missing.Add(itemId);
            }
            if (missing.Count == 0)
                return;

            OpcDaItemDef[] definitions = new OpcDaItemDef[missing.Count];
            for (int index = 0; index < missing.Count; index++)
            {
                definitions[index] = new OpcDaItemDef
                {
                    AccessPath = string.Empty,
                    ItemId = missing[index],
                    Active = 1,
                    ClientHandle = _itemsByAddress.Count + index + 1,
                    BlobSize = 0,
                    Blob = IntPtr.Zero,
                    RequestedDataType = 0,
                    Reserved = 0
                };
            }

            IntPtr resultsPointer = IntPtr.Zero;
            IntPtr errorsPointer = IntPtr.Zero;
            _itemMgt.AddItems(definitions.Length, definitions, out resultsPointer, out errorsPointer);
            try
            {
                int resultSize = Marshal.SizeOf(typeof(OpcDaItemResult));
                for (int index = 0; index < definitions.Length; index++)
                {
                    int hr = ReadError(errorsPointer, index);
                    if (hr < 0)
                    {
                        errors[missing[index]] = hr;
                        continue;
                    }
                    IntPtr resultPointer = IntPtr.Add(resultsPointer, index * resultSize);
                    OpcDaItemResult result = (OpcDaItemResult)Marshal.PtrToStructure(resultPointer, typeof(OpcDaItemResult));
                    _itemsByAddress[missing[index]] = new OpcDaItemHandle(
                        missing[index],
                        result.ServerHandle,
                        result.CanonicalDataType);
                }
            }
            finally
            {
                DestroyItemResults(resultsPointer, definitions.Length);
                FreeCoTaskMemory(errorsPointer);
            }
        }

        private void ReadPendingItems(List<PendingRead> pending, PlcBatchReadResult[] ordered)
        {
            int[] handles = new int[pending.Count];
            for (int i = 0; i < pending.Count; i++)
                handles[i] = pending[i].Item.ServerHandle;

            IntPtr valuesPointer = IntPtr.Zero;
            IntPtr errorsPointer = IntPtr.Zero;
            try
            {
                _syncIO.Read(_readSource, pending.Count, handles, out valuesPointer, out errorsPointer);
                int stateSize = Marshal.SizeOf(typeof(OpcDaItemState));

                for (int i = 0; i < pending.Count; i++)
                {
                    PendingRead read = pending[i];
                    try
                    {
                        int error = ReadError(errorsPointer, i);
                        if (error != 0)
                            Marshal.ThrowExceptionForHR(error);

                        IntPtr statePointer = IntPtr.Add(valuesPointer, i * stateSize);
                        OpcDaItemState state = (OpcDaItemState)Marshal.PtrToStructure(statePointer, typeof(OpcDaItemState));
                        ordered[read.Index] = BuildReadResult(read, state);
                    }
                    catch (Exception ex)
                    {
                        ordered[read.Index] = PlcBatchReadResult.FromFailure(read.Request, ex.Message, false);
                    }
                }
            }
            catch (Exception ex)
            {
                var scope = IsCommunicationException(ex) ? PlcReadFailureScope.Transport
                    : pending.Count == 1 ? PlcReadFailureScope.Tag : PlcReadFailureScope.Batch;
                throw new PlcProtocolException(scope, ex.Message);
            }
            finally
            {
                DestroyItemStates(valuesPointer, pending.Count);
                FreeCoTaskMemory(errorsPointer);
            }
        }

        private static PlcBatchReadResult BuildReadResult(PendingRead read, OpcDaItemState state)
        {
            return PlcBatchReadResult.FromSuccess(read.Request, DecodeItemValue(read.Item, state,
                read.Request.DataType, read.Request.ElementCount, read.Request.ElementOffset));
        }

        private static PlcReadResult DecodeItemValue(OpcDaItemHandle item, OpcDaItemState state, PlcDataType type, int count, int offset)
        {
            int quality = state.Quality & 0xC0;
            if (quality != OpcQualityGood && quality != 0x40)
                throw new PlcProtocolException(PlcReadFailureScope.Tag, "OPC DA Item has bad quality: " + item.ItemId,
                    "QUALITY-0x" + state.Quality.ToString("X4", CultureInfo.InvariantCulture));
            long fileTime = ((long)state.Timestamp.dwHighDateTime << 32) | (uint)state.Timestamp.dwLowDateTime;
            DateTime? source = null;
            if (fileTime > 0)
            {
                try { source = DateTime.FromFileTimeUtc(fileTime); }
                catch (ArgumentOutOfRangeException) { }
            }
            return new PlcReadResult((ushort)item.CanonicalDataType, VarTypeName(item.CanonicalDataType), ConvertForRead(state.Value, type, count, offset))
            {
                SourceTimestampUtc = source,
                IsUncertain = quality == 0x40
            };
        }

        private static bool IsCommunicationException(Exception exception)
        {
            Exception current = exception;
            while (current != null)
            {
                if (current is PlcCommunicationException ||
                    current is TimeoutException ||
                    current is IOException ||
                    current is SocketException ||
                    current is ObjectDisposedException)
                    return true;

                COMException comException = current as COMException;
                if (comException != null && IsCommunicationComException(comException))
                    return true;

                string text = (current.Message ?? string.Empty).ToLowerInvariant();
                if (text.IndexOf("timeout", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("timed out", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("socket", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("closed", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("not connected", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("disconnected", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("unavailable", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("rpc server", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("unreachable", StringComparison.Ordinal) >= 0)
                    return true;

                current = current.InnerException;
            }

            return false;
        }

        private static bool IsCommunicationComException(COMException exception)
        {
            uint errorCode = unchecked((uint)exception.ErrorCode);
            return errorCode == 0x80010105U ||
                   errorCode == 0x80010108U ||
                   errorCode == 0x800401FDU ||
                   errorCode == 0x800706BAU ||
                   errorCode == 0x800706BEU;
        }

        private static string NormalizeItemId(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentException("OPC DA ItemID cannot be empty.", "address");
            return address.Trim();
        }

        private void EnsureConnected()
        {
            if (!IsConnected)
                throw new InvalidOperationException("OPC DA client is not connected.");
        }

        private static PlcBatchReadRequest EnsureRequest(PlcBatchReadRequest request)
        {
            return request ?? new PlcBatchReadRequest(string.Empty, PlcDataType.Int16, 1, 0);
        }

        private static object ConvertForRead(object value, PlcDataType dataType, int elementCount, int elementOffset)
        {
            bool isArray = PlcDataTypeHelper.IsArray(dataType);
            if (value is not Array source)
            {
                if (isArray || elementOffset != 0) throw new PlcTagException("OPC DA Item does not expose an array.");
                return ConvertScalar(value, dataType);
            }
            int count = isArray ? elementCount : 1;
            if (source.Rank != 1 || elementOffset < 0 || count < 1 || elementOffset > source.Length - count)
                throw new PlcTagException("OPC DA array range is outside the available elements.");
            int first = checked(source.GetLowerBound(0) + elementOffset);
            if (!isArray) return ConvertScalar(source.GetValue(first), dataType);
            Array result = PlcDataTypeHelper.CreateArray(dataType, count);
            for (int i = 0; i < count; i++)
                result.SetValue(ConvertScalar(source.GetValue(first + i), ArrayElementType(dataType)), i);
            return result;
        }

        private static object ParseValue(PlcDataType dataType, string valueText)
        {
            if (PlcDataTypeHelper.IsArray(dataType))
            {
                string[] parts = string.IsNullOrWhiteSpace(valueText)
                    ? new[] { string.Empty }
                    : valueText.Split(new[] { ',', ';', '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                Array values = PlcDataTypeHelper.CreateArray(dataType, Math.Max(1, parts.Length));
                PlcDataType elementType = ArrayElementType(dataType);
                for (int i = 0; i < values.Length; i++)
                    values.SetValue(ConvertScalar(parts[i], elementType), i);
                return values;
            }

            return ConvertScalar(valueText, dataType);
        }

        private static PlcDataType ArrayElementType(PlcDataType dataType)
        {
            switch (dataType)
            {
                case PlcDataType.BoolArray:
                    return PlcDataType.Bool;
                case PlcDataType.Int8Array:
                    return PlcDataType.Int8;
                case PlcDataType.ByteArray:
                    return PlcDataType.Byte;
                case PlcDataType.UInt8Array:
                    return PlcDataType.UInt8;
                case PlcDataType.Int16Array:
                    return PlcDataType.Int16;
                case PlcDataType.UInt16Array:
                    return PlcDataType.UInt16;
                case PlcDataType.Int32Array:
                    return PlcDataType.Int32;
                case PlcDataType.UInt32Array:
                    return PlcDataType.UInt32;
                case PlcDataType.Int64Array:
                    return PlcDataType.Int64;
                case PlcDataType.UInt64Array:
                    return PlcDataType.UInt64;
                case PlcDataType.FloatArray:
                    return PlcDataType.Float;
                case PlcDataType.DoubleArray:
                    return PlcDataType.Double;
                case PlcDataType.CoilArray:
                    return PlcDataType.Coil;
                case PlcDataType.DiscreteInputArray:
                    return PlcDataType.DiscreteInput;
                default:
                    return dataType;
            }
        }

        private static object ConvertScalar(object value, PlcDataType dataType)
        {
            if (value == null)
                value = string.Empty;

            switch (dataType)
            {
                case PlcDataType.Bool:
                case PlcDataType.Coil:
                case PlcDataType.DiscreteInput:
                    return ParseBool(value);
                case PlcDataType.Int8:
                    return Convert.ToSByte(value, CultureInfo.InvariantCulture);
                case PlcDataType.Byte:
                case PlcDataType.UInt8:
                    return Convert.ToByte(value, CultureInfo.InvariantCulture);
                case PlcDataType.Int16:
                    return Convert.ToInt16(value, CultureInfo.InvariantCulture);
                case PlcDataType.UInt16:
                    return Convert.ToUInt16(value, CultureInfo.InvariantCulture);
                case PlcDataType.Int32:
                    return Convert.ToInt32(value, CultureInfo.InvariantCulture);
                case PlcDataType.UInt32:
                    return Convert.ToUInt32(value, CultureInfo.InvariantCulture);
                case PlcDataType.Int64:
                    return Convert.ToInt64(value, CultureInfo.InvariantCulture);
                case PlcDataType.UInt64:
                    return Convert.ToUInt64(value, CultureInfo.InvariantCulture);
                case PlcDataType.Float:
                    return Convert.ToSingle(value, CultureInfo.InvariantCulture);
                case PlcDataType.Double:
                    return Convert.ToDouble(value, CultureInfo.InvariantCulture);
                case PlcDataType.String:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
                default:
                    throw new ArgumentOutOfRangeException("dataType");
            }
        }

        private static bool ParseBool(object value)
        {
            if (value is bool)
                return (bool)value;

            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(text))
                return false;

            text = text.Trim();
            if (string.Equals(text, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "on", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(text, "0", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "off", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "no", StringComparison.OrdinalIgnoreCase))
                return false;

            return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }

        private static int ReadError(IntPtr errorsPointer, int index)
        {
            if (errorsPointer == IntPtr.Zero)
                return 0;
            return Marshal.ReadInt32(errorsPointer, index * 4);
        }

        private static void DestroyItemResults(IntPtr pointer, int count)
        {
            if (pointer == IntPtr.Zero)
                return;

            int size = Marshal.SizeOf(typeof(OpcDaItemResult));
            for (int i = 0; i < count; i++)
            {
                IntPtr current = IntPtr.Add(pointer, i * size);
                OpcDaItemResult result = (OpcDaItemResult)Marshal.PtrToStructure(current, typeof(OpcDaItemResult));
                FreeCoTaskMemory(result.Blob);
                Marshal.DestroyStructure(current, typeof(OpcDaItemResult));
            }
            FreeCoTaskMemory(pointer);
        }

        private static void DestroyItemStates(IntPtr pointer, int count)
        {
            if (pointer == IntPtr.Zero)
                return;

            int size = Marshal.SizeOf(typeof(OpcDaItemState));
            for (int i = 0; i < count; i++)
            {
                IntPtr current = IntPtr.Add(pointer, i * size);
                Marshal.DestroyStructure(current, typeof(OpcDaItemState));
            }
            FreeCoTaskMemory(pointer);
        }

        private static void FreeCoTaskMemory(IntPtr pointer)
        {
            if (pointer != IntPtr.Zero)
                Marshal.FreeCoTaskMem(pointer);
        }

        private static void ReleaseComObject<T>(ref T value) where T : class
        {
            object instance = value;
            value = null;
            if (instance != null && Marshal.IsComObject(instance))
                Marshal.FinalReleaseComObject(instance);
        }

        private static string VarTypeName(short value)
        {
            try
            {
                return ((VarEnum)value).ToString();
            }
            catch
            {
                return value.ToString(CultureInfo.InvariantCulture);
            }
        }

        private sealed class PendingRead
        {
            public PendingRead(int index, PlcBatchReadRequest request, OpcDaItemHandle item)
            {
                Index = index;
                Request = request;
                Item = item;
            }

            public int Index { get; private set; }
            public PlcBatchReadRequest Request { get; private set; }
            public OpcDaItemHandle Item { get; private set; }
        }

        private sealed class OpcDaItemHandle
        {
            public OpcDaItemHandle(string itemId, int serverHandle, short canonicalDataType)
            {
                ItemId = itemId;
                ServerHandle = serverHandle;
                CanonicalDataType = canonicalDataType;
            }

            public string ItemId { get; private set; }
            public int ServerHandle { get; private set; }
            public short CanonicalDataType { get; private set; }
        }
    }

    internal enum OpcDaDataSource
    {
        Cache = 1,
        Device = 2
    }

    [ComImport]
    [Guid("39C13A4D-011E-11D0-9675-0020AFD8ADB3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IOPCServer
    {
        void AddGroup(
            [MarshalAs(UnmanagedType.LPWStr)] string name,
            int active,
            int requestedUpdateRate,
            int clientHandle,
            IntPtr timeBias,
            IntPtr percentDeadband,
            int localeId,
            out int serverGroupHandle,
            out int revisedUpdateRate,
            ref Guid interfaceId,
            [MarshalAs(UnmanagedType.IUnknown)] out object group);
    }

    [ComImport]
    [Guid("39C13A54-011E-11D0-9675-0020AFD8ADB3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IOPCItemMgt
    {
        void AddItems(
            int count,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] OpcDaItemDef[] itemArray,
            out IntPtr addResults,
            out IntPtr errors);
    }

    [ComImport]
    [Guid("39C13A52-011E-11D0-9675-0020AFD8ADB3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IOPCSyncIO
    {
        void Read(
            OpcDaDataSource source,
            int count,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] int[] serverHandles,
            out IntPtr itemValues,
            out IntPtr errors);

        void Write(
            int count,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] int[] serverHandles,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.Struct, SizeParamIndex = 0)] object[] values,
            out IntPtr errors);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct OpcDaItemDef
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string AccessPath;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string ItemId;

        public int Active;
        public int ClientHandle;
        public int BlobSize;
        public IntPtr Blob;
        public short RequestedDataType;
        public short Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct OpcDaItemResult
    {
        public int ServerHandle;
        public short CanonicalDataType;
        public short Reserved;
        public int AccessRights;
        public int BlobSize;
        public IntPtr Blob;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct OpcDaItemState
    {
        public int ClientHandle;
        public System.Runtime.InteropServices.ComTypes.FILETIME Timestamp;
        public short Quality;
        public short Reserved;

        [MarshalAs(UnmanagedType.Struct)]
        public object Value;
    }
}

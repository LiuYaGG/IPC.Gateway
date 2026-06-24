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
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.OpcDa
{
    
    
    
    
    
    
    
    
    
    
    public sealed class OpcDaClient : IPlcClient
    {
        private const int OpcQualityGood = 0xC0;

        private readonly PlcConnectionOptions _options;
        private readonly object _syncRoot;
        private readonly Dictionary<string, OpcDaItemHandle> _itemsByAddress;

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

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            lock (_syncRoot)
            {
                EnsureConnected();
                OpcDaItemHandle item = GetOrAddItem(address);
                int[] handles = new[] { item.ServerHandle };
                IntPtr valuesPointer;
                IntPtr errorsPointer;
                _syncIO.Read(OpcDaDataSource.Device, 1, handles, out valuesPointer, out errorsPointer);

                try
                {
                    int error = ReadError(errorsPointer, 0);
                    if (error != 0)
                        Marshal.ThrowExceptionForHR(error);

                    OpcDaItemState state = (OpcDaItemState)Marshal.PtrToStructure(valuesPointer, typeof(OpcDaItemState));
                    if ((state.Quality & OpcQualityGood) != OpcQualityGood)
                        throw new PlcCommunicationException("OPC DA read quality is not good. ItemID: " + item.ItemId);

                    object value = ConvertForRead(state.Value, dataType, elementCount, elementOffset);
                    return new PlcReadResult((ushort)item.CanonicalDataType, VarTypeName(item.CanonicalDataType), value);
                }
                finally
                {
                    DestroyItemStates(valuesPointer, 1);
                    FreeCoTaskMemory(errorsPointer);
                }
            }
        }

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
            lock (_syncRoot)
            {
                EnsureConnected();
                OpcDaItemHandle item = GetOrAddItem(address);
                int[] handles = new[] { item.ServerHandle };
                object[] values = new[] { ParseValue(dataType, valueText) };
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
            AddDistinct(values, 1000);
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

        private static object ConvertForRead(object value, PlcDataType dataType, int elementCount, int elementOffset)
        {
            if (PlcDataTypeHelper.IsArray(dataType))
                return ConvertToArray(value, dataType, Math.Max(1, elementCount), Math.Max(0, elementOffset));
            return ConvertScalar(value, dataType);
        }

        private static Array ConvertToArray(object value, PlcDataType dataType, int count, int offset)
        {
            Array result = PlcDataTypeHelper.CreateArray(dataType, count);
            IList list = value as IList;
            for (int i = 0; i < count; i++)
            {
                int sourceIndex = offset + i;
                object item = list != null && sourceIndex < list.Count ? list[sourceIndex] : value;
                result.SetValue(ConvertScalar(item, ArrayElementType(dataType)), i);
            }
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

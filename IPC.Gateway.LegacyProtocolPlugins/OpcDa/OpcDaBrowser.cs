/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.OpcDa
* 项目描述 ：
* 类 名 称 ：OpcDaBrowser
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
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.OpcDa
{
    
    
    
    
    
    
    
    
    
    
    public sealed class OpcDaBrowser : IDisposable
    {
        private const int OpcNsHierarchical = 1;
        private const int OpcNsFlat = 2;
        private const int OpcBrowseUp = 1;
        private const int OpcBrowseDown = 2;
        private const int OpcBrowseBranch = 1;
        private const int OpcBrowseLeaf = 2;
        private const int OpcBrowseFlat = 3;

        private readonly PlcConnectionOptions _options;
        private object _serverObject;
        private IOPCBrowseServerAddressSpace _browser;

        public OpcDaBrowser(PlcConnectionOptions options)
        {
            _options = options ?? new PlcConnectionOptions();
        }

        public IList<OpcDaBrowseItem> Browse(int maxItems)
        {
            Connect();
            int limit = maxItems <= 0 ? 5000 : maxItems;
            List<OpcDaBrowseItem> items = new List<OpcDaBrowseItem>();

            int namespaceType;
            _browser.QueryOrganization(out namespaceType);
            if (namespaceType == OpcNsHierarchical)
                BrowseHierarchical(items, 0, limit, 0);
            else if (namespaceType == OpcNsFlat)
                BrowseFlat(items, limit);
            else
                BrowseFlat(items, limit);

            return items;
        }

        public void Dispose()
        {
            ReleaseComObject(ref _browser);
            ReleaseComObject(ref _serverObject);
        }

        private void Connect()
        {
            if (_browser != null)
                return;

            string progId = GetServerProgId();
            Type serverType = CreateServerType(progId);
            _serverObject = Activator.CreateInstance(serverType);
            _browser = (IOPCBrowseServerAddressSpace)_serverObject;
        }

        private void BrowseFlat(List<OpcDaBrowseItem> items, int maxItems)
        {
            IEnumString enumerator = BrowseNames(OpcBrowseFlat, string.Empty);
            foreach (string name in ReadNames(enumerator, maxItems - items.Count))
            {
                items.Add(new OpcDaBrowseItem
                {
                    Name = name,
                    ItemId = name,
                    IsLeaf = true,
                    Level = 0
                });
                if (items.Count >= maxItems)
                    return;
            }
        }

        private void BrowseHierarchical(List<OpcDaBrowseItem> items, int level, int maxItems, int depth)
        {
            if (items.Count >= maxItems || depth > 12)
                return;

            List<string> branches = ReadNames(BrowseNames(OpcBrowseBranch, string.Empty), maxItems - items.Count);
            for (int i = 0; i < branches.Count; i++)
            {
                string branch = branches[i];
                items.Add(new OpcDaBrowseItem
                {
                    Name = branch,
                    ItemId = string.Empty,
                    IsLeaf = false,
                    Level = level
                });

                if (items.Count >= maxItems)
                    return;

                _browser.ChangeBrowsePosition(OpcBrowseDown, branch);
                try
                {
                    BrowseHierarchical(items, level + 1, maxItems, depth + 1);
                }
                finally
                {
                    _browser.ChangeBrowsePosition(OpcBrowseUp, string.Empty);
                }
            }

            List<string> leaves = ReadNames(BrowseNames(OpcBrowseLeaf, string.Empty), maxItems - items.Count);
            for (int i = 0; i < leaves.Count; i++)
            {
                string leaf = leaves[i];
                string itemId = leaf;
                IntPtr itemIdPointer;
                _browser.GetItemID(leaf, out itemIdPointer);
                try
                {
                    if (itemIdPointer != IntPtr.Zero)
                        itemId = Marshal.PtrToStringUni(itemIdPointer);
                }
                finally
                {
                    if (itemIdPointer != IntPtr.Zero)
                        Marshal.FreeCoTaskMem(itemIdPointer);
                }

                items.Add(new OpcDaBrowseItem
                {
                    Name = leaf,
                    ItemId = itemId,
                    IsLeaf = true,
                    Level = level
                });

                if (items.Count >= maxItems)
                    return;
            }
        }

        private IEnumString BrowseNames(int browseType, string filter)
        {
            IEnumString enumerator;
            _browser.BrowseOPCItemIDs(browseType, filter ?? string.Empty, 0, 0, out enumerator);
            return enumerator;
        }

        private static List<string> ReadNames(IEnumString enumerator, int maxItems)
        {
            List<string> names = new List<string>();
            if (enumerator == null || maxItems <= 0)
                return names;

            string[] buffer = new string[1];
            IntPtr fetchedPointer = Marshal.AllocCoTaskMem(4);
            try
            {
                while (names.Count < maxItems)
                {
                    Marshal.WriteInt32(fetchedPointer, 0);
                    int hr = enumerator.Next(1, buffer, fetchedPointer);
                    int fetched = Marshal.ReadInt32(fetchedPointer);
                    if (hr != 0 || fetched <= 0)
                        break;
                    if (!string.IsNullOrWhiteSpace(buffer[0]))
                        names.Add(buffer[0]);
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(fetchedPointer);
                ReleaseComObject(ref enumerator);
            }

            return names;
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

        private static void ReleaseComObject<T>(ref T value) where T : class
        {
            object instance = value;
            value = null;
            if (instance != null && Marshal.IsComObject(instance))
                Marshal.FinalReleaseComObject(instance);
        }

        [ComImport]
        [Guid("39C13A4F-011E-11D0-9675-0020AFD8ADB3")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IOPCBrowseServerAddressSpace
        {
            void QueryOrganization(out int namespaceType);

            void ChangeBrowsePosition(
                int browseDirection,
                [MarshalAs(UnmanagedType.LPWStr)] string stringValue);

            void BrowseOPCItemIDs(
                int browseFilterType,
                [MarshalAs(UnmanagedType.LPWStr)] string filterCriteria,
                short dataTypeFilter,
                int accessRightsFilter,
                out IEnumString enumString);

            void GetItemID(
                [MarshalAs(UnmanagedType.LPWStr)] string itemDataId,
                out IntPtr itemId);

            void BrowseAccessPaths(
                [MarshalAs(UnmanagedType.LPWStr)] string itemId,
                out IEnumString enumString);
        }
    }
}

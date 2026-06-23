/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.Core
* 项目描述 ：
* 类 名 称 ：PlcDataTypeHelper
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.Core
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

namespace IPC.Plc.Communication.Core
{
    
    
    
    
    
    
    
    
    
    public static class PlcDataTypeHelper
    {
        public static bool IsArray(PlcDataType dataType)
        {
            return dataType == PlcDataType.BoolArray ||
                   dataType == PlcDataType.Int16Array ||
                   dataType == PlcDataType.UInt16Array ||
                   dataType == PlcDataType.Int32Array ||
                   dataType == PlcDataType.UInt32Array ||
                   dataType == PlcDataType.Int64Array ||
                   dataType == PlcDataType.UInt64Array ||
                   dataType == PlcDataType.FloatArray ||
                   dataType == PlcDataType.DoubleArray ||
                   dataType == PlcDataType.CoilArray ||
                   dataType == PlcDataType.DiscreteInputArray;
        }

        public static int GetElementSize(PlcDataType dataType)
        {
            switch (dataType)
            {
                case PlcDataType.Bool:
                case PlcDataType.BoolArray:
                case PlcDataType.Coil:
                case PlcDataType.CoilArray:
                case PlcDataType.DiscreteInput:
                case PlcDataType.DiscreteInputArray:
                    return 1;
                case PlcDataType.Int16:
                case PlcDataType.Int16Array:
                case PlcDataType.UInt16:
                case PlcDataType.UInt16Array:
                    return 2;
                case PlcDataType.Int32:
                case PlcDataType.Int32Array:
                case PlcDataType.UInt32:
                case PlcDataType.UInt32Array:
                case PlcDataType.Float:
                case PlcDataType.FloatArray:
                    return 4;
                case PlcDataType.Int64:
                case PlcDataType.Int64Array:
                case PlcDataType.UInt64:
                case PlcDataType.UInt64Array:
                case PlcDataType.Double:
                case PlcDataType.DoubleArray:
                    return 8;
                case PlcDataType.String:
                    return 86;
                default:
                    throw new ArgumentOutOfRangeException("dataType");
            }
        }

        public static Array CreateArray(PlcDataType dataType, int length)
        {
            switch (dataType)
            {
                case PlcDataType.BoolArray:
                    return new bool[length];
                case PlcDataType.Int16Array:
                    return new short[length];
                case PlcDataType.UInt16Array:
                    return new ushort[length];
                case PlcDataType.Int32Array:
                    return new int[length];
                case PlcDataType.UInt32Array:
                    return new uint[length];
                case PlcDataType.Int64Array:
                    return new long[length];
                case PlcDataType.UInt64Array:
                    return new ulong[length];
                case PlcDataType.FloatArray:
                    return new float[length];
                case PlcDataType.DoubleArray:
                    return new double[length];
                case PlcDataType.CoilArray:
                case PlcDataType.DiscreteInputArray:
                    return new bool[length];
                default:
                    throw new ArgumentException("只有数组类型可以创建数组。", "dataType");
            }
        }
    }
}

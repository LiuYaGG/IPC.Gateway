/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.Cip
* 项目描述 ：
* 类 名 称 ：CipTypeCodes
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.Cip
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
using IPC.Plc.Communication.Core;





namespace IPC.Plc.Communication.Cip
{
    
    
    
    
    
    
    
    
    
    public static class CipTypeCodes
    {
        public const ushort Bool = 0x00C1;
        public const ushort Sint = 0x00C2;
        public const ushort Int = 0x00C3;
        public const ushort Dint = 0x00C4;
        public const ushort Lint = 0x00C5;
        public const ushort Usint = 0x00C6;
        public const ushort Uint = 0x00C7;
        public const ushort Udint = 0x00C8;
        public const ushort Ulint = 0x00C9;
        public const ushort Real = 0x00CA;
        public const ushort LReal = 0x00CB;
        public const ushort String = 0x00D0;
        public const ushort Dword = 0x00D3;
        public const ushort ShortString = 0x00DA;
        public const ushort AbbreviatedStructure = 0x02A0;

        public static ushort FromPlcDataType(PlcDataType dataType)
        {
            switch (dataType)
            {
                case PlcDataType.Int8:
                case PlcDataType.Int8Array:
                    return Sint;
                case PlcDataType.UInt8:
                case PlcDataType.UInt8Array:
                    return Usint;
                case PlcDataType.Bool:
                case PlcDataType.BoolArray:
                    return Bool;
                case PlcDataType.Int16:
                case PlcDataType.Int16Array:
                    return Int;
                case PlcDataType.UInt16:
                case PlcDataType.UInt16Array:
                    return Uint;
                case PlcDataType.Int32:
                case PlcDataType.Int32Array:
                    return Dint;
                case PlcDataType.UInt32:
                case PlcDataType.UInt32Array:
                    return Udint;
                case PlcDataType.Int64:
                case PlcDataType.Int64Array:
                    return Lint;
                case PlcDataType.UInt64:
                case PlcDataType.UInt64Array:
                    return Ulint;
                case PlcDataType.Float:
                case PlcDataType.FloatArray:
                    return Real;
                case PlcDataType.Double:
                case PlcDataType.DoubleArray:
                    return LReal;
                case PlcDataType.String:
                    return String;
                default:
                    throw new ArgumentOutOfRangeException("dataType");
            }
        }

        public static string ToName(ushort typeCode)
        {
            switch (typeCode)
            {
                case Bool:
                    return "BOOL";
                case Sint:
                    return "SINT";
                case Int:
                    return "INT";
                case Dint:
                    return "DINT";
                case Lint:
                    return "LINT";
                case Usint:
                    return "USINT";
                case Uint:
                    return "UINT";
                case Udint:
                    return "UDINT";
                case Ulint:
                    return "ULINT";
                case Real:
                    return "REAL";
                case LReal:
                    return "LREAL";
                case String:
                    return "STRING";
                case Dword:
                    return "DWORD";
                case ShortString:
                    return "SHORT_STRING";
                case AbbreviatedStructure:
                    return "ABBREVIATED_STRUCTURE";
                default:
                    return "0x" + typeCode.ToString("X4");
            }
        }
    }
}

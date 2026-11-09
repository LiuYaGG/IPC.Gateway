/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Mqtt.Sparkplug
* 项目描述 ：
* 类 名 称 ：SparkplugDataType
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Mqtt.Sparkplug
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
namespace IPC.Gateway.Mqtt.Sparkplug;

public enum SparkplugDataType : uint
{
    Unknown = 0,
    Int8 = 1,
    Int16 = 2,
    Int32 = 3,
    Int64 = 4,
    UInt8 = 5,
    UInt16 = 6,
    UInt32 = 7,
    UInt64 = 8,
    Float = 9,
    Double = 10,
    Boolean = 11,
    String = 12,
    DateTime = 13,
    Text = 14,
    Uuid = 15,
    DataSet = 16,
    Bytes = 17,
    File = 18,
    Template = 19,
    PropertySet = 20,
    PropertySetList = 21,
    Int8Array = 22,
    Int16Array = 23,
    Int32Array = 24,
    Int64Array = 25,
    UInt8Array = 26,
    UInt16Array = 27,
    UInt32Array = 28,
    UInt64Array = 29,
    FloatArray = 30,
    DoubleArray = 31,
    BooleanArray = 32,
    StringArray = 33,
    DateTimeArray = 34
}

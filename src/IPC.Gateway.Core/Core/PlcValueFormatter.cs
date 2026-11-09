/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.Core
* 项目描述 ：
* 类 名 称 ：PlcValueFormatter
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
using System.Collections.Generic;
using System.Globalization;

namespace IPC.Plc.Communication.Core
{
    
    
    
    
    
    
    
    
    
    public static class PlcValueFormatter
    {
        public static string Format(object? value)
        {
            if (value == null)
                return string.Empty;

            if (value is not Array array)
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

            List<string> values = new List<string>();
            foreach (object? item in array)
                values.Add(Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty);
            return string.Join(", ", values.ToArray());
        }
    }
}

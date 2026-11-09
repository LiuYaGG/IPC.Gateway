/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Scaling
* 项目描述 ：
* 类 名 称 ：TagValueScaler
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Runtime.Scaling
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
using IPC.Runtime.Configuration;

namespace IPC.Runtime.Scaling
{
    
    
    
    
    
    
    
    
    
    public static class TagValueScaler
    {
        public static object? Scale(object? rawValue, ScalingConfig? scaling)
        {
            if (rawValue == null || scaling == null || !scaling.Enabled)
                return rawValue;

            if (rawValue is string || rawValue is bool)
                return rawValue;

            if (rawValue is Array array)
                return ScaleArray(array, scaling);

            if (rawValue is IEnumerable enumerable)
                return ScaleEnumerable(enumerable, scaling);

            double number;
            if (!TryToDouble(rawValue, out number))
                return rawValue;

            return ScaleNumber(number, scaling);
        }

        public static object? Unscale(object? engineeringValue, ScalingConfig? scaling)
        {
            if (engineeringValue == null || scaling == null || !scaling.Enabled)
                return engineeringValue;

            if (engineeringValue is string || engineeringValue is bool)
                return engineeringValue;

            if (engineeringValue is Array array)
                return UnscaleArray(array, scaling);

            if (engineeringValue is IEnumerable enumerable)
                return UnscaleEnumerable(enumerable, scaling);

            double number;
            if (!TryToDouble(engineeringValue, out number))
                return engineeringValue;

            return UnscaleNumber(number, scaling);
        }

        public static string Format(object? value, ScalingConfig? scaling)
        {
            if (value == null)
                return string.Empty;

            if (value is Array array)
            {
                List<string> parts = new List<string>();
                foreach (object? item in array)
                {
                    parts.Add(Format(item, scaling));
                }
                return string.Join(", ", parts.ToArray());
            }

            if (value is IEnumerable enumerable && !(value is string))
            {
                List<string> parts = new List<string>();
                foreach (object? item in enumerable)
                {
                    parts.Add(Format(item, scaling));
                }
                return string.Join(", ", parts.ToArray());
            }

            double number;
            if (scaling != null && scaling.Enabled && TryToDouble(value, out number))
                return number.ToString("F" + Math.Max(0, scaling.DecimalPlaces), CultureInfo.InvariantCulture);

            if (value is IFormattable formattable)
                return formattable.ToString(null, CultureInfo.InvariantCulture);

            return value.ToString() ?? string.Empty;
        }

        private static object[] ScaleArray(Array values, ScalingConfig scaling)
        {
            object[] scaled = new object[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                scaled[i] = Scale(values.GetValue(i), scaling) ?? string.Empty;
            }
            return scaled;
        }

        private static object[] UnscaleArray(Array values, ScalingConfig scaling)
        {
            object[] raw = new object[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                raw[i] = Unscale(values.GetValue(i), scaling) ?? string.Empty;
            }
            return raw;
        }

        private static object[] ScaleEnumerable(IEnumerable values, ScalingConfig scaling)
        {
            List<object> scaled = new List<object>();
            foreach (object? value in values)
            {
                scaled.Add(Scale(value, scaling) ?? string.Empty);
            }
            return scaled.ToArray();
        }

        private static object[] UnscaleEnumerable(IEnumerable values, ScalingConfig scaling)
        {
            List<object> raw = new List<object>();
            foreach (object? value in values)
            {
                raw.Add(Unscale(value, scaling) ?? string.Empty);
            }
            return raw.ToArray();
        }

        private static double ScaleNumber(double value, ScalingConfig scaling)
        {
            double scaled = value * scaling.Multiplier + scaling.Offset;
            if (scaling.ClampEnabled)
            {
                if (scaled < scaling.MinValue)
                    scaled = scaling.MinValue;
                if (scaled > scaling.MaxValue)
                    scaled = scaling.MaxValue;
            }
            return Math.Round(scaled, Math.Max(0, scaling.DecimalPlaces));
        }

        private static double UnscaleNumber(double value, ScalingConfig scaling)
        {
            if (scaling.Multiplier == 0D)
                throw new InvalidOperationException("Scaling multiplier cannot be zero.");
            return (value - scaling.Offset) / scaling.Multiplier;
        }

        private static bool TryToDouble(object value, out double number)
        {
            try
            {
                number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                number = 0D;
                return false;
            }
        }
    }
}

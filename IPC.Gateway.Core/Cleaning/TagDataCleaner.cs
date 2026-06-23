/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Cleaning
* 项目描述 ：
* 类 名 称 ：TagDataCleaner
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Runtime.Cleaning
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
using System.Globalization;
using IPC.Runtime.Configuration;
using IPC.Runtime.Scaling;
using IPC.Runtime.Values;

namespace IPC.Runtime.Cleaning
{
    public static class TagDataCleaner
    {
        public static void Clean(TagValueSnapshot snapshot, TagConfig tag, TagValueSnapshot? previous)
        {
            if (snapshot == null || tag == null)
                return;

            DataCleaningConfig cleaning = tag.Cleaning ?? DataCleaningConfig.Default();
            if (!cleaning.Enabled)
                return;

            object currentValue = snapshot.Value;
            string currentText = snapshot.ValueText ?? string.Empty;
            string currentUnit = snapshot.Unit ?? string.Empty;

            if (cleaning.UnitConversionEnabled)
                ApplyUnitConversion(snapshot, cleaning, ref currentValue, ref currentText, ref currentUnit);

            double currentNumber;
            bool hasNumber = TryToDouble(currentValue, out currentNumber);
            if (hasNumber && TryApplyFilter(snapshot, cleaning, previous, currentNumber))
                return;

            if (hasNumber && cleaning.OutOfRangeEnabled && IsOutOfRange(currentNumber, cleaning))
            {
                snapshot.Quality = TagQuality.OutOfRange;
                snapshot.ErrorMessage = "Value is out of configured range.";
                Mark(snapshot, "OutOfRange", snapshot.ErrorMessage);
            }

            if (cleaning.EnumMappingEnabled)
                ApplyEnumMapping(snapshot, cleaning, currentValue, currentText);
        }

        private static void ApplyUnitConversion(
            TagValueSnapshot snapshot,
            DataCleaningConfig cleaning,
            ref object value,
            ref string valueText,
            ref string unit)
        {
            double number;
            if (!TryToDouble(value, out number))
                return;

            double converted = number * SafeMultiplier(cleaning.UnitMultiplier) + cleaning.UnitOffset;
            value = converted;
            valueText = FormatNumber(converted, snapshot.Precision);
            snapshot.Value = value;
            snapshot.ValueText = valueText;
            if (!string.IsNullOrWhiteSpace(cleaning.TargetUnit))
            {
                unit = cleaning.TargetUnit.Trim();
                snapshot.Unit = unit;
            }

            Mark(snapshot, "UnitConverted", "Unit conversion applied.");
        }

        private static bool TryApplyFilter(
            TagValueSnapshot snapshot,
            DataCleaningConfig cleaning,
            TagValueSnapshot? previous,
            double currentNumber)
        {
            if (previous == null || previous.Quality == TagQuality.Unknown)
                return false;

            double previousNumber;
            bool hasPreviousNumber = TryToDouble(previous.Value, out previousNumber);
            if (!hasPreviousNumber)
                return false;

            double delta = Math.Abs(currentNumber - previousNumber);
            if (cleaning.SpikeFilterEnabled &&
                cleaning.SpikeThreshold > 0D &&
                delta > cleaning.SpikeThreshold &&
                IsWithinSpikeWindow(snapshot, previous, cleaning.SpikeWindowSeconds))
            {
                ApplyPreservedValue(snapshot, previous, cleaning, TagQuality.Spike, "SpikeFiltered", "Spike value filtered.");
                return true;
            }

            if (cleaning.DeadbandEnabled && cleaning.Deadband > 0D && delta <= cleaning.Deadband)
            {
                ApplyPreservedValue(snapshot, previous, cleaning, TagQuality.Filtered, "DeadbandFiltered", "Deadband value filtered.");
                return true;
            }

            if (cleaning.DuplicateFilterEnabled && AreEquivalent(snapshot.Value, previous.Value))
            {
                ApplyPreservedValue(snapshot, previous, cleaning, TagQuality.Filtered, "DuplicateFiltered", "Duplicate value filtered.");
                return true;
            }

            return false;
        }

        private static void ApplyPreservedValue(
            TagValueSnapshot snapshot,
            TagValueSnapshot previous,
            DataCleaningConfig cleaning,
            TagQuality quality,
            string action,
            string message)
        {
            if (cleaning.PreserveLastGoodOnFilter)
            {
                snapshot.Value = previous.Value;
                snapshot.ValueText = previous.ValueText;
                snapshot.Unit = previous.Unit;
            }

            snapshot.Quality = quality;
            snapshot.ErrorMessage = message;
            Mark(snapshot, action, message);
        }

        private static void ApplyEnumMapping(
            TagValueSnapshot snapshot,
            DataCleaningConfig cleaning,
            object value,
            string valueText)
        {
            if (cleaning.EnumMappings == null || cleaning.EnumMappings.Count == 0)
                return;

            string rawText = snapshot.RawValueText ?? string.Empty;
            for (int i = 0; i < cleaning.EnumMappings.Count; i++)
            {
                DataCleaningEnumMappingConfig mapping = cleaning.EnumMappings[i];
                if (mapping == null || string.IsNullOrWhiteSpace(mapping.RawValue))
                    continue;

                if (!MatchesMapping(mapping.RawValue, value, valueText, rawText))
                    continue;

                snapshot.Value = mapping.CleanValue ?? string.Empty;
                snapshot.ValueText = mapping.CleanValue ?? string.Empty;
                Mark(snapshot, "EnumMapped", "Enum mapping applied.");
                return;
            }
        }

        private static bool MatchesMapping(string configured, object value, string valueText, string rawText)
        {
            string normalized = NormalizeText(configured);
            if (normalized.Length == 0)
                return false;

            if (NormalizeText(valueText) == normalized || NormalizeText(rawText) == normalized)
                return true;

            double left;
            double right;
            return TryToDouble(configured, out left) && TryToDouble(value, out right) && Math.Abs(left - right) < 0.000001D;
        }

        private static bool IsOutOfRange(double value, DataCleaningConfig cleaning)
        {
            double min = Math.Min(cleaning.MinValue, cleaning.MaxValue);
            double max = Math.Max(cleaning.MinValue, cleaning.MaxValue);
            return value < min || value > max;
        }

        private static bool IsWithinSpikeWindow(TagValueSnapshot snapshot, TagValueSnapshot previous, int seconds)
        {
            if (seconds <= 0)
                return true;
            if (snapshot.Timestamp == DateTime.MinValue || previous.Timestamp == DateTime.MinValue)
                return true;
            return Math.Abs((snapshot.Timestamp - previous.Timestamp).TotalSeconds) <= seconds;
        }

        private static bool AreEquivalent(object left, object right)
        {
            double leftNumber;
            double rightNumber;
            if (TryToDouble(left, out leftNumber) && TryToDouble(right, out rightNumber))
                return Math.Abs(leftNumber - rightNumber) < 0.000001D;
            return string.Equals(NormalizeText(Convert.ToString(left, CultureInfo.InvariantCulture)), NormalizeText(Convert.ToString(right, CultureInfo.InvariantCulture)), StringComparison.OrdinalIgnoreCase);
        }

        private static void Mark(TagValueSnapshot snapshot, string action, string message)
        {
            snapshot.CleaningApplied = true;
            snapshot.CleaningAction = action ?? string.Empty;
            snapshot.CleaningMessage = message ?? string.Empty;
        }

        private static double SafeMultiplier(double multiplier)
        {
            return Math.Abs(multiplier) < 0.000000001D ? 1D : multiplier;
        }

        private static string FormatNumber(double value, int precision)
        {
            if (precision >= 0)
                return value.ToString("F" + Math.Min(8, precision), CultureInfo.InvariantCulture);
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string NormalizeText(string? value)
        {
            return (value ?? string.Empty).Trim();
        }

        private static bool TryToDouble(object? value, out double number)
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

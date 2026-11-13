using System;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.MitsubishiMc
{
    internal interface IMitsubishiDeviceAddress<T>
    {
        int DeviceNumber { get; }
        int MaximumDeviceNumber { get; }
        bool IsBitDevice { get; }
        int BitOffset { get; }
        bool HasBitOffset { get; }
        T AddDeviceOffset(int offset);
        T AddBitOffset(int offset);
    }

    internal static class MitsubishiDeviceAddress
    {
        public static T ResolveOffset<T>(this T address, PlcDataType type, int offset) where T : IMitsubishiDeviceAddress<T>
        {
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            bool bitAccess = type == PlcDataType.Bool || type == PlcDataType.BoolArray;
            if (address.HasBitOffset && (!bitAccess || address.IsBitDevice))
                throw new FormatException("仅字软元件的 Bool 或 BoolArray 标签可使用 .bit 后缀。");

            T start = bitAccess
                ? address.AddBitOffset(offset)
                : address.AddWordOffset(McDataCodec.GetDeviceOffset(type, offset));
            start.EnsureTransferRange(1, bitAccess);
            return start;
        }

        public static T AddWordOffset<T>(this T address, int words) where T : IMitsubishiDeviceAddress<T>
            => address.AddDeviceOffset(checked(words * (address.IsBitDevice ? 16 : 1)));

        public static void EnsureTagRange<T>(this T address, PlcDataType type, int count) where T : IMitsubishiDeviceAddress<T>
        {
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            bool bitAccess = type == PlcDataType.Bool || type == PlcDataType.BoolArray;
            int points = bitAccess
                ? (type == PlcDataType.Bool ? 1 : count)
                : McDataCodec.GetWordCount(type, count);
            if (bitAccess && !address.IsBitDevice)
                points = checked((int)(((long)address.BitOffset + points + 15) / 16));
            address.EnsureTransferRange(points, bitAccess);
        }

        public static void EnsureTransferRange<T>(this T address, int points, bool bitAccess) where T : IMitsubishiDeviceAddress<T>
        {
            int stride = address.IsBitDevice && !bitAccess ? 16 : 1;
            long last = address.DeviceNumber + (long)points * stride - 1;
            if (points <= 0 || address.DeviceNumber < 0 || last > address.MaximumDeviceNumber)
                throw new ArgumentOutOfRangeException(nameof(points), "三菱软元件范围超过当前报文可寻址的范围。");
        }
    }
}

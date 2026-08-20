using System;
using System.Globalization;

namespace IPC.Plc.Communication.Dnp3
{
    public enum Dnp3PointType
    {
        Binary,
        DoubleBitBinary,
        Analog,
        Counter,
        FrozenCounter,
        BinaryOutput,
        AnalogOutput
    }

    public sealed class Dnp3Address
    {
        private Dnp3Address(Dnp3PointType pointType, ushort index)
        {
            PointType = pointType;
            Index = index;
        }

        public Dnp3PointType PointType { get; }
        public ushort Index { get; }

        public static Dnp3Address Parse(string address)
        {
            string[] parts = (address ?? string.Empty).Trim().Split(':');
            if (parts.Length != 2 || !Enum.TryParse(parts[0], true, out Dnp3PointType pointType) ||
                !ushort.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out ushort index))
                throw new FormatException("DNP3 地址格式应为 PointType:Index，例如 Analog:12。");
            return new Dnp3Address(pointType, index);
        }

        public override string ToString() => PointType + ":" + Index.ToString(CultureInfo.InvariantCulture);
    }
}

using System;
using System.Text;

namespace IPC.Plc.Communication.Cip
{
    public sealed class CipDeviceIdentity
    {
        public ushort VendorId { get; private set; }
        public ushort DeviceType { get; private set; }
        public ushort ProductCode { get; private set; }
        public byte MajorRevision { get; private set; }
        public byte MinorRevision { get; private set; }
        public uint SerialNumber { get; private set; }
        public string ProductName { get; private set; } = string.Empty;

        internal static CipDeviceIdentity TryParse(byte[] body)
        {
            try
            {
                if (body == null || body.Length < 6)
                    return null;
                int count = ReadUInt16(body, 0);
                int offset = 2;
                for (int i = 0; i < count; i++)
                {
                    if (offset + 4 > body.Length)
                        return null;
                    ushort type = ReadUInt16(body, offset);
                    int length = ReadUInt16(body, offset + 2);
                    offset += 4;
                    if (offset + length > body.Length)
                        return null;
                    if (type == 0x000C && length >= 33)
                        return ParseIdentityItem(body, offset, length);
                    offset += length;
                }
            }
            catch
            {
            }
            return null;
        }

        private static CipDeviceIdentity ParseIdentityItem(byte[] data, int offset, int length)
        {
            int identityOffset = offset + 18;
            if (identityOffset + 15 > offset + length)
                return null;
            int nameLength = data[identityOffset + 14];
            if (identityOffset + 15 + nameLength > offset + length)
                nameLength = Math.Max(0, offset + length - identityOffset - 15);
            return new CipDeviceIdentity
            {
                VendorId = ReadUInt16(data, identityOffset),
                DeviceType = ReadUInt16(data, identityOffset + 2),
                ProductCode = ReadUInt16(data, identityOffset + 4),
                MajorRevision = data[identityOffset + 6],
                MinorRevision = data[identityOffset + 7],
                SerialNumber = ReadUInt32(data, identityOffset + 10),
                ProductName = Encoding.ASCII.GetString(data, identityOffset + 15, nameLength)
            };
        }

        private static ushort ReadUInt16(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
        }
    }
}

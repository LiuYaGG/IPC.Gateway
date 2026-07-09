using System;
using System.Globalization;

namespace IPC.Plc.Communication.CanOpen
{
    internal sealed class CanOpenSdoClient
    {
        private readonly SlcanAdapter _adapter;

        public CanOpenSdoClient(SlcanAdapter adapter)
        {
            _adapter = adapter ?? throw new ArgumentNullException("adapter");
        }

        public byte[] Upload(CanOpenObjectAddress address)
        {
            byte[] request = new byte[8];
            request[0] = 0x40;
            WriteIndex(request, address.Index);
            request[3] = address.SubIndex;

            _adapter.SendFrame(new CanFrame(0x600 + address.NodeId, request));
            CanFrame response = _adapter.ReceiveFrame(0x580 + address.NodeId);
            return ParseUploadResponse(address, response.Data);
        }

        public void Download(CanOpenObjectAddress address, byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException("data");
            if (data.Length != 1 && data.Length != 2 && data.Length != 4)
                throw new NotSupportedException("CANopen expedited SDO download supports 1, 2, or 4 bytes.");

            byte[] request = new byte[8];
            request[0] = data.Length == 1 ? (byte)0x2F : data.Length == 2 ? (byte)0x2B : (byte)0x23;
            WriteIndex(request, address.Index);
            request[3] = address.SubIndex;
            Buffer.BlockCopy(data, 0, request, 4, data.Length);

            _adapter.SendFrame(new CanFrame(0x600 + address.NodeId, request));
            CanFrame response = _adapter.ReceiveFrame(0x580 + address.NodeId);
            ParseDownloadResponse(address, response.Data);
        }

        private static byte[] ParseUploadResponse(CanOpenObjectAddress address, byte[] data)
        {
            EnsureResponseLength(data);
            EnsureIndex(data, address);
            if (data[0] == 0x80)
                throw CreateAbortException(address, data);
            if ((data[0] & 0xE0) != 0x40 || (data[0] & 0x02) == 0)
                throw new NotSupportedException("CANopen segmented SDO upload is not supported.");

            int unusedBytes = (data[0] >> 2) & 0x03;
            int length = ((data[0] & 0x01) != 0) ? 4 - unusedBytes : 4;
            byte[] result = new byte[length];
            Buffer.BlockCopy(data, 4, result, 0, length);
            return result;
        }

        private static void ParseDownloadResponse(CanOpenObjectAddress address, byte[] data)
        {
            EnsureResponseLength(data);
            EnsureIndex(data, address);
            if (data[0] == 0x80)
                throw CreateAbortException(address, data);
            if (data[0] != 0x60)
                throw new InvalidOperationException("CANopen SDO download response command is invalid: 0x" + data[0].ToString("X2", CultureInfo.InvariantCulture));
        }

        private static void EnsureResponseLength(byte[] data)
        {
            if (data == null || data.Length < 8)
                throw new InvalidOperationException("CANopen SDO response is too short.");
        }

        private static void EnsureIndex(byte[] data, CanOpenObjectAddress address)
        {
            ushort index = (ushort)(data[1] | (data[2] << 8));
            if (index != address.Index || data[3] != address.SubIndex)
                throw new InvalidOperationException("CANopen SDO response object does not match the request.");
        }

        private static Exception CreateAbortException(CanOpenObjectAddress address, byte[] data)
        {
            uint abortCode = (uint)(data[4] | (data[5] << 8) | (data[6] << 16) | (data[7] << 24));
            return new InvalidOperationException(
                "CANopen SDO abort at node " + address.NodeId.ToString(CultureInfo.InvariantCulture) +
                ", index 0x" + address.Index.ToString("X4", CultureInfo.InvariantCulture) +
                ", sub " + address.SubIndex.ToString(CultureInfo.InvariantCulture) +
                ": 0x" + abortCode.ToString("X8", CultureInfo.InvariantCulture));
        }

        private static void WriteIndex(byte[] data, ushort index)
        {
            data[1] = (byte)(index & 0xFF);
            data[2] = (byte)((index >> 8) & 0xFF);
        }
    }
}

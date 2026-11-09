using System;
using System.Globalization;
using System.IO;
using IPC.Plc.Communication.Core;

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

            _adapter.DiscardFrames(0x580 + address.NodeId);
            _adapter.SendFrame(new CanFrame(0x600 + address.NodeId, request));
            CanFrame response = _adapter.ReceiveFrame(0x580 + address.NodeId);
            return ParseUploadResponse(address, response.Data);
        }

        public void Download(CanOpenObjectAddress address, byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException("data");
            if (data.Length > 4)
            {
                DownloadSegmented(address, data);
                return;
            }
            if (data.Length != 1 && data.Length != 2 && data.Length != 4)
                throw new NotSupportedException("CANopen SDO download supports 1, 2, 4, or segmented payloads larger than 4 bytes.");

            byte[] request = new byte[8];
            request[0] = data.Length == 1 ? (byte)0x2F : data.Length == 2 ? (byte)0x2B : (byte)0x23;
            WriteIndex(request, address.Index);
            request[3] = address.SubIndex;
            Buffer.BlockCopy(data, 0, request, 4, data.Length);

            _adapter.DiscardFrames(0x580 + address.NodeId);
            _adapter.SendFrame(new CanFrame(0x600 + address.NodeId, request));
            CanFrame response = _adapter.ReceiveFrame(0x580 + address.NodeId);
            ParseDownloadResponse(address, response.Data);
        }

        private byte[] ParseUploadResponse(CanOpenObjectAddress address, byte[] data)
        {
            EnsureResponseLength(data);
            EnsureIndex(data, address);
            if (data[0] == 0x80)
                throw CreateAbortException(address, data);
            if ((data[0] & 0xE0) != 0x40)
                throw new InvalidOperationException("CANopen SDO upload response command is invalid.");
            if ((data[0] & 0x02) == 0)
                return UploadSegmented(address, data);

            int unusedBytes = (data[0] >> 2) & 0x03;
            int length = ((data[0] & 0x01) != 0) ? 4 - unusedBytes : 4;
            byte[] result = new byte[length];
            Buffer.BlockCopy(data, 4, result, 0, length);
            return result;
        }

        private byte[] UploadSegmented(CanOpenObjectAddress address, byte[] initiateResponse)
        {
            int expectedLength = (initiateResponse[0] & 0x01) != 0
                ? ReadInt32(initiateResponse, 4)
                : -1;
            using MemoryStream stream = expectedLength > 0
                ? new MemoryStream(expectedLength)
                : new MemoryStream();
            int toggle = 0;

            while (true)
            {
                byte[] request = new byte[8];
                request[0] = (byte)(0x60 | (toggle << 4));
                _adapter.SendFrame(new CanFrame(0x600 + address.NodeId, request));
                byte[] response = _adapter.ReceiveFrame(0x580 + address.NodeId).Data;
                EnsureResponseLength(response);
                if (response[0] == 0x80)
                    throw CreateAbortException(address, response);
                if ((response[0] & 0xE0) != 0 || ((response[0] >> 4) & 1) != toggle)
                    throw new InvalidOperationException("CANopen segmented SDO upload toggle mismatch.");

                bool last = (response[0] & 0x01) != 0;
                int unused = (response[0] >> 1) & 0x07;
                int count = last ? 7 - unused : 7;
                if (count < 0 || count > 7)
                    throw new InvalidOperationException("CANopen segmented SDO upload length is invalid.");
                stream.Write(response, 1, count);
                if (last)
                    break;
                toggle ^= 1;
            }

            byte[] value = stream.ToArray();
            if (expectedLength >= 0 && value.Length != expectedLength)
                throw new InvalidOperationException("CANopen segmented SDO upload length does not match the announced size.");
            return value;
        }

        private void DownloadSegmented(CanOpenObjectAddress address, byte[] data)
        {
            byte[] initiate = new byte[8];
            initiate[0] = 0x21;
            WriteIndex(initiate, address.Index);
            initiate[3] = address.SubIndex;
            WriteInt32(initiate, 4, data.Length);
            _adapter.DiscardFrames(0x580 + address.NodeId);
            _adapter.SendFrame(new CanFrame(0x600 + address.NodeId, initiate));
            ParseDownloadResponse(address, _adapter.ReceiveFrame(0x580 + address.NodeId).Data);

            int offset = 0;
            int toggle = 0;
            while (offset < data.Length)
            {
                int count = Math.Min(7, data.Length - offset);
                bool last = offset + count >= data.Length;
                int unused = 7 - count;
                byte[] request = new byte[8];
                request[0] = (byte)((toggle << 4) | (unused << 1) | (last ? 1 : 0));
                Buffer.BlockCopy(data, offset, request, 1, count);
                _adapter.SendFrame(new CanFrame(0x600 + address.NodeId, request));
                byte[] response = _adapter.ReceiveFrame(0x580 + address.NodeId).Data;
                EnsureResponseLength(response);
                if (response[0] == 0x80)
                    throw CreateAbortException(address, response);
                if ((response[0] & 0xE0) != 0x20 || ((response[0] >> 4) & 1) != toggle)
                    throw new InvalidOperationException("CANopen segmented SDO download toggle mismatch.");
                offset += count;
                toggle ^= 1;
            }
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
            PlcReadFailureScope scope = IsObjectAbort(abortCode)
                ? PlcReadFailureScope.Tag
                : PlcReadFailureScope.Device;
            return new PlcProtocolException(
                scope,
                "CANopen SDO abort at node " + address.NodeId.ToString(CultureInfo.InvariantCulture) +
                ", index 0x" + address.Index.ToString("X4", CultureInfo.InvariantCulture) +
                ", sub " + address.SubIndex.ToString(CultureInfo.InvariantCulture) +
                ": 0x" + abortCode.ToString("X8", CultureInfo.InvariantCulture),
                "0x" + abortCode.ToString("X8", CultureInfo.InvariantCulture));
        }

        private static bool IsObjectAbort(uint abortCode)
        {
            return abortCode == 0x06010000 ||
                   abortCode == 0x06010001 ||
                   abortCode == 0x06010002 ||
                   abortCode == 0x06020000 ||
                   abortCode == 0x06040041 ||
                   abortCode == 0x06040042 ||
                   abortCode == 0x06070010 ||
                   abortCode == 0x06090011 ||
                   abortCode == 0x06090030 ||
                   abortCode == 0x06090031 ||
                   abortCode == 0x06090032;
        }

        private static int ReadInt32(byte[] data, int offset)
        {
            return data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16 | data[offset + 3] << 24;
        }

        private static void WriteInt32(byte[] data, int offset, int value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteIndex(byte[] data, ushort index)
        {
            data[1] = (byte)(index & 0xFF);
            data[2] = (byte)((index >> 8) & 0xFF);
        }
    }
}

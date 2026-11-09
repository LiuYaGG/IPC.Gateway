/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.Metering.Dlt645
* 项目描述 ：
* 类 名 称 ：Dlt645Frame
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.Metering.Dlt645
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
using System.IO;

namespace IPC.Plc.Communication.Metering.Dlt645
{
    
    
    
    
    
    
    
    
    
    internal static class Dlt645Frame
    {
        public static byte[] BuildReadRequest(Dlt645Address address)
        {
            MemoryStream stream = new MemoryStream();
            stream.WriteByte(0xFE);
            stream.WriteByte(0xFE);
            stream.WriteByte(0xFE);
            stream.WriteByte(0xFE);
            stream.WriteByte(0x68);
            byte[] meterAddress = address.GetAddressBytes();
            stream.Write(meterAddress, 0, meterAddress.Length);
            stream.WriteByte(0x68);
            stream.WriteByte(0x11);
            stream.WriteByte(0x04);

            byte[] dataIdentifier = address.GetDataIdentifierBytes();
            for (int i = 0; i < dataIdentifier.Length; i++)
                stream.WriteByte(Add33(dataIdentifier[i]));

            byte[] withoutChecksum = stream.ToArray();
            byte checksum = Checksum(withoutChecksum, 4, withoutChecksum.Length - 4);
            stream.WriteByte(checksum);
            stream.WriteByte(0x16);
            return stream.ToArray();
        }

        public static byte[] ExtractReadData(byte[] frame, Dlt645Address requestAddress)
        {
            if (frame == null || frame.Length < 16)
                throw new FormatException("DLT645-2007响应帧长度不足。");
            if (frame[0] != 0x68 || frame[7] != 0x68 || frame[frame.Length - 1] != 0x16)
                throw new FormatException("DLT645-2007响应帧格式错误。");
            if (Checksum(frame, 0, frame.Length - 2) != frame[frame.Length - 2])
                throw new FormatException("DLT645-2007响应帧校验失败。");

            byte control = frame[8];
            if ((control & 0x40) == 0x40)
                throw new InvalidOperationException("DLT645-2007电表返回异常应答。");
            if (control != 0x91)
                throw new InvalidOperationException("DLT645-2007响应控制码不正确: 0x" + control.ToString("X2"));

            int length = frame[9];
            if (length < 4 || frame.Length != length + 12)
                throw new FormatException("DLT645-2007响应数据长度错误。");

            byte[] decoded = new byte[length];
            for (int i = 0; i < length; i++)
                decoded[i] = Sub33(frame[10 + i]);

            byte[] expectedDi = requestAddress.GetDataIdentifierBytes();
            for (int i = 0; i < 4; i++)
            {
                if (decoded[i] != expectedDi[i])
                    throw new FormatException("DLT645-2007响应数据标识与请求不一致。");
            }

            byte[] value = new byte[length - 4];
            Array.Copy(decoded, 4, value, 0, value.Length);
            return value;
        }

        private static byte Checksum(byte[] buffer, int offset, int count)
        {
            int sum = 0;
            for (int i = offset; i < offset + count; i++)
                sum += buffer[i];
            return (byte)(sum & 0xFF);
        }

        private static byte Add33(byte value)
        {
            return (byte)((value + 0x33) & 0xFF);
        }

        private static byte Sub33(byte value)
        {
            return (byte)((value - 0x33) & 0xFF);
        }
    }
}

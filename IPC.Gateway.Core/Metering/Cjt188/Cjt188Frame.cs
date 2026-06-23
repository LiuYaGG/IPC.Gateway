/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.Metering.Cjt188
* 项目描述 ：
* 类 名 称 ：Cjt188Frame
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.Metering.Cjt188
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

namespace IPC.Plc.Communication.Metering.Cjt188
{
    
    
    
    
    
    
    
    
    
    internal static class Cjt188Frame
    {
        public static byte[] BuildReadRequest(Cjt188Address address)
        {
            MemoryStream stream = new MemoryStream();
            stream.WriteByte(0xFE);
            stream.WriteByte(0xFE);
            stream.WriteByte(0xFE);
            stream.WriteByte(0xFE);
            stream.WriteByte(0x68);
            stream.WriteByte(address.MeterType);

            byte[] meterAddress = address.GetAddressBytes();
            stream.Write(meterAddress, 0, meterAddress.Length);
            stream.WriteByte(0x01);
            stream.WriteByte(0x03);

            byte[] dataIdentifier = address.GetDataIdentifierBytes();
            stream.Write(dataIdentifier, 0, dataIdentifier.Length);
            stream.WriteByte(0x00);

            byte[] withoutChecksum = stream.ToArray();
            byte checksum = Checksum(withoutChecksum, 4, withoutChecksum.Length - 4);
            stream.WriteByte(checksum);
            stream.WriteByte(0x16);
            return stream.ToArray();
        }

        public static byte[] ExtractReadData(byte[] frame, Cjt188Address requestAddress)
        {
            if (frame == null || frame.Length < 14)
                throw new FormatException("CJ/T188-2004响应帧长度不足。");
            if (frame[0] != 0x68 || frame[frame.Length - 1] != 0x16)
                throw new FormatException("CJ/T188-2004响应帧格式错误。");
            if (Checksum(frame, 0, frame.Length - 2) != frame[frame.Length - 2])
                throw new FormatException("CJ/T188-2004响应帧校验失败。");
            if (frame[1] != requestAddress.MeterType)
                throw new FormatException("CJ/T188-2004响应仪表类型与请求不一致。");

            byte control = frame[9];
            if ((control & 0x40) == 0x40)
                throw new InvalidOperationException("CJ/T188-2004仪表返回异常应答。");
            if (control != 0x81)
                throw new InvalidOperationException("CJ/T188-2004响应控制码不正确: 0x" + control.ToString("X2"));

            int length = frame[10];
            if (length < 2 || frame.Length != length + 13)
                throw new FormatException("CJ/T188-2004响应数据长度错误。");

            byte[] expectedDi = requestAddress.GetDataIdentifierBytes();
            if (frame[11] != expectedDi[0] || frame[12] != expectedDi[1])
                throw new FormatException("CJ/T188-2004响应数据标识与请求不一致。");

            byte[] value = new byte[length - 2];
            Array.Copy(frame, 13, value, 0, value.Length);
            return value;
        }

        private static byte Checksum(byte[] buffer, int offset, int count)
        {
            int sum = 0;
            for (int i = offset; i < offset + count; i++)
                sum += buffer[i];
            return (byte)(sum & 0xFF);
        }
    }
}

/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.Cip
* 项目描述 ：
* 类 名 称 ：CipPath
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.Cip
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
using System.IO;
using System.Text;





namespace IPC.Plc.Communication.Cip
{
    
    
    
    
    
    
    
    
    
    public static class CipPath
    {
        public static byte[] EncodeTagPath(string tagName)
        {
            if (string.IsNullOrWhiteSpace(tagName))
                throw new ArgumentException("标签名不能为空。", "tagName");

            MemoryStream stream = new MemoryStream();
            string[] parts = tagName.Split('.');
            for (int i = 0; i < parts.Length; i++)
            {
                WritePart(stream, parts[i]);
            }

            if ((stream.Length % 2) != 0)
                stream.WriteByte(0);

            return stream.ToArray();
        }

        private static void WritePart(Stream stream, string part)
        {
            int pos = 0;
            while (pos < part.Length)
            {
                int bracket = part.IndexOf('[', pos);
                string name = bracket < 0 ? part.Substring(pos) : part.Substring(pos, bracket - pos);
                if (name.Length > 0)
                    WriteSymbol(stream, name);

                if (bracket < 0)
                    break;

                int end = part.IndexOf(']', bracket + 1);
                if (end < 0)
                    throw new ArgumentException("标签数组下标缺少右括号: " + part);

                string indexText = part.Substring(bracket + 1, end - bracket - 1);
                string[] indexes = indexText.Split(',');
                for (int i = 0; i < indexes.Length; i++)
                {
                    int index;
                    if (!int.TryParse(indexes[i].Trim(), out index) || index < 0)
                        throw new ArgumentException("无效的数组下标: " + indexes[i]);
                    WriteElementIndex(stream, index);
                }

                pos = end + 1;
            }
        }

        private static void WriteSymbol(Stream stream, string symbol)
        {
            byte[] nameBytes = Encoding.ASCII.GetBytes(symbol);
            if (nameBytes.Length > 255)
                throw new ArgumentException("标签段过长: " + symbol);

            stream.WriteByte(0x91);
            stream.WriteByte((byte)nameBytes.Length);
            stream.Write(nameBytes, 0, nameBytes.Length);
            if ((nameBytes.Length % 2) != 0)
                stream.WriteByte(0);
        }

        private static void WriteElementIndex(Stream stream, int index)
        {
            if (index <= byte.MaxValue)
            {
                stream.WriteByte(0x28);
                stream.WriteByte((byte)index);
            }
            else if (index <= ushort.MaxValue)
            {
                stream.WriteByte(0x29);
                stream.WriteByte(0);
                WriteUInt16(stream, (ushort)index);
            }
            else
            {
                stream.WriteByte(0x2A);
                stream.WriteByte(0);
                WriteUInt32(stream, (uint)index);
            }
        }

        internal static void WriteUInt16(Stream stream, ushort value)
        {
            stream.WriteByte((byte)(value & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
        }

        internal static void WriteUInt32(Stream stream, uint value)
        {
            stream.WriteByte((byte)(value & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
            stream.WriteByte((byte)((value >> 16) & 0xFF));
            stream.WriteByte((byte)((value >> 24) & 0xFF));
        }
    }
}

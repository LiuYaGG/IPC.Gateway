using System;
using System.IO;
using System.Text;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Cip
{
    public static class CipExplicitDataCodec
    {
        public static PlcReadResult Decode(
            PlcDataType dataType,
            byte[] data,
            int elementCount,
            int elementOffset)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (elementCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(elementCount));
            if (elementOffset < 0)
                throw new ArgumentOutOfRangeException(nameof(elementOffset));

            if (dataType == PlcDataType.String)
            {
                if (elementOffset != 0)
                    throw new ArgumentException("CIP 字符串属性不支持元素偏移。", nameof(elementOffset));
                return DecodeString(data);
            }

            ushort typeCode = CipTypeCodes.FromPlcDataType(dataType);
            int count = PlcDataTypeHelper.IsArray(dataType) ? elementCount : 1;
            int elementSize = PlcDataTypeHelper.GetElementSize(dataType);
            int byteOffset = checked(elementOffset * elementSize);
            int byteCount = checked(count * elementSize);
            if (byteOffset > data.Length || data.Length - byteOffset < byteCount)
                throw new InvalidOperationException("CIP 属性返回的数据长度不足，无法按配置的数据类型解码。");

            byte[] selected = new byte[byteCount];
            Buffer.BlockCopy(data, byteOffset, selected, 0, byteCount);
            object value = CipDataCodec.Decode(dataType, typeCode, selected, count);
            return new PlcReadResult(typeCode, CipTypeCodes.ToName(typeCode), value);
        }

        public static byte[] Encode(PlcDataType dataType, string valueText, int elementOffset)
        {
            if (elementOffset != 0)
                throw new ArgumentException("Set_Attribute_Single 只能写入完整属性，元素偏移必须为 0。", nameof(elementOffset));

            if (dataType != PlcDataType.String)
                return CipDataCodec.Encode(dataType, valueText);

            byte[] text = Encoding.UTF8.GetBytes(valueText ?? string.Empty);
            if (text.Length > ushort.MaxValue)
                throw new ArgumentException("CIP STRING 长度不能超过 65535 字节。", nameof(valueText));

            using MemoryStream stream = new MemoryStream();
            CipPath.WriteUInt16(stream, (ushort)text.Length);
            stream.Write(text, 0, text.Length);
            return stream.ToArray();
        }

        private static PlcReadResult DecodeString(byte[] data)
        {
            if (data.Length == 0)
                return new PlcReadResult(CipTypeCodes.String, "STRING", string.Empty);

            int length;
            int offset;
            ushort typeCode;
            if (data.Length >= 2)
            {
                int longLength = data[0] | (data[1] << 8);
                if (longLength <= data.Length - 2)
                {
                    length = longLength;
                    offset = 2;
                    typeCode = CipTypeCodes.String;
                }
                else
                {
                    length = data[0];
                    offset = 1;
                    typeCode = CipTypeCodes.ShortString;
                }
            }
            else
            {
                length = data[0];
                offset = 1;
                typeCode = CipTypeCodes.ShortString;
            }

            if (length > data.Length - offset)
                throw new InvalidOperationException("CIP 字符串属性的长度字段无效。");
            string value = Encoding.UTF8.GetString(data, offset, length).TrimEnd('\0');
            return new PlcReadResult(typeCode, CipTypeCodes.ToName(typeCode), value);
        }
    }
}

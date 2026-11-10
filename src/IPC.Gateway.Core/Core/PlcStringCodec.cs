using System.Text;

namespace IPC.Plc.Communication.Core;

/// <summary>Byte capacities exclude protocol headers and terminators.</summary>
public static class PlcStringCodec
{
    public static byte[] EncodeFixed(string? text, int capacity, Encoding encoding, int alignment = 1)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (alignment <= 0) throw new ArgumentOutOfRangeException(nameof(alignment));
        byte[] result = new byte[checked((checked(capacity + alignment - 1) / alignment) * alignment)];
        byte[] encoded = encoding.GetBytes(text ?? string.Empty);
        int length = Math.Min(capacity, encoded.Length);
        // Do not split a UTF-8 character at the configured byte boundary.
        if (encoding.CodePage == Encoding.UTF8.CodePage && length < encoded.Length)
            while (length > 0 && (encoded[length] & 0xc0) == 0x80) length--;
        encoded.AsSpan(0, length).CopyTo(result);
        return result;
    }

    public static string DecodeFixed(byte[] data, int offset, int capacity, Encoding encoding)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (offset < 0 || offset > data.Length || capacity > data.Length - offset)
            throw new InvalidOperationException("字符串读取响应长度不足。");
        int length = 0;
        while (length < capacity && data[offset + length] != 0) length++;
        return encoding.GetString(data, offset, length);
    }

    public static void PreserveRegisterTail(byte[] encoded, int capacity, byte[] lastRegister)
    {
        if ((capacity & 1) == 0) return;
        if (lastRegister.Length != 2 || encoded.Length != capacity + 1)
            throw new InvalidOperationException("保留字符串末尾相邻字节时，寄存器响应长度无效。");
        encoded[capacity] = lastRegister[1];
    }
}

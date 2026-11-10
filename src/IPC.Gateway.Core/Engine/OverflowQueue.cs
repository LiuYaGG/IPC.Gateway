using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IPC.Runtime.Engine;

/// <summary>
/// FIFO with a bounded in-memory portion. Bursts spill to a temporary file instead of
/// dropping transitions or blocking a device actor on a subscriber that may write to it.
/// The owner must serialize access and call Clear on shutdown. This is not a crash journal.
/// </summary>
internal sealed class OverflowQueue<T>
{
    private readonly Queue<T> _memory = new();
    private readonly int _memoryLimit;
    private FileStream? _spill;
    private long _readPosition;
    private int _spillCount;
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public OverflowQueue(int memoryLimit) => _memoryLimit = Math.Max(1, memoryLimit);
    public int Count => _memory.Count + _spillCount;

    public void Enqueue(T value)
    {
        if (_spillCount == 0 && _memory.Count < _memoryLimit)
        {
            _memory.Enqueue(value);
            return;
        }

        byte[] data = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        _spill ??= new FileStream(Path.Combine(Path.GetTempPath(), "ipc-events-" + Guid.NewGuid().ToString("N") + ".tmp"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
        long previousLength = _spill.Length;
        try
        {
            _spill.Position = previousLength;
            Span<byte> header = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header, data.Length);
            _spill.Write(header);
            _spill.Write(data);
            _spillCount++;
        }
        catch
        {
            _spill.SetLength(previousLength);
            throw;
        }
    }

    public T Dequeue()
    {
        if (_memory.Count > 0) return _memory.Dequeue();
        if (_spillCount == 0 || _spill == null) throw new InvalidOperationException("Queue is empty.");
        _spill.Position = _readPosition;
        Span<byte> header = stackalloc byte[4];
        _spill.ReadExactly(header);
        int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 0 || length > _spill.Length - _spill.Position)
            throw new InvalidDataException("Runtime event spool record is invalid.");
        byte[] data = new byte[length];
        _spill.ReadExactly(data);
        T value = JsonSerializer.Deserialize<T>(data, JsonOptions)!;
        _readPosition = _spill.Position;
        if (--_spillCount == 0)
        {
            _spill.Dispose();
            _spill = null;
            _readPosition = 0;
        }
        return value;
    }

    public void Clear()
    {
        _memory.Clear();
        _spill?.Dispose();
        _spill = null;
        _readPosition = 0;
        _spillCount = 0;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        options.Converters.Add(new SnapshotValueConverter());
        return options;
    }

    // object-valued PLC data must retain its numeric/array type after spilling.
    // Never resolve arbitrary CLR types from the spool.
    private sealed class SnapshotValueConverter : JsonConverter<object>
    {
        private static readonly Dictionary<string, Type> Types = BuildTypes();
        private static Dictionary<string, Type> BuildTypes()
        {
            var types = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (Type type in new[] { typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
                typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal),
                typeof(string), typeof(char), typeof(DateTime), typeof(DateTimeOffset), typeof(TimeSpan), typeof(Guid), typeof(object) })
            {
                types[type.FullName!] = type;
                types[type.MakeArrayType().FullName!] = type.MakeArrayType();
            }
            types[typeof(JsonElement).FullName!] = typeof(JsonElement);
            return types;
        }

        public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            string name = root.GetProperty("type").GetString()!;
            if (!Types.TryGetValue(name, out Type? type) || type == typeof(object))
                return root.GetProperty("value").Clone();
            return root.GetProperty("value").Deserialize(type, options);
        }

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            Type type = value.GetType();
            bool known = Types.ContainsKey(type.FullName!) && type != typeof(object);
            writer.WriteStartObject();
            writer.WriteString("type", known ? type.FullName : typeof(JsonElement).FullName);
            writer.WritePropertyName("value");
            if (type == typeof(object)) writer.WriteStartObject();
            else JsonSerializer.Serialize(writer, value, type, options);
            if (type == typeof(object)) writer.WriteEndObject();
            writer.WriteEndObject();
        }
    }
}

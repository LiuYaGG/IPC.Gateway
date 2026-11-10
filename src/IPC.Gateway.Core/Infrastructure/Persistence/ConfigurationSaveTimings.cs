using System.Diagnostics;
using System.Text;

namespace IPC.Gateway.Core.Infrastructure.Persistence;

internal sealed class ConfigurationSaveTimings : IDisposable
{
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private readonly List<string> _steps = new();
    private readonly string _type;
    private int _bytes;
    private long _previous;

    public ConfigurationSaveTimings(string type, string payload)
    {
        _type = type;
        _bytes = Encoding.UTF8.GetByteCount(payload);
    }

    public void SetPayload(string payload) => _bytes = Encoding.UTF8.GetByteCount(payload);
    public void SetPayload(byte[] payload) => _bytes = payload.Length;

    public void Mark(string name)
    {
        long current = _watch.ElapsedMilliseconds;
        _steps.Add(name + "=" + (current - _previous) + "ms");
        _previous = current;
    }

    public void Dispose() => IpcLogService.WriteInfo(
        $"ConfigurationSaveTiming type={_type} bytes={_bytes} totalMs={_watch.ElapsedMilliseconds} steps=[{string.Join("; ", _steps)}]");
}

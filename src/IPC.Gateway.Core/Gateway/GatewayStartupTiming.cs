using System.Diagnostics;

namespace IPC.Gateway.Core.Gateway;

public static class GatewayStartupTiming
{
    private static long RecordStart(string stage)
    {
        IpcLogService.WriteInfo($"GatewayStartup pid={Environment.ProcessId} stage={stage} status=开始");
        return Stopwatch.GetTimestamp();
    }

    private static void RecordFailure(string stage, long start, Exception error) =>
        IpcLogService.WriteError($"GatewayStartup pid={Environment.ProcessId} stage={stage} status=失败 elapsedMs={(long)Stopwatch.GetElapsedTime(start).TotalMilliseconds} errorType={error.GetType().Name}");

    public static void RecordCompleted(string stage, long startTimestamp) =>
        IpcLogService.WriteInfo($"GatewayStartup pid={Environment.ProcessId} stage={stage} status=完成 elapsedMs={(long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds}");

    public static void Measure(string stage, Action action) =>
        Measure(stage, () => { action(); return true; });

    public static T Measure<T>(string stage, Func<T> action)
    {
        long start = RecordStart(stage);
        try
        {
            T result = action();
            RecordCompleted(stage, start);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(stage, start, ex);
            throw;
        }
    }

    public static Task MeasureAsync(string stage, Func<Task> action) =>
        MeasureAsync(stage, async () => { await action().ConfigureAwait(false); return true; });

    public static async Task<T> MeasureAsync<T>(string stage, Func<Task<T>> action)
    {
        long start = RecordStart(stage);
        try
        {
            T result = await action().ConfigureAwait(false);
            RecordCompleted(stage, start);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(stage, start, ex);
            throw;
        }
    }
}

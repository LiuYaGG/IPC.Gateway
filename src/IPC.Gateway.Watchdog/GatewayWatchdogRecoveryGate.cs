namespace IPC.Gateway.Watchdog;

internal sealed class GatewayWatchdogRecoveryGate
{
    private int _active;

    public bool TryEnter()
    {
        return Interlocked.CompareExchange(ref _active, 1, 0) == 0;
    }

    public void Release()
    {
        Interlocked.Exchange(ref _active, 0);
    }

    public Task ReleaseWhenCompleted(Task recoveryTask)
    {
        ArgumentNullException.ThrowIfNull(recoveryTask);
        return recoveryTask.ContinueWith(
            completed =>
            {
                _ = completed.Exception;
                Release();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}

namespace IPC.Plc.Communication.Core
{
    public enum PlcClientAsyncKind
    {
        SyncOnly = 0,
        SynchronousCompletion = 1,
        DedicatedThread = 2,
        NativeIo = 3
    }
}

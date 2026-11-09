using IPC.Runtime.Values;

namespace IPC.Runtime.Engine
{
    internal sealed class RuntimeHealthEvaluator
    {
        public RuntimeSchedulerHealth Evaluate(RuntimeSchedulerStatus status)
        {
            return RuntimeSchedulerHealthEvaluator.Evaluate(status);
        }
    }
}

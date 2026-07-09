using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IPC.Plc.Communication.Core
{
    public interface IPlcSubscription : IDisposable
    {
        bool IsActive { get; }
        IReadOnlyCollection<string> MonitoredKeys { get; }
        ValueTask UpdateAsync(
            IList<PlcSubscriptionRequest> requests,
            PlcSubscriptionOptions options,
            CancellationToken cancellationToken);
    }
}

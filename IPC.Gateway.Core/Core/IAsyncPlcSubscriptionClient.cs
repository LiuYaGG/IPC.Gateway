using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IPC.Plc.Communication.Core
{
    public interface IAsyncPlcSubscriptionClient
    {
        ValueTask<IPlcSubscription> SubscribeAsync(
            IList<PlcSubscriptionRequest> requests,
            PlcSubscriptionOptions options,
            Func<PlcSubscriptionUpdate, ValueTask> onUpdate,
            CancellationToken cancellationToken);
    }
}

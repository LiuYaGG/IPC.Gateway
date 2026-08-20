using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IPC.Plc.Communication.Core;

namespace IPC.Gateway.LegacyProtocolPlugins
{
    internal class LicensedPlcClient : IPlcClient, IPlcClientCapabilityProvider
    {
        protected readonly string DriverId;
        protected readonly IPlcClient Inner;

        protected LicensedPlcClient(string driverId, IPlcClient inner)
        {
            DriverId = driverId ?? throw new ArgumentNullException(nameof(driverId));
            Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public static IPlcClient Wrap(string driverId, IPlcClient inner)
        {
            bool async = inner is IAsyncPlcClient;
            bool asyncBatch = inner is IAsyncPlcBatchReadClient;
            bool batch = inner is IPlcBatchReadClient;
            bool subscription = inner is IAsyncPlcSubscriptionClient;

            if (async && asyncBatch && subscription)
                return new LicensedAsyncBatchSubscriptionPlcClient(driverId, inner);
            if (batch && subscription)
                return new LicensedBatchSubscriptionPlcClient(driverId, inner);
            if (async && asyncBatch)
                return new LicensedAsyncBatchPlcClient(driverId, inner);
            if (batch)
                return new LicensedBatchPlcClient(driverId, inner);
            if (async)
                return new LicensedAsyncPlcClient(driverId, inner);
            return new LicensedPlcClient(driverId, inner);
        }

        public bool IsConnected
        {
            get
            {
                try
                {
                    DemandLicense();
                    return Inner.IsConnected;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }
        }

        public PlcProtocol Protocol => Inner.Protocol;

        public void Connect()
        {
            DemandLicense();
            Inner.Connect();
        }

        public void Disconnect() => Inner.Disconnect();

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            DemandLicense();
            return Inner.Read(address, dataType, elementCount, elementOffset);
        }

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
            DemandLicense();
            Inner.Write(address, dataType, valueText, elementOffset);
        }

        public PlcClientCapabilities GetCapabilities()
        {
            return Inner is IPlcClientCapabilityProvider provider
                ? provider.GetCapabilities()
                : PlcClientCapabilityCatalog.ForProtocol(Inner.Protocol);
        }

        public void Dispose() => Inner.Dispose();

        protected void DemandLicense() => LegacyPluginLicense.EnsureDriverAllowed(DriverId);
    }

    internal class LicensedBatchPlcClient : LicensedPlcClient, IPlcBatchReadClient
    {
        public LicensedBatchPlcClient(string driverId, IPlcClient inner) : base(driverId, inner) { }

        public IList<PlcBatchReadResult> ReadMany(IList<PlcBatchReadRequest> requests)
        {
            DemandLicense();
            return ((IPlcBatchReadClient)Inner).ReadMany(requests);
        }
    }

    internal class LicensedAsyncPlcClient : LicensedPlcClient, IAsyncPlcClient
    {
        public LicensedAsyncPlcClient(string driverId, IPlcClient inner) : base(driverId, inner) { }

        public ValueTask ConnectAsync(CancellationToken cancellationToken)
        {
            DemandLicense();
            return ((IAsyncPlcClient)Inner).ConnectAsync(cancellationToken);
        }

        public ValueTask DisconnectAsync(CancellationToken cancellationToken) =>
            ((IAsyncPlcClient)Inner).DisconnectAsync(cancellationToken);

        public ValueTask<PlcReadResult> ReadAsync(string address, PlcDataType dataType, int elementCount, int elementOffset, CancellationToken cancellationToken)
        {
            DemandLicense();
            return ((IAsyncPlcClient)Inner).ReadAsync(address, dataType, elementCount, elementOffset, cancellationToken);
        }

        public ValueTask WriteAsync(string address, PlcDataType dataType, string valueText, int elementOffset, CancellationToken cancellationToken)
        {
            DemandLicense();
            return ((IAsyncPlcClient)Inner).WriteAsync(address, dataType, valueText, elementOffset, cancellationToken);
        }
    }

    internal class LicensedAsyncBatchPlcClient : LicensedAsyncPlcClient, IPlcBatchReadClient, IAsyncPlcBatchReadClient
    {
        public LicensedAsyncBatchPlcClient(string driverId, IPlcClient inner) : base(driverId, inner) { }

        public IList<PlcBatchReadResult> ReadMany(IList<PlcBatchReadRequest> requests)
        {
            DemandLicense();
            return ((IPlcBatchReadClient)Inner).ReadMany(requests);
        }

        public ValueTask<IList<PlcBatchReadResult>> ReadManyAsync(IList<PlcBatchReadRequest> requests, CancellationToken cancellationToken)
        {
            DemandLicense();
            return ((IAsyncPlcBatchReadClient)Inner).ReadManyAsync(requests, cancellationToken);
        }
    }

    internal sealed class LicensedBatchSubscriptionPlcClient : LicensedBatchPlcClient, IAsyncPlcSubscriptionClient
    {
        public LicensedBatchSubscriptionPlcClient(string driverId, IPlcClient inner) : base(driverId, inner) { }

        public ValueTask<IPlcSubscription> SubscribeAsync(IList<PlcSubscriptionRequest> requests, PlcSubscriptionOptions options, Func<PlcSubscriptionUpdate, ValueTask> onUpdate, CancellationToken cancellationToken)
        {
            DemandLicense();
            return ((IAsyncPlcSubscriptionClient)Inner).SubscribeAsync(
                requests,
                options,
                update =>
                {
                    DemandLicense();
                    return onUpdate(update);
                },
                cancellationToken);
        }
    }

    internal sealed class LicensedAsyncBatchSubscriptionPlcClient : LicensedAsyncBatchPlcClient, IAsyncPlcSubscriptionClient
    {
        public LicensedAsyncBatchSubscriptionPlcClient(string driverId, IPlcClient inner) : base(driverId, inner) { }

        public ValueTask<IPlcSubscription> SubscribeAsync(IList<PlcSubscriptionRequest> requests, PlcSubscriptionOptions options, Func<PlcSubscriptionUpdate, ValueTask> onUpdate, CancellationToken cancellationToken)
        {
            DemandLicense();
            return ((IAsyncPlcSubscriptionClient)Inner).SubscribeAsync(
                requests,
                options,
                update =>
                {
                    DemandLicense();
                    return onUpdate(update);
                },
                cancellationToken);
        }
    }
}

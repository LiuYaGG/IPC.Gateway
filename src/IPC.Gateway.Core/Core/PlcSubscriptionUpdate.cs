using System;

namespace IPC.Plc.Communication.Core
{
    public sealed class PlcSubscriptionUpdate
    {
        private PlcSubscriptionUpdate(PlcSubscriptionRequest request)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            Key = request.Key ?? string.Empty;
            ErrorMessage = string.Empty;
            FailureScope = PlcReadFailureScope.None;
            TimestampUtc = DateTime.UtcNow;
        }

        public string Key { get; private set; }
        public PlcSubscriptionRequest Request { get; private set; }
        public PlcReadResult? Result { get; private set; }
        public string ErrorMessage { get; private set; }
        public PlcReadFailureScope FailureScope { get; private set; }
        public DateTime TimestampUtc { get; private set; }

        public bool Success
        {
            get { return Result != null && string.IsNullOrEmpty(ErrorMessage); }
        }

        public static PlcSubscriptionUpdate FromSuccess(PlcSubscriptionRequest request, PlcReadResult result)
        {
            return new PlcSubscriptionUpdate(request)
            {
                Result = result ?? throw new ArgumentNullException(nameof(result)),
                FailureScope = PlcReadFailureScope.None
            };
        }

        public static PlcSubscriptionUpdate FromFailure(PlcSubscriptionRequest request, string errorMessage, PlcReadFailureScope failureScope)
        {
            return new PlcSubscriptionUpdate(request)
            {
                ErrorMessage = errorMessage ?? string.Empty,
                FailureScope = failureScope == PlcReadFailureScope.None ? PlcReadFailureScope.Tag : failureScope
            };
        }
    }
}

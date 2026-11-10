using System;
using System.Collections.Generic;

namespace IPC.Plc.Communication.Core
{
    public enum PlcReadFailureScope
    {
        None = 0,
        Tag = 1,
        Batch = 2,
        Device = 3,
        Session = 4,
        Transport = 5
    }

    public sealed class PlcBatchReadResult
    {
        private PlcBatchReadResult(PlcBatchReadRequest request)
        {
            Request = request ?? throw new ArgumentNullException("request");
            ErrorMessage = string.Empty;
            FailureScope = PlcReadFailureScope.None;
        }

        public PlcBatchReadRequest Request { get; private set; }
        public PlcReadResult? Result { get; private set; }
        public string ErrorMessage { get; private set; }
        public bool IsCommunicationError { get; private set; }
        public PlcReadFailureScope FailureScope { get; private set; }

        public bool Success
        {
            get { return Result != null && string.IsNullOrEmpty(ErrorMessage); }
        }

        public static PlcBatchReadResult FromSuccess(PlcBatchReadRequest request, PlcReadResult result)
        {
            return new PlcBatchReadResult(request)
            {
                Result = result ?? throw new ArgumentNullException("result")
            };
        }

        public static PlcBatchReadResult FromFailure(PlcBatchReadRequest request, string errorMessage, bool isCommunicationError)
        {
            return FromFailure(request, errorMessage, isCommunicationError ? PlcReadFailureScope.Transport : PlcReadFailureScope.Tag);
        }

        public static PlcBatchReadResult FromFailure(PlcBatchReadRequest request, string errorMessage, PlcReadFailureScope failureScope)
        {
            return new PlcBatchReadResult(request)
            {
                ErrorMessage = errorMessage ?? string.Empty,
                FailureScope = NormalizeFailureScope(failureScope),
                IsCommunicationError = IsConnectionFailureScope(failureScope)
            };
        }

        public static bool IsConnectionFailureScope(PlcReadFailureScope failureScope)
        {
            return failureScope == PlcReadFailureScope.Device ||
                   failureScope == PlcReadFailureScope.Session ||
                   failureScope == PlcReadFailureScope.Transport;
        }

        // Preserve completed reads when a later wire request fails or times out.
        public static void FailPending(IList<PlcBatchReadRequest> requests, PlcBatchReadResult[] results, Exception error)
        {
            PlcReadFailureScope scope = error is OperationCanceledException
                ? PlcReadFailureScope.Transport
                : PlcFailureClassifier.Classify(error, PlcReadFailureScope.Batch);
            string message = error is OperationCanceledException ? "PLC batch operation timed out or was cancelled." : error.Message;
            for (int i = 0; i < results.Length; i++)
                results[i] ??= FromFailure(requests[i], message, scope);
        }

        private static PlcReadFailureScope NormalizeFailureScope(PlcReadFailureScope failureScope)
        {
            return failureScope == PlcReadFailureScope.None ? PlcReadFailureScope.Tag : failureScope;
        }
    }
}

using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Cip
{
    internal sealed class CipBatchReadOperation
    {
        public CipBatchReadOperation(
            CipBatchReadItem owner,
            int operationIndex,
            string tagName,
            PlcDataType dataType,
            int elementCount,
            byte[] requestBytes)
        {
            Owner = owner;
            OperationIndex = operationIndex;
            TagName = tagName;
            DataType = dataType;
            ElementCount = elementCount;
            RequestBytes = requestBytes;
            ErrorMessage = string.Empty;
        }

        public CipBatchReadItem Owner { get; private set; }
        public int OperationIndex { get; private set; }
        public string TagName { get; private set; }
        public PlcDataType DataType { get; private set; }
        public int ElementCount { get; private set; }
        public byte[] RequestBytes { get; private set; }
        public PlcReadResult Result { get; private set; }
        public string ErrorMessage { get; private set; }
        public bool IsCommunicationError { get; private set; }
        public PlcReadFailureScope FailureScope { get; private set; }

        public bool Success
        {
            get { return Result != null && string.IsNullOrEmpty(ErrorMessage); }
        }

        public void SetSuccess(PlcReadResult result)
        {
            Result = result;
            ErrorMessage = string.Empty;
            IsCommunicationError = false;
            FailureScope = PlcReadFailureScope.None;
        }

        public void SetFailure(string errorMessage, bool isCommunicationError)
        {
            SetFailure(
                errorMessage,
                isCommunicationError ? PlcReadFailureScope.Transport : PlcReadFailureScope.Tag);
        }

        public void SetFailure(string errorMessage, PlcReadFailureScope failureScope)
        {
            ErrorMessage = errorMessage ?? string.Empty;
            FailureScope = failureScope == PlcReadFailureScope.None ? PlcReadFailureScope.Tag : failureScope;
            IsCommunicationError = PlcBatchReadResult.IsConnectionFailureScope(FailureScope);
        }
    }

    internal enum CipBatchReadKind
    {
        Direct,
        PackedBool,
        IndexedBool,
        SegmentedArray
    }
}

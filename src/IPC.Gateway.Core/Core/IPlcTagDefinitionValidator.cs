namespace IPC.Plc.Communication.Core
{
    public interface IPlcTagDefinitionValidator
    {
        PlcTagValidationResult ValidateTag(
            PlcConnectionOptions options,
            string address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset);
    }

    public sealed class PlcTagValidationResult
    {
        private PlcTagValidationResult(bool isValid, string normalizedAddress, string errorMessage)
        {
            IsValid = isValid;
            NormalizedAddress = normalizedAddress ?? string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public bool IsValid { get; }
        public string NormalizedAddress { get; }
        public string ErrorMessage { get; }

        public static PlcTagValidationResult Valid(string normalizedAddress) =>
            new PlcTagValidationResult(true, normalizedAddress, string.Empty);

        public static PlcTagValidationResult Invalid(string normalizedAddress, string errorMessage) =>
            new PlcTagValidationResult(false, normalizedAddress, errorMessage);
    }
}

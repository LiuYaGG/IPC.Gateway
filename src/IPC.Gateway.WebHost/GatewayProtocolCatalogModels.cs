using System.Collections.Generic;

namespace IPC.Gateway.WebHost;

public sealed class GatewayProtocolCatalogItem
{
    public string DriverId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool BuiltIn { get; set; }
    public string SignatureStatus { get; set; } = string.Empty;
    public string SignatureError { get; set; } = string.Empty;
    public GatewayProtocolCapabilities Capabilities { get; set; } = new GatewayProtocolCapabilities();
    public IList<GatewayConnectionParameterDefinition> Parameters { get; set; } = new List<GatewayConnectionParameterDefinition>();
}

public sealed class GatewayProtocolCapabilities
{
    public string AsyncKind { get; set; } = string.Empty;
    public string PreferredReadMode { get; set; } = string.Empty;
    public bool SupportsRead { get; set; }
    public bool SupportsWrite { get; set; }
    public bool SupportsNativeAsync { get; set; }
    public bool SupportsBatchRead { get; set; }
    public bool SupportsSubscription { get; set; }
    public bool SupportsAddressValidation { get; set; }
    public bool SupportsConcurrentRequests { get; set; }
    public bool RequiresSerializedAccess { get; set; }
    public int MaxBatchItems { get; set; }
    public int MaxSubscriptionItems { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public sealed class GatewayConnectionParameterDefinition
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string ParameterType { get; set; } = "text";
    public string Group { get; set; } = string.Empty;
    public string DefaultValue { get; set; } = string.Empty;
    public string Placeholder { get; set; } = string.Empty;
    public string HelpText { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public bool Required { get; set; }
    public bool Secret { get; set; }
    public bool Advanced { get; set; }
    public bool ReadOnly { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public IList<string> Options { get; set; } = new List<string>();
}

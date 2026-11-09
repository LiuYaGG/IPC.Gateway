using System.Collections.Generic;
using System.Linq;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.Infrastructure;

namespace IPC.Gateway.WebHost;

public sealed class GatewayProtocolCatalogService
{
    private static readonly HashSet<string> HiddenProtocols = new(StringComparer.OrdinalIgnoreCase)
    {
        "MitsubishiSerial"
    };

    public IReadOnlyList<GatewayProtocolCatalogItem> GetProtocols()
    {
        PlcDriverPluginRegistry.RefreshDefaultPlugins();
        return PlcDriverPluginRegistry.GetRegisteredDrivers()
            .Where(driver =>
                !string.IsNullOrWhiteSpace(driver.Protocol) &&
                IsVisibleProtocol(driver.Protocol))
            .Select(ToCatalogItem)
            .OrderBy(item => CategoryOrder(item.Category))
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static bool IsVisibleProtocol(string protocol)
    {
        return !string.IsNullOrWhiteSpace(protocol) && !HiddenProtocols.Contains(protocol);
    }

    private static GatewayProtocolCatalogItem ToCatalogItem(PlcDriverPluginInfo driver)
    {
        return new GatewayProtocolCatalogItem
        {
            DriverId = driver.DriverId,
            DisplayName = driver.DisplayName,
            Protocol = driver.Protocol,
            Category = GetCategory(driver.Protocol),
            BuiltIn = driver.BuiltIn,
            SignatureStatus = driver.SignatureStatus,
            SignatureError = driver.SignatureError,
            Capabilities = ToCapabilities(driver.Capabilities),
            Parameters = driver.ConnectionParameters.Select(ToParameter).ToList()
        };
    }

    private static GatewayProtocolCapabilities ToCapabilities(PlcClientCapabilities capabilities)
    {
        return new GatewayProtocolCapabilities
        {
            AsyncKind = capabilities.AsyncKind.ToString(),
            PreferredReadMode = capabilities.PreferredReadMode.ToString(),
            SupportsRead = capabilities.SupportsRead,
            SupportsWrite = capabilities.SupportsWrite,
            SupportsNativeAsync = capabilities.SupportsNativeAsync,
            SupportsBatchRead = capabilities.SupportsBatchRead,
            SupportsSubscription = capabilities.SupportsSubscription,
            SupportsAddressValidation = capabilities.SupportsAddressValidation,
            SupportsConcurrentRequests = capabilities.SupportsConcurrentRequests,
            RequiresSerializedAccess = capabilities.RequiresSerializedAccess,
            MaxBatchItems = capabilities.MaxBatchItems,
            MaxSubscriptionItems = capabilities.MaxSubscriptionItems,
            Notes = capabilities.Notes
        };
    }

    private static GatewayConnectionParameterDefinition ToParameter(PlcConnectionParameterDefinition parameter)
    {
        return new GatewayConnectionParameterDefinition
        {
            Key = parameter.Key,
            Label = parameter.Label,
            ParameterType = parameter.ParameterType,
            Group = parameter.Group,
            DefaultValue = parameter.DefaultValue,
            Placeholder = parameter.Placeholder,
            HelpText = parameter.HelpText,
            Unit = parameter.Unit,
            Required = parameter.Required,
            Secret = parameter.Secret,
            Advanced = parameter.Advanced,
            ReadOnly = parameter.ReadOnly,
            Min = parameter.Min,
            Max = parameter.Max,
            Options = parameter.Options == null ? new List<string>() : new List<string>(parameter.Options)
        };
    }

    private static string GetCategory(string protocol)
    {
        return protocol switch
        {
            "VirtualPlc" => "simulated",
            "Dlt6452007" or "Cjt1882004" or "Cjt1882018" => "meter",
            "ModbusRtu" or "ModbusAscii" or "MitsubishiSerial" or "MitsubishiQlSerial" => "serial",
            "OpcUa" or "OpcDa" => "opc",
            "Plugin" => "plugin",
            _ => "network"
        };
    }

    private static int CategoryOrder(string category)
    {
        return category switch
        {
            "simulated" => 0,
            "network" => 1,
            "meter" => 2,
            "serial" => 3,
            "opc" => 4,
            "plugin" => 5,
            _ => 9
        };
    }
}

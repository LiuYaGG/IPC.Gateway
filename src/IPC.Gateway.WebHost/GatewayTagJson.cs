using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using IPC.Gateway.Core.Application.Gateway.Contracts;

namespace IPC.Gateway.WebHost;

internal static class GatewayTagJson
{
    public static void Configure(JsonSerializerOptions options)
    {
        var resolver = options.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver();
        options.TypeInfoResolver = resolver.WithAddedModifier(ConfigureType);
    }

    private static void ConfigureType(JsonTypeInfo type)
    {
        if (type.Type == typeof(WriteTagResultDto))
        {
            foreach (var property in type.Properties)
                if (property.Name == "errorMessage")
                    property.ShouldSerialize = HasText;
            return;
        }

        if (type.Type != typeof(TagValueSnapshotDto)) return;
        foreach (var property in type.Properties)
        {
            switch (property.Name)
            {
                case "rawValueText":
                    // An empty raw value can legitimately differ from the processed text.
                    property.ShouldSerialize = static (row, raw) =>
                        !string.Equals((string?)raw, ((TagValueSnapshotDto)row).ValueText, StringComparison.Ordinal);
                    break;
                case "unit":
                case "pointCode":
                case "assetPath":
                case "businessType":
                case "source":
                case "cleaningAction":
                case "cleaningMessage":
                case "errorMessage":
                case "lastReadError":
                    property.ShouldSerialize = HasText;
                    break;
                case "precision":
                    property.ShouldSerialize = static (_, value) => value is int precision && precision != -1;
                    break;
                case "cleaningApplied":
                case "isTagIsolated":
                case "isStaticValidationError":
                    property.ShouldSerialize = static (_, value) => value is true;
                    break;
                case "tagConsecutiveFailures":
                    property.ShouldSerialize = static (_, value) => value is int failures && failures != 0;
                    break;
                case "nextTagRecoveryProbeTime":
                    property.ShouldSerialize = static (_, value) => value is DateTime time && time != DateTime.MinValue;
                    break;
            }
        }
    }

    private static bool HasText(object _, object? value) => value is string { Length: > 0 };
}

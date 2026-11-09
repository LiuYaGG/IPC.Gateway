using IPC.Plc.Communication.Infrastructure;

namespace IPC.Gateway.WebHost;

public sealed class GatewayCompatibilityService
{
    public GatewayCompatibilityMatrix GetMatrix()
    {
        string version = typeof(GatewayCompatibilityService).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
        GatewayCompatibilityMatrix matrix = new GatewayCompatibilityMatrix
        {
            GatewayVersion = version
        };

        matrix.Items.Add(Item("Gateway WebHost", version, version, "Compatible", "Current running WebHost assembly."));
        matrix.Items.Add(Item("Project configuration", "project.v1", "project.v1", "Compatible", "Device, group, tag, rule and flow-rule configuration schema."));
        matrix.Items.Add(Item("Project backup", "ipc.gateway.project.backup.v1", "ipc.gateway.project.backup.v1", "Compatible", "Project backup envelope."));
        matrix.Items.Add(Item("Plugin manifest", "1.0", "1.0+", "Compatible", "Supports min/max gateway versions and optional RSA signature metadata."));
        matrix.Items.Add(Item("Prometheus metrics", "0.0.4", "Prometheus text format 0.0.4", "Compatible", "Exposed at the configured metrics path."));

        foreach (var driver in PlcDriverPluginRegistry.GetRegisteredDrivers())
        {
            string range = string.IsNullOrWhiteSpace(driver.MinGatewayVersion + driver.MaxGatewayVersion)
                ? "Any"
                : (string.IsNullOrWhiteSpace(driver.MinGatewayVersion) ? "*" : driver.MinGatewayVersion) + " - " +
                  (string.IsNullOrWhiteSpace(driver.MaxGatewayVersion) ? "*" : driver.MaxGatewayVersion);
            matrix.Items.Add(Item(
                "Protocol driver: " + driver.DriverId,
                driver.Version,
                range,
                driver.SignatureStatus == "Invalid" || driver.SignatureStatus == "HashMismatch" ? "Untrusted" : "Compatible",
                string.IsNullOrWhiteSpace(driver.Signer) ? driver.Protocol : driver.Protocol + " signed by " + driver.Signer));
        }

        return matrix;
    }

    private static GatewayCompatibilityItem Item(string capability, string currentVersion, string compatibleRange, string status, string notes)
    {
        return new GatewayCompatibilityItem
        {
            Capability = capability,
            CurrentVersion = currentVersion,
            CompatibleRange = compatibleRange,
            Status = status,
            Notes = notes
        };
    }
}

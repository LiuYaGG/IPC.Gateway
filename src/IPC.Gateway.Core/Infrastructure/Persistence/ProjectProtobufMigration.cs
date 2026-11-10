using System.Text.Json;
using System.Text.Json.Serialization;
using IPC.Gateway.Core.Domain.Configuration;
using IPC.Runtime.Configuration;
using SqlSugar;

namespace IPC.Gateway.Core.Infrastructure.Persistence;

/// <summary>One-time database upgrade only; normal project reads never accept JSON.</summary>
internal static class ProjectProtobufMigration
{
    public static void ConvertProjects(ISqlSugarClient db)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        while (true)
        {
            var rows = db.Queryable<GatewayConfigurationEntity>()
                .Where(row => row.ConfigType == GatewayConfigurationType.Project && row.PayloadBinary == null)
                .OrderBy(row => row.Version)
                .Take(32).ToList();
            if (rows.Count == 0) return;
            foreach (var row in rows)
            {
                try
                {
                    var project = JsonSerializer.Deserialize<ProjectConfig>(row.Payload, options)
                        ?? throw new InvalidDataException("Project configuration is empty.");
                    // Existing protected secrets stay encrypted byte-for-byte.
                    var bytes = ProjectProtobufCodec.SerializeProject(project);
                    db.Updateable<GatewayConfigurationEntity>()
                        .SetColumns(value => new GatewayConfigurationEntity { PayloadBinary = bytes, Payload = string.Empty })
                        .Where(value => value.Id == row.Id).ExecuteCommand();
                }
                catch (Exception error)
                {
                    throw new InvalidOperationException($"Project Protobuf migration failed at configuration version {row.Version} ({row.Id}).", error);
                }
            }
        }
    }
}

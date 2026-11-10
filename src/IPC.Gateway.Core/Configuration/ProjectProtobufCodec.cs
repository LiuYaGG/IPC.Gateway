using System.Collections;
using System.Linq.Expressions;
using IPC.EdgeGateway;
using IPC.Gateway.Core.Gateway;
using ProtoBuf;
using ProtoBuf.Meta;

namespace IPC.Runtime.Configuration;

/// <summary>Binary project storage. Field numbers in ProjectProtobufContracts are permanent.</summary>
public static class ProjectProtobufCodec
{
    private const uint ProjectMagic = 0x49504350;
    private const uint BackupMagic = 0x49504342;
    private const int FormatVersion = 1;
    private static readonly RuntimeTypeModel Model = CreateModel();
    private static readonly IReadOnlyDictionary<Type, ReferenceField[]> ReferenceFields = CreateReferenceFields();

    public static byte[] SerializeProject(ProjectConfig project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return Serialize(new ProjectDocument { Magic = ProjectMagic, Version = FormatVersion, Project = project });
    }

    public static ProjectConfig DeserializeProject(byte[] payload)
    {
        var document = Deserialize<ProjectDocument>(payload);
        if (document.Magic != ProjectMagic || document.Version != FormatVersion || document.Project == null)
            throw new InvalidDataException("Unsupported Protobuf project file or format version.");
        RestoreEmptyReferences(document.Project);
        return document.Project;
    }

    public static byte[] SerializeBackup(ProjectBackupDocument backup)
    {
        ArgumentNullException.ThrowIfNull(backup);
        return Serialize(new BackupDocument { Magic = BackupMagic, Version = FormatVersion, Backup = backup });
    }

    public static ProjectBackupDocument DeserializeBackup(byte[] payload)
    {
        var document = Deserialize<BackupDocument>(payload);
        if (document.Magic != BackupMagic || document.Version != FormatVersion || document.Backup == null)
            throw new InvalidDataException("Unsupported Protobuf project backup or format version.");
        var backup = document.Backup;
        if (backup.Project == null || backup.Mqtt == null || backup.OpcUa == null || backup.History == null || backup.StorageHealth == null)
            throw new InvalidDataException("The Protobuf project backup is incomplete.");
        RestoreEmptyReferences(backup.Project);
        RestoreEmptyReferences(backup.Mqtt);
        RestoreEmptyReferences(backup.OpcUa);
        RestoreEmptyReferences(backup.History);
        RestoreEmptyReferences(backup.StorageHealth);
        return backup;
    }

    private static byte[] Serialize<T>(T value)
    {
        using var stream = new MemoryStream();
        Model.Serialize(stream, value);
        return stream.ToArray();
    }

    private static T Deserialize<T>(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length == 0) throw new InvalidDataException("The Protobuf project payload is empty.");
        try
        {
            using var stream = new MemoryStream(payload, writable: false);
            return (T)Model.Deserialize(stream, null, typeof(T));
        }
        catch (ProtoException error)
        {
            throw new InvalidDataException("Invalid Protobuf project payload.", error);
        }
    }

    private static RuntimeTypeModel CreateModel()
    {
        var model = RuntimeTypeModel.Create();
        model.IncludeDateTimeKind = true;
        ProjectProtobufContracts.Register(model);
        model.Add(typeof(ProjectDocument), false).Add(1, nameof(ProjectDocument.Magic))
            .Add(2, nameof(ProjectDocument.Version)).Add(3, nameof(ProjectDocument.Project));
        model.Add(typeof(BackupDocument), false).Add(1, nameof(BackupDocument.Magic))
            .Add(2, nameof(BackupDocument.Version)).Add(3, nameof(BackupDocument.Backup));
        model.Add(typeof(ProjectBackupDocument), false).Add(1, nameof(ProjectBackupDocument.BackupId))
            .Add(2, nameof(ProjectBackupDocument.CreatedTimeUtc)).Add(3, nameof(ProjectBackupDocument.Project))
            .Add(4, nameof(ProjectBackupDocument.Mqtt)).Add(5, nameof(ProjectBackupDocument.OpcUa))
            .Add(6, nameof(ProjectBackupDocument.History)).Add(7, nameof(ProjectBackupDocument.StorageHealth))
            .UseConstructor = false;
        model.CompileInPlace();
        return model;
    }

    // Constructors contain defaults such as Enabled=true. Skipping them preserves false/zero
    // values omitted by protobuf. Restore only empty strings and collections, with cached accessors.
    private static IReadOnlyDictionary<Type, ReferenceField[]> CreateReferenceFields()
    {
        return Model.GetTypes().Cast<MetaType>().Where(meta => meta.Type.IsClass)
            .ToDictionary(meta => meta.Type, meta => meta.GetFields()
            .Select(field => meta.Type.GetProperty(field.Member.Name)!)
            .Where(property => !property.PropertyType.IsValueType)
            .Select(property =>
            {
                var owner = Expression.Parameter(typeof(object));
                var value = Expression.Parameter(typeof(object));
                var member = Expression.Property(Expression.Convert(owner, meta.Type), property);
                var get = Expression.Lambda<Func<object, object?>>(Expression.Convert(member, typeof(object)), owner).Compile();
                var set = Expression.Lambda<Action<object, object?>>(
                    Expression.Assign(member, Expression.Convert(value, property.PropertyType)), owner, value).Compile();
                Func<object>? empty = property.PropertyType == typeof(string) ? () => string.Empty
                    : property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(List<>)
                        ? Expression.Lambda<Func<object>>(Expression.Convert(Expression.New(property.PropertyType), typeof(object))).Compile()
                        : null;
                return new ReferenceField(get, set, empty);
            }).ToArray());
    }

    private static void RestoreEmptyReferences(object item)
    {
        if (!ReferenceFields.TryGetValue(item.GetType(), out var fields)) return;
        foreach (var field in fields)
        {
            var value = field.Get(item);
            if (value == null && field.Empty != null)
                field.Set(item, value = field.Empty());
            if (value == null || value is string) continue;
            if (value is IEnumerable collection)
            {
                foreach (var child in collection)
                    if (child != null) RestoreEmptyReferences(child);
            }
            else RestoreEmptyReferences(value);
        }
    }

    private sealed record ReferenceField(Func<object, object?> Get, Action<object, object?> Set, Func<object>? Empty);

    private sealed class ProjectDocument
    {
        public uint Magic { get; set; }
        public int Version { get; set; }
        public ProjectConfig? Project { get; set; }
    }

    private sealed class BackupDocument
    {
        public uint Magic { get; set; }
        public int Version { get; set; }
        public ProjectBackupDocument? Backup { get; set; }
    }
}

public sealed class ProjectBackupDocument
{
    public string BackupId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedTimeUtc { get; set; } = DateTime.UtcNow;
    public ProjectConfig Project { get; set; } = new();
    public MqttGatewayOptions Mqtt { get; set; } = new();
    public OpcUaServerOptions OpcUa { get; set; } = new();
    public LocalHistoryOptions History { get; set; } = new();
    public StorageHealthThresholds StorageHealth { get; set; } = new();
}

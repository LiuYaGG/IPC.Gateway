namespace IPC.Gateway.Inference;

/// <summary>Resolves a persisted rule's model reference to this node's verified local cache.</summary>
public sealed class SharedModelPathResolver
{
    private IReadOnlyDictionary<string, string> _paths = new Dictionary<string, string>();

    internal void Replace(IReadOnlyDictionary<string, string> paths) => Volatile.Write(ref _paths, paths);

    public string? Resolve(string reference) => Volatile.Read(ref _paths).TryGetValue(Normalize(reference), out var path) ? path : null;

    internal static string Normalize(string reference) => reference.Trim().Replace('\\', '/');
}

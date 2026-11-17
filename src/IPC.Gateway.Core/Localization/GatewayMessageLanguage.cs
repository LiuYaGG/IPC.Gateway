using System.Text;

namespace IPC.EdgeGateway;

/// <summary>One persisted outgoing-message language per gateway, independent of project reloads.</summary>
public sealed class GatewayMessageLanguage
{
    private readonly object _sync = new();
    private readonly string? _path;
    private string _locale = "zh-CN";

    public GatewayMessageLanguage(string? path = null)
    {
        _path = path;
        if (path != null && File.Exists(path))
        {
            try
            {
                string saved = File.ReadAllText(path, Encoding.UTF8).Trim();
                if (saved is "zh-CN" or "en-US") _locale = saved;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A display preference must never prevent acquisition or the web host from starting.
                IpcLogService.WriteError("Failed to read MQTT message language; using zh-CN.", ex);
            }
        }
    }

    public string Locale => Volatile.Read(ref _locale);
    public bool English => Locale == "en-US";

    public string SetLocale(string locale)
    {
        if (locale is not ("zh-CN" or "en-US"))
            throw new ArgumentException("Language must be zh-CN or en-US.", nameof(locale));
        lock (_sync)
        {
            if (_locale == locale) return locale;
            if (_path != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
                string temporary = _path + ".tmp";
                try
                {
                    File.WriteAllText(temporary, locale, new UTF8Encoding(false));
                    File.Move(temporary, _path, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            // Publish only after a successful durable save; readers never wait for file I/O.
            Volatile.Write(ref _locale, locale);
            return locale;
        }
    }
}

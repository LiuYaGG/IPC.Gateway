using System.Globalization;

namespace IPC.Gateway.WebHost;

internal static class WorkbookTimeZone
{
    internal static TimeZoneInfo FromRequest(HttpContext context)
    {
        if (!context.Request.Query.TryGetValue("timeZone", out var values)) return TimeZoneInfo.Local;
        string id = values.ToString();
        if (values.Count != 1 || string.IsNullOrWhiteSpace(id) || id.Length > 128) throw new ArgumentException("Invalid time zone.");
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { throw new ArgumentException("Unknown time zone."); }
        catch (InvalidTimeZoneException) { throw new ArgumentException("Invalid time zone."); }
    }

    internal static string Format(DateTimeOffset timestamp, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(timestamp, zone).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
}

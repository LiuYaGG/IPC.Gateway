using System.Text.RegularExpressions;

namespace IPC.Gateway.Core.Application.Gateway;

public static class WebhookSecretProtector
{
    public const string RedactedSecret = "********";

    private static readonly Regex SensitiveQueryValue = new Regex(
        @"(?<prefix>[?&](?<name>access_token|token|secret|sign|signature|key|api_key|appkey)=)(?<value>[^&#]*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex HeaderLine = new Regex(
        @"^(?<name>[^:\r\n]+):(?<value>.*)$",
        RegexOptions.Compiled);

    private static readonly HashSet<string> SensitiveHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Proxy-Authorization",
        "X-Api-Key",
        "Api-Key",
        "X-Token",
        "Access-Token",
        "Secret",
        "Signature"
    };

    public static string SanitizeUrl(string? url)
    {
        return SensitiveQueryValue.Replace(url ?? string.Empty, match =>
            match.Groups["prefix"].Value + RedactedSecret);
    }

    public static string PreserveUrl(string? submittedUrl, string? currentUrl)
    {
        string submitted = submittedUrl ?? string.Empty;
        if (string.Equals(submitted.Trim(), RedactedSecret, StringComparison.Ordinal))
            return currentUrl ?? string.Empty;
        if (!submitted.Contains(RedactedSecret, StringComparison.Ordinal))
            return submitted;

        Dictionary<string, string> currentValues = ReadSensitiveQueryValues(currentUrl);
        return SensitiveQueryValue.Replace(submitted, match =>
        {
            if (!string.Equals(match.Groups["value"].Value, RedactedSecret, StringComparison.Ordinal))
                return match.Value;
            string name = match.Groups["name"].Value;
            return currentValues.TryGetValue(name, out string? value)
                ? match.Groups["prefix"].Value + value
                : match.Groups["prefix"].Value;
        });
    }

    public static string SanitizeHeaders(string? headers)
    {
        return RewriteHeaders(headers, null, sanitize: true);
    }

    public static string PreserveHeaders(string? submittedHeaders, string? currentHeaders)
    {
        if (string.Equals(submittedHeaders?.Trim(), RedactedSecret, StringComparison.Ordinal))
            return currentHeaders ?? string.Empty;
        return RewriteHeaders(submittedHeaders, ReadHeaders(currentHeaders), sanitize: false);
    }

    private static Dictionary<string, string> ReadSensitiveQueryValues(string? url)
    {
        Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in SensitiveQueryValue.Matches(url ?? string.Empty))
            values[match.Groups["name"].Value] = match.Groups["value"].Value;
        return values;
    }

    private static Dictionary<string, string> ReadHeaders(string? headers)
    {
        Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in SplitLines(headers))
        {
            Match match = HeaderLine.Match(line);
            if (match.Success)
                values[match.Groups["name"].Value.Trim()] = match.Groups["value"].Value.Trim();
        }
        return values;
    }

    private static string RewriteHeaders(
        string? headers,
        Dictionary<string, string>? currentValues,
        bool sanitize)
    {
        List<string> output = new List<string>();
        foreach (string line in SplitLines(headers))
        {
            Match match = HeaderLine.Match(line);
            if (!match.Success)
            {
                output.Add(line);
                continue;
            }

            string name = match.Groups["name"].Value.Trim();
            string value = match.Groups["value"].Value.Trim();
            if (!SensitiveHeaders.Contains(name))
            {
                output.Add(line);
                continue;
            }

            if (sanitize && !string.IsNullOrEmpty(value))
                value = RedactedSecret;
            else if (!sanitize && string.Equals(value, RedactedSecret, StringComparison.Ordinal))
                value = currentValues != null && currentValues.TryGetValue(name, out string? current) ? current : string.Empty;
            output.Add(name + ": " + value);
        }
        return string.Join(Environment.NewLine, output);
    }

    private static IEnumerable<string> SplitLines(string? headers)
    {
        return (headers ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }
}

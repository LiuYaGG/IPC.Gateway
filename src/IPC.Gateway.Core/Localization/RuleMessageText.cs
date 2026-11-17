using System.Text.Json;
using System.Text.RegularExpressions;

namespace IPC.EdgeGateway;

/// <summary>Shared with the browser: translates presentation text, never rule state used for evaluation.</summary>
public static class RuleMessageText
{
    private sealed class Catalog
    {
        public Dictionary<string, string[]> Labels { get; set; } = new();
        public Dictionary<string, string[]> Prefixes { get; set; } = new();
        public string[][] Messages { get; set; } = [];
    }

    private static readonly Catalog Texts = Load();
    private static readonly Dictionary<string, string[]> Labels = BuildLabels();
    private static readonly (string[] Text, Regex Pattern, int[] Ids)[] Templates = Texts.Messages
        .Where(pair => pair[0].Contains("{0}")).SelectMany(pair => pair.Select(template =>
        {
            var ids = new List<int>();
            string pattern = string.Concat(Regex.Split(template, @"(\{\d+\})").Select(part =>
            {
                if (Regex.IsMatch(part, @"^\{\d+\}$"))
                {
                    ids.Add(int.Parse(part[1..^1], System.Globalization.CultureInfo.InvariantCulture));
                    return "([\\s\\S]+?)";
                }
                return Regex.Escape(part);
            }));
            return (pair, new Regex("^" + pattern + "$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)), ids.ToArray());
        })).ToArray();

    public static string Label(string? value, bool english) => LabelCore(value ?? string.Empty, english, 0);

    private static string LabelCore(string value, bool english, int depth)
    {
        if (Labels.TryGetValue(value, out var pair)) return pair[english ? 1 : 0];
        if (depth < 8)
            foreach (var prefix in Texts.Prefixes)
                foreach (string candidate in new[] { prefix.Key, prefix.Value[0], prefix.Value[1] })
                    if (value.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
                        return prefix.Value[english ? 1 : 0] + LabelCore(value[candidate.Length..].TrimStart(), english, depth + 1);
        return value;
    }

    public static string Message(string? value, bool english)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        foreach (var pair in Texts.Messages)
            if (value == pair[0] || value == pair[1]) return pair[english ? 1 : 0];
        // Unknown/user-defined text is preserved; only known system templates are translated.
        if (value.Length <= 16384)
            foreach (var template in Templates)
            {
                Match match;
                try { match = template.Pattern.Match(value); }
                catch (RegexMatchTimeoutException) { continue; }
                if (!match.Success) continue;
                var values = new Dictionary<int, string>();
                for (int i = 0; i < template.Ids.Length; i++) values[template.Ids[i]] = match.Groups[i + 1].Value;
                return Regex.Replace(template.Text[english ? 1 : 0], @"\{(\d+)\}", item =>
                    values[int.Parse(item.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)]);
            }
        return value;
    }

    private static Catalog Load()
    {
        using var stream = typeof(RuleMessageText).Assembly.GetManifestResourceStream("IPC.Gateway.RuleText.json")!;
        return JsonSerializer.Deserialize<Catalog>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private static Dictionary<string, string[]> BuildLabels()
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Texts.Labels)
        {
            result[entry.Key] = entry.Value;
            foreach (string alias in entry.Value) result.TryAdd(alias, entry.Value);
        }
        return result;
    }
}

namespace TechBazar.Application.Catalog;

public static class SpecFilterParser
{
    /// <summary>Parses <c>Key:Value</c> entries into key -> distinct values. Malformed entries are ignored.</summary>
    public static Dictionary<string, List<string>> Parse(IEnumerable<string>? specs)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (specs is null) return result;
        foreach (var raw in specs)
        {
            if (!TryParseOne(raw, out var key, out var value)) continue;
            if (!result.TryGetValue(key, out var values)) result[key] = values = [];
            if (!values.Contains(value, StringComparer.OrdinalIgnoreCase)) values.Add(value);
        }
        return result;
    }

    public static bool TryParseOne(string? raw, out string key, out string value)
    {
        key = value = string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var idx = raw.IndexOf(':');
        if (idx <= 0 || idx == raw.Length - 1) return false;
        key = raw[..idx].Trim();
        value = raw[(idx + 1)..].Trim();
        return key.Length > 0 && value.Length > 0;
    }
}

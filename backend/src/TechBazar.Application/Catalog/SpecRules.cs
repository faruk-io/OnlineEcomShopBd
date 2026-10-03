using System.Globalization;
using System.Text.RegularExpressions;

namespace TechBazar.Application.Catalog;

/// <summary>Single definition of which spec keys are filters / numeric, shared by the seeder and the admin product editor.</summary>
public static partial class SpecRules
{
    /// <summary>Spec keys exposed as catalog filters/facets.</summary>
    public static readonly HashSet<string> FilterableKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Series", "Generation", "Socket", "Chipset", "Form Factor", "RAM Type", "Capacity", "Interface", "GPU Chipset",
        "Video Memory", "Wattage", "Efficiency", "Modular", "Screen Size", "Resolution", "Panel Type", "Refresh Rate",
        "Cores", "Storage Type", "Storage Capacity", "Supported Motherboards", "Cooler Type",
    };

    /// <summary>Spec keys whose leading number is stored in NumericValue (normalised: TB -> GB, units dropped).</summary>
    public static readonly HashSet<string> NumericKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "TDP", "Wattage", "Recommended PSU", "Cores", "Threads", "Capacity", "Storage Capacity", "Video Memory",
        "Screen Size", "Refresh Rate", "Speed", "Output Power", "Max GPU Length", "Length", "Max Memory",
        "Memory Slots", "M.2 Slots", "SATA Ports", "TDP Rating", "Height", "Max CPU Cooler Height",
    };

    public static bool IsFilterable(string key) => FilterableKeys.Contains(key);

    public static decimal? NumericFor(string key, string value) => NumericKeys.Contains(key) ? ParseNumeric(value) : null;

    /// <summary>"65 W" -> 65, "1 TB" -> 1000 (GB), "3200 MHz" -> 3200, "DDR5" -> null.</summary>
    public static decimal? ParseNumeric(string? value)
    {
        if (value is null) return null;
        var m = LeadingNumber().Match(value);
        if (!m.Success || !decimal.TryParse(m.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)) return null;
        return m.Groups[2].Value.Equals("TB", StringComparison.OrdinalIgnoreCase) ? n * 1000 : n;
    }

    [GeneratedRegex(@"^\s*(\d+(?:\.\d+)?)\s*([A-Za-z]+)?")]
    private static partial Regex LeadingNumber();
}

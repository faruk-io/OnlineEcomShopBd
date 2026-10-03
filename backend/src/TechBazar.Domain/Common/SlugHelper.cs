using System.Text;
using System.Text.RegularExpressions;

namespace TechBazar.Domain.Common;

public static partial class SlugHelper
{
    /// <summary>Lower-case, ASCII, hyphen separated slug. "Core i5-12400F (Box)" -> "core-i5-12400f-box".</summary>
    public static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (ch == '+') sb.Append("plus");
            else sb.Append('-');
        }
        return MultiDash().Replace(sb.ToString(), "-").Trim('-');
    }

    [GeneratedRegex("-{2,}")]
    private static partial Regex MultiDash();
}

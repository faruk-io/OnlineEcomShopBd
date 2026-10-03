namespace TechBazar.Application.Catalog;

/// <summary>
/// Builds LIKE patterns for <c>EF.Functions.Like(column, pattern, TextSearch.Escape)</c>.
/// LIKE (rather than string.Contains) gives the same case-insensitive behaviour on every provider
/// and lets user input contain %, _ or [ safely.
/// </summary>
public static class TextSearch
{
    public const string Escape = "\\";

    public static string Contains(string term) => $"%{Sanitize(term)}%";
    public static string StartsWith(string term) => $"{Sanitize(term)}%";

    private static string Sanitize(string term) => term
        .Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");
}

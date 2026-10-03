using System.Globalization;

namespace TechBazar.Application.Common;

public static class Money
{
    /// <summary>Formats BDT with Bangladeshi digit grouping: 125000 -> "৳1,25,000" (paisa only when non-zero).</summary>
    public static string Bdt(decimal amount)
    {
        var negative = amount < 0;
        var fixedStr = Math.Abs(amount).ToString("0.00", CultureInfo.InvariantCulture).Split('.');
        var digits = fixedStr[0];
        var grouped = digits.Length <= 3 ? digits : System.Text.RegularExpressions.Regex.Replace(digits[..^3], @"\B(?=(\d{2})+(?!\d))", ",") + "," + digits[^3..];
        return $"{(negative ? "-" : "")}৳{grouped}{(fixedStr[1] == "00" ? "" : "." + fixedStr[1])}";
    }
}

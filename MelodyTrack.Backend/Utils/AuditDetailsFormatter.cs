using System.Globalization;
using System.Text.RegularExpressions;

namespace MelodyTrack.Backend.Utils;

public static class AuditDetailsFormatter
{
    private static readonly Regex IsoDateTimeRegex = new(
        @"\b\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string? DescribeChange(string label, string? before, string? after)
    {
        if (Normalize(before) == Normalize(after))
        {
            return null;
        }

        return $"{label}: {FormatValue(before)} \u2192 {FormatValue(after)}";
    }

    public static string? DescribeChange(string label, DateTime? beforeUtc, DateTime? afterUtc)
    {
        return DescribeChange(label, beforeUtc?.ToString("O"), afterUtc?.ToString("O"));
    }

    public static string DescribeContext(string label, string? value)
    {
        return $"{label}: {FormatValue(value)}";
    }

    public static string DescribeContext(string label, DateTime? valueUtc)
    {
        return DescribeContext(label, valueUtc?.ToString("O"));
    }

    public static string JoinChanges(params string?[] changes)
    {
        return string.Join("; ", changes.Where(change => !string.IsNullOrWhiteSpace(change)));
    }

    public static string? FormatForDisplay(string? details, TimeZoneInfo timezone)
    {
        if (string.IsNullOrWhiteSpace(details))
        {
            return details;
        }

        var localized = IsoDateTimeRegex.Replace(details, match =>
        {
            if (!DateTimeOffset.TryParse(
                    match.Value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var parsed))
            {
                return match.Value;
            }

            return TimeZoneInfo.ConvertTime(parsed, timezone)
                .ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
        });

        return localized.Replace("->", "\u2192", StringComparison.Ordinal);
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
    }

    private static string FormatValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
    }
}

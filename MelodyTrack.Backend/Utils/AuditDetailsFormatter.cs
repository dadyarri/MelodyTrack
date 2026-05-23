namespace MelodyTrack.Backend.Utils;

public static class AuditDetailsFormatter
{
    public static string? DescribeChange(string label, string? before, string? after)
    {
        if (Normalize(before) == Normalize(after))
        {
            return null;
        }

        return $"{label}: {FormatValue(before)} -> {FormatValue(after)}";
    }

    public static string JoinChanges(params string?[] changes)
    {
        return string.Join("; ", changes.Where(change => !string.IsNullOrWhiteSpace(change)));
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

namespace MelodyTrack.Rewrite.Domain;

/// <summary>Normalizes contact values for advisory duplicate comparison without changing stored values.</summary>
public static class ClientContactComparison
{
    public static string? Normalize(ContactFormat format, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return format switch
        {
            ContactFormat.Phone => NormalizePhone(value),
            ContactFormat.Email => NormalizeEmail(value),
            _ => null
        };
    }

    private static string NormalizePhone(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return !value.TrimStart().StartsWith('+') && digits.Length == 11 && digits[0] == '8'
            ? "7" + digits[1..] : digits;
    }

    private static string NormalizeEmail(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        var at = normalized.LastIndexOf('@');
        if (at <= 0) return normalized;
        var plus = normalized.IndexOf('+');
        return plus > 0 && plus < at ? normalized[..plus] + normalized[at..] : normalized;
    }
}

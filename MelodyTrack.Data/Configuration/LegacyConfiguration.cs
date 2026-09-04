namespace MelodyTrack.Data.Configuration;

public static class LegacyConfiguration
{
    public static IReadOnlyDictionary<string, string?> ReadEnvironmentAliases()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        Map(values, "MELODY_TRACK_DATABASE_URL", $"{DatabaseOptions.SectionName}:ConnectionString");
        Map(values, "MELODY_TRACK_APP_DOMAIN", "PublicUrl:BaseUrl");
        Map(values, "MELODY_TRACK_PII_MASTER_KEY_VERSION", $"{PersonalDataOptions.SectionName}:CurrentKeyVersion");
        Map(values, "MELODY_TRACK_PII_MASTER_KEY", $"{PersonalDataOptions.SectionName}:CurrentKey");
        Map(values, "MELODY_TRACK_LOG_BOOTSTRAP_SECRETS", $"{InitializationOptions.SectionName}:LogBootstrapSecrets");

        var additionalKeys = Environment.GetEnvironmentVariable("MELODY_TRACK_PII_MASTER_KEYS");
        if (!string.IsNullOrWhiteSpace(additionalKeys))
        {
            foreach (var pair in additionalKeys.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separatorIndex = pair.IndexOf('=');
                if (separatorIndex <= 0 || separatorIndex == pair.Length - 1)
                {
                    values[$"{PersonalDataOptions.SectionName}:Keys:__invalid"] = string.Empty;
                    continue;
                }

                values[$"{PersonalDataOptions.SectionName}:Keys:{pair[..separatorIndex].Trim()}"] = pair[(separatorIndex + 1)..].Trim();
            }
        }

        return values;
    }

    private static void Map(IDictionary<string, string?> target, string environmentName, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentName);
        if (value is not null)
        {
            target[configurationKey] = value;
        }
    }
}

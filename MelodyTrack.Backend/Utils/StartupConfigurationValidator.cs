using MelodyTrack.Backend.Exceptions;

namespace MelodyTrack.Backend.Utils;

public static class StartupConfigurationValidator
{
    private const int MinimumJwtSigningKeyLength = 32;
    private const int MinimumPiiMasterKeyLength = 32;
    private const string DefaultPiiMasterKeyVersion = "v1";

    public static StartupConfiguration LoadAndValidate(string contentRootPath)
    {
        var environment = EnvironmentUtils.GetRequiredEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var appDomain = EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_APP_DOMAIN");
        var jwtSigningKey = EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_JWT_SIGNING_KEY");
        var piiMasterKeyVersion = Environment.GetEnvironmentVariable("MELODY_TRACK_PII_MASTER_KEY_VERSION") ?? DefaultPiiMasterKeyVersion;
        var piiMasterKey = EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_PII_MASTER_KEY");
        var piiMasterKeys = LoadPiiMasterKeys(piiMasterKeyVersion, piiMasterKey);
        var databaseUrl = EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_DATABASE_URL");
        var quartzSqlPath = Path.Combine(contentRootPath, "quartz.sql");

        ValidateAppDomain(appDomain);
        ValidateJwtSigningKey(jwtSigningKey);
        ValidatePiiMasterKeyVersion(piiMasterKeyVersion);
        ValidatePiiMasterKeys(piiMasterKeys);
        ValidateRequiredFile(quartzSqlPath, environment);

        return new StartupConfiguration
        {
            Environment = environment,
            AppDomain = appDomain,
            JwtSigningKey = jwtSigningKey,
            PiiMasterKeyVersion = piiMasterKeyVersion,
            PiiMasterKey = piiMasterKey,
            PiiMasterKeys = piiMasterKeys,
            DatabaseUrl = databaseUrl,
            QuartzSqlPath = quartzSqlPath
        };
    }

    private static void ValidateAppDomain(string appDomain)
    {
        if (!Uri.TryCreate(appDomain, UriKind.Absolute, out var uri))
        {
            throw new InvalidEnvironmentVariableException("MELODY_TRACK_APP_DOMAIN", "must be an absolute URI");
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidEnvironmentVariableException("MELODY_TRACK_APP_DOMAIN", "must use http or https");
        }
    }

    private static void ValidateJwtSigningKey(string jwtSigningKey)
    {
        if (jwtSigningKey.Length < MinimumJwtSigningKeyLength)
        {
            throw new InvalidEnvironmentVariableException(
                "MELODY_TRACK_JWT_SIGNING_KEY",
                $"must be at least {MinimumJwtSigningKeyLength} characters long");
        }
    }

    private static void ValidatePiiMasterKeyVersion(string piiMasterKeyVersion)
    {
        if (string.IsNullOrWhiteSpace(piiMasterKeyVersion))
        {
            throw new InvalidEnvironmentVariableException(
                "MELODY_TRACK_PII_MASTER_KEY_VERSION",
                "must not be empty");
        }

        if (piiMasterKeyVersion.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '-' or '_')))
        {
            throw new InvalidEnvironmentVariableException(
                "MELODY_TRACK_PII_MASTER_KEY_VERSION",
                "must contain only letters, digits, '-' or '_'");
        }
    }

    private static IReadOnlyDictionary<string, string> LoadPiiMasterKeys(string currentVersion, string currentKey)
    {
        var configuredKeys = Environment.GetEnvironmentVariable("MELODY_TRACK_PII_MASTER_KEYS");
        var keys = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [currentVersion] = currentKey
        };

        if (string.IsNullOrWhiteSpace(configuredKeys))
        {
            return keys;
        }

        foreach (var pair in configuredKeys.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = pair.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex == pair.Length - 1)
            {
                throw new InvalidEnvironmentVariableException(
                    "MELODY_TRACK_PII_MASTER_KEYS",
                    "must use 'version=key;version2=key2' format");
            }

            var version = pair[..separatorIndex].Trim();
            var key = pair[(separatorIndex + 1)..].Trim();
            keys[version] = key;
        }

        return keys;
    }

    private static void ValidatePiiMasterKeys(IReadOnlyDictionary<string, string> piiMasterKeys)
    {
        foreach (var (version, key) in piiMasterKeys)
        {
            ValidatePiiMasterKeyVersion(version);

            if (key.Length < MinimumPiiMasterKeyLength)
            {
                throw new InvalidEnvironmentVariableException(
                    "MELODY_TRACK_PII_MASTER_KEY",
                    $"key for version '{version}' must be at least {MinimumPiiMasterKeyLength} characters long");
            }
        }
    }

    private static void ValidateRequiredFile(string filePath, string environment)
    {
        if (environment == "Test")
        {
            return;
        }

        if (!File.Exists(filePath))
        {
            throw new RequiredStartupFileNotFoundException(filePath);
        }
    }
}

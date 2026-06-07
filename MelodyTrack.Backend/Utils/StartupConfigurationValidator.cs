using MelodyTrack.Backend.Exceptions;

namespace MelodyTrack.Backend.Utils;

public static class StartupConfigurationValidator
{
    private const int MinimumJwtSigningKeyLength = 32;
    private const int MinimumPiiMasterKeyLength = 32;
    private const string DefaultPiiMasterKeyVersion = "v1";
    private const string BootstrapSecretsLoggingVariableName = "MELODY_TRACK_LOG_BOOTSTRAP_SECRETS";

    public static StartupConfiguration LoadAndValidate(string contentRootPath)
    {
        var environment = EnvironmentUtils.GetRequiredEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var appDomain = EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_APP_DOMAIN");
        var logBootstrapSecrets = LoadLogBootstrapSecrets(environment);
        var jwtSigningKey = EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_JWT_SIGNING_KEY");
        var piiMasterKeyVersion = Environment.GetEnvironmentVariable("MELODY_TRACK_PII_MASTER_KEY_VERSION") ?? DefaultPiiMasterKeyVersion;
        var piiMasterKey = EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_PII_MASTER_KEY");
        var piiMasterKeys = LoadPiiMasterKeys(piiMasterKeyVersion, piiMasterKey);
        var databaseUrl = EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_DATABASE_URL");
        var quartzSqlPath = Path.Combine(contentRootPath, "quartz.sql");

        ValidateAppDomain(appDomain, environment);
        ValidateJwtSigningKey(jwtSigningKey);
        ValidatePiiMasterKeyVersion(piiMasterKeyVersion);
        ValidatePiiMasterKeys(piiMasterKeys);
        ValidateRequiredFile(quartzSqlPath, environment);

        return new StartupConfiguration
        {
            Environment = environment,
            AppDomain = appDomain,
            LogBootstrapSecrets = logBootstrapSecrets,
            JwtSigningKey = jwtSigningKey,
            PiiMasterKeyVersion = piiMasterKeyVersion,
            PiiMasterKey = piiMasterKey,
            PiiMasterKeys = piiMasterKeys,
            DatabaseUrl = databaseUrl,
            QuartzSqlPath = quartzSqlPath
        };
    }

    private static void ValidateAppDomain(string appDomain, string environment)
    {
        if (!Uri.TryCreate(appDomain, UriKind.Absolute, out var uri))
        {
            throw new InvalidEnvironmentVariableException("MELODY_TRACK_APP_DOMAIN", "must be an absolute URI");
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidEnvironmentVariableException("MELODY_TRACK_APP_DOMAIN", "must use http or https");
        }

        if (!IsLocalOrTestEnvironment(environment) && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidEnvironmentVariableException("MELODY_TRACK_APP_DOMAIN", "must use https outside Development or Test");
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

    private static bool LoadLogBootstrapSecrets(string environment)
    {
        var configuredValue = Environment.GetEnvironmentVariable(BootstrapSecretsLoggingVariableName);
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return IsLocalOrTestEnvironment(environment);
        }

        if (bool.TryParse(configuredValue, out var parsed))
        {
            return parsed;
        }

        throw new InvalidEnvironmentVariableException(
            BootstrapSecretsLoggingVariableName,
            "must be either 'true' or 'false'");
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

    private static bool IsLocalOrTestEnvironment(string environment)
    {
        return environment is "Development" or "Test";
    }
}

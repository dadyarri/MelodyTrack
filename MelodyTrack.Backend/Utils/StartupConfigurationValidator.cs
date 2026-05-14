using MelodyTrack.Backend.Exceptions;

namespace MelodyTrack.Backend.Utils;

public static class StartupConfigurationValidator
{
    private const int MinimumJwtSigningKeyLength = 32;

    public static StartupConfiguration LoadAndValidate(string contentRootPath)
    {
        var environment = EnvironmentUtils.GetRequiredEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var appDomain = EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_APP_DOMAIN");
        var jwtSigningKey = EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_JWT_SIGNING_KEY");
        var databaseUrl = EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_DATABASE_URL");
        var quartzSqlPath = Path.Combine(contentRootPath, "quartz.sql");

        ValidateAppDomain(appDomain);
        ValidateJwtSigningKey(jwtSigningKey);
        ValidateRequiredFile(quartzSqlPath, environment);

        return new StartupConfiguration
        {
            Environment = environment,
            AppDomain = appDomain,
            JwtSigningKey = jwtSigningKey,
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

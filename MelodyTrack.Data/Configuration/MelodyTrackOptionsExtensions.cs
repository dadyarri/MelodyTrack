using MelodyTrack.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MelodyTrack.Data.Security;

namespace MelodyTrack.Data.Configuration;

public static class MelodyTrackOptionsExtensions
{
    public static IServiceCollection AddDatabaseOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetRequiredSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), "Database connection string must not be empty.")
            .ValidateOnStart();
        return services;
    }

    public static IServiceCollection AddPersonalDataOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PersonalDataOptions>()
            .Bind(configuration.GetRequiredSection(PersonalDataOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(ValidatePersonalDataOptions, "Personal-data key versions and keys must be valid.")
            .ValidateOnStart();
        return services;
    }

    public static IServiceCollection AddAuthenticationSecretsOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetRequiredSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<AuthenticationSecretsOptions>()
            .Bind(configuration.GetRequiredSection(AuthenticationSecretsOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(ValidateAuthenticationSecrets, "Authentication secrets must use independent high-entropy keys and a P-256 signing key.")
            .ValidateOnStart();
        return services;
    }

    public static IServiceCollection AddPublicUrlOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<PublicUrlOptions>, PublicUrlOptionsValidator>();
        services.AddOptions<PublicUrlOptions>()
            .Bind(configuration.GetRequiredSection(PublicUrlOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        return services;
    }

    public static IServiceCollection AddInitializationOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<InitializationOptions>()
            .Bind(configuration.GetSection(InitializationOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.QuartzSqlPath), "Quartz SQL path must not be empty.")
            .ValidateOnStart();
        return services;
    }

    private static bool ValidatePersonalDataOptions(PersonalDataOptions options)
    {
        var keys = options.BuildKeyRing();
        return IsValidKeyVersion(options.CurrentKeyVersion)
               && keys.All(pair => IsValidKeyVersion(pair.Key) && pair.Value.Length >= 32);
    }

    private static bool ValidateAuthenticationSecrets(AuthenticationSecretsOptions options)
    {
        try
        {
            _ = AuthenticationSecretMaterial.DecodeP256PrivateKey(options.JwtSigningPrivateKey);
            var keys = new[]
            {
                AuthenticationSecretMaterial.DecodeSymmetricKey(options.PasswordPepper, "AuthenticationSecrets:PasswordPepper"),
                AuthenticationSecretMaterial.DecodeSymmetricKey(options.PortalPinPepper, "AuthenticationSecrets:PortalPinPepper"),
                AuthenticationSecretMaterial.DecodeSymmetricKey(options.RefreshTokenHashKey, "AuthenticationSecrets:RefreshTokenHashKey"),
                AuthenticationSecretMaterial.DecodeSymmetricKey(options.CsrfSigningKey, "AuthenticationSecrets:CsrfSigningKey")
            };
            return keys.Select(Convert.ToHexString).Distinct(StringComparer.Ordinal).Count() == keys.Length;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsValidKeyVersion(string version)
    {
        return !string.IsNullOrWhiteSpace(version)
               && version.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_');
    }

    private sealed class PublicUrlOptionsValidator(IHostEnvironment environment) : IValidateOptions<PublicUrlOptions>
    {
        public ValidateOptionsResult Validate(string? name, PublicUrlOptions options)
        {
            if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https"))
            {
                return ValidateOptionsResult.Fail("PublicUrl:BaseUrl must be an absolute HTTP or HTTPS URL.");
            }

            if (!environment.IsDevelopment()
                && !environment.IsEnvironment("Test")
                && uri.Scheme != Uri.UriSchemeHttps)
            {
                return ValidateOptionsResult.Fail("PublicUrl:BaseUrl must use HTTPS outside Development or Test.");
            }

            return ValidateOptionsResult.Success;
        }
    }
}

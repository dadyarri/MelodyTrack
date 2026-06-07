using MelodyTrack.Backend.Exceptions;
using MelodyTrack.Backend.Utils;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StartupConfigurationTestCollection
{
    public const string Name = "startup-configuration";
}

[Collection(StartupConfigurationTestCollection.Name)]
public sealed class StartupConfigurationValidatorTests
{
    [Fact]
    public void LoadAndValidate_ProductionHttpAppDomain_Throws()
    {
        using var environmentScope = new StartupEnvironmentScope(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["MELODY_TRACK_APP_DOMAIN"] = "http://example.com",
            ["MELODY_TRACK_DATABASE_URL"] = "Host=localhost;Database=test;Username=test;Password=test",
            ["MELODY_TRACK_JWT_SIGNING_KEY"] = "12345678901234567890123456789012",
            ["MELODY_TRACK_PII_MASTER_KEY"] = "12345678901234567890123456789012"
        });

        var contentRoot = CreateContentRoot();

        var exception = Should.Throw<InvalidEnvironmentVariableException>(() =>
            StartupConfigurationValidator.LoadAndValidate(contentRoot));

        exception.Message.ShouldContain("MELODY_TRACK_APP_DOMAIN");
        exception.Message.ShouldContain("https");
    }

    [Fact]
    public void LoadAndValidate_ProductionWithoutOverride_DisablesBootstrapSecretLogging()
    {
        using var environmentScope = new StartupEnvironmentScope(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["MELODY_TRACK_APP_DOMAIN"] = "https://example.com",
            ["MELODY_TRACK_DATABASE_URL"] = "Host=localhost;Database=test;Username=test;Password=test",
            ["MELODY_TRACK_JWT_SIGNING_KEY"] = "12345678901234567890123456789012",
            ["MELODY_TRACK_PII_MASTER_KEY"] = "12345678901234567890123456789012",
            ["MELODY_TRACK_LOG_BOOTSTRAP_SECRETS"] = null
        });

        var contentRoot = CreateContentRoot();

        var config = StartupConfigurationValidator.LoadAndValidate(contentRoot);

        config.LogBootstrapSecrets.ShouldBeFalse();
    }

    [Fact]
    public void LoadAndValidate_DevelopmentWithoutOverride_EnablesBootstrapSecretLogging()
    {
        using var environmentScope = new StartupEnvironmentScope(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["MELODY_TRACK_APP_DOMAIN"] = "http://localhost:5173",
            ["MELODY_TRACK_DATABASE_URL"] = "Host=localhost;Database=test;Username=test;Password=test",
            ["MELODY_TRACK_JWT_SIGNING_KEY"] = "12345678901234567890123456789012",
            ["MELODY_TRACK_PII_MASTER_KEY"] = "12345678901234567890123456789012",
            ["MELODY_TRACK_LOG_BOOTSTRAP_SECRETS"] = null
        });

        var contentRoot = CreateContentRoot();

        var config = StartupConfigurationValidator.LoadAndValidate(contentRoot);

        config.LogBootstrapSecrets.ShouldBeTrue();
    }

    [Fact]
    public void LoadAndValidate_InvalidBootstrapSecretsOverride_Throws()
    {
        using var environmentScope = new StartupEnvironmentScope(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["MELODY_TRACK_APP_DOMAIN"] = "https://example.com",
            ["MELODY_TRACK_DATABASE_URL"] = "Host=localhost;Database=test;Username=test;Password=test",
            ["MELODY_TRACK_JWT_SIGNING_KEY"] = "12345678901234567890123456789012",
            ["MELODY_TRACK_PII_MASTER_KEY"] = "12345678901234567890123456789012",
            ["MELODY_TRACK_LOG_BOOTSTRAP_SECRETS"] = "sometimes"
        });

        var contentRoot = CreateContentRoot();

        var exception = Should.Throw<InvalidEnvironmentVariableException>(() =>
            StartupConfigurationValidator.LoadAndValidate(contentRoot));

        exception.Message.ShouldContain("MELODY_TRACK_LOG_BOOTSTRAP_SECRETS");
    }

    private static string CreateContentRoot()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), $"melodytrack-startup-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(contentRoot);
        File.WriteAllText(Path.Combine(contentRoot, "quartz.sql"), "-- test");
        return contentRoot;
    }

    private sealed class StartupEnvironmentScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originalValues;

        public StartupEnvironmentScope(IReadOnlyDictionary<string, string?> values)
        {
            _originalValues = values.Keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);

            foreach (var (key, value) in values)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        public void Dispose()
        {
            foreach (var (key, value) in _originalValues)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}

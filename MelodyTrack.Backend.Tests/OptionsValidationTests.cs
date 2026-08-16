using MelodyTrack.Core.Configuration;
using MelodyTrack.Data.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public sealed class OptionsValidationTests
{
    [Fact]
    public void PublicUrl_ProductionHttpUrl_FailsClearly()
    {
        using var host = CreateHost(Environments.Production, new Dictionary<string, string?>
        {
            ["PublicUrl:BaseUrl"] = "http://example.com"
        }, (services, configuration) => services.AddPublicUrlOptions(configuration));

        var exception = Should.Throw<OptionsValidationException>(() =>
            host.Services.GetRequiredService<IOptions<PublicUrlOptions>>().Value);

        exception.Message.ShouldContain("HTTPS");
    }

    [Fact]
    public void AuthenticationSecrets_ShortJwtKey_FailsClearly()
    {
        using var host = CreateHost(Environments.Production, new Dictionary<string, string?>
        {
            ["AuthenticationSecrets:JwtSigningKey"] = "too-short"
        }, (services, configuration) => services.AddAuthenticationSecretsOptions(configuration));

        var exception = Should.Throw<OptionsValidationException>(() =>
            host.Services.GetRequiredService<IOptions<AuthenticationSecretsOptions>>().Value);

        exception.Message.ShouldContain(nameof(AuthenticationSecretsOptions.JwtSigningKey));
    }

    [Fact]
    public void PersonalData_MissingCurrentKey_FailsClearly()
    {
        using var host = CreateHost(Environments.Production, new Dictionary<string, string?>
        {
            ["PersonalData:CurrentKeyVersion"] = "v1",
            ["PersonalData:CurrentKey"] = string.Empty
        }, (services, configuration) => services.AddPersonalDataOptions(configuration));

        Should.Throw<OptionsValidationException>(() =>
            host.Services.GetRequiredService<IOptions<PersonalDataOptions>>().Value);
    }

    private static IHost CreateHost(
        string environment,
        IReadOnlyDictionary<string, string?> values,
        Action<IServiceCollection, IConfiguration> register)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = environment
        });
        builder.Configuration.AddInMemoryCollection(values);
        register(builder.Services, builder.Configuration);
        return builder.Build();
    }
}

using MelodyTrack.Rewrite.Api;
using MelodyTrack.Rewrite.Persistence.Clients;
using MelodyTrack.Rewrite.Persistence.Import;
using MelodyTrack.Rewrite.Persistence.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace MelodyTrack.Rewrite.Api.Tests;

public sealed class PersonalDataProtectionTests
{
    private static readonly string TestKey = Convert.ToBase64String(Enumerable.Repeat((byte)9, 32).ToArray());

    private static IHost CreateHost(Dictionary<string, string?> values)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(values);
        builder.Services.AddPersonalDataProtection();
        return builder.Build();
    }

    [Fact]
    public async Task Start_ValidKeys_RegistersTargetProtectionOnly()
    {
        using var host = CreateHost(new()
        {
            ["PersonalData:CurrentKeyVersion"] = "v1", ["PersonalData:Keys:v1"] = TestKey
        });

        await host.StartAsync(TestContext.Current.CancellationToken);

        var protector = host.Services.GetRequiredService<PersonalDataProtector>();
        Assert.Same(protector, host.Services.GetRequiredService<PersonalDataProtector>());
        Assert.NotNull(host.Services.GetRequiredService<ClientDataProtection>());
        Assert.Null(host.Services.GetService<LegacyPersonalDataReader>());
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Start_MissingKeys_RejectsBeforeServingRequests()
    {
        using var host = CreateHost(new());

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Start_InvalidKey_DoesNotExposeSecretInFailure()
    {
        const string invalidKey = "this-invalid-configured-secret-must-not-appear-in-errors";
        using var host = CreateHost(new()
        {
            ["PersonalData:CurrentKeyVersion"] = "v1", ["PersonalData:Keys:v1"] = invalidKey
        });

        var error = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));

        Assert.DoesNotContain(invalidKey, error.ToString());
    }

    [Fact]
    public async Task Dispose_Host_AlsoDisposesLoadedKeys()
    {
        var host = CreateHost(new()
        {
            ["PersonalData:CurrentKeyVersion"] = "v1", ["PersonalData:Keys:v1"] = TestKey
        });
        var protector = host.Services.GetRequiredService<PersonalDataProtector>();
        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        host.Dispose();

        Assert.Throws<ObjectDisposedException>(() => protector.Protect("value", "test/context"));
    }
}

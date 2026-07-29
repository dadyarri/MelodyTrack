using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public sealed class PublicUrlBuilderTests
{
    [Fact]
    public void BuildsAppAndApiUrlsFromTheirDedicatedBases()
    {
        var builder = new PublicUrlBuilder(new StartupConfiguration
        {
            Environment = "Production",
            AppDomain = "https://mt.dadyarri.dev",
            PublicApiBaseUrl = "https://mt.dadyarri.dev/api",
            LogBootstrapSecrets = false,
            JwtSigningKey = "test",
            PiiMasterKeyVersion = "v1",
            PiiMasterKey = "test",
            PiiMasterKeys = new Dictionary<string, string>(),
            DatabaseUrl = "test",
            QuartzSqlPath = "test"
        });
        var code = Ulid.Parse("01K7PVV27FAPWXRHE8H93T0DZM");

        builder.GetInviteUrl(code).ShouldBe($"https://mt.dadyarri.dev/invite/{code}");
        builder.GetResetPasswordUrl("token with spaces").ShouldBe("https://mt.dadyarri.dev/restore?code=token%20with%20spaces");
        builder.GetClientPortalAccessUrl("portal-token").ShouldBe("https://mt.dadyarri.dev/portal/access/portal-token");
        builder.GetCalendarSubscriptionUrl("calendar-token").ShouldBe("https://mt.dadyarri.dev/api/calendar-subscriptions/calendar-token.ics");
    }
}

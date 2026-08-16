using MelodyTrack.Backend.Services;
using MelodyTrack.Core.Configuration;
using Microsoft.Extensions.Options;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public sealed class PublicUrlBuilderTests
{
    [Fact]
    public void BuildsAppAndApiUrlsFromCanonicalBase()
    {
        var builder = new PublicUrlBuilder(Options.Create(new PublicUrlOptions
        {
            BaseUrl = "https://mt.dadyarri.dev"
        }));
        var code = Ulid.Parse("01K7PVV27FAPWXRHE8H93T0DZM");

        builder.GetInviteUrl(code).ShouldBe($"https://mt.dadyarri.dev/invite/{code}");
        builder.GetResetPasswordUrl("token with spaces").ShouldBe("https://mt.dadyarri.dev/restore?code=token%20with%20spaces");
        builder.GetClientPortalAccessUrl("portal-token").ShouldBe("https://mt.dadyarri.dev/portal/access/portal-token");
        builder.GetCalendarSubscriptionUrl("calendar-token").ShouldBe("https://mt.dadyarri.dev/calendar-subscriptions/calendar-token.ics");
    }
}

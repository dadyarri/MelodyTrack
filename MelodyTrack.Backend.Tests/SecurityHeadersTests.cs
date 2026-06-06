using System.Net;
using System.Net.Http.Headers;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Backend.Utils;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public class SecurityHeadersTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task ProtectedResponse_IncludesSecurityHeaders()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var response = await App.Client.GetAsync("/services?page=1&page_size=1", TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = null;

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task NotFoundResponse_IncludesSecurityHeaders()
    {
        var response = await App.Client.GetAsync("/missing-endpoint", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        AssertSecurityHeaders(response);
    }

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        response.Headers.GetValues("X-Frame-Options").Single().ShouldBe("DENY");
        response.Headers.GetValues("Referrer-Policy").Single().ShouldBe("no-referrer");
        response.Headers.GetValues("Permissions-Policy").Single().ShouldBe("camera=(), microphone=(), geolocation=()");
    }
}

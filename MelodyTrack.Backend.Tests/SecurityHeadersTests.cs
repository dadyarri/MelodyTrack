using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

    [Fact]
    public async Task AuthResponse_IncludesNoStoreCacheHeaders()
    {
        var response = await App.Client.GetAsync("/auth/me", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        AssertSensitiveCacheHeaders(response);
    }

    [Fact]
    public async Task PasswordResetLinkResponse_IncludesNoStoreCacheHeaders()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));

        var response = await App.Client.PostAsJsonAsync(
            $"/users/{user.Id}/password-reset-link",
            new { },
            TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = null;

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        AssertSensitiveCacheHeaders(response);
    }

    [Fact]
    public async Task NonSensitiveResponse_DoesNotIncludeNoStoreCacheHeaders()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var response = await App.Client.GetAsync("/services?page=1&page_size=1", TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = null;

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("Cache-Control").ShouldBeFalse();
        response.Headers.Contains("Pragma").ShouldBeFalse();
        response.Content.Headers.Contains("Expires").ShouldBeFalse();
    }

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        response.Headers.GetValues("X-Frame-Options").Single().ShouldBe("DENY");
        response.Headers.GetValues("Referrer-Policy").Single().ShouldBe("no-referrer");
        response.Headers.GetValues("Permissions-Policy").Single().ShouldBe("camera=(), microphone=(), geolocation=()");
    }

    private static void AssertSensitiveCacheHeaders(HttpResponseMessage response)
    {
        response.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store, no-cache, max-age=0");
        response.Headers.GetValues("Pragma").Single().ShouldBe("no-cache");
        response.Content.Headers.GetValues("Expires").Single().ShouldBe("0");
    }
}

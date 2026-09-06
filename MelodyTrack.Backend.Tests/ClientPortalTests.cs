using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Api.ClientPortal.Responses;
using MelodyTrack.Backend.Api.ClientPortal.Aliases;
using MelodyTrack.Backend.Api.ClientPortal.Aliases.Requests;
using MelodyTrack.Backend.Api.ClientPortal.Aliases.Responses;
using MelodyTrack.Backend.Api.Clients.Responses;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.CourseEnrollments.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public class ClientPortalTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task PortalApis_WithoutAlias_RequireSelectionWhileSessionAndCandidatesRemainAvailable()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var client = await TestDataFactory.CreateClientAsync(db, "Анна", "Клиент", TestContext.Current.CancellationToken);
        var token = await CreatePortalTokenAsync(client.Id, assignAlias: false);
        await AuthenticatePortalAsync(token);

        using var me = await App.Client.GetAsync("/auth/me", TestContext.Current.CancellationToken);
        using var candidate = await App.Client.GetAsync("/client-portal/alias-candidates/next", TestContext.Current.CancellationToken);
        using var schedule = await App.Client.GetAsync("/client-portal/schedule?timezone=UTC", TestContext.Current.CancellationToken);

        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        candidate.StatusCode.ShouldBe(HttpStatusCode.OK);
        schedule.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ChooseAlias_ValidCandidate_PersistsPermanentlyAndUnlocksPortal()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var client = await TestDataFactory.CreateClientAsync(db, "Анна", "Клиент", TestContext.Current.CancellationToken);
        var token = await CreatePortalTokenAsync(client.Id, assignAlias: false);
        await AuthenticatePortalAsync(token);
        var candidate = await App.Client.GetFromJsonAsync<PortalAliasCandidateResponse>(
            "/client-portal/alias-candidates/next", TestContext.Current.CancellationToken);

        using var selection = await App.Client.PostAsJsonAsync(
            "/client-portal/alias",
            new ChoosePortalAliasRequest
            {
                EpithetId = candidate.ShouldNotBeNull().EpithetId,
                NounKind = candidate.NounKind,
                NounId = candidate.NounId,
                Value = candidate.Value,
                CandidateToken = candidate.CandidateToken
            },
            TestContext.Current.CancellationToken);
        using var repeated = await App.Client.PostAsJsonAsync(
            "/client-portal/alias",
            new ChoosePortalAliasRequest
            {
                EpithetId = candidate.EpithetId,
                NounKind = candidate.NounKind,
                NounId = candidate.NounId,
                Value = candidate.Value,
                CandidateToken = candidate.CandidateToken
            },
            TestContext.Current.CancellationToken);
        using var schedule = await App.Client.GetAsync("/client-portal/schedule?timezone=UTC", TestContext.Current.CancellationToken);

        selection.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        repeated.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        schedule.StatusCode.ShouldBe(HttpStatusCode.OK);
        db.ChangeTracker.Clear();
        var alias = await db.Users.Where(user => user.ClientId == client.Id)
            .Select(user => user.PortalAlias).SingleAsync(TestContext.Current.CancellationToken);
        alias.ShouldBe(candidate.Value);
    }

    [Fact]
    public async Task GetNextAlias_WithPreviousCandidate_DoesNotRepeatImmediately()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var client = await TestDataFactory.CreateClientAsync(db, "Анна", "Клиент", TestContext.Current.CancellationToken);
        var token = await CreatePortalTokenAsync(client.Id, assignAlias: false);
        await AuthenticatePortalAsync(token);
        var first = (await App.Client.GetFromJsonAsync<PortalAliasCandidateResponse>(
            "/client-portal/alias-candidates/next", TestContext.Current.CancellationToken)).ShouldNotBeNull();
        var query = $"?previousEpithetId={first.EpithetId}&previousNounKind={first.NounKind}&previousNounId={first.NounId}";

        var second = await App.Client.GetFromJsonAsync<PortalAliasCandidateResponse>(
            "/client-portal/alias-candidates/next" + query, TestContext.Current.CancellationToken);

        second.ShouldNotBeNull().Value.ShouldNotBe(first.Value);
    }

    [Fact]
    public async Task ChooseAlias_ConcurrentSelectionOfSameAlias_ReturnsOneConflict()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var firstClient = await TestDataFactory.CreateClientAsync(db, "Анна", "Первая", TestContext.Current.CancellationToken);
        var secondClient = await TestDataFactory.CreateClientAsync(db, "Мария", "Вторая", TestContext.Current.CancellationToken);
        var firstToken = await CreatePortalTokenAsync(firstClient.Id, assignAlias: false);
        var secondToken = await CreatePortalTokenAsync(secondClient.Id, assignAlias: false);
        await db.AliasEpithets.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        await db.AliasCreatures.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        await db.AliasBardicTerms.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        db.AliasEpithets.Add(new AliasEpithet { Id = Ulid.NewUlid(), Value = "Единственный", IsEnabled = true });
        db.AliasCreatures.Add(new AliasCreature { Id = Ulid.NewUlid(), Value = "Грифон", IsEnabled = true });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        using var firstDevice = CreatePortalDeviceClient();
        using var secondDevice = CreatePortalDeviceClient();
        var firstLogin = await OpenAndRefreshAsync(firstDevice, firstToken);
        var secondLogin = await OpenAndRefreshAsync(secondDevice, secondToken);
        firstDevice.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", firstLogin.AccessToken);
        secondDevice.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secondLogin.AccessToken);
        var firstCandidate = await firstDevice.GetFromJsonAsync<PortalAliasCandidateResponse>(
            "/client-portal/alias-candidates/next", TestContext.Current.CancellationToken);
        var secondCandidate = await secondDevice.GetFromJsonAsync<PortalAliasCandidateResponse>(
            "/client-portal/alias-candidates/next", TestContext.Current.CancellationToken);

        var responses = await Task.WhenAll(
            firstDevice.PostAsJsonAsync("/client-portal/alias", firstCandidate, TestContext.Current.CancellationToken),
            secondDevice.PostAsJsonAsync("/client-portal/alias", secondCandidate, TestContext.Current.CancellationToken));

        responses.Select(response => response.StatusCode).Order()
            .ShouldBe(new[] { HttpStatusCode.NoContent, HttpStatusCode.Conflict }.Order());
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task OpenLink_ValidToken_CreatesHashOnlySessionAndRedirectsWithoutToken()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var client = await TestDataFactory.CreateClientAsync(db, "Анна", "Клиент", TestContext.Current.CancellationToken);
        var token = await CreatePortalTokenAsync(client.Id);
        using var device = CreatePortalDeviceClient();

        using var response = await device.GetAsync($"/p/{token}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location?.OriginalString.ShouldBe("/portal");
        response.Headers.CacheControl?.NoStore.ShouldBeTrue();
        response.Headers.GetValues("Referrer-Policy").ShouldContain("no-referrer");
        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();
        cookies.ShouldContain(cookie => cookie.StartsWith("MelodyTrack.Refresh=") && cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        cookies.ShouldAllBe(cookie => cookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotContain(token);
        var storedLink = await db.ClientPortalLoginLinks.AsNoTracking().SingleAsync(item => item.User.ClientId == client.Id, TestContext.Current.CancellationToken);
        storedLink.TokenHash.ShouldBe(UserUtils.HashOpaqueToken(token));
        storedLink.TokenHash.ShouldNotBe(token);
        var session = await db.Sessions.AsNoTracking().SingleAsync(item => item.User.ClientId == client.Id, TestContext.Current.CancellationToken);
        var rawRefresh = ReadCookie(response, "MelodyTrack.Refresh");
        session.RefreshToken.ShouldNotBe(rawRefresh);

        var login = await RefreshAsync(device, ReadCookie(response, "MelodyTrack.Csrf"));
        device.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        using var meResponse = await device.GetAsync("/auth/me", TestContext.Current.CancellationToken);
        var me = await meResponse.Content.ReadFromJsonAsync<MeResponse>(TestContext.Current.CancellationToken);
        me.ShouldNotBeNull().LinkedClientId.ShouldBe(client.Id);
        me.IsClientPortal.ShouldBeTrue();
        using var schedule = await device.GetAsync("/client-portal/schedule?timezone=UTC", TestContext.Current.CancellationToken);
        schedule.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var staff = await device.GetAsync("/clients", TestContext.Current.CancellationToken);
        staff.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OpenLink_TwoDevicesAndRepeatedEntry_PreservesIndependentSessions()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var client = await TestDataFactory.CreateClientAsync(db, "Анна", "Клиент", TestContext.Current.CancellationToken);
        var token = await CreatePortalTokenAsync(client.Id);
        using var firstDevice = CreatePortalDeviceClient();
        using var secondDevice = CreatePortalDeviceClient();

        var first = await OpenAndRefreshAsync(firstDevice, token);
        var second = await OpenAndRefreshAsync(secondDevice, token);
        var repeated = await OpenAndRefreshAsync(firstDevice, token);

        first.AccessToken.ShouldNotBe(second.AccessToken);
        repeated.AccessToken.ShouldNotBe(first.AccessToken);
        secondDevice.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second.AccessToken);
        using var me = await secondDevice.GetAsync("/auth/me", TestContext.Current.CancellationToken);
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OpenLink_AnotherClient_ReplacesBrowserRefreshIdentity()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var firstClient = await TestDataFactory.CreateClientAsync(db, "Анна", "Первая", TestContext.Current.CancellationToken);
        var secondClient = await TestDataFactory.CreateClientAsync(db, "Мария", "Вторая", TestContext.Current.CancellationToken);
        var firstToken = await CreatePortalTokenAsync(firstClient.Id);
        var secondToken = await CreatePortalTokenAsync(secondClient.Id);
        using var device = CreatePortalDeviceClient();
        await OpenAndRefreshAsync(device, firstToken);

        var login = await OpenAndRefreshAsync(device, secondToken);

        device.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        using var meResponse = await device.GetAsync("/auth/me", TestContext.Current.CancellationToken);
        var me = await meResponse.Content.ReadFromJsonAsync<MeResponse>(TestContext.Current.CancellationToken);
        me.ShouldNotBeNull().LinkedClientId.ShouldBe(secondClient.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangeLink_ActiveSessions_InvalidatesOldLinkAndAllClientSessions(bool revoke)
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var client = await TestDataFactory.CreateClientAsync(db, "Анна", "Клиент", TestContext.Current.CancellationToken);
        var token = await CreatePortalTokenAsync(client.Id);
        using var first = CreatePortalDeviceClient();
        using var second = CreatePortalDeviceClient();
        var firstLogin = await OpenAndRefreshAsync(first, token);
        var secondLogin = await OpenAndRefreshAsync(second, token);

        using var mutation = revoke
            ? await App.Client.DeleteAsync($"/clients/{client.Id}/portal-links", TestContext.Current.CancellationToken)
            : await App.Client.PostAsJsonAsync($"/clients/{client.Id}/portal-links", new { }, TestContext.Current.CancellationToken);

        mutation.StatusCode.ShouldBe(revoke ? HttpStatusCode.NoContent : HttpStatusCode.Created);
        first.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", firstLogin.AccessToken);
        second.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secondLogin.AccessToken);
        using var firstMe = await first.GetAsync("/auth/me", TestContext.Current.CancellationToken);
        using var secondMe = await second.GetAsync("/auth/me", TestContext.Current.CancellationToken);
        firstMe.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        secondMe.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var oldLink = await first.GetAsync($"/p/{token}", TestContext.Current.CancellationToken);
        oldLink.Headers.Location?.OriginalString.ShouldBe("/portal/error");
        oldLink.Headers.Contains("Set-Cookie").ShouldBeFalse();
        if (!revoke)
        {
            var newLink = await mutation.Content.ReadFromJsonAsync<CreateClientPortalLinkResponse>(TestContext.Current.CancellationToken);
            var newToken = new Uri(newLink.ShouldNotBeNull().Url).Segments.Last();
            newToken.ShouldNotBe(token);
            await OpenAndRefreshAsync(first, newToken);
        }
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("unknown-token-with-enough-entropy")]
    public async Task OpenLink_InvalidToken_RedirectsToTokenFreeErrorWithoutCreatingSession(string token)
    {
        using var device = CreatePortalDeviceClient();

        using var response = await device.GetAsync($"/p/{token}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location?.OriginalString.ShouldBe("/portal/error");
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        response.Headers.CacheControl?.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task OpenLink_DifferentInvalidTokens_SharesAddressRateLimit()
    {
        using var device = CreatePortalDeviceClient();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            using var response = await device.GetAsync($"/p/unknown-portal-token-{attempt}", TestContext.Current.CancellationToken);
            response.Headers.Location?.OriginalString.ShouldBe("/portal/error");
        }

        using var throttled = await device.GetAsync("/p/another-unknown-portal-token", TestContext.Current.CancellationToken);

        throttled.Headers.Location?.OriginalString.ShouldBe("/portal/error?reason=rate-limit");
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task ManageLink_ClientCaller_ReturnsForbidden(string method)
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var client = await TestDataFactory.CreateClientAsync(db, "Анна", "Клиент", TestContext.Current.CancellationToken);
        var token = await CreatePortalTokenAsync(client.Id);
        using var device = CreatePortalDeviceClient();
        var login = await OpenAndRefreshAsync(device, token);
        device.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        using var request = new HttpRequestMessage(new HttpMethod(method), $"/clients/{client.Id}/portal-links");

        using var response = await device.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetSchedule_PlannedAppointment_ReturnsClientNextAppointment()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var client = await TestDataFactory.CreateClientAsync(db, "Анна", "Клиент", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Урок", TestContext.Current.CancellationToken);
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var appointment = new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            StartDate = now.AddDays(2),
            EndDate = now.AddDays(2).AddHours(1),
            Status = AppointmentStatus.Planned,
            IsDeleted = false
        };
        await db.Appointments.AddAsync(appointment, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var token = await CreatePortalTokenAsync(client.Id);
        await AuthenticatePortalAsync(token);

        using var response = await App.Client.GetAsync("/client-portal/schedule?timezone=UTC", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var schedule = await response.Content.ReadFromJsonAsync<GetClientPortalScheduleResponse>(TestContext.Current.CancellationToken);
        var next = schedule.ShouldNotBeNull().NextAppointment.ShouldNotBeNull();
        next.Id.ShouldBe(appointment.Id);
        next.CourseTheme.ShouldBeNull();
    }

    [Fact]
    public async Task ClientPortalCourseEnrollments_ReturnStructuredCourseBlocksAndBranches()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Nora", "Keys", TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));

        var courseResponse = await App.Client.PostAsJsonAsync(
            "/courses",
            new
            {
                name = "Portal structure",
                blocks = new object[]
                {
                    new
                    {
                        title = "Foundation",
                        order = 1,
                        branches = new object[]
                        {
                            new
                            {
                                title = "Rhythm",
                                order = 1,
                                themes = new object[]
                                {
                                    new
                                    {
                                        key = "pulse",
                                        title = "Feel the pulse",
                                        order = 1,
                                        experiencePointsReward = 5,
                                        dependencyKeys = Array.Empty<string>()
                                    },
                                    new
                                    {
                                        key = "groove",
                                        title = "Build a groove",
                                        order = 2,
                                        experiencePointsReward = 7,
                                        dependencyKeys = Array.Empty<string>()
                                    }
                                }
                            },
                            new
                            {
                                title = "Melody",
                                order = 2,
                                themes = new object[]
                                {
                                    new
                                    {
                                        key = "motif",
                                        title = "First motif",
                                        order = 1,
                                        experiencePointsReward = 6,
                                        dependencyKeys = new[] { "pulse" }
                                    }
                                }
                            }
                        }
                    }
                }
            },
            TestContext.Current.CancellationToken);
        courseResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var courseId = await courseResponse.Content.ReadFromJsonAsync<CreateEntityResponse>(cancellationToken: TestContext.Current.CancellationToken);
        courseId.ShouldNotBeNull();

        var enrollmentResponse = await App.Client.PostAsJsonAsync(
            "/course-enrollments",
            new
            {
                clientId = client.Id,
                courseId = courseId.Id
            },
            TestContext.Current.CancellationToken);
        enrollmentResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var portalToken = await CreatePortalTokenAsync(client.Id);
        await AuthenticatePortalAsync(portalToken);

        var portalResponse = await App.Client.GetAsync("/client-portal/course-enrollments", TestContext.Current.CancellationToken);
        portalResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = await portalResponse.Content.ReadFromJsonAsync<GetCourseEnrollmentsResponse>(cancellationToken: TestContext.Current.CancellationToken);
        payload.ShouldNotBeNull();

        var enrollment = payload.Enrollments.ShouldHaveSingleItem();
        enrollment.Course.Blocks.Count.ShouldBe(1);
        enrollment.Course.Blocks[0].Title.ShouldBe("Foundation");
        enrollment.Course.Blocks[0].Branches.Select(branch => branch.Title).ShouldBe(["Rhythm", "Melody"]);
        enrollment.Course.Blocks[0].Branches[0].Themes.Select(theme => theme.Title).ShouldBe(["Feel the pulse", "Build a groove"]);
        enrollment.Course.Blocks[0].Branches[1].Themes[0].DependencyThemeIds.Count.ShouldBe(1);
        enrollment.Themes.Count.ShouldBe(3);
    }

    private async Task<string> CreatePortalTokenAsync(Ulid clientId, bool assignAlias = true)
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));
        using var response = await App.Client.PostAsJsonAsync($"/clients/{clientId}/portal-links", new { }, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var payload = await response.Content.ReadFromJsonAsync<CreateClientPortalLinkResponse>(TestContext.Current.CancellationToken);
        payload.ShouldNotBeNull().Url.ShouldContain("/p/");
        var token = new Uri(payload.Url).Segments.Last();
        token.Length.ShouldBe(22);
        if (assignAlias)
        {
            var portalUser = await db.Users.SingleAsync(user => user.ClientId == clientId, TestContext.Current.CancellationToken);
            portalUser.PortalAlias ??= $"Игрок {clientId}";
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        return token;
    }

    private async Task AuthenticatePortalAsync(string token)
    {
        using var device = CreatePortalDeviceClient();
        var login = await OpenAndRefreshAsync(device, token);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
    }

    private HttpClient CreatePortalDeviceClient()
    {
        var client = App.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Forwarded-For", Ulid.NewUlid().ToString());
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MelodyTrack.Tests/1.0");
        return client;
    }

    private static async Task<LoginResponse> OpenAndRefreshAsync(HttpClient device, string token)
    {
        using var response = await device.GetAsync($"/p/{token}", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location?.OriginalString.ShouldBe("/portal");
        return await RefreshAsync(device, ReadCookie(response, "MelodyTrack.Csrf"));
    }

    private static async Task<LoginResponse> RefreshAsync(HttpClient device, string csrf)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh");
        request.Headers.Add("X-CSRF-Token", csrf);
        using var response = await device.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<LoginResponse>(TestContext.Current.CancellationToken)).ShouldNotBeNull();
    }

    private static string ReadCookie(HttpResponseMessage response, string name) => response.Headers.GetValues("Set-Cookie")
        .Single(value => value.StartsWith(name + "=", StringComparison.Ordinal)).Split(';')[0][(name.Length + 1)..];
}

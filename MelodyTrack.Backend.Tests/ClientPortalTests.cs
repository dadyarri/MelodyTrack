using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Api.ClientPortal.Responses;
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
    public async Task ClientPortalLinkFlow_CreatesClientUser_AndAllowsReadOnlyScheduleAccess()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = new Client
        {
            Id = Ulid.NewUlid(),
            FirstName = "Mila",
            LastName = "Student",
            CreatedAtUtc = DateTime.UtcNow,
            Contacts = new ClientContacts
            {
                Id = Ulid.NewUlid()
            }
        };
        var service = await TestDataFactory.CreateServiceAsync(db, "Piano lesson", TestContext.Current.CancellationToken);
        var startDate = DateTime.UtcNow.AddDays(2);
        var endDate = startDate.AddHours(1);

        await db.Clients.AddAsync(client, TestContext.Current.CancellationToken);
        var appointment = new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            StartDate = startDate,
            EndDate = endDate,
            Status = AppointmentStatus.Planned,
            IsDeleted = false
        };
        await db.Appointments.AddAsync(appointment, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));

        using var createLinkResponse = await App.Client.PostAsJsonAsync(
            $"/clients/{client.Id}/portal-links",
            new { },
            TestContext.Current.CancellationToken);
        createLinkResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var createLinkPayload =
            await createLinkResponse.Content.ReadFromJsonAsync<CreateClientPortalLinkResponse>(cancellationToken: TestContext.Current.CancellationToken);

        createLinkPayload.ShouldNotBeNull();
        var token = createLinkPayload.Url.Split("/portal/access/").LastOrDefault();
        token.ShouldNotBeNullOrWhiteSpace();

        var clientUser = db.Users
            .Where(user => user.ClientId == client.Id)
            .Select(user => new { user.ClientId, user.Email, RoleName = user.Role.RoleName })
            .Single();
        clientUser.RoleName.ShouldBe(UserRoles.Client);
        clientUser.Email.ShouldBe($"client-{client.Id}".ToLowerInvariant() + "@portal.melodytrack.local");

        App.Client.DefaultRequestHeaders.Authorization = null;
        App.Client.DefaultRequestHeaders.UserAgent.ParseAdd("MelodyTrack.Tests/1.0");

        using var statusResponse = await App.Client.GetAsync(
            $"/client-portal/auth/link?token={Uri.EscapeDataString(token)}",
            TestContext.Current.CancellationToken);
        statusResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var statusPayload =
            await statusResponse.Content.ReadFromJsonAsync<GetClientPortalLinkStatusResponse>(cancellationToken: TestContext.Current.CancellationToken);

        statusPayload.ShouldNotBeNull();
        statusPayload.FirstName.ShouldBe(client.FirstName);
        statusPayload.HasPin.ShouldBeFalse();

        using var consumeResponse = await App.Client.PostAsJsonAsync(
            "/client-portal/auth/link",
            new
            {
                token,
                pin = "1234",
                pinConfirmation = "1234"
            },
            TestContext.Current.CancellationToken);
        consumeResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var consumePayload = await consumeResponse.Content.ReadFromJsonAsync<ClientPortalAuthenticationResponse>(cancellationToken: TestContext.Current.CancellationToken);

        consumePayload.ShouldNotBeNull();
        consumePayload.SavedIdentity.Reference.ShouldNotBeNullOrWhiteSpace();
        consumePayload.SavedIdentity.DisplayLabel.ShouldBe("Mila S.");

        var storedCredentials = db.ClientPortalLoginLinks
            .AsNoTracking()
            .Single(item => item.User.ClientId == client.Id);
        storedCredentials.TokenHash.ShouldBe(UserUtils.HashOpaqueToken(token));
        storedCredentials.TokenHash.ShouldNotBe(token);
        storedCredentials.PinHash.ShouldNotBe("1234");
        UserUtils.IsValidPortalPin(storedCredentials.PinHash.ShouldNotBeNull(), "1234").ShouldBeTrue();
        db.ClientPortalSavedIdentityReferences.AsNoTracking()
            .Single(item => item.LoginLinkId == storedCredentials.Id)
            .ReferenceHash.ShouldBe(UserUtils.HashOpaqueToken(consumePayload.SavedIdentity.Reference));

        using var savedStatusResponse = await App.Client.GetAsync(
            $"/client-portal/auth/saved?reference={Uri.EscapeDataString(consumePayload.SavedIdentity.Reference)}",
            TestContext.Current.CancellationToken);
        savedStatusResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var savedLoginResponse = await App.Client.PostAsJsonAsync(
            "/client-portal/auth/saved",
            new { reference = consumePayload.SavedIdentity.Reference, pin = "1234" },
            TestContext.Current.CancellationToken);
        savedLoginResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var savedLoginPayload = await savedLoginResponse.Content.ReadFromJsonAsync<ClientPortalAuthenticationResponse>(TestContext.Current.CancellationToken);
        savedLoginPayload.ShouldNotBeNull();
        savedLoginPayload.SavedIdentity.Reference.ShouldBe(consumePayload.SavedIdentity.Reference);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", savedLoginPayload.AccessToken);

        using var meResponse = await App.Client.GetAsync("/auth/me", TestContext.Current.CancellationToken);
        meResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var mePayload = await meResponse.Content.ReadFromJsonAsync<MeResponse>(cancellationToken: TestContext.Current.CancellationToken);

        mePayload.ShouldNotBeNull();
        mePayload.IsClientPortal.ShouldBeTrue();
        mePayload.LinkedClientId.ShouldBe(client.Id);

        var scheduleUrl = "/client-portal/schedule?timezone=UTC";
        using var scheduleResponse = await App.Client.GetAsync(scheduleUrl, TestContext.Current.CancellationToken);
        scheduleResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var schedulePayload = await scheduleResponse.Content.ReadFromJsonAsync<GetClientPortalScheduleResponse>(cancellationToken: TestContext.Current.CancellationToken);

        schedulePayload.ShouldNotBeNull();
        schedulePayload.NextAppointment.ShouldNotBeNull();
        schedulePayload.NextAppointment.Id.ShouldBe(appointment.Id);
        schedulePayload.NextAppointment.CourseTheme.ShouldBeNull();

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));

        using var resetPinResponse = await App.Client.PostAsJsonAsync(
            $"/clients/{client.Id}/portal-pin-resets",
            new { },
            TestContext.Current.CancellationToken);
        resetPinResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", savedLoginPayload.AccessToken);
        using var revokedMeResponse = await App.Client.GetAsync("/auth/me", TestContext.Current.CancellationToken);
        revokedMeResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        App.Client.DefaultRequestHeaders.Authorization = null;

        using var secondStatusResponse = await App.Client.GetAsync(
            $"/client-portal/auth/link?token={Uri.EscapeDataString(token)}",
            TestContext.Current.CancellationToken);
        secondStatusResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var secondStatusPayload =
            await secondStatusResponse.Content.ReadFromJsonAsync<GetClientPortalLinkStatusResponse>(cancellationToken: TestContext.Current.CancellationToken);

        secondStatusPayload.ShouldNotBeNull();
        secondStatusPayload.HasPin.ShouldBeFalse();

        using var secondConsumeResponse = await App.Client.PostAsJsonAsync(
            "/client-portal/auth/link",
            new
            {
                token,
                pin = "4321",
                pinConfirmation = "4321"
            },
            TestContext.Current.CancellationToken);
        secondConsumeResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var secondConsumePayload = await secondConsumeResponse.Content.ReadFromJsonAsync<LoginResponse>(TestContext.Current.CancellationToken);
        secondConsumePayload.ShouldNotBeNull();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var failedPinResponse = await App.Client.PostAsJsonAsync(
                "/client-portal/auth/link",
                new { token, pin = "9999" },
                TestContext.Current.CancellationToken);
            failedPinResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var failedLink = db.ClientPortalLoginLinks.AsNoTracking().Single(item => item.User.ClientId == client.Id);
        failedLink.FailedPinAttempts.ShouldBe(3);
        db.AuditLogs.AsNoTracking()
            .Any(item => item.EntityId == failedLink.Id.ToString() && item.Action == "portal_pin_repeated_failures")
            .ShouldBeTrue();

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));
        using var rotateResponse = await App.Client.PostAsJsonAsync(
            $"/clients/{client.Id}/portal-links",
            new { },
            TestContext.Current.CancellationToken);
        rotateResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var rotatePayload = await rotateResponse.Content.ReadFromJsonAsync<CreateClientPortalLinkResponse>(TestContext.Current.CancellationToken);
        rotatePayload.ShouldNotBeNull();
        var rotatedToken = rotatePayload.Url.Split("/portal/access/").Last();
        rotatedToken.ShouldNotBe(token);

        App.Client.DefaultRequestHeaders.Authorization = null;
        using var oldLinkStatus = await App.Client.GetAsync(
            $"/client-portal/auth/link?token={Uri.EscapeDataString(token)}",
            TestContext.Current.CancellationToken);
        oldLinkStatus.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var staleSavedIdentityStatus = await App.Client.GetAsync(
            $"/client-portal/auth/saved?reference={Uri.EscapeDataString(consumePayload.SavedIdentity.Reference)}",
            TestContext.Current.CancellationToken);
        staleSavedIdentityStatus.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var rotatedLinkStatus = await App.Client.GetAsync(
            $"/client-portal/auth/link?token={Uri.EscapeDataString(rotatedToken)}",
            TestContext.Current.CancellationToken);
        rotatedLinkStatus.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rotatedStatusPayload = await rotatedLinkStatus.Content.ReadFromJsonAsync<GetClientPortalLinkStatusResponse>(TestContext.Current.CancellationToken);
        rotatedStatusPayload.ShouldNotBeNull();
        rotatedStatusPayload.HasPin.ShouldBeTrue();

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secondConsumePayload.AccessToken);
        using var rotatedSessionResponse = await App.Client.GetAsync("/auth/me", TestContext.Current.CancellationToken);
        rotatedSessionResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));
        using var revokeResponse = await App.Client.DeleteAsync($"/clients/{client.Id}/portal-links", TestContext.Current.CancellationToken);
        revokeResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        App.Client.DefaultRequestHeaders.Authorization = null;
        using var revokedLinkStatus = await App.Client.GetAsync(
            $"/client-portal/auth/link?token={Uri.EscapeDataString(rotatedToken)}",
            TestContext.Current.CancellationToken);
        revokedLinkStatus.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PortalAuthentication_TwoDevices_CreatesIndependentActiveSessions()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var client = await TestDataFactory.CreateClientAsync(
            db,
            "Анна",
            "Многодевайсная",
            TestContext.Current.CancellationToken);
        var token = await CreatePortalTokenAsync(client.Id);
        using var firstDevice = CreatePortalDeviceClient("portal-device-one");
        using var secondDevice = CreatePortalDeviceClient("portal-device-two");

        using var firstLoginResponse = await firstDevice.PostAsJsonAsync(
            "/client-portal/auth/link",
            new { token, pin = "1234", pinConfirmation = "1234" },
            TestContext.Current.CancellationToken);
        using var secondLoginResponse = await secondDevice.PostAsJsonAsync(
            "/client-portal/auth/link",
            new { token, pin = "1234" },
            TestContext.Current.CancellationToken);

        firstLoginResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        secondLoginResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var firstLogin = await firstLoginResponse.Content.ReadFromJsonAsync<ClientPortalAuthenticationResponse>(
            TestContext.Current.CancellationToken);
        var secondLogin = await secondLoginResponse.Content.ReadFromJsonAsync<ClientPortalAuthenticationResponse>(
            TestContext.Current.CancellationToken);
        firstLogin.ShouldNotBeNull();
        secondLogin.ShouldNotBeNull();
        firstLogin.AccessToken.ShouldNotBe(secondLogin.AccessToken);

        db.ChangeTracker.Clear();
        var activeSessions = await db.Sessions
            .AsNoTracking()
            .CountAsync(
                session => session.User.ClientId == client.Id && !session.WasRevoked,
                TestContext.Current.CancellationToken);
        activeSessions.ShouldBe(2);

        firstDevice.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", firstLogin.AccessToken);
        secondDevice.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secondLogin.AccessToken);
        using var firstMeResponse = await firstDevice.GetAsync("/auth/me", TestContext.Current.CancellationToken);
        using var secondMeResponse = await secondDevice.GetAsync("/auth/me", TestContext.Current.CancellationToken);
        firstMeResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        secondMeResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PortalAuthentication_ThirdFailedPinAttempt_BlocksImmediateRetry()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var client = await TestDataFactory.CreateClientAsync(
            db,
            "Павел",
            "Заблокированный",
            TestContext.Current.CancellationToken);
        var token = await CreatePortalTokenAsync(client.Id);
        using var setupDevice = CreatePortalDeviceClient("portal-setup-device");
        using var setupResponse = await setupDevice.PostAsJsonAsync(
            "/client-portal/auth/link",
            new { token, pin = "1234", pinConfirmation = "1234" },
            TestContext.Current.CancellationToken);
        setupResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var retryDevice = CreatePortalDeviceClient("portal-retry-device");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var failedResponse = await retryDevice.PostAsJsonAsync(
                "/client-portal/auth/link",
                new { token, pin = "9999" },
                TestContext.Current.CancellationToken);
            failedResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var blockedResponse = await retryDevice.PostAsJsonAsync(
            "/client-portal/auth/link",
            new { token, pin = "1234" },
            TestContext.Current.CancellationToken);

        blockedResponse.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        blockedResponse.Headers.RetryAfter.ShouldNotBeNull();
        blockedResponse.Headers.RetryAfter.Delta.ShouldNotBeNull();
        blockedResponse.Headers.RetryAfter.Delta.Value.ShouldBeGreaterThan(TimeSpan.Zero);
        db.ChangeTracker.Clear();
        var link = await db.ClientPortalLoginLinks
            .AsNoTracking()
            .SingleAsync(item => item.User.ClientId == client.Id, TestContext.Current.CancellationToken);
        link.FailedPinAttempts.ShouldBe(3);
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

    private async Task<string> CreatePortalTokenAsync(Ulid clientId)
    {
        App.Client.DefaultRequestHeaders.Authorization = null;
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));

        using var createLinkResponse = await App.Client.PostAsJsonAsync($"/clients/{clientId}/portal-links", new { }, TestContext.Current.CancellationToken);
        createLinkResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var payload = await createLinkResponse.Content.ReadFromJsonAsync<CreateClientPortalLinkResponse>(cancellationToken: TestContext.Current.CancellationToken);
        payload.ShouldNotBeNull();
        return payload.Url.Split("/portal/access/").Last();
    }

    private async Task AuthenticatePortalAsync(string token)
    {
        App.Client.DefaultRequestHeaders.Authorization = null;
        App.Client.DefaultRequestHeaders.UserAgent.ParseAdd("MelodyTrack.Tests/1.0");

        using var consumeResponse = await App.Client.PostAsJsonAsync(
            "/client-portal/auth/link",
            new
            {
                token,
                pin = "1234",
                pinConfirmation = "1234"
            },
            TestContext.Current.CancellationToken);
        consumeResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var consumePayload = await consumeResponse.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: TestContext.Current.CancellationToken);
        consumePayload.ShouldNotBeNull();
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", consumePayload.AccessToken);
    }

    private HttpClient CreatePortalDeviceClient(string identity)
    {
        var client = App.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Forwarded-For", identity);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MelodyTrack.Tests/1.0");
        return client;
    }
}

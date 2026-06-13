using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Api.ClientPortal.Responses;
using MelodyTrack.Backend.Api.Clients.Responses;
using MelodyTrack.Backend.Api.Schedule.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Backend.Utils;
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
        await db.Appointments.AddAsync(new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            StartDate = startDate,
            EndDate = endDate,
            Status = AppointmentStatus.Planned,
            IsDeleted = false
        }, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));

        using var createLinkResponse = await App.Client.PostAsJsonAsync(
            $"/clients/{client.Id}/portal-link",
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
        var consumePayload = await consumeResponse.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: TestContext.Current.CancellationToken);

        consumePayload.ShouldNotBeNull();
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", consumePayload.AccessToken);

        using var meResponse = await App.Client.GetAsync("/auth/me", TestContext.Current.CancellationToken);
        meResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var mePayload = await meResponse.Content.ReadFromJsonAsync<MeResponse>(cancellationToken: TestContext.Current.CancellationToken);

        mePayload.ShouldNotBeNull();
        mePayload.IsClientPortal.ShouldBeTrue();
        mePayload.LinkedClientId.ShouldBe(client.Id);

        var scheduleUrl =
            $"/client-portal/schedule?timezone=UTC&startDate={Uri.EscapeDataString(startDate.AddDays(-1).ToString("O"))}&endDate={Uri.EscapeDataString(endDate.AddDays(1).ToString("O"))}";
        using var scheduleResponse = await App.Client.GetAsync(scheduleUrl, TestContext.Current.CancellationToken);
        scheduleResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var schedulePayload = await scheduleResponse.Content.ReadFromJsonAsync<GetAppointmentsResponse>(cancellationToken: TestContext.Current.CancellationToken);

        schedulePayload.ShouldNotBeNull();
        schedulePayload.Appointments.Count.ShouldBe(1);
        schedulePayload.Appointments[0].Client.Id.ShouldBe(client.Id);
        schedulePayload.Appointments[0].Client.Contacts.ShouldBeNull();

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));

        using var resetPinResponse = await App.Client.PostAsJsonAsync(
            $"/clients/{client.Id}/portal-pin/reset",
            new { },
            TestContext.Current.CancellationToken);
        resetPinResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", consumePayload.AccessToken);
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
    }
}

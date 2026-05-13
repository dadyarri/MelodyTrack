using System.Net;
using System.Net.Http.Headers;
using FastEndpoints;
using FastEndpoints.Testing;
using MelodyTrack.Backend.Api.Schedule.Endpoints;
using MelodyTrack.Backend.Api.Schedule.Requests;
using MelodyTrack.Backend.Api.Schedule.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Backend.Utils;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public class ScheduleEndpointTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task GetMiniSchedule_ReturnsOnlyCurrentUsersUpcomingAppointments()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var currentUser = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var otherUser = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Anna", "Petrova", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Vocal lesson", TestContext.Current.CancellationToken);
        var nowUtc = DateTime.UtcNow;

        var visibleAppointment = new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            Provider = currentUser,
            StartDate = nowUtc.AddMinutes(30),
            EndDate = nowUtc.AddMinutes(90),
            IsCompleted = false,
            IsCanceled = false,
            IsDeleted = false
        };

        var pastAppointment = new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            Provider = currentUser,
            StartDate = nowUtc.AddHours(-2),
            EndDate = nowUtc.AddMinutes(-10),
            IsCompleted = false,
            IsCanceled = false,
            IsDeleted = false
        };

        var otherUsersAppointment = new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            Provider = otherUser,
            StartDate = nowUtc.AddHours(2),
            EndDate = nowUtc.AddHours(3),
            IsCompleted = false,
            IsCanceled = false,
            IsDeleted = false
        };

        await db.Appointments.AddRangeAsync(
            [visibleAppointment, pastAppointment, otherUsersAppointment],
            TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(currentUser));

        var (rsp, res) = await App.Client.GETAsync<GetMiniScheduleEndpoint, BaseGetAppointmentsRequest, GetMiniScheduleResponse>(
            new BaseGetAppointmentsRequest
            {
                Timezone = "UTC"
            });

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.ShouldNotBeNull();

        var returnedAppointments = res.Appointments.SelectMany(day => day.Value).ToList();

        returnedAppointments.Count.ShouldBe(1);
        returnedAppointments[0].Id.ShouldBe(visibleAppointment.Id);
        returnedAppointments[0].Provider.ShouldNotBeNull();
        returnedAppointments[0].Provider!.Id.ShouldBe(currentUser.Id);
    }
}

using System.Net;
using System.Net.Http.Headers;
using FastEndpoints;
using FastEndpoints.Testing;
using MelodyTrack.Backend.Api.CalendarSubscriptions.Endpoints;
using MelodyTrack.Backend.Api.CalendarSubscriptions.Responses;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Backend.Utils;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public class CalendarSubscriptionEndpointTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task ClientSubscription_ReturnsPastAndNearestUpcomingAppointment()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Анна", "Иванова", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Вокал", TestContext.Current.CancellationToken);
        var now = DateTime.UtcNow;
        await db.Appointments.AddRangeAsync([
            CreateAppointment(client, service, now.AddDays(-1)),
            CreateAppointment(client, service, now.AddDays(1)),
            CreateAppointment(client, service, now.AddDays(2))
        ], TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));
        var (createResponse, subscription) = await App.Client.POSTAsync<RegenerateClientCalendarSubscriptionEndpoint, GetEntityRequest, CalendarSubscriptionResponse>(
            new GetEntityRequest { Id = client.Id });
        App.Client.DefaultRequestHeaders.Authorization = null;

        createResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var calendarResponse = await App.Client.GetAsync($"/calendar-subscriptions/{subscription.Token}.ics", TestContext.Current.CancellationToken);
        var calendar = await calendarResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        calendarResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        calendarResponse.Content.Headers.ContentType!.MediaType.ShouldBe("text/calendar");
        calendar.ShouldContain("BEGIN:VCALENDAR");
        calendar.Split("BEGIN:VEVENT").Length.ShouldBe(3);
    }

    [Fact]
    public async Task RegeneratingSubscription_RevokesPreviousPublicLink()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Ирина", "Петрова", TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));
        var (_, first) = await App.Client.POSTAsync<RegenerateClientCalendarSubscriptionEndpoint, GetEntityRequest, CalendarSubscriptionResponse>(new GetEntityRequest { Id = client.Id });
        var (_, second) = await App.Client.POSTAsync<RegenerateClientCalendarSubscriptionEndpoint, GetEntityRequest, CalendarSubscriptionResponse>(new GetEntityRequest { Id = client.Id });
        App.Client.DefaultRequestHeaders.Authorization = null;

        first.Token.ShouldNotBe(second.Token);
        (await App.Client.GetAsync($"/calendar-subscriptions/{first.Token}.ics", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await App.Client.GetAsync($"/calendar-subscriptions/{second.Token}.ics", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static Appointment CreateAppointment(Client client, Service service, DateTime startDate) => new()
    {
        Id = Ulid.NewUlid(), Client = client, Service = service, StartDate = startDate, EndDate = startDate.AddHours(1), Status = AppointmentStatus.Planned, IsDeleted = false
    };
}

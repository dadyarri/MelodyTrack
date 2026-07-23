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
        subscription.Url.ShouldBe($"http://localhost:5000/calendar-subscriptions/{subscription.Token}.ics");
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

    [Fact]
    public async Task UserSubscription_IncludesAssignedRecurringTasksAndReminders()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        admin.Phone = "+79990000000";
        var client = await TestDataFactory.CreateClientAsync(db, "Мария", "Соколова", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Фортепиано", TestContext.Current.CancellationToken);
        var startAtUtc = DateTime.UtcNow.AddHours(1);
        await db.Appointments.AddAsync(CreateAppointment(client, service, startAtUtc, admin), TestContext.Current.CancellationToken);
        await db.RecurringTaskRules.AddAsync(new RecurringTaskRule
        {
            Id = Ulid.NewUlid(),
            Name = "Расписание преподавателя для календаря",
            Type = RecurringTaskType.TeacherDailySchedule,
            IsEnabled = true,
            MessageTemplate = "Расписание на {date}",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        }, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));
        var (createResponse, subscription) = await App.Client.POSTAsync<RegenerateUserCalendarSubscriptionEndpoint, GetEntityRequest, CalendarSubscriptionResponse>(
            new GetEntityRequest { Id = admin.Id });
        App.Client.DefaultRequestHeaders.Authorization = null;

        createResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var calendar = await (await App.Client.GetAsync($"/calendar-subscriptions/{subscription.Token}.ics", TestContext.Current.CancellationToken))
            .Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        calendar.ShouldContain("SUMMARY:Фортепиано (Соколова Мария)");
        calendar.ShouldContain("SUMMARY:Отправить расписание: Viewer Admin");
        calendar.ShouldNotContain("DESCRIPTION:Расписание на");
        calendar.Split("BEGIN:VALARM").Length.ShouldBeGreaterThan(2);
        calendar.ShouldContain("TRIGGER:-PT15M");
    }

    private static Appointment CreateAppointment(Client client, Service service, DateTime startDate, User? provider = null) => new()
    {
        Id = Ulid.NewUlid(), Client = client, Service = service, Provider = provider, StartDate = startDate, EndDate = startDate.AddHours(1), Status = AppointmentStatus.Planned, IsDeleted = false
    };
}

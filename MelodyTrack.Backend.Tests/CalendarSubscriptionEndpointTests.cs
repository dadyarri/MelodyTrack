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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public class CalendarSubscriptionEndpointTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task ClientSubscription_PreservesPastAppointmentsAndLimitsFutureAppointmentsToTheRollingWindow()
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
        calendar.Split("BEGIN:VEVENT").Length.ShouldBe(4);
    }

    [Fact]
    public async Task ClientSubscription_MaterializesOnlySubscribedClientsRecurringAppointments()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Анна", "Иванова", TestContext.Current.CancellationToken);
        var otherClient = await TestDataFactory.CreateClientAsync(db, "Ирина", "Петрова", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Вокал", TestContext.Current.CancellationToken);
        var recurrenceType = await db.RecurrenceTypes.FirstAsync(
            type => type.Type == AppointmentRecurrenceType.Daily,
            TestContext.Current.CancellationToken);
        var nowUtc = DateTime.UtcNow;
        var firstStartUtc = nowUtc.Date.AddDays(2).AddHours(11);
        var withinHorizonStartUtc = nowUtc.Date.AddDays(10).AddHours(11);
        var outsideHorizonStartUtc = nowUtc.Date.AddDays(15).AddHours(11);

        await db.RecurrenceRules.AddRangeAsync([
            CreateDailyRule(client, service, recurrenceType, firstStartUtc, firstStartUtc.AddDays(2)),
            CreateDailyRule(client, service, recurrenceType, withinHorizonStartUtc, withinHorizonStartUtc),
            CreateDailyRule(client, service, recurrenceType, outsideHorizonStartUtc, outsideHorizonStartUtc),
            CreateDailyRule(otherClient, service, recurrenceType, firstStartUtc, firstStartUtc.AddDays(2))
        ], TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));
        var (_, subscription) = await App.Client.POSTAsync<RegenerateClientCalendarSubscriptionEndpoint, GetEntityRequest, CalendarSubscriptionResponse>(
            new GetEntityRequest { Id = client.Id });
        App.Client.DefaultRequestHeaders.Authorization = null;

        var calendar = await (await App.Client.GetAsync($"/calendar-subscriptions/{subscription.Token}.ics", TestContext.Current.CancellationToken))
            .Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        calendar.Split("BEGIN:VEVENT").Length.ShouldBe(5);
        calendar.ShouldContain(ToCalendarStart(firstStartUtc));
        calendar.ShouldContain(ToCalendarStart(firstStartUtc.AddDays(1)));
        calendar.ShouldContain(ToCalendarStart(firstStartUtc.AddDays(2)));
        calendar.ShouldNotContain(ToCalendarStart(withinHorizonStartUtc));
        calendar.ShouldNotContain(ToCalendarStart(outsideHorizonStartUtc));
        calendar.ShouldNotContain("RRULE:");

        var generatedClientIds = await db.Appointments
            .Select(appointment => appointment.Client.Id)
            .Distinct()
            .ToListAsync(TestContext.Current.CancellationToken);
        generatedClientIds.ShouldBe([client.Id]);
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

    [Fact]
    public async Task UserSubscription_MaterializesOnlyAssignedRecurringAppointments()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subscribedUser = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var otherProvider = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Мария", "Соколова", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Фортепиано", TestContext.Current.CancellationToken);
        var recurrenceType = await db.RecurrenceTypes.FirstAsync(
            type => type.Type == AppointmentRecurrenceType.Daily,
            TestContext.Current.CancellationToken);
        var nowUtc = DateTime.UtcNow;
        var firstStartUtc = nowUtc.Date.AddDays(2).AddHours(11);
        var withinHorizonStartUtc = nowUtc.Date.AddDays(20).AddHours(11);
        var outsideHorizonStartUtc = nowUtc.Date.AddDays(32).AddHours(11);

        await db.RecurrenceRules.AddRangeAsync([
            CreateDailyRule(client, service, recurrenceType, firstStartUtc, firstStartUtc.AddDays(2), subscribedUser),
            CreateDailyRule(client, service, recurrenceType, withinHorizonStartUtc, withinHorizonStartUtc, subscribedUser),
            CreateDailyRule(client, service, recurrenceType, outsideHorizonStartUtc, outsideHorizonStartUtc, subscribedUser),
            CreateDailyRule(client, service, recurrenceType, firstStartUtc, firstStartUtc.AddDays(2), otherProvider)
        ], TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(subscribedUser));
        var (_, subscription) = await App.Client.POSTAsync<RegenerateUserCalendarSubscriptionEndpoint, GetEntityRequest, CalendarSubscriptionResponse>(
            new GetEntityRequest { Id = subscribedUser.Id });
        App.Client.DefaultRequestHeaders.Authorization = null;

        var calendar = await (await App.Client.GetAsync($"/calendar-subscriptions/{subscription.Token}.ics", TestContext.Current.CancellationToken))
            .Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        calendar.ShouldContain(ToCalendarStart(firstStartUtc));
        calendar.ShouldNotContain(ToCalendarStart(withinHorizonStartUtc));
        calendar.ShouldNotContain(ToCalendarStart(outsideHorizonStartUtc));
        calendar.ShouldContain("SUMMARY:Фортепиано (Соколова Мария)");
        calendar.ShouldNotContain("RRULE:");

        var generatedProviderIds = await db.Appointments
            .Select(appointment => appointment.Provider!.Id)
            .Distinct()
            .ToListAsync(TestContext.Current.CancellationToken);
        generatedProviderIds.ShouldBe([subscribedUser.Id]);
    }

    private static Appointment CreateAppointment(Client client, Service service, DateTime startDate, User? provider = null) => new()
    {
        Id = Ulid.NewUlid(), Client = client, Service = service, Provider = provider, StartDate = startDate, EndDate = startDate.AddHours(1), Status = AppointmentStatus.Planned, IsDeleted = false
    };

    private static AppointmentRecurrenceRule CreateDailyRule(
        Client client,
        Service service,
        RecurrenceType recurrenceType,
        DateTime startDate,
        DateTime endDate,
        User? provider = null) => new()
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            Provider = provider,
            StartDate = startDate,
            EndDate = endDate,
            RecurrenceType = recurrenceType,
            RecurrencePattern = 1
        };

    private static string ToCalendarStart(DateTime startDate)
    {
        return $"DTSTART:{startDate:yyyyMMdd'T'HHmmss'Z'}";
    }
}

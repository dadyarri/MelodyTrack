using System.Net;
using System.Net.Http.Headers;
using MelodyTrack.Backend.Api.Dashboard.Endpoints;
using MelodyTrack.Backend.Api.Dashboard.Requests;
using MelodyTrack.Backend.Api.Dashboard.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Backend.Utils;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public class PersonalDashboardEndpointTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Theory]
    [InlineData(UserRoles.User)]
    [InlineData(UserRoles.Admin)]
    [InlineData(UserRoles.Superuser)]
    public async Task GetDashboardStats_AllStaffRolesReceiveOnlyTheirOwnWork(UserRoles role)
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var currentUser = await CreateUserAsync(db, role);
        var otherUser = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var ownClient = await TestDataFactory.CreateClientAsync(db, "Anna", "Petrova", TestContext.Current.CancellationToken);
        var otherClient = await TestDataFactory.CreateClientAsync(db, "Elena", "Sidorova", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Vocal lesson", TestContext.Current.CancellationToken);
        var nowUtc = DateTime.UtcNow;
        var monthStartUtc = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var tomorrowStartUtc = nowUtc.Date.AddDays(1);
        var ownTomorrowId = Ulid.NewUlid();

        await db.ServicePriceHistory.AddAsync(new ServicePrice
        {
            Id = Ulid.NewUlid(),
            Service = service,
            Price = 150m,
            EffectiveDate = monthStartUtc.AddDays(-1)
        }, TestContext.Current.CancellationToken);
        await db.Appointments.AddRangeAsync(
            [
                CreateAppointment(ownTomorrowId, ownClient, service, currentUser, tomorrowStartUtc.AddHours(10), AppointmentStatus.Planned),
                CreateAppointment(Ulid.NewUlid(), otherClient, service, otherUser, tomorrowStartUtc.AddHours(11), AppointmentStatus.Planned),
                CreateAppointment(Ulid.NewUlid(), ownClient, service, currentUser, monthStartUtc.AddHours(10), AppointmentStatus.Completed),
                CreateAppointment(Ulid.NewUlid(), otherClient, service, otherUser, monthStartUtc.AddHours(11), AppointmentStatus.Completed)
            ],
            TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var response = await GetDashboardAsync(currentUser, "UTC");

        response.PersonalClientsCount.ShouldBe(1);
        response.MonthIncome.ShouldBe(150m);
        response.Tomorrow.Count.ShouldBe(response.Tomorrow.Appointments.Count);
        response.Tomorrow.Appointments.ShouldHaveSingleItem().Id.ShouldBe(ownTomorrowId);
        if (role == UserRoles.User)
        {
            response.Organization.ShouldBeNull();
        }
        else
        {
            response.Organization.ShouldNotBeNull();
            response.Organization.AppointmentsTomorrow.ShouldBe(2);
            response.Organization.MonthIncome.ShouldBe(300m);
        }
    }

    [Fact]
    public async Task GetDashboardStats_CountsExactlyTheVisibleTodayAndTomorrowAppointments()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var currentUser = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var otherUser = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Anna", "Petrova", TestContext.Current.CancellationToken);
        var vacationClient = await TestDataFactory.CreateClientAsync(db, "Maria", "Ivanova", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Vocal lesson", TestContext.Current.CancellationToken);
        var nowUtc = DateTime.UtcNow;
        var todayStartUtc = nowUtc.Date;
        var tomorrowStartUtc = todayStartUtc.AddDays(1);
        var dayAfterTomorrowStartUtc = todayStartUtc.AddDays(2);
        var todayVisibleId = Ulid.NewUlid();
        var tomorrowVisibleId = Ulid.NewUlid();
        var todayVisibleStart = nowUtc > todayStartUtc.AddMinutes(1) ? nowUtc.AddMinutes(-1) : todayStartUtc;

        await db.ClientVacations.AddAsync(new ClientVacation
        {
            Id = Ulid.NewUlid(),
            ClientId = vacationClient.Id,
            StartDate = todayStartUtc,
            EndDate = todayStartUtc.AddDays(1)
        }, TestContext.Current.CancellationToken);
        await db.Appointments.AddRangeAsync(
            [
                CreateAppointment(todayVisibleId, client, service, currentUser, todayVisibleStart, AppointmentStatus.Planned, nowUtc.AddMinutes(30)),
                CreateAppointment(tomorrowVisibleId, client, service, currentUser, tomorrowStartUtc.AddHours(10), AppointmentStatus.Planned),
                CreateAppointment(Ulid.NewUlid(), client, service, currentUser, todayStartUtc, AppointmentStatus.Planned, nowUtc.AddTicks(-1)),
                CreateAppointment(Ulid.NewUlid(), client, service, currentUser, nowUtc.AddMinutes(5), AppointmentStatus.Completed),
                CreateAppointment(Ulid.NewUlid(), client, service, currentUser, nowUtc.AddMinutes(10), AppointmentStatus.Cancelled),
                CreateAppointment(Ulid.NewUlid(), client, service, currentUser, nowUtc.AddMinutes(15), AppointmentStatus.Burned),
                CreateAppointment(Ulid.NewUlid(), client, service, currentUser, nowUtc.AddMinutes(20), AppointmentStatus.Planned, isDeleted: true),
                CreateAppointment(Ulid.NewUlid(), client, service, otherUser, nowUtc.AddMinutes(25), AppointmentStatus.Planned),
                CreateAppointment(Ulid.NewUlid(), vacationClient, service, currentUser, nowUtc.AddMinutes(30), AppointmentStatus.Planned),
                CreateAppointment(Ulid.NewUlid(), client, service, currentUser, dayAfterTomorrowStartUtc, AppointmentStatus.Planned)
            ],
            TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var response = await GetDashboardAsync(currentUser, "UTC");

        response.Today.Date.ShouldBe(DateOnly.FromDateTime(todayStartUtc));
        response.Today.Count.ShouldBe(response.Today.Appointments.Count);
        response.Today.Appointments.ShouldHaveSingleItem().Id.ShouldBe(todayVisibleId);
        response.Tomorrow.Date.ShouldBe(DateOnly.FromDateTime(tomorrowStartUtc));
        response.Tomorrow.Count.ShouldBe(response.Tomorrow.Appointments.Count);
        response.Tomorrow.Appointments.ShouldHaveSingleItem().Id.ShouldBe(tomorrowVisibleId);
    }

    [Fact]
    public async Task GetDashboardStats_UsesBrowserTimezoneForBoundariesAndClientVacations()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var currentUser = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var visibleClient = await TestDataFactory.CreateClientAsync(db, "Anna", "Petrova", TestContext.Current.CancellationToken);
        var vacationClient = await TestDataFactory.CreateClientAsync(db, "Elena", "Sidorova", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Vocal lesson", TestContext.Current.CancellationToken);
        var timezone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        var nowUtc = DateTime.UtcNow;
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, timezone));
        var todayStartUtc = TimeZoneInfo.ConvertTimeToUtc(localToday.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), timezone);
        var tomorrowStartUtc = TimeZoneInfo.ConvertTimeToUtc(localToday.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), timezone);
        var dayAfterTomorrowStartUtc = TimeZoneInfo.ConvertTimeToUtc(localToday.AddDays(2).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), timezone);
        var visibleId = Ulid.NewUlid();

        await db.ClientVacations.AddAsync(new ClientVacation
        {
            Id = Ulid.NewUlid(),
            ClientId = vacationClient.Id,
            StartDate = todayStartUtc,
            EndDate = tomorrowStartUtc
        }, TestContext.Current.CancellationToken);
        await db.Appointments.AddRangeAsync(
            [
                CreateAppointment(visibleId, visibleClient, service, currentUser, todayStartUtc.AddMinutes(1), AppointmentStatus.Planned, dayAfterTomorrowStartUtc),
                CreateAppointment(Ulid.NewUlid(), visibleClient, service, currentUser, todayStartUtc.AddTicks(-1), AppointmentStatus.Planned, dayAfterTomorrowStartUtc),
                CreateAppointment(Ulid.NewUlid(), vacationClient, service, currentUser, todayStartUtc.AddMinutes(2), AppointmentStatus.Planned, dayAfterTomorrowStartUtc)
            ],
            TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var response = await GetDashboardAsync(currentUser, timezone.Id);

        response.Today.Date.ShouldBe(localToday);
        response.Today.Count.ShouldBe(1);
        response.Today.Appointments.ShouldHaveSingleItem().Id.ShouldBe(visibleId);
        DateOnly.FromDateTime(response.Today.Appointments[0].StartDate).ShouldBe(localToday);
    }

    private async Task<GetDashboardStatsResponse> GetDashboardAsync(User user, string timezone)
    {
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));
        var (httpResponse, response) = await App.Client.GETAsync<GetDashboardStatsEndpoint, GetDashboardStatsRequest, GetDashboardStatsResponse>(
            new GetDashboardStatsRequest { Timezone = timezone });
        httpResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        return response.ShouldNotBeNull();
    }

    private static Appointment CreateAppointment(
        Ulid id,
        Client client,
        Service service,
        User provider,
        DateTime startDate,
        AppointmentStatus status,
        DateTime? endDate = null,
        bool isDeleted = false)
    {
        return new Appointment
        {
            Id = id,
            Client = client,
            Service = service,
            Provider = provider,
            StartDate = DateTime.SpecifyKind(startDate, DateTimeKind.Utc),
            EndDate = DateTime.SpecifyKind(endDate ?? startDate.AddHours(1), DateTimeKind.Utc),
            Status = status,
            IsDeleted = isDeleted
        };
    }

    private static Task<User> CreateUserAsync(AppDbContext db, UserRoles role)
    {
        return role switch
        {
            UserRoles.Admin => TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken),
            UserRoles.Superuser => TestDataFactory.CreateSuperuserAsync(db, TestContext.Current.CancellationToken),
            _ => TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken)
        };
    }
}

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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public class DashboardEndpointTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task GetDashboardStats_CountsBurnedAppointmentsInMonthIncome()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Anna", "Petrova", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Vocal lesson", TestContext.Current.CancellationToken);

        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        await db.ServicePriceHistory.AddAsync(new ServicePrice
        {
            Id = Ulid.NewUlid(),
            Service = service,
            Price = 120m,
            EffectiveDate = monthStart
        }, TestContext.Current.CancellationToken);

        await db.Appointments.AddRangeAsync(
            [
                new Appointment
                {
                    Id = Ulid.NewUlid(),
                    Client = client,
                    Service = service,
                    Provider = user,
                    StartDate = monthStart.AddDays(3).AddHours(10),
                    EndDate = monthStart.AddDays(3).AddHours(11),
                    Status = AppointmentStatus.Completed,
                    IsDeleted = false
                },
                new Appointment
                {
                    Id = Ulid.NewUlid(),
                    Client = client,
                    Service = service,
                    Provider = user,
                    StartDate = monthStart.AddDays(4).AddHours(10),
                    EndDate = monthStart.AddDays(4).AddHours(11),
                    Status = AppointmentStatus.Burned,
                    IsDeleted = false
                },
                new Appointment
                {
                    Id = Ulid.NewUlid(),
                    Client = client,
                    Service = service,
                    Provider = user,
                    StartDate = monthStart.AddDays(5).AddHours(10),
                    EndDate = monthStart.AddDays(5).AddHours(11),
                    Status = AppointmentStatus.Cancelled,
                    IsDeleted = false
                }
            ],
            TestContext.Current.CancellationToken);

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var (rsp, res) = await App.Client.GETAsync<GetDashboardStatsEndpoint, GetDashboardStatsRequest, GetDashboardStatsResponse>(
            new GetDashboardStatsRequest
            {
                Timezone = "UTC"
            });

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.ShouldNotBeNull();
        res.MonthIncome.ShouldBe(240m);
    }

    [Fact]
    public async Task GetDashboardStats_UsesEarliestKnownPrice_WhenAppointmentPredatesPriceHistory()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Anna", "Petrova", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Vocal lesson", TestContext.Current.CancellationToken);

        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var appointmentStart = monthStart.AddDays(2).AddHours(10);

        await db.ServicePriceHistory.AddAsync(new ServicePrice
        {
            Id = Ulid.NewUlid(),
            Service = service,
            Price = 180m,
            EffectiveDate = appointmentStart.AddDays(5)
        }, TestContext.Current.CancellationToken);

        await db.Appointments.AddAsync(new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            Provider = user,
            StartDate = appointmentStart,
            EndDate = appointmentStart.AddHours(1),
            Status = AppointmentStatus.Completed,
            IsDeleted = false
        }, TestContext.Current.CancellationToken);

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var (rsp, res) = await App.Client.GETAsync<GetDashboardStatsEndpoint, GetDashboardStatsRequest, GetDashboardStatsResponse>(
            new GetDashboardStatsRequest
            {
                Timezone = "UTC"
            });

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.ShouldNotBeNull();
        res.MonthIncome.ShouldBe(180m);
    }

    [Fact]
    public async Task GetDashboardStats_SumsOrganizationClientBalances()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var firstClient = await TestDataFactory.CreateClientAsync(db, "Anna", "Petrova", TestContext.Current.CancellationToken);
        var secondClient = await TestDataFactory.CreateClientAsync(db, "Elena", "Sidorova", TestContext.Current.CancellationToken);
        var thirdClient = await TestDataFactory.CreateClientAsync(db, "Maria", "Ivanova", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Vocal lesson", TestContext.Current.CancellationToken);

        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        await db.ServicePriceHistory.AddAsync(new ServicePrice
        {
            Id = Ulid.NewUlid(),
            Service = service,
            Price = 100m,
            EffectiveDate = monthStart
        }, TestContext.Current.CancellationToken);

        await db.Payments.AddRangeAsync(
            [
                new Payment
                {
                    Id = Ulid.NewUlid(),
                    Client = firstClient,
                    Service = service,
                    Amount = 250m,
                    Date = monthStart.AddDays(1),
                    Description = "Advance"
                },
                new Payment
                {
                    Id = Ulid.NewUlid(),
                    Client = secondClient,
                    Service = service,
                    Amount = 50m,
                    Date = monthStart.AddDays(1),
                    Description = "Partial"
                },
                new Payment
                {
                    Id = Ulid.NewUlid(),
                    Client = thirdClient,
                    Service = service,
                    Amount = 100m,
                    Date = monthStart.AddDays(1),
                    Description = "Exact"
                }
            ],
            TestContext.Current.CancellationToken);

        await db.Appointments.AddRangeAsync(
            [
                new Appointment
                {
                    Id = Ulid.NewUlid(),
                    Client = firstClient,
                    Service = service,
                    Provider = user,
                    StartDate = monthStart.AddDays(2).AddHours(10),
                    EndDate = monthStart.AddDays(2).AddHours(11),
                    Status = AppointmentStatus.Completed,
                    IsDeleted = false
                },
                new Appointment
                {
                    Id = Ulid.NewUlid(),
                    Client = secondClient,
                    Service = service,
                    Provider = user,
                    StartDate = monthStart.AddDays(3).AddHours(10),
                    EndDate = monthStart.AddDays(3).AddHours(11),
                    Status = AppointmentStatus.Completed,
                    IsDeleted = false
                },
                new Appointment
                {
                    Id = Ulid.NewUlid(),
                    Client = thirdClient,
                    Service = service,
                    Provider = user,
                    StartDate = monthStart.AddDays(4).AddHours(10),
                    EndDate = monthStart.AddDays(4).AddHours(11),
                    Status = AppointmentStatus.Completed,
                    IsDeleted = false
                }
            ],
            TestContext.Current.CancellationToken);

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var (rsp, res) = await App.Client.GETAsync<GetDashboardStatsEndpoint, GetDashboardStatsRequest, GetDashboardStatsResponse>(
            new GetDashboardStatsRequest
            {
                Timezone = "UTC"
            });

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.ShouldNotBeNull();
        res.Organization.ShouldNotBeNull();
        res.Organization.TotalPositiveBalance.ShouldBe(150m);
        res.Organization.TotalDebt.ShouldBe(50m);
    }

    [Fact]
    public async Task GetDashboardStats_MaterializesRecurringAppointmentsForPlannedCounts()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Anna", "Petrova", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Vocal lesson", TestContext.Current.CancellationToken);
        var recurrenceType = await db.RecurrenceTypes.FirstAsync(e => e.Type == AppointmentRecurrenceType.Daily, TestContext.Current.CancellationToken);
        var timezone = TimeZoneInfo.Utc;
        var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timezone).Date;
        var appointmentStart = DateTime.SpecifyKind(today.AddDays(1).AddHours(10), DateTimeKind.Utc);

        await db.ServicePriceHistory.AddAsync(new ServicePrice
        {
            Id = Ulid.NewUlid(),
            Service = service,
            Price = 120m,
            EffectiveDate = appointmentStart.AddDays(-1)
        }, TestContext.Current.CancellationToken);

        await db.RecurrenceRules.AddAsync(new AppointmentRecurrenceRule
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            Provider = user,
            StartDate = appointmentStart,
            EndDate = appointmentStart.Date,
            RecurrenceType = recurrenceType,
            RecurrencePattern = null
        }, TestContext.Current.CancellationToken);

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var (rsp, res) = await App.Client.GETAsync<GetDashboardStatsEndpoint, GetDashboardStatsRequest, GetDashboardStatsResponse>(
            new GetDashboardStatsRequest
            {
                Timezone = "UTC"
            });

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.ShouldNotBeNull();
        res.Tomorrow.Count.ShouldBeGreaterThanOrEqualTo(1);
        res.Tomorrow.Count.ShouldBe(res.Tomorrow.Appointments.Count);
    }

    [Fact]
    public async Task GetDashboardStats_ScheduleUser_OnlySeesOwnPlannedAppointments()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var currentUser = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var otherUser = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Anna", "Petrova", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Vocal lesson", TestContext.Current.CancellationToken);
        var nowUtc = DateTime.UtcNow;
        var visibleStart = nowUtc.AddMinutes(30);
        var visibleAppointmentId = Ulid.NewUlid();

        await db.Appointments.AddRangeAsync(
            [
                new Appointment
                {
                    Id = visibleAppointmentId,
                    Client = client,
                    Service = service,
                    Provider = currentUser,
                    StartDate = visibleStart,
                    EndDate = visibleStart.AddHours(1),
                    Status = AppointmentStatus.Planned,
                    IsDeleted = false
                },
                new Appointment
                {
                    Id = Ulid.NewUlid(),
                    Client = client,
                    Service = service,
                    Provider = otherUser,
                    StartDate = visibleStart.AddMinutes(15),
                    EndDate = visibleStart.AddHours(1).AddMinutes(15),
                    Status = AppointmentStatus.Planned,
                    IsDeleted = false
                }
            ],
            TestContext.Current.CancellationToken);

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(currentUser));

        var (rsp, res) = await App.Client.GETAsync<GetDashboardStatsEndpoint, GetDashboardStatsRequest, GetDashboardStatsResponse>(
            new GetDashboardStatsRequest
            {
                Timezone = "UTC"
            });

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.ShouldNotBeNull();
        res.Today.Count.ShouldBe(1);
        res.Today.Appointments.ShouldHaveSingleItem().Id.ShouldBe(visibleAppointmentId);
    }

}

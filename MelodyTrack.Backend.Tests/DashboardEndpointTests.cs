using System.Net;
using System.Net.Http.Headers;
using FastEndpoints;
using FastEndpoints.Testing;
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
    public async Task GetRevenueAnalytics_ReturnsRevenuePlannedRevenueAndTeacherBreakdown()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var teacher = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Anna", "Petrova", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Vocal lesson", TestContext.Current.CancellationToken);

        await db.ServicePriceHistory.AddAsync(new ServicePrice
        {
            Id = Ulid.NewUlid(),
            Service = service,
            Price = 150m,
            EffectiveDate = new DateTime(2026, 05, 01, 0, 0, 0, DateTimeKind.Utc)
        }, TestContext.Current.CancellationToken);

        await db.Appointments.AddRangeAsync(
            [
                new Appointment
                {
                    Id = Ulid.NewUlid(),
                    Client = client,
                    Service = service,
                    Provider = teacher,
                    StartDate = new DateTime(2026, 05, 10, 10, 0, 0, DateTimeKind.Utc),
                    EndDate = new DateTime(2026, 05, 10, 11, 0, 0, DateTimeKind.Utc),
                    Status = AppointmentStatus.Completed,
                    IsDeleted = false
                },
                new Appointment
                {
                    Id = Ulid.NewUlid(),
                    Client = client,
                    Service = service,
                    Provider = teacher,
                    StartDate = new DateTime(2026, 05, 11, 10, 0, 0, DateTimeKind.Utc),
                    EndDate = new DateTime(2026, 05, 11, 11, 0, 0, DateTimeKind.Utc),
                    Status = AppointmentStatus.Burned,
                    IsDeleted = false
                },
                new Appointment
                {
                    Id = Ulid.NewUlid(),
                    Client = client,
                    Service = service,
                    Provider = teacher,
                    StartDate = new DateTime(2026, 05, 12, 10, 0, 0, DateTimeKind.Utc),
                    EndDate = new DateTime(2026, 05, 12, 11, 0, 0, DateTimeKind.Utc),
                    Status = AppointmentStatus.Planned,
                    IsDeleted = false
                },
                new Appointment
                {
                    Id = Ulid.NewUlid(),
                    Client = client,
                    Service = service,
                    Provider = teacher,
                    StartDate = new DateTime(2026, 05, 13, 10, 0, 0, DateTimeKind.Utc),
                    EndDate = new DateTime(2026, 05, 13, 11, 0, 0, DateTimeKind.Utc),
                    Status = AppointmentStatus.Cancelled,
                    IsDeleted = false
                }
            ],
            TestContext.Current.CancellationToken);

        await db.Expenses.AddAsync(new Expense
        {
            Id = Ulid.NewUlid(),
            Description = "Rent",
            Amount = 90m,
            Date = new DateTime(2026, 05, 15, 0, 0, 0, DateTimeKind.Utc)
        }, TestContext.Current.CancellationToken);

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(teacher));

        var (rsp, res) = await App.Client.GETAsync<GetRevenueAnalyticsEndpoint, GetRevenueAnalyticsRequest, GetRevenueAnalyticsResponse>(
            new GetRevenueAnalyticsRequest
            {
                Start = new DateTime(2026, 05, 01, 0, 0, 0, DateTimeKind.Utc),
                End = new DateTime(2026, 05, 31, 0, 0, 0, DateTimeKind.Utc),
                Timezone = "UTC"
            });

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.ShouldNotBeNull();
        res.TotalRevenue.ShouldBe(300m);
        res.PlannedRevenue.ShouldBe(150m);
        res.TotalExpenses.ShouldBe(90m);
        res.NetProfit.ShouldBe(210m);
        res.AverageReceipt.ShouldBe(150m);
        res.RevenueCountedAppointmentsCount.ShouldBe(2);
        res.PlannedAppointmentsCount.ShouldBe(1);
        res.Teachers.Count.ShouldBe(1);
        res.Teachers[0].TeacherId.ShouldBe(teacher.Id);
        res.Teachers[0].Revenue.ShouldBe(300m);
        res.Teachers[0].RevenueCountedAppointmentsCount.ShouldBe(2);
        res.Teachers[0].CompletedAppointmentsCount.ShouldBe(1);
        res.Teachers[0].BurnedAppointmentsCount.ShouldBe(1);
        res.Teachers[0].ServicesProvidedCount.ShouldBe(1);
    }
}

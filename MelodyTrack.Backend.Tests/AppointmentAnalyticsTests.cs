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
public class AppointmentAnalyticsTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task GetAppointmentsAnalytics_EmptyRange_ReturnsStableEmptyBuckets()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));

        var (response, payload) = await App.Client.GETAsync<
            GetAppointmentsAnalyticsEndpoint,
            GetAppointmentsAnalyticsRequest,
            GetAppointmentsAnalyticsResponse>(new GetAppointmentsAnalyticsRequest
            {
                Start = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                End = new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc),
                Timezone = "UTC"
            });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        payload.ShouldNotBeNull();
        payload.TotalAppointmentsCount.ShouldBe(0);
        payload.TotalRevenue.ShouldBe(0m);
        payload.BurnedShare.ShouldBeNull();
        payload.CancellationShare.ShouldBeNull();
        payload.AverageGapBetweenServicesHours.ShouldBeNull();
        payload.DailyLoad.Count.ShouldBe(2);
        payload.Hours.Count.ShouldBe(24);
        foreach (var status in payload.Statuses)
        {
            status.Count.ShouldBe(0);
            status.Share.ShouldBeNull();
        }
        payload.Teachers.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAppointmentsAnalytics_PostgreSqlDataset_CoversBoundariesStatusesGroupingPricesAndGaps()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var teacher = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var firstClient = await TestDataFactory.CreateClientAsync(db, "Anna", "Boundary", TestContext.Current.CancellationToken);
        var secondClient = await TestDataFactory.CreateClientAsync(db, "Boris", "Burned", TestContext.Current.CancellationToken);
        var firstService = await TestDataFactory.CreateServiceAsync(db, "Piano", TestContext.Current.CancellationToken);
        var secondService = await TestDataFactory.CreateServiceAsync(db, "Vocal", TestContext.Current.CancellationToken);
        var day = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

        await db.ServicePriceHistory.AddRangeAsync(
        [
            new ServicePrice { Id = Ulid.NewUlid(), Service = firstService, Price = 100m, EffectiveDate = day.AddDays(-1) },
            new ServicePrice { Id = Ulid.NewUlid(), Service = firstService, Price = 150m, EffectiveDate = day.AddHours(10) },
            new ServicePrice { Id = Ulid.NewUlid(), Service = secondService, Price = 200m, EffectiveDate = day.AddDays(-1) }
        ], TestContext.Current.CancellationToken);

        await db.Appointments.AddRangeAsync(
        [
            CreateAppointment(firstClient, firstService, teacher, day.AddHours(9), AppointmentStatus.Completed),
            CreateAppointment(secondClient, firstService, teacher, day.AddHours(11), AppointmentStatus.Burned),
            CreateAppointment(firstClient, secondService, teacher, day.AddHours(12), AppointmentStatus.Planned),
            CreateAppointment(firstClient, secondService, teacher, day.AddHours(13), AppointmentStatus.Cancelled),
            CreateAppointment(firstClient, secondService, teacher, day.AddHours(15), AppointmentStatus.Completed),
            CreateAppointment(firstClient, firstService, teacher, day.AddDays(1), AppointmentStatus.Completed)
        ], TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(teacher));

        var (response, payload) = await App.Client.GETAsync<
            GetAppointmentsAnalyticsEndpoint,
            GetAppointmentsAnalyticsRequest,
            GetAppointmentsAnalyticsResponse>(new GetAppointmentsAnalyticsRequest
            {
                Start = day,
                End = day,
                Timezone = "UTC"
            });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        payload.ShouldNotBeNull();
        payload.TotalAppointmentsCount.ShouldBe(5);
        payload.PlannedAppointmentsCount.ShouldBe(1);
        payload.CompletedAppointmentsCount.ShouldBe(2);
        payload.CancelledAppointmentsCount.ShouldBe(1);
        payload.BurnedAppointmentsCount.ShouldBe(1);
        payload.TotalRevenue.ShouldBe(450m);
        payload.AverageGapBetweenServicesHours.ShouldBe(1m);
        payload.DailyLoad.ShouldHaveSingleItem().Revenue.ShouldBe(450m);
        payload.Hours.Single(hour => hour.Hour == 11).BurnedAppointmentsCount.ShouldBe(1);
        payload.BurnedClients.ShouldHaveSingleItem().ClientId.ShouldBe(secondClient.Id);

        var teacherAnalytics = payload.Teachers.ShouldHaveSingleItem();
        teacherAnalytics.TeacherId.ShouldBe(teacher.Id);
        teacherAnalytics.AverageGapBetweenServicesHours.ShouldBe(5m);
        teacherAnalytics.TopServices.Select(service => service.ServiceName).ShouldBe(["Piano", "Vocal"]);
        teacherAnalytics.TopServices.Single(service => service.ServiceId == firstService.Id).Revenue.ShouldBe(250m);
        teacherAnalytics.TopServices.Single(service => service.ServiceId == secondService.Id).Revenue.ShouldBe(200m);
    }

    private static Appointment CreateAppointment(
        Client client,
        Service service,
        User provider,
        DateTime startDate,
        AppointmentStatus status) => new()
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            Provider = provider,
            StartDate = startDate,
            EndDate = startDate.AddHours(1),
            Status = status,
            IsDeleted = false
        };
}

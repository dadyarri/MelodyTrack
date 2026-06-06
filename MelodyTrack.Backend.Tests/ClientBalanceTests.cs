using System.Net;
using System.Net.Http.Headers;
using FastEndpoints;
using FastEndpoints.Testing;
using MelodyTrack.Backend.Api.Clients.Endpoints;
using MelodyTrack.Backend.Api.Clients.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Backend.Utils;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public class ClientBalanceTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task GetClientsWithNegativeBalance_UsesResolvedPricePerAppointment()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Anna", "Petrova", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Vocal lesson", TestContext.Current.CancellationToken);

        var firstPriceDate = new DateTime(2026, 05, 01, 0, 0, 0, DateTimeKind.Utc);
        var secondPriceDate = new DateTime(2026, 05, 20, 0, 0, 0, DateTimeKind.Utc);

        await db.ServicePriceHistory.AddRangeAsync(
            [
                new ServicePrice
                {
                    Id = Ulid.NewUlid(),
                    Service = service,
                    Price = 100m,
                    EffectiveDate = firstPriceDate
                },
                new ServicePrice
                {
                    Id = Ulid.NewUlid(),
                    Service = service,
                    Price = 130m,
                    EffectiveDate = secondPriceDate
                }
            ],
            TestContext.Current.CancellationToken);

        await db.Payments.AddAsync(new Payment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            Amount = 150m,
            Date = new DateTime(2026, 05, 25, 12, 0, 0, DateTimeKind.Utc),
            Description = "Partial payment"
        }, TestContext.Current.CancellationToken);

        await db.Appointments.AddRangeAsync(
            [
                new Appointment
                {
                    Id = Ulid.NewUlid(),
                    Client = client,
                    Service = service,
                    Provider = user,
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
                    Provider = user,
                    StartDate = new DateTime(2026, 05, 25, 10, 0, 0, DateTimeKind.Utc),
                    EndDate = new DateTime(2026, 05, 25, 11, 0, 0, DateTimeKind.Utc),
                    Status = AppointmentStatus.Completed,
                    IsDeleted = false
                }
            ],
            TestContext.Current.CancellationToken);

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var (rsp, res) = await App.Client.GETAsync<GetClientsWithNegativeBalanceEndpoint, EmptyRequest, GetClientsWithNegativeBalanceResponse>(
            EmptyRequest.Instance);

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.ShouldNotBeNull();
        res.Debtors.Count.ShouldBe(1);
        res.Debtors[0].Balance.ShouldBe(-80m);
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Payments.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public class PaymentEndpointTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task UpdatePayment_UpdatesEntity()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var originalClient = await TestDataFactory.CreateClientAsync(db, "Ivan", "Petrov", TestContext.Current.CancellationToken);
        var updatedClient = await TestDataFactory.CreateClientAsync(db, "Petr", "Sidorov", TestContext.Current.CancellationToken);
        var originalService = await TestDataFactory.CreateServiceAsync(db, "Piano", TestContext.Current.CancellationToken);
        var updatedService = await TestDataFactory.CreateServiceAsync(db, "Vocal", TestContext.Current.CancellationToken);

        var payment = new Payment
        {
            Id = Ulid.NewUlid(),
            Client = originalClient,
            Service = originalService,
            Amount = 1500m,
            Date = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc),
            Description = "Initial lesson"
        };

        await db.Payments.AddAsync(payment, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var response = await App.Client.PutAsJsonAsync(
            $"/payments/{payment.Id}",
            new UpdatePaymentRequest
            {
                Id = payment.Id,
                ClientId = updatedClient.Id,
                ServiceId = updatedService.Id,
                Amount = 2400m,
                Date = new DateTime(2026, 6, 2, 12, 30, 0, DateTimeKind.Utc),
                Description = "Updated lesson"
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var verificationScope = App.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updatedPayment = await verificationDb.Payments
            .Include(e => e.Client)
            .Include(e => e.Service)
            .SingleAsync(e => e.Id == payment.Id, TestContext.Current.CancellationToken);

        updatedPayment.Client.Id.ShouldBe(updatedClient.Id);
        updatedPayment.Service.ShouldNotBeNull();
        updatedPayment.Service.Id.ShouldBe(updatedService.Id);
        updatedPayment.Amount.ShouldBe(2400m);
        updatedPayment.Date.ShouldBe(new DateTime(2026, 6, 2, 12, 30, 0, DateTimeKind.Utc));
        updatedPayment.Description.ShouldBe("Updated lesson");
    }

    [Fact]
    public async Task UpdatePayment_ReturnsConflictWhenExpectedActivityIdIsStale()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Anna", "Smirnova", TestContext.Current.CancellationToken);
        var payment = new Payment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Amount = 1000m,
            Date = DateTime.UtcNow,
            Description = "Payment"
        };

        await db.Payments.AddAsync(payment, TestContext.Current.CancellationToken);

        var latestActivityId = Ulid.NewUlid();
        await db.AuditLogs.AddAsync(
            new AuditLog
            {
                Id = latestActivityId,
                CreatedAtUtc = DateTime.UtcNow,
                Category = "payments",
                Action = "payment_created",
                EntityType = "payment",
                EntityId = payment.Id.ToString(),
                Details = "Payment created"
            },
            TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var response = await App.Client.PutAsJsonAsync(
            $"/payments/{payment.Id}",
            new UpdatePaymentRequest
            {
                Id = payment.Id,
                ExpectedActivityId = Ulid.NewUlid(),
                ClientId = client.Id,
                Amount = 1200m,
                Date = payment.Date,
                Description = "Updated"
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var payload = await response.Content.ReadFromJsonAsync<StaleEntityConflictResponse>(cancellationToken: TestContext.Current.CancellationToken);
        payload.ShouldNotBeNull();
        payload.EntityType.ShouldBe("payment");
        payload.CurrentActivity.ShouldNotBeNull();
        payload.CurrentActivity.Id.ShouldBe(latestActivityId);

        var storedPayment = await db.Payments.SingleAsync(e => e.Id == payment.Id, TestContext.Current.CancellationToken);
        storedPayment.Amount.ShouldBe(1000m);
        storedPayment.Description.ShouldBe("Payment");
    }
}

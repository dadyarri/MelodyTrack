using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Payments.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.ErrorHandling;
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
    public async Task CreatePayment_ReplaysCompletedResponseForSameCallerKeyAndPayload()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Replay", "Client", TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var request = CreateRequest(client.Id, 1500m);
        var firstResponse = await SendCreatePaymentAsync(request, "payment-replay", TestContext.Current.CancellationToken);
        var secondResponse = await SendCreatePaymentAsync(request, "payment-replay", TestContext.Current.CancellationToken);

        firstResponse.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            await firstResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        secondResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var first = await firstResponse.Content.ReadFromJsonAsync<CreateEntityResponse>(
            cancellationToken: TestContext.Current.CancellationToken);
        var second = await secondResponse.Content.ReadFromJsonAsync<CreateEntityResponse>(
            cancellationToken: TestContext.Current.CancellationToken);
        first.ShouldNotBeNull();
        second.ShouldNotBeNull();
        second.Id.ShouldBe(first.Id);
        firstResponse.Headers.Location?.ToString().ShouldBe($"/payments/{first.Id}");
        secondResponse.Headers.Location?.ToString().ShouldBe($"/payments/{first.Id}");

        await using var verificationScope = App.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await verificationDb.Payments.CountAsync(
            payment => payment.Id == first.Id,
            TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task CreatePayment_CoalescesConcurrentRequestsWithoutPolling()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Concurrent", "Client", TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));
        var request = CreateRequest(client.Id, 1700m);

        var responses = await Task.WhenAll(
            SendCreatePaymentAsync(request, "concurrent-payment", TestContext.Current.CancellationToken),
            SendCreatePaymentAsync(request, "concurrent-payment", TestContext.Current.CancellationToken));

        responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.Created);
        var ids = new List<Ulid>();
        foreach (var response in responses)
        {
            var payload = await response.Content.ReadFromJsonAsync<CreateEntityResponse>(
                cancellationToken: TestContext.Current.CancellationToken);
            payload.ShouldNotBeNull();
            ids.Add(payload.Id);
        }

        ids.Distinct().Count().ShouldBe(1);
    }

    [Fact]
    public async Task CreatePayment_ReturnsConflictWhenSameCallerReusesKeyForDifferentPayload()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Mismatch", "Client", TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var firstResponse = await SendCreatePaymentAsync(
            CreateRequest(client.Id, 1500m),
            "payment-mismatch",
            TestContext.Current.CancellationToken);
        var secondResponse = await SendCreatePaymentAsync(
            CreateRequest(client.Id, 2500m),
            "payment-mismatch",
            TestContext.Current.CancellationToken);

        firstResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        secondResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        secondResponse.Content.Headers.ContentType?.MediaType.ShouldBe(ApiMediaTypes.ProblemJson);
        var problem = await secondResponse.Content.ReadFromJsonAsync<ApiProblemDetails>(TestContext.Current.CancellationToken);
        problem.ShouldNotBeNull();
        problem.Status.ShouldBe((int)HttpStatusCode.Conflict);
        problem.Type.ShouldBe(ApiProblemTypes.IdempotencyConflict);
        problem.Code.ShouldBe(ApiProblemCodes.IdempotencyConflict);
    }

    [Fact]
    public async Task CreatePayment_AllowsDifferentCallersToReuseKey()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var firstUser = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var secondUser = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Scoped", "Client", TestContext.Current.CancellationToken);
        var request = CreateRequest(client.Id, 1500m);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(firstUser));
        var firstResponse = await SendCreatePaymentAsync(request, "shared-payment-key", TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(secondUser));
        var secondResponse = await SendCreatePaymentAsync(request, "shared-payment-key", TestContext.Current.CancellationToken);

        firstResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        secondResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var first = await firstResponse.Content.ReadFromJsonAsync<CreateEntityResponse>(
            cancellationToken: TestContext.Current.CancellationToken);
        var second = await secondResponse.Content.ReadFromJsonAsync<CreateEntityResponse>(
            cancellationToken: TestContext.Current.CancellationToken);
        first.ShouldNotBeNull();
        second.ShouldNotBeNull();
        second.Id.ShouldNotBe(first.Id);
    }

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

        var response = await App.Client.PatchAsJsonAsync(
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

        var response = await App.Client.PatchAsJsonAsync(
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
        response.Content.Headers.ContentType?.MediaType.ShouldBe(ApiMediaTypes.ProblemJson);
        payload.Status.ShouldBe((int)HttpStatusCode.Conflict);
        payload.Type.ShouldBe(ApiProblemTypes.StaleEntity);
        payload.Code.ShouldBe("stale_entity");
        payload.TraceId.ShouldNotBeNullOrWhiteSpace();
        payload.EntityType.ShouldBe("payment");
        payload.CurrentActivity.ShouldNotBeNull();
        payload.CurrentActivity.Id.ShouldBe(latestActivityId);

        var storedPayment = await db.Payments.SingleAsync(e => e.Id == payment.Id, TestContext.Current.CancellationToken);
        storedPayment.Amount.ShouldBe(1000m);
        storedPayment.Description.ShouldBe("Payment");
    }

    [Fact]
    public async Task GetPayments_ReturnsForbiddenForRegularUser()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var response = await App.Client.GetAsync("/payments", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static CreatePaymentRequest CreateRequest(Ulid clientId, decimal amount) =>
        new()
        {
            ClientId = clientId,
            Amount = amount,
            Date = new DateTime(2026, 7, 24, 12, 0, 0, DateTimeKind.Utc),
            Description = "Idempotency test"
        };

    private async Task<HttpResponseMessage> SendCreatePaymentAsync(
        CreatePaymentRequest request,
        string idempotencyKey,
        CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/payments")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        return await App.Client.SendAsync(message, ct);
    }
}

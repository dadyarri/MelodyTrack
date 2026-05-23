using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MelodyTrack.Backend.Api.Services.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public class ServiceEndpointTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task LookupServices_ReturnsCurrentPriceFromPriceHistory()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Vocal lesson", TestContext.Current.CancellationToken);

        await db.ServicePriceHistory.AddAsync(
            new ServicePrice
            {
                Id = Ulid.NewUlid(),
                Service = service,
                Price = 3200m,
                EffectiveDate = DateTime.UtcNow
            },
            TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var response = await App.Client.GetAsync("/services/lookup", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<LookupServicesResponse>(cancellationToken: TestContext.Current.CancellationToken);
        body.ShouldNotBeNull();

        var returnedService = body.Services.Single(item => item.Id == service.Id);
        returnedService.Price.ShouldBe(3200m);
    }

    [Fact]
    public async Task UpdateService_UpdatesNameAndDescription()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Old name", TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var response = await App.Client.PutAsJsonAsync(
            $"/services/{service.Id}",
            new
            {
                id = service.Id,
                name = "New name",
                description = "Updated description"
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        db.ChangeTracker.Clear();

        var updated = await db.Services
            .AsNoTracking()
            .FirstAsync(item => item.Id == service.Id, TestContext.Current.CancellationToken);
        updated.Name.ShouldBe("New name");
        updated.Description.ShouldBe("Updated description");
    }

    [Fact]
    public async Task DeleteService_RemovesUnusedService()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Disposable", TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var response = await App.Client.DeleteAsync($"/services/{service.Id}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var exists = await db.Services.AnyAsync(item => item.Id == service.Id, TestContext.Current.CancellationToken);
        exists.ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteService_WhenServiceUsedByPayment_ReturnsValidationProblem()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Anna", "Petrova", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Protected", TestContext.Current.CancellationToken);

        await db.Payments.AddAsync(
            new Payment
            {
                Id = Ulid.NewUlid(),
                Client = client,
                Service = service,
                Amount = 2500m,
                Date = DateTime.UtcNow,
                Description = "Linked payment"
            },
            TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var response = await App.Client.DeleteAsync($"/services/{service.Id}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        payload.RootElement.GetProperty("detail").GetString().ShouldBe("Нельзя удалить услугу, которая уже используется в платежах или расписании.");

        var exists = await db.Services.AnyAsync(item => item.Id == service.Id, TestContext.Current.CancellationToken);
        exists.ShouldBeTrue();
    }
}

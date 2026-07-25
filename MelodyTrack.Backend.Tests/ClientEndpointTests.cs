using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public class ClientEndpointTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task UpdateClientVacations_WritesNamedBeforeAndAfterAuditContext()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Анна", "Иванова", TestContext.Current.CancellationToken);
        await db.ClientVacations.AddAsync(
            new ClientVacation
            {
                Id = Ulid.NewUlid(),
                ClientId = client.Id,
                Client = client,
                StartDate = new DateOnly(2026, 8, 1),
                EndDate = new DateOnly(2026, 8, 10)
            },
            TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));
        var response = await App.Client.PutAsJsonAsync(
            $"/clients/{client.Id}",
            new
            {
                firstName = client.FirstName,
                lastName = client.LastName,
                vacations = new[]
                {
                    new { startDate = "2026-09-02", endDate = "2026-09-12" }
                }
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        db.ChangeTracker.Clear();
        var auditLog = await db.AuditLogs
            .AsNoTracking()
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstAsync(item => item.EntityId == client.Id.ToString(), TestContext.Current.CancellationToken);
        auditLog.Action.ShouldBe("client_vacations_updated");
        auditLog.Details.ShouldBe(
            "Клиент: Иванова Анна; Периоды отсутствия: 2026-08-01–2026-08-10 → 2026-09-02–2026-09-12");
    }
}

using MelodyTrack.Backend.Data;
using MelodyTrack.Data.Initialization;
using MelodyTrack.Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public sealed class DatabaseInitializationTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task DevelopmentMode_IsRepeatableAndAppliesEachSeedVersionOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await App.RunInitializationAsync(InitializationMode.Development, cancellationToken);
        await App.RunInitializationAsync(InitializationMode.Development, cancellationToken);

        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var markerCount = await db.AuditLogs
            .AsNoTracking()
            .CountAsync(log => log.Action == "development_seed_v1", cancellationToken);
        var developmentUserCount = await db.Users
            .AsNoTracking()
            .CountAsync(user => user.Id == Ulid.Parse("01K00000000000000000000001"), cancellationToken);

        markerCount.ShouldBe(1);
        developmentUserCount.ShouldBe(1);
    }
}

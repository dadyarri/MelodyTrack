using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public sealed class RecordActivityServiceTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task GetLatestActivities_ManyHistoryRows_ReturnsOneLatestActivityPerEntity()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var expectedActivityIds = new Dictionary<string, Ulid>();
        var firstTimestamp = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var entityIndex = 0; entityIndex < 12; entityIndex++)
        {
            var entityId = Ulid.NewUlid().ToString();
            for (var activityIndex = 0; activityIndex < 10; activityIndex++)
            {
                var activityId = Ulid.NewUlid();
                await db.AuditLogs.AddAsync(new AuditLog
                {
                    Id = activityId,
                    CreatedAtUtc = firstTimestamp.AddMinutes(activityIndex),
                    Category = "services",
                    Action = "service_updated",
                    EntityType = "service",
                    EntityId = entityId,
                    Details = $"Activity {activityIndex}"
                }, TestContext.Current.CancellationToken);

                if (activityIndex == 9)
                {
                    expectedActivityIds.Add(entityId, activityId);
                }
            }
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var activityService = scope.ServiceProvider.GetRequiredService<IRecordActivityService>();
        App.DatabaseCommands.Reset();

        var activities = await activityService.GetLatestActivitiesAsync(
            "service",
            expectedActivityIds.Keys.ToArray(),
            TestContext.Current.CancellationToken);

        activities.Count.ShouldBe(expectedActivityIds.Count);
        foreach (var expected in expectedActivityIds)
        {
            activities[expected.Key].Id.ShouldBe(expected.Value);
        }

        App.DatabaseCommands.Count.ShouldBe(1);
    }
}

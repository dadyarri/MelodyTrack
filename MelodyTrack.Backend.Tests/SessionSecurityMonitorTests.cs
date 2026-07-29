using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public class SessionSecurityMonitorTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    public async Task AuditFanOutIfUnusual_UsesActiveSessionThreshold(int activeSessions, bool expectsAudit)
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        for (var index = 0; index < activeSessions; index++)
        {
            await db.Sessions.AddAsync(new Session
            {
                Id = Ulid.NewUlid(), User = user, DeviceInfo = $"test-{index}",
                RefreshToken = $"hash-{index}", ValidUntil = DateTime.UtcNow.AddHours(1), WasRevoked = false
            }, TestContext.Current.CancellationToken);
        }
        await db.Sessions.AddAsync(new Session
        {
            Id = Ulid.NewUlid(), User = user, DeviceInfo = "revoked",
            RefreshToken = "revoked-hash", ValidUntil = DateTime.UtcNow.AddHours(1), WasRevoked = true
        }, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await scope.ServiceProvider.GetRequiredService<SessionSecurityMonitor>()
            .AuditFanOutIfUnusualAsync(user, TestContext.Current.CancellationToken);

        var auditExists = await db.AuditLogs.AnyAsync(
            item => item.Action == "unusual_session_fanout" && item.EntityId == user.Id.ToString(),
            TestContext.Current.CancellationToken);
        auditExists.ShouldBe(expectsAudit);
    }
}

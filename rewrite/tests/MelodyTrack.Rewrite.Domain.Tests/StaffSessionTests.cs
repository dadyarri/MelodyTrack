using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class StaffSessionTests
{
    [Fact]
    public void Refresh_ValidSession_ExtendsFromRefreshTime()
    {
        var staff = MakeStaff();
        var session = Ok(StaffSessionOperations.Start(new(Guid(100)), staff, Utc(1, 0)));

        var updated = Ok(session.Refresh(staff, Utc(5, 0)));

        Assert.Equal(Utc(19, 0), updated.RefreshExpiresAt);
    }

    [Fact]
    public void Refresh_ExpiredSession_DoesNotRenew()
    {
        var staff = MakeStaff();
        var session = Ok(StaffSessionOperations.Start(new(Guid(100)), staff, Utc(1, 0)));

        AssertError(new ModelError.Forbidden(), session.Refresh(staff, Utc(15, 0)));
    }

    [Fact]
    public void CanUse_BlockedStaff_RejectsImmediately()
    {
        var staff = MakeStaff();
        var session = Ok(StaffSessionOperations.Start(new(Guid(100)), staff, Utc(1, 0)));

        Assert.False(session.CanUse(staff.Block(), Utc(1, 1)));
    }
}

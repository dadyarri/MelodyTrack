using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class SchoolClosureTests
{
    private static SchoolClosure Closure() => Ok(SchoolClosureRules.Create(new(Guid(51)), Period(Utc(5, 15), Utc(7, 15)), "Ремонт"));
    [Fact]
    public void Create_EmptyReason_RejectsClosure() => AssertError(new CatalogueError.Required("reason"), SchoolClosureRules.Create(new(Guid(51)), Period(Utc(5, 15), Utc(6, 15)), " "));
    [Fact]
    public void ChangePeriod_ActiveClosure_PreservesIdentityAndReason()
    {
        var closure = Closure();
        var period = Period(Utc(5, 15), Utc(6, 15));
        var changed = Ok(closure.ChangePeriod(period));
        Assert.Equal(closure.Id, changed.Id);
        Assert.Equal("Ремонт", changed.Reason);
        Assert.Equal(period, changed.Period);
    }
    [Fact]
    public void ChangePeriod_DeletedClosure_RejectsChange()
    {
        var deleted = Closure().Remove();
        AssertError(new SchoolClosureError.Deleted(), deleted.ChangePeriod(Period(Utc(5, 15), Utc(6, 15))));
        Assert.True(deleted.IsDeleted);
    }

    [Fact]
    public void ChangeReason_ActiveClosure_TrimsReasonAndPreservesPeriod()
    {
        var closure = Closure();

        var changed = Ok(closure.ChangeReason("  Праздник  "));

        Assert.Equal("Праздник", changed.Reason);
        Assert.Equal(closure.Id, changed.Id);
        Assert.Equal(closure.Period, changed.Period);
        Assert.Equal("Ремонт", closure.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ChangeReason_EmptyReason_RejectsChange(string? reason)
    {
        AssertError(new CatalogueError.Required("reason"), Closure().ChangeReason(reason));
    }

    [Fact]
    public void ChangeReason_DeletedClosure_RejectsChange()
    {
        AssertError(new SchoolClosureError.Deleted(), Closure().Remove().ChangeReason("Праздник"));
    }
}

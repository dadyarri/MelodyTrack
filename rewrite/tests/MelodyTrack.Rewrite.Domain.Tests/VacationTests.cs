using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class VacationTests
{
    [Fact]
    public void ChangePeriod_ActiveVacation_PreservesIdentityAndExactMinutes()
    {
        var vacation = MakeVacation(50, new VacationSubject.Client(TestData.ClientId), Utc(5, 15), Utc(6, 15));
        var period = Period(Utc(5, 15).AddMinutes(30), Utc(6, 14));
        var changed = Ok(vacation.ChangePeriod(period));
        Assert.Equal(vacation.Id, changed.Id);
        Assert.Equal(period, changed.Period);
    }
    [Fact]
    public void ChangePeriod_DeletedVacation_RejectsChangeAndPreservesHistory()
    {
        var deleted = MakeVacation(50, new VacationSubject.Client(TestData.ClientId), Utc(5, 15), Utc(6, 15)).Remove();
        AssertError(new VacationError.VacationDeleted(), deleted.ChangePeriod(Period(Utc(5, 15), Utc(7, 15))));
        Assert.Equal(new VacationSubject.Client(TestData.ClientId), deleted.Subject);
    }
}

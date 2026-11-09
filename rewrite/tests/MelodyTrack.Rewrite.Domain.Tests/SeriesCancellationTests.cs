using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class SeriesCancellationTests
{
    [Fact]
    public void ApplyVacation_ExplicitlyCancelledMeeting_DoesNotAppendCompensation()
    {
        var series = MakeSeries(new SeriesEnd.AfterAppointments(Ok(AppointmentCount.Create(8))));
        var original = Ok(AppointmentRules.CreateFromSeries(new(Guid(110)), series, Number(5), Utc(12, 15)));
        var cancelled = Ok(original.Cancel(StaffRole.Superuser, Now));
        var vacation = MakeVacation(111, new VacationSubject.Client(TestData.ClientId), Utc(12, 14), Utc(13, 15));

        var appointments = Ok(AbsenceEffects.ApplyVacation(vacation, [cancelled]));

        Assert.Equal(cancelled, Assert.Single(appointments));
        Assert.Equal(1, SeriesEffects.CountedAppointments(series, appointments));
        Assert.Null(appointments[0].Series!.Compensates);
    }

    [Fact]
    public void CountedAppointments_RepeatedPosition_IsNotCountedTwice()
    {
        var appointment = SeriesAppointment(110, 1, Utc(5, 15));

        Assert.Equal(1, SeriesEffects.CountedAppointments(MakeSeries(), [appointment, appointment]));
    }
}

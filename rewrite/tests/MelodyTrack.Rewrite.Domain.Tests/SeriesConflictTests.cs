using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class SeriesConflictTests
{
    private static (SeriesConflict Conflict, Appointment Appointment) Setup()
    {
        var series = MakeSeries();
        var step = Assert.IsType<SeriesStep>(Ok(series.NextCandidate(0)));
        return (SeriesConflictRules.Create(series, step.Candidate), Ok(AppointmentRules.CreateFromSeries(new(Guid(90)), series, step.Candidate.SequenceNumber, step.Candidate.StartsAt)));
    }
    [Fact]
    public void Create_Conflict_CreatesProblemWithoutAppointmentOrResolution()
    {
        var (conflict, _) = Setup();
        Assert.IsType<ConflictResolution.Pending>(conflict.Resolution);
        Assert.Equal(1L, conflict.SequenceNumber.Value);
    }
    [Fact]
    public void ConfirmCreated_RemainingConflict_LeavesProblemPending()
    {
        var (conflict, appointment) = Setup();
        AssertError(new SeriesConflictError.StillBlocked(), conflict.ConfirmCreated(StaffRole.Administrator, [new AvailabilityConflict.OutsideWorkingHours()], appointment));
        Assert.IsType<ConflictResolution.Pending>(conflict.Resolution);
    }
    [Fact]
    public void ConfirmCreated_ExplicitConfirmation_RecordsAppointmentIdIdempotently()
    {
        var (conflict, appointment) = Setup();
        var created = Ok(conflict.ConfirmCreated(StaffRole.Administrator, [], appointment));
        var repeated = Ok(created.ConfirmCreated(StaffRole.Administrator, [], appointment));
        Assert.Equal(new ConflictResolution.Created(appointment.Id), repeated.Resolution);
        Assert.Equal(created, repeated);
    }
    [Fact]
    public void ConfirmCreated_WrongSeriesPosition_RejectsAppointment()
    {
        var (conflict, _) = Setup();
        AssertError(new SeriesConflictError.WrongAppointment(), conflict.ConfirmCreated(StaffRole.Administrator, [], SeriesAppointment(91, 2, Utc(8, 16))));
    }

    [Fact]
    public void ConfirmCreated_DifferentAppointmentForResolvedPosition_RejectsReplacement()
    {
        var (conflict, appointment) = Setup();
        var created = Ok(conflict.ConfirmCreated(StaffRole.Administrator, [], appointment));
        var replacement = SeriesAppointment(92, conflict.SequenceNumber.Value, conflict.DesiredStart);

        var result = created.ConfirmCreated(StaffRole.Administrator, [], replacement);

        AssertError(new SeriesConflictError.AlreadyResolved(), result);
        Assert.Equal(new ConflictResolution.Created(appointment.Id), created.Resolution);
    }
    [Fact]
    public void Skip_ExplicitChoice_PreservesOriginalPositionAndPreventsLaterCreation()
    {
        var (conflict, appointment) = Setup();
        var skipped = Ok(conflict.Skip(StaffRole.Administrator, "Клиент согласовал"));
        var resolution = Assert.IsType<ConflictResolution.Skipped>(skipped.Resolution);
        Assert.Equal(conflict.SequenceNumber, resolution.Skip.SequenceNumber);
        Assert.Equal(conflict.DesiredStart, resolution.Skip.StartsAt);
        Assert.Equal("Клиент согласовал", resolution.Skip.Reason);
        AssertError(new SeriesConflictError.AlreadyResolved(), skipped.ConfirmCreated(StaffRole.Administrator, [], appointment));
    }
    [Fact]
    public void Outstanding_RepeatedProblems_DeduplicatesAndGroupsBySeries()
    {
        var (first, _) = Setup();
        var series = MakeSeries();
        var advanced = Assert.IsType<SeriesStep>(Ok(series.NextCandidate(0))).Series;
        var candidate = Assert.IsType<SeriesStep>(Ok(advanced.NextCandidate(1))).Candidate;
        var second = SeriesConflictRules.Create(series, candidate);
        var group = Assert.Single(SeriesConflictRules.Outstanding([first, first, second]));
        Assert.Equal(2, group.Value.Length);
    }
    [Fact]
    public void Skip_Teacher_RejectsManagementAction()
    {
        var (conflict, _) = Setup();
        AssertError(new SeriesConflictError.Forbidden(), conflict.Skip(StaffRole.Teacher, null));
    }
}

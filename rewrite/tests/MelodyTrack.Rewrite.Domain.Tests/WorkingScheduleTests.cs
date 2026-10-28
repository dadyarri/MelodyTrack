
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class WorkingScheduleTests
{
    private static WeeklyWorkingSlot[] Hours => [new(Weekday.Monday, Window(17, 21))];
    private static WorkingSchedule Schedule(IEnumerable<WeeklyWorkingSlot>? hours = null, IEnumerable<WeeklyWorkingSlot>? breaks = null, IEnumerable<DatedBreak>? dated = null) =>
        Ok(WorkingSchedule.Create(new(Guid(60)), TestData.StaffId, Date(5), hours ?? Hours, breaks ?? [], dated ?? []));
    private static WorkingSchedule Approve(WorkingSchedule schedule) => Ok(Ok(schedule.Submit()).Approve(StaffRole.Superuser, false, false));
    [Fact]
    public void PeriodsOn_DraftSchedule_DoesNotApplyUnapprovedHours()
    {
        var periods = Ok(Schedule().PeriodsOn(Date(5)));
        Assert.Empty(periods.Hours);
        Assert.Empty(periods.Breaks);
    }
    [Fact]
    public void Approve_Administrator_RejectsApproval() => AssertError(new WorkingScheduleError.Forbidden(), Ok(Schedule().Submit()).Approve(StaffRole.Administrator, false, false));
    [Fact]
    public void Approve_UnresolvedAppointments_RequiresTheirTransfer() => AssertError(new WorkingScheduleError.ConflictingAppointments(), Ok(Schedule().Submit()).Approve(StaffRole.Superuser, true, false));
    [Fact]
    public void Approve_UnresolvedSeries_RequiresCorrectedFuturePlan() => AssertError(new WorkingScheduleError.ConflictingSeries(), Ok(Schedule().Submit()).Approve(StaffRole.Superuser, false, true));
    [Fact]
    public void PeriodsOn_BeforeEffectiveDate_DoesNotApplyFutureSchedule()
    {
        var periods = Ok(Approve(Schedule()).PeriodsOn(Date(4)));
        Assert.Empty(periods.Hours);
        Assert.Empty(periods.Breaks);
    }
    [Fact]
    public void PeriodsOn_ApprovedHoursAndBothBreakKinds_ProducesUtcPeriods()
    {
        var schedule = Approve(Schedule(breaks: [new(Weekday.Monday, Window(18, 19))], dated: [new(Date(5), Window(20, 21))]));
        var periods = Ok(schedule.PeriodsOn(Date(5)));
        Assert.Equal(Period(Utc(5, 14), Utc(5, 18)), Assert.Single(periods.Hours));
        Assert.Equal(2, periods.Breaks.Length);
        Assert.Contains(Period(Utc(5, 15), Utc(5, 16)), periods.Breaks);
        Assert.Contains(Period(Utc(5, 17), Utc(5, 18)), periods.Breaks);
    }
    [Fact]
    public void PeriodsOn_AfterOvernightShift_IncludesPreviousDaysWindow()
    {
        var periods = Ok(Approve(Schedule([new(Weekday.Monday, Window(23, 2))])).PeriodsOn(Date(6)));
        Assert.True(Period(Utc(5, 22), Utc(5, 23)).IsCoveredBy(periods.Hours));
    }
    [Fact]
    public void Revise_ApprovedSchedule_RequiresNewApprovalInsteadOfMutation() => AssertError(new WorkingScheduleError.InvalidState(), Approve(Schedule()).Revise(Date(6), [], [], []));
    [Fact]
    public void Reject_SubmittedSchedule_AllowsRevisionAndResubmission()
    {
        var rejected = Ok(Ok(Schedule().Submit()).Reject(StaffRole.Superuser));
        var revised = Ok(Ok(rejected.Revise(Date(12), Hours, [], [])).Submit());
        Assert.Equal(Date(12), revised.EffectiveFrom);
        Assert.Equal(WorkingScheduleStatus.Submitted, revised.Status);
    }
}

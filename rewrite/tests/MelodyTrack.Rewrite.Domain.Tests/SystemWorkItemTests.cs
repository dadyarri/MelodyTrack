using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class SystemWorkItemTests
{
    private static TaskTemplate Template() => Ok(TaskTemplateRules.Create(new(Guid(200)), "Проверить результаты", "Проверить занятия", null));

    [Fact]
    public void EnsureLessonReminder_RepeatedPass_ReusesTaskAndPreservesManualPostponement()
    {
        var appointment = MakeAppointment(201, Utc(10, 15));
        var task = Assert.IsType<WorkItem>(Ok(SystemWorkItemRules.EnsureLessonReminder(new(Guid(202)), Template(), appointment, MakeService(), TimeSpan.FromHours(24), [], Utc(1, 0))));
        var postponed = Ok(WorkItemRules.Postpone(task, Utc(8, 15), Utc(1, 0)));

        var updated = Assert.IsType<WorkItem>(Ok(SystemWorkItemRules.EnsureLessonReminder(new(Guid(203)), Template(), appointment, MakeService(), TimeSpan.FromHours(48), [postponed], Utc(1, 0))));

        Assert.Equal(task.Id, updated.Id);
        Assert.Equal(Utc(8, 15), updated.EffectiveDueAt);
        Assert.Equal(ReminderKind.Appointment, Assert.IsType<WorkItemSource.LessonReminder>(updated.Source).Kind);
    }

    [Fact]
    public void EnsureBirthday_ClientWithoutContacts_CreatesOneTaskAtConfiguredMorning()
    {
        var client = Ok(ClientRules.Create(TestData.ClientId, Profile with { Birthdate = new DateOnly(2000, 1, 3) }, [], Now));

        var task = Assert.IsType<WorkItem>(Ok(SystemWorkItemRules.EnsureBirthday(new(Guid(201)), Template(), client, Date(3), [], Utc(3, 6), new(9, 0))));

        Assert.Equal(Utc(3, 6), task.DueAt);
        Assert.Equal(task, Ok(SystemWorkItemRules.EnsureBirthday(new(Guid(202)), Template(), client, Date(3), [task], Utc(3, 6), new(9, 0))));
    }

    [Fact]
    public void EnsureTeacherSchedule_MoscowDay_ContainsOnlyOwnNonCancelledAppointments()
    {
        var own = MakeAppointment(201, Utc(1, 20));
        var other = Ok(AppointmentRules.Create(new(Guid(202)), TestData.ClientId, TestData.ServiceId, new StaffId(Guid(250)), null, Utc(1, 10), Duration(60)));
        var nextDay = MakeAppointment(203, Utc(1, 21));

        var task = Assert.IsType<WorkItem>(Ok(SystemWorkItemRules.EnsureTeacherSchedule(new(Guid(204)), Template(), MakeStaff(), Date(1), [own, other, nextDay], [], Utc(1, 5), new(8, 0))));

        Assert.Equal(own.Id, Assert.Single(Assert.IsType<WorkItemSource.TeacherSchedule>(task.Source).Appointments));
        Assert.Equal(Utc(1, 5), task.DueAt);
    }

    [Fact]
    public void EnsureMissingResults_BeforeNextMorning_DoesNotCreateTask()
    {
        var appointment = MakeAppointment(201, Utc(1, 20));

        var result = Ok(SystemWorkItemRules.EnsureMissingResults(new(Guid(202)), Template(), Date(1), [appointment], [],
            Utc(2, 5).AddTicks(-1), SystemWorkItemRules.DefaultMorningTime));

        Assert.Null(result);
    }

    [Fact]
    public void EnsureMissingResults_MoscowDay_GroupsOnlyUnresolvedMeetingsAndReusesTask()
    {
        var first = MakeAppointment(201, Utc(1, 0));
        var second = MakeAppointment(202, new(2025, 12, 31, 22, 0, 0, TimeSpan.Zero));
        var nextDay = MakeAppointment(203, Utc(1, 21));
        var completed = Ok(MakeAppointment(204, Utc(1, 10)).Complete(Utc(2, 5)));
        Appointment[] appointments = [first, second, nextDay, completed];

        var result = Ok(SystemWorkItemRules.EnsureMissingResults(new(Guid(205)), Template(), Date(1), appointments, [],
            Utc(2, 5), SystemWorkItemRules.DefaultMorningTime));

        var task = Assert.IsType<WorkItem>(result);
        var source = Assert.IsType<WorkItemSource.MissingResults>(task.Source);
        Assert.Equal(Utc(2, 5), task.DueAt);
        Assert.Equal(2, source.Appointments.Count);
        Assert.Contains(first.Id, source.Appointments);
        Assert.Contains(second.Id, source.Appointments);
        Assert.Equal(task, Ok(SystemWorkItemRules.EnsureMissingResults(new(Guid(206)), Template(), Date(1), appointments, [task],
            Utc(2, 6), SystemWorkItemRules.DefaultMorningTime)));
    }

    [Fact]
    public void RefreshMissingResults_LastResultMarked_CompletesTaskWithoutChangingMeetings()
    {
        var first = MakeAppointment(201, Utc(1, 10));
        var second = MakeAppointment(202, Utc(1, 11));
        var created = Assert.IsType<WorkItem>(Ok(SystemWorkItemRules.EnsureMissingResults(new(Guid(203)), Template(), Date(1), [first, second], [], Utc(2, 5), new(8, 0))));
        var completedFirst = Ok(first.Complete(Utc(2, 5)));

        var partial = Ok(created.RefreshMissingResults([completedFirst, second]));
        var finished = Ok(partial.RefreshMissingResults([completedFirst, Ok(second.Burn(Utc(2, 5)))]));

        Assert.Equal(WorkItemStatus.Open, partial.Status);
        Assert.Equal(WorkItemStatus.Completed, finished.Status);
        Assert.Equal(AppointmentStatus.Scheduled, first.Status);
        Assert.Equal(AppointmentStatus.Scheduled, second.Status);
    }

    [Fact]
    public void RefreshMissingResults_MissingIncludedMeeting_ExposesProblem()
    {
        var appointment = MakeAppointment(201, Utc(1, 10));
        var created = Assert.IsType<WorkItem>(Ok(SystemWorkItemRules.EnsureMissingResults(new(Guid(203)), Template(), Date(1), [appointment], [], Utc(2, 5), new(8, 0))));

        var refreshed = Ok(created.RefreshMissingResults([]));

        Assert.Equal(WorkItemStatus.Open, refreshed.Status);
        Assert.Contains(WorkItemProblem.SourceUnavailable, refreshed.Problems);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RefreshMissingResults_ResultClearedAfterTaskCreation_KeepsTaskPendingAndIncludesMeeting(bool useEnsure)
    {
        var first = MakeAppointment(201, Utc(1, 10));
        var second = Ok(MakeAppointment(202, Utc(1, 11)).Complete(Utc(2, 5)));
        var created = Assert.IsType<WorkItem>(Ok(SystemWorkItemRules.EnsureMissingResults(new(Guid(203)), Template(), Date(1), [first, second], [], Utc(2, 5), new(8, 0))));
        var postponed = Ok(WorkItemRules.Postpone(created, Utc(4, 10), Utc(2, 5)));
        Appointment[] current = [Ok(first.Complete(Utc(2, 5))), Ok(second.ClearResult())];

        var refreshed = useEnsure
            ? Assert.IsType<WorkItem>(Ok(SystemWorkItemRules.EnsureMissingResults(new(Guid(204)), Template(), Date(1), current, [postponed], Utc(2, 6), new(8, 0))))
            : Ok(postponed.RefreshMissingResults(current));

        Assert.Equal(created.Id, refreshed.Id);
        Assert.Equal(WorkItemStatus.Postponed, refreshed.Status);
        Assert.Equal(postponed.PostponedUntil, refreshed.PostponedUntil);
        Assert.Contains(second.Id, Assert.IsType<WorkItemSource.MissingResults>(refreshed.Source).Appointments);
    }

    [Fact]
    public void RefreshMissingResults_DeletedAndMovedMeetings_ExcludesThemFromCurrentPendingDay()
    {
        var moved = MakeAppointment(201, Utc(1, 10));
        var deleted = MakeAppointment(202, Utc(1, 11));
        var remaining = MakeAppointment(203, Utc(1, 12));
        var created = Assert.IsType<WorkItem>(Ok(SystemWorkItemRules.EnsureMissingResults(new(Guid(204)), Template(), Date(1), [moved, deleted, remaining], [], Utc(2, 5), new(8, 0))));
        var movedToNextDay = MakeAppointment(201, Utc(1, 21));
        var removed = Ok(deleted.Delete());

        var refreshed = Ok(created.RefreshMissingResults([movedToNextDay, removed, remaining]));
        var finished = Ok(refreshed.RefreshMissingResults([movedToNextDay, removed, Ok(remaining.Complete(Utc(2, 5)))]));

        Assert.Equal(WorkItemStatus.Open, refreshed.Status);
        Assert.Equal(remaining.Id, Assert.Single(Assert.IsType<WorkItemSource.MissingResults>(refreshed.Source).Appointments));
        Assert.Equal(WorkItemStatus.Completed, finished.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RefreshMissingResults_ClosedTaskHasNewMissingResult_RequiresDecisionWithoutReopening(bool useEnsure)
    {
        var first = MakeAppointment(201, Utc(1, 10));
        var second = Ok(MakeAppointment(202, Utc(1, 11)).Complete(Utc(2, 5)));
        var created = Assert.IsType<WorkItem>(Ok(SystemWorkItemRules.EnsureMissingResults(new(Guid(203)), Template(), Date(1), [first, second], [], Utc(2, 5), new(8, 0))));
        var completedFirst = Ok(first.Complete(Utc(2, 5)));
        var closed = Ok(created.RefreshMissingResults([completedFirst, second]));
        Appointment[] current = [completedFirst, Ok(second.ClearResult())];

        if (useEnsure)
            AssertError(new ModelError.DecisionRequired(), SystemWorkItemRules.EnsureMissingResults(new(Guid(204)), Template(), Date(1), current, [closed], Utc(2, 6), new(8, 0)));
        else
            AssertError(new ModelError.DecisionRequired(), closed.RefreshMissingResults(current));

        Assert.Equal(WorkItemStatus.Completed, closed.Status);
    }

    [Fact]
    public void EnsureVacationFollowUp_ChangedVacation_PreservesIdentityAndUpdatesDueDateAndComment()
    {
        var vacation = Ok(VacationRules.Create(new(Guid(210)), new VacationSubject.Client(TestData.ClientId), Period(Utc(1, 0), Utc(3, 0)), null, "Подтвердить расписание"));
        var task = Ok(SystemWorkItemRules.EnsureVacationFollowUp(new(Guid(211)), Template(), vacation, []));
        var postponed = Ok(WorkItemRules.Postpone(task, Utc(8, 0), Utc(2, 0)));
        var changed = Ok(vacation.ChangePeriod(Period(Utc(1, 0), Utc(5, 0))));

        var updated = Ok(SystemWorkItemRules.EnsureVacationFollowUp(new(Guid(212)), Template(), changed, [postponed]));

        Assert.Equal(task.Id, updated.Id);
        Assert.Equal(Utc(5, 0), updated.DueAt);
        Assert.Equal(Utc(8, 0), updated.EffectiveDueAt);
        Assert.Equal(vacation.Comment, updated.SubjectComment);
    }

    [Fact]
    public void RefreshVacationFollowUp_DeletedVacation_CancelsPendingAndPreservesCompletedHistory()
    {
        var vacation = Ok(VacationRules.Create(new(Guid(210)), new VacationSubject.Client(TestData.ClientId), Period(Utc(1, 0), Utc(3, 0)), null, "Связаться после отпуска"));
        var task = Ok(SystemWorkItemRules.EnsureVacationFollowUp(new(Guid(211)), Template(), vacation, []));
        var completed = Ok(WorkItemRules.Complete(task));
        var deleted = vacation.Remove();

        var cancelled = task.RefreshVacationFollowUp(deleted);

        Assert.Equal(WorkItemStatus.Cancelled, cancelled.Status);
        Assert.Equal("vacation.deleted", cancelled.CancellationReason);
        Assert.Equal(completed, completed.RefreshVacationFollowUp(deleted));
    }
}

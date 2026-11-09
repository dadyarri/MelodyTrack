using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class WorkItemTests
{
    private static Actor Admin => new Actor.Staff(TestData.StaffId, StaffRole.Administrator);
    private static Actor Super => new Actor.Staff(TestData.StaffId, StaffRole.Superuser);
    private static TaskTemplate Template() => Ok(TaskTemplateOperations.Create(new(Guid(100)), "Напомнить", "Связаться", null, Super));

    [Fact]
    public void RecalculateReminder_MovedLesson_PreservesManualPostponement()
    {
        var appointment = MakeAppointment(101, Utc(10, 15));
        var item = Ok(WorkItemRules.FromTemplate(new(Guid(102)), Template(), new WorkItemSource.LessonReminder(appointment.Id, ReminderKind.Homework), Utc(4, 15)));
        item = Ok(item.Postpone(Utc(6, 15), Utc(1, 15), Admin));

        var updated = Ok(item.RecalculateReminder(appointment, TimeSpan.FromHours(72), Utc(2, 15)));

        Assert.Equal(Utc(7, 15), updated.DueAt);
        Assert.Equal(Utc(6, 15), updated.EffectiveDueAt);
    }

    [Fact]
    public void RefreshDebt_RepaidBalance_CancelsPostponedReminder()
    {
        var item = Ok(WorkItemRules.FromTemplate(new(Guid(102)), Template(), new WorkItemSource.Debt(TestData.ClientId, TestData.ServiceId), Utc(4, 15)));
        item = Ok(item.Postpone(Utc(6, 15), Utc(1, 15), Admin));

        var updated = item.RefreshDebt(TestData.ClientId, TestData.ServiceId, 0);

        Assert.Equal(WorkItemStatus.Cancelled, updated.Status);
        Assert.Equal("debt.repaid", updated.CancellationReason);
    }

    [Fact]
    public void Reopen_CompletedSystemItem_ReturnsToWork()
    {
        var item = Ok(WorkItemRules.FromTemplate(new(Guid(102)), Template(), new WorkItemSource.Debt(TestData.ClientId, TestData.ServiceId), Utc(4, 15)));

        var current = new WorkItemReopenContext { ServiceBalances = System.Collections.Immutable.ImmutableDictionary<(ClientId, ServiceId), decimal>.Empty.Add((TestData.ClientId, TestData.ServiceId), -100) };
        var reopened = Ok(Ok(item.Complete(Admin)).Reopen(Admin, [], current));

        Assert.Equal(WorkItemStatus.Open, reopened.Status);
        Assert.False(reopened.IsProblematic);
    }

    [Fact]
    public void Reopen_NextCalendarTaskPending_ReturnsProblemWithoutCancellingNextTask()
    {
        var rule = Ok(CalendarTaskRules.Create(new(Guid(110)), Template().Id, Date(1), null, new(8, 0), new CalendarTaskPattern.Daily()));
        var previous = Ok(WorkItemRules.FromTemplate(new(Guid(111)), Template(), new WorkItemSource.Calendar(rule.Id, Utc(2, 5)), Utc(2, 5)));
        var next = Ok(WorkItemRules.FromTemplate(new(Guid(112)), Template(), new WorkItemSource.Calendar(rule.Id, Utc(3, 5)), Utc(3, 5)));

        var reopened = Ok(Ok(previous.Complete(Admin)).Reopen(Admin, [next], new() { CalendarRules = [rule] }));

        Assert.Equal(WorkItemStatus.Open, reopened.Status);
        Assert.Contains(WorkItemProblem.AnotherCalendarTaskPending, reopened.Problems);
        Assert.Equal(WorkItemStatus.Open, next.Status);
        Assert.Equal(reopened, reopened.CancelAutomatically("rule.disabled"));
    }

    [Fact]
    public void Reopen_RepaidDebt_RemainsProblematicAfterRefresh()
    {
        var item = Ok(WorkItemRules.FromTemplate(new(Guid(102)), Template(), new WorkItemSource.Debt(TestData.ClientId, TestData.ServiceId), Utc(4, 15)));
        item = item.RefreshDebt(TestData.ClientId, TestData.ServiceId, 0);
        var current = new WorkItemReopenContext { ServiceBalances = System.Collections.Immutable.ImmutableDictionary<(ClientId, ServiceId), decimal>.Empty.Add((TestData.ClientId, TestData.ServiceId), 0) };

        var reopened = Ok(item.Reopen(Admin, [], current)).RefreshDebt(TestData.ClientId, TestData.ServiceId, 0);

        Assert.Equal(WorkItemStatus.Open, reopened.Status);
        Assert.Contains(WorkItemProblem.SourceNoLongerRelevant, reopened.Problems);
        Assert.Null(reopened.CancellationReason);
    }

    [Fact]
    public void Reopen_MissingSystemSource_ReturnsExplicitProblem()
    {
        var item = Ok(WorkItemRules.FromTemplate(new(Guid(102)), Template(), new WorkItemSource.Debt(TestData.ClientId, TestData.ServiceId), Utc(4, 15)));

        var reopened = Ok(Ok(item.Complete(Admin)).Reopen(Admin, [], new()));

        Assert.Equal(WorkItemStatus.Open, reopened.Status);
        Assert.Contains(WorkItemProblem.SourceUnavailable, reopened.Problems);
    }

    [Fact]
    public void RefreshDebt_OtherServiceHasPositiveBalance_PreservesDebtReminder()
    {
        var item = Ok(WorkItemRules.FromTemplate(new(Guid(102)), Template(), new WorkItemSource.Debt(TestData.ClientId, TestData.ServiceId), Utc(4, 15)));

        var refreshed = item.RefreshDebt(TestData.ClientId, new ServiceId(Guid(150)), 2000);

        Assert.Equal(item, refreshed);
        Assert.Equal(WorkItemStatus.Open, refreshed.Status);
    }
}

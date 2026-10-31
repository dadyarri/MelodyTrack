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
        var item = Ok(WorkItemRules.FromTemplate(new(Guid(102)), Template(), new WorkItemSource.Debt(TestData.ClientId), Utc(4, 15)));
        item = Ok(item.Postpone(Utc(6, 15), Utc(1, 15), Admin));

        var updated = item.RefreshDebt(0);

        Assert.Equal(WorkItemStatus.Cancelled, updated.Status);
        Assert.Equal("debt.repaid", updated.CancellationReason);
    }

    [Fact]
    public void Reopen_CompletedSystemItem_ReturnsToWork()
    {
        var item = Ok(WorkItemRules.FromTemplate(new(Guid(102)), Template(), new WorkItemSource.Debt(TestData.ClientId), Utc(4, 15)));

        var reopened = Ok(Ok(item.Complete(Admin)).Reopen(Admin, []));

        Assert.Equal(WorkItemStatus.Open, reopened.Status);
    }
}

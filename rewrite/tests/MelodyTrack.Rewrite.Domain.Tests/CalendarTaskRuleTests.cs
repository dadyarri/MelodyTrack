using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class CalendarTaskRuleTests
{
    private static Actor Super => new Actor.Staff(TestData.StaffId, StaffRole.Superuser);
    private static CalendarTaskRule Rule(CalendarTaskPattern pattern) => Ok(CalendarTaskRuleOperations.Create(new(Guid(100)), new(Guid(101)), Date(1), null, new(15, 0), pattern, Super));

    [Fact]
    public void NextAfter_LateCompletion_UsesAnchorWithoutCatchUp()
    {
        var rule = Rule(Ok(CalendarTaskPattern.Every.DaysApart(3)));

        var next = Ok(rule.NextAfter(Utc(5, 20), []));

        Assert.Equal(Ok(SchoolTime.At(Date(7), new(15, 0))), next);
    }

    [Fact]
    public void NextAfter_MonthWithoutRequestedDay_UsesLastDay()
    {
        var rule = Rule(Ok(CalendarTaskPattern.Monthly.Create(31)));

        var next = Ok(rule.NextAfter(new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), []));

        Assert.Equal(Ok(SchoolTime.At(new(2026, 2, 28), new(15, 0))), next);
    }

    [Fact]
    public void NextAfter_PostponedItem_DoesNotCreateAnotherCopy()
    {
        var rule = Rule(new CalendarTaskPattern.Daily());
        var template = Ok(TaskTemplateOperations.Create(rule.TemplateId, "Задача", "Проверить", null, Super));
        var item = Ok(WorkItemRules.FromTemplate(new(Guid(102)), template, new WorkItemSource.Calendar(rule.Id, Utc(2, 12)), Utc(2, 12)));
        item = Ok(item.Postpone(Utc(8, 12), Utc(3, 12), Super));

        Assert.Null(Ok(rule.NextAfter(Utc(5, 12), [item])));
    }
}

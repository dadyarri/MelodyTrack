using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class AuditEntryTests
{
    [Fact]
    public void ForExpenseChange_DescriptionChanged_RecordsMarkerWithoutText()
    {
        var before = Ok(ExpenseRules.Create(new(Guid(100)), "Закрытый прежний текст", Ok(Money.Create(100)), Date(1), null));
        var after = Ok(ExpenseRules.Create(before.Id, "Закрытый новый текст", Ok(Money.Create(120)), Date(1), null));

        var entry = Ok(AuditEntryRules.ForExpenseChange(new(Guid(101)),
            new Actor.Staff(TestData.StaffId, StaffRole.Superuser), before, after, Now));

        Assert.Contains(entry.Changes, change => change is AuditChange.ExpenseDescriptionChanged);
        Assert.Contains(entry.Changes, change => change is AuditChange.ExpenseAmountChanged);
        Assert.DoesNotContain(before.Description, entry.ToString());
        Assert.DoesNotContain(after.Description, entry.ToString());
        Assert.Null(entry.Reason);
    }

    [Fact]
    public void ForClientNotesChange_PersonalText_RecordsOnlyChangeMarker()
    {
        var change = Ok(ClientNotesChange.Create(Guid(100), TestData.ClientId, TestData.StaffId, Now,
            "Закрытый прежний текст", "Закрытый новый текст"));

        var entry = Ok(AuditEntryRules.ForClientNotesChange(new(Guid(101)),
            new Actor.Staff(TestData.StaffId, StaffRole.Administrator), change));

        Assert.IsType<AuditChange.NotesChanged>(Assert.Single(entry.Changes));
        Assert.Equal(new AuditLink("client", TestData.ClientId.Value), Assert.Single(entry.Links));
        Assert.Equal("client.notes.changed", entry.Operation);
        Assert.Null(entry.Reason);
        Assert.Empty(entry.FinancialEffects);
        Assert.DoesNotContain(change.Before!, entry.ToString());
        Assert.DoesNotContain(change.After!, entry.ToString());
    }

    [Fact]
    public void ForClientNotesChange_DifferentAuthor_RejectsEntry()
    {
        var change = Ok(ClientNotesChange.Create(Guid(100), TestData.ClientId, TestData.StaffId, Now, null, "Текст"));

        var result = AuditEntryRules.ForClientNotesChange(new(Guid(101)),
            new Actor.Staff(new(Guid(102)), StaffRole.Superuser), change);

        AssertError(new ModelError.WrongOwner(), result);
    }
}

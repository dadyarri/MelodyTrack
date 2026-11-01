using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class ClientContactRestorationTests
{
    [Fact]
    public void Restore_ArchivedKind_PreservesExistingContact()
    {
        var kind = PhoneKind.Archive();

        var restored = Ok(ClientContactRules.Restore(new(Guid(30)), kind, "+44 20 1234 5678", null, true));

        Assert.Equal(kind.Id, restored.KindId);
        Assert.True(restored.IsPrimary);
        AssertError(new ClientError.ContactKindArchived(),
            ClientContactRules.Create(new(Guid(31)), kind, restored.Value, null, true));
    }

    [Fact]
    public void Restore_OriginalSpacing_PreservesValueAndCommentExactly()
    {
        const string value = "  +44 20 1234 5678  ";
        const string comment = "  старое представление  ";

        var restored = Ok(ClientContactRules.Restore(new(Guid(30)), PhoneKind, value, comment, false));

        Assert.Equal(value, restored.Value);
        Assert.Equal(comment, restored.Comment);
        Assert.False(restored.IsPrimary);
    }

    [Fact]
    public void Restore_InvalidValue_ReportsProblemInsteadOfRepairingIt()
    {
        var result = ClientContactRules.Restore(new(Guid(30)), PhoneKind, "not a phone", null, true);

        AssertError(new ClientError.InvalidContactValue(), result);
    }

    [Fact]
    public void Restore_EmptyContactIdentity_RejectsUnstableIdentity()
    {
        var result = ClientContactRules.Restore(default, PhoneKind, "+44 20 1234 5678", null, true);

        AssertError(new IdentifierError.Empty(), result);
    }
}

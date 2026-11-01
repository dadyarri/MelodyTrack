using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class ContactKindTests
{
    [Fact]
    public void Create_UnknownFormat_RejectsUnsupportedStorageValue()
    {
        var result = ContactKindRules.Create(new(Guid(21)), "Контакт", (ContactFormat)99);

        AssertError(new ModelError.InvalidValue(), result);
        Assert.Contains(result.Error.Issues, issue => issue.Field == "format");
    }

    [Fact]
    public void ChangeFormat_UnknownFormat_RejectsUnsupportedStorageValue()
    {
        var result = PhoneKind.ChangeFormat(false, (ContactFormat)99);

        AssertError(new ModelError.InvalidValue(), result);
    }

    [Fact]
    public void ChangeFormat_UsedKind_RejectsFormatChange() => AssertError(new ClientError.UsedContactFormat(), PhoneKind.ChangeFormat(true, ContactFormat.Email));
    [Fact]
    public void Rename_UsedKind_PreservesIdentityAndFormat()
    {
        var original = PhoneKind;
        var renamed = Ok(original.Rename("Мобильный"));
        Assert.Equal(original.Id, renamed.Id);
        Assert.Equal(ContactFormat.Phone, renamed.Format);
        Assert.Equal("Мобильный", renamed.Name);
        Assert.False(ContactKindRules.CanDelete(true));
    }
    [Fact]
    public void ChangeFormat_UnusedKind_AllowsChange()
    {
        Assert.Equal(ContactFormat.Link, Ok(PhoneKind.ChangeFormat(false, ContactFormat.Link)).Format);
        Assert.True(ContactKindRules.CanDelete(false));
    }
}

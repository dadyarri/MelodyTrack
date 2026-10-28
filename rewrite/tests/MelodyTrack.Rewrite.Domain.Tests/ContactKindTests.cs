
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class ContactKindTests
{
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
        Assert.False(ContactKind.CanDelete(true));
    }
    [Fact]
    public void ChangeFormat_UnusedKind_AllowsChange()
    {
        Assert.Equal(ContactFormat.Link, Ok(PhoneKind.ChangeFormat(false, ContactFormat.Link)).Format);
        Assert.True(ContactKind.CanDelete(false));
    }
}

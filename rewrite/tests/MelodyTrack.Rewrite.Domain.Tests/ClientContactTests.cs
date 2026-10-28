
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class ClientContactTests
{
    private static ContactKind Kind(ContactFormat format) => Ok(ContactKind.Create(new(Guid(21)), "Контакт", format));
    [Theory]
    [InlineData("+44 20 1234 5678")]
    [InlineData("+1 (202) 555-0100")]
    [InlineData("+7 999 123-45-67")]
    public void Create_InternationalPhone_PreservesInputWithoutRussianOnlyMask(string value) => Assert.Equal(value, Ok(ClientContact.Create(new(Guid(20)), Kind(ContactFormat.Phone), value, null, true)).Value);
    [Fact]
    public void Create_PhoneWithMisplacedPlus_RejectsValue() => AssertError(new ClientError.InvalidContactValue(), ClientContact.Create(new(Guid(20)), PhoneKind, "123+456", null, true));
    [Fact]
    public void Create_EmailFormat_RejectsInvalidAddress() => AssertError(new ClientError.InvalidContactValue(), ClientContact.Create(new(Guid(20)), Kind(ContactFormat.Email), "not an address", null, true));
    [Theory]
    [InlineData("/profile")]
    [InlineData("profile")]
    [InlineData("C:\\contacts\\profile")]
    public void Create_LinkFormat_RejectsPathsWithoutExplicitUriScheme(string value) => AssertError(new ClientError.InvalidContactValue(), ClientContact.Create(new(Guid(20)), Kind(ContactFormat.Link), value, null, true));
    [Fact]
    public void Create_ArchivedKind_RejectsNewContact() => AssertError(new ClientError.ContactKindArchived(), ClientContact.Create(new(Guid(20)), PhoneKind.Archive(), "+1 2025550100", null, true));
    [Fact]
    public void Create_EmailAndLink_AcceptsValidValues()
    {
        Assert.True(ClientContact.Create(new(Guid(20)), Kind(ContactFormat.Email), "test@example.com", null, true).IsSuccess);
        Assert.True(ClientContact.Create(new(Guid(20)), Kind(ContactFormat.Link), "https://example.com/profile", null, true).IsSuccess);
    }
}

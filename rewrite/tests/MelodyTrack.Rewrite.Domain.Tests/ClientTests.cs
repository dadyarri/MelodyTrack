using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class ClientTests
{
    private static ClientContact Email() => Ok(ClientContactRules.Create(new(Guid(33)), Ok(ContactKindRules.Create(new(Guid(32)), "Email", ContactFormat.Email)), "test@example.com", null, true));
    [Fact]
    public void Create_NoContacts_RejectsClient() => AssertError(new ClientError.ContactsRequired(), ClientRules.Create(TestData.ClientId, Profile, []));
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Create_ZeroOrTwoPrimaryContacts_RejectsClient(bool primary) => AssertError(new ClientError.PrimaryContactRequired(PhoneKind.Id), ClientRules.Create(TestData.ClientId, Profile, [Contact(30, primary), Contact(31, primary)]));
    [Fact]
    public void Create_DuplicateContactIdentity_RejectsClient()
    {
        var contact = Contact(30, true);
        AssertError(new ClientError.DuplicateContact(), ClientRules.Create(TestData.ClientId, Profile, [contact, contact]));
    }
    [Fact]
    public void SelectPrimary_ExistingContact_ChangesOnlyItsKindAndPreservesOriginalClient()
    {
        var first = Contact(30, true);
        var second = Contact(31, false);
        var email = Email();
        var original = Ok(ClientRules.Create(TestData.ClientId, Profile, [first, second, email]));

        var updated = Ok(original.SelectPrimary(second.Id));

        Assert.True(original.Contacts.Single(contact => contact.Id == first.Id).IsPrimary);
        Assert.False(updated.Contacts.Single(contact => contact.Id == first.Id).IsPrimary);
        Assert.True(updated.Contacts.Single(contact => contact.Id == second.Id).IsPrimary);
        Assert.Equal(email, updated.Contacts.Single(contact => contact.Id == email.Id));
        Assert.Equal(original.Id, updated.Id);
    }
    [Fact]
    public void SelectPrimary_UnknownContact_ReturnsDomainError() => AssertError(new ClientError.UnknownContact(), MakeClient().SelectPrimary(new(Guid(99))));
    [Fact]
    public void SelectPrimary_AlreadyPrimary_IsIdempotent()
    {
        var original = Ok(ClientRules.Create(TestData.ClientId, Profile, [Contact(30, true), Contact(31, false)]));
        Assert.Equal(original, Ok(original.SelectPrimary(original.Contacts[0].Id)));
    }
    [Fact]
    public void RemoveContact_PrimaryWithRemainingKind_RequiresExplicitReplacement()
    {
        var client = Ok(ClientRules.Create(TestData.ClientId, Profile, [Contact(30, true), Contact(31, false)]));
        AssertError(new ClientError.PrimaryContactRequired(PhoneKind.Id), client.RemoveContact(new(Guid(30)), null));
    }
    [Fact]
    public void RemoveContact_ExplicitReplacement_PromotesChosenContact()
    {
        var client = Ok(ClientRules.Create(TestData.ClientId, Profile, [Contact(30, true), Contact(31, false)]));
        var updated = Ok(client.RemoveContact(new(Guid(30)), new(Guid(31))));
        var remaining = Assert.Single(updated.Contacts);
        Assert.Equal(new ClientContactId(Guid(31)), remaining.Id);
        Assert.True(remaining.IsPrimary);
    }
    [Fact]
    public void RemoveContact_ReplacementFromAnotherKind_RejectsChoice()
    {
        var email = Email();
        var client = Ok(ClientRules.Create(TestData.ClientId, Profile, [Contact(30, true), Contact(31, false), email]));
        AssertError(new ClientError.PrimaryContactRequired(PhoneKind.Id), client.RemoveContact(new(Guid(30)), email.Id));
    }
    [Fact]
    public void RemoveContact_LastContact_RejectsRemoval() => AssertError(new ClientError.ContactsRequired(), MakeClient().RemoveContact(new(Guid(30)), null));
    [Fact]
    public void Create_SamePhoneForDifferentClients_AllowsBoth()
    {
        var first = MakeClient();
        var second = Ok(ClientRules.Create(new(Guid(2)), Profile, [Contact(31, true)]));
        Assert.NotEqual(first.Id, second.Id);
    }
    [Fact]
    public void Archive_ActiveSeries_BlocksUntilExplicitTermination() => AssertError(new CatalogueError.ActiveSeriesExist(), MakeClient().Archive(NoUsage with { HasActiveSeries = true }));
    [Fact]
    public void Unarchive_ArchivedClient_PreservesProfileAndContacts()
    {
        var client = MakeClient();
        var restored = Ok(client.Archive(NoUsage)).Unarchive();
        Assert.False(restored.IsArchived);
        Assert.Equal(TestData.ClientId, restored.Id);
        Assert.Equal(client.Profile, restored.Profile);
        Assert.Single(restored.Contacts);
    }
    [Fact]
    public void Create_MissingName_RejectsProfileName() => AssertError(new CatalogueError.Required("firstName"), PersonName.Create(" ", "Иванов", null));
}

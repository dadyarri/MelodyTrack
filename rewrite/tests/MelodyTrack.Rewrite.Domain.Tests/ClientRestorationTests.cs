using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class ClientRestorationTests
{
    [Fact]
    public void Create_ExplicitCreationTime_PreservesInstantInUtc()
    {
        var createdAt = new DateTimeOffset(2025, 10, 20, 18, 0, 0, TimeSpan.FromHours(3));

        var client = Ok(ClientRules.Create(TestData.ClientId, Profile, [Contact(30, true)], createdAt));

        Assert.Equal(createdAt.UtcDateTime, client.CreatedAt.UtcDateTime);
        Assert.Equal(TimeSpan.Zero, client.CreatedAt.Offset);
        Assert.False(client.IsArchived);
    }

    [Fact]
    public void Create_MissingSource_RejectsNewClient()
    {
        var profile = Profile with { SourceId = null };

        var result = ClientRules.Create(TestData.ClientId, profile, [Contact(30, true)], Now);

        AssertError(new IdentifierError.Empty(), result);
        Assert.Contains(result.Error.Issues, issue => issue.Field == "sourceId");
    }

    [Fact]
    public void Restore_MissingSourceAndArchivedClient_PreservesHistoricalState()
    {
        var profile = Profile with { SourceId = null, Birthdate = new DateOnly(2000, 2, 29) };
        var createdAt = new DateTimeOffset(2025, 11, 1, 10, 0, 0, TimeSpan.FromHours(3));

        var client = Ok(ClientRules.Restore(TestData.ClientId, profile, [Contact(30, true)], createdAt, isArchived: true));

        Assert.Null(client.Profile.SourceId);
        Assert.Equal(createdAt.ToUniversalTime(), client.CreatedAt);
        Assert.True(client.IsArchived);
        Assert.Equal(profile.Notes, client.Profile.Notes);
        Assert.Equal(new DateOnly(2000, 2, 29), client.Profile.Birthdate);
    }

    [Fact]
    public void Unarchive_RestoredClient_PreservesIdentityAndHistory()
    {
        var client = Ok(ClientRules.Restore(TestData.ClientId, Profile, [Contact(30, true)], Now, isArchived: true));

        var reopened = client.Unarchive();

        Assert.False(reopened.IsArchived);
        Assert.True(client.IsArchived);
        Assert.Equal(client.Id, reopened.Id);
        Assert.Equal(client.CreatedAt, reopened.CreatedAt);
        Assert.Equal(client.Profile, reopened.Profile);
        Assert.Equal(client.Contacts, reopened.Contacts);
    }

    [Fact]
    public void Restore_TechnicalMinimumCreationTime_DoesNotInventRegistrationDate()
    {
        var client = Ok(ClientRules.Restore(TestData.ClientId, Profile, [Contact(30, true)], DateTimeOffset.MinValue, isArchived: false));

        Assert.Equal(DateTimeOffset.MinValue, client.CreatedAt);
    }

    [Fact]
    public void Restore_EmptySourceIdentifier_RejectsInvalidReference()
    {
        var profile = Profile with { SourceId = new ClientSourceId(System.Guid.Empty) };

        var result = ClientRules.Restore(TestData.ClientId, profile, [Contact(30, true)], Now, isArchived: false);

        AssertError(new IdentifierError.Empty(), result);
    }

    [Fact]
    public void Restore_NoContacts_PreservesEmptyCollection()
    {
        var client = Ok(ClientRules.Restore(TestData.ClientId, Profile, [], Now, isArchived: false));

        Assert.Empty(client.Contacts);
        Assert.Equal(TestData.ClientId, client.Id);
    }

    [Fact]
    public void UpdateProfile_RestoredClient_PreservesOriginalMetadata()
    {
        var client = Ok(ClientRules.Restore(TestData.ClientId, Profile, [Contact(30, true)], Now, isArchived: true));

        var birthdate = new DateOnly(2000, 2, 29);

        var updated = Ok(client.UpdateProfile(Profile with { Birthdate = birthdate }));

        Assert.Equal(client.CreatedAt, updated.CreatedAt);
        Assert.True(updated.IsArchived);
        Assert.Equal(birthdate, updated.Profile.Birthdate);
        Assert.Equal(client.Profile.Notes, updated.Profile.Notes);
    }
}

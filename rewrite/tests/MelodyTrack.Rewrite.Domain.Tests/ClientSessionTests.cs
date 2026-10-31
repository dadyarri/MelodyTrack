using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class ClientSessionTests
{
    private static Actor Admin => new Actor.Staff(TestData.StaffId, StaffRole.Administrator);

    [Fact]
    public void CanUse_ReplacedAccess_RejectsOldSession()
    {
        var client = MakeClient();
        var access = Ok(ClientAccessOperations.Issue(new(Guid(100)), client, Admin));
        var session = Ok(ClientSessionRules.Start(new(Guid(101)), access, client));
        var replacement = Ok(ClientAccessOperations.Issue(new(Guid(102)), client, Admin));

        Assert.True(session.CanUse(access, client));
        Assert.False(session.CanUse(replacement, client));
    }

    [Fact]
    public void CanUse_RevokedAccessAfterUnarchive_RemainsRevoked()
    {
        var client = MakeClient();
        var access = Ok(ClientAccessOperations.Issue(new(Guid(100)), client, Admin));
        var session = Ok(ClientSessionRules.Start(new(Guid(101)), access, client));
        var archived = Ok(client.Archive(NoUsage));

        Assert.False(session.CanUse(access.RevokeForArchive(), archived.Unarchive()));
    }
}

using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class ClientAuthenticationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static Actor Administrator => new Actor.Staff(TestData.StaffId, StaffRole.Administrator);

    [Fact]
    public async Task ReplaceLinkAsync_Applied_ReturnsSecretWhoseHashWasStored()
    {
        var store = new LinkStore(new ClientLinkWriteResult.Applied());
        var authentication = new ClientAuthentication(store);
        var commandId = Guid(10);

        var result = await authentication.ReplaceLinkAsync(TestData.ClientId, commandId, Administrator, Token);

        var issued = Assert.IsType<ClientLinkReplacement.Issued>(Ok(result));
        Assert.True(OpaqueTokens.IsValid(issued.Link.Secret));
        Assert.Equal(OpaqueTokens.Hash(issued.Link.Secret), store.LinkHash);
        Assert.Equal(commandId, store.CommandId);
        Assert.DoesNotContain(issued.Link.Secret, issued.ToString());
        Assert.DoesNotContain(issued.Link.Secret, issued.Link.ToString());
    }

    [Fact]
    public async Task ReplaceLinkAsync_IdenticalReplay_ReturnsAlreadyAppliedWithoutSecret()
    {
        var store = new LinkStore(new ClientLinkWriteResult.AlreadyApplied());
        var authentication = new ClientAuthentication(store);

        var result = await authentication.ReplaceLinkAsync(TestData.ClientId, Guid(10), Administrator, Token);

        Assert.IsType<ClientLinkReplacement.AlreadyApplied>(Ok(result));
    }

    [Fact]
    public async Task ReplaceLinkAsync_ChangedCommandMeaning_PreservesStoreError()
    {
        var errors = ErrorList.Single(new ModelError.InvalidState(), "commandId");
        var authentication = new ClientAuthentication(new LinkStore(errors));

        var result = await authentication.ReplaceLinkAsync(TestData.ClientId, Guid(10), Administrator, Token);

        Assert.True(result.IsFailure);
        Assert.Same(errors, result.Error);
    }

    private sealed class LinkStore(Result<ClientLinkWriteResult, ErrorList> result) : IClientAuthenticationStore
    {
        public string? LinkHash { get; private set; }
        public Guid CommandId { get; private set; }

        public Task<Result<ClientLinkWriteResult, ErrorList>> ReplaceLinkAsync(ClientId clientId, Guid commandId,
            StaffId authorId, string? linkHash, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LinkHash = linkHash;
            CommandId = commandId;
            return Task.FromResult(result);
        }

        public Task<ClientSessionIdentity?> OpenSessionAsync(string linkHash, Guid sessionId, string refreshHash,
            string accessHash, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ClientSessionIdentity?> RefreshAsync(string refreshHash, string accessHash,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ClientSessionIdentity?> ActivateAsync(string refreshHash, string accessHash,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ClientSessionIdentity?> AuthenticateAsync(string accessHash,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RevokeSessionAsync(ClientSessionIdentity identity,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

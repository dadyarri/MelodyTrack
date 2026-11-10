using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain;

namespace MelodyTrack.Rewrite.Application;

public sealed record ClientSessionIdentity(ClientId ClientId, Guid SessionId);

public closed record ClientLinkWriteResult
{
    public sealed record Applied : ClientLinkWriteResult;
    public sealed record AlreadyApplied : ClientLinkWriteResult;
}

public closed record ClientLinkReplacement
{
    public sealed override string ToString() => nameof(ClientLinkReplacement);

    public sealed record Issued(ClientLinkSecret Link) : ClientLinkReplacement;
    public sealed record AlreadyApplied : ClientLinkReplacement;
}

public interface IClientAuthenticationStore
{
    Task<Result<ClientLinkWriteResult, ErrorList>> ReplaceLinkAsync(ClientId clientId, Guid commandId, StaffId authorId, string? linkHash, CancellationToken cancellationToken);
    Task<ClientSessionIdentity?> OpenSessionAsync(string linkHash, Guid sessionId, string refreshHash, string accessHash, CancellationToken cancellationToken);
    Task<ClientSessionIdentity?> RefreshAsync(string refreshHash, string accessHash, CancellationToken cancellationToken);
    Task<ClientSessionIdentity?> ActivateAsync(string refreshHash, string accessHash, CancellationToken cancellationToken);
    Task<ClientSessionIdentity?> AuthenticateAsync(string accessHash, CancellationToken cancellationToken);
    Task RevokeSessionAsync(ClientSessionIdentity identity, CancellationToken cancellationToken);
}

public sealed class ClientLinkSecret(string secret)
{
    public string Secret { get; } = secret;
    public override string ToString() => nameof(ClientLinkSecret);
}

public sealed class ClientSessionCredentials(string accessToken, string refreshToken)
{
    public string AccessToken { get; } = accessToken;
    public string RefreshToken { get; } = refreshToken;
    public int ExpiresIn => 300;
    public override string ToString() => nameof(ClientSessionCredentials);
}

public sealed class ClientAuthentication(IClientAuthenticationStore store)
{
    public async Task<Result<ClientLinkReplacement, ErrorList>> ReplaceLinkAsync(ClientId clientId, Guid commandId, Actor actor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (actor is not Actor.Staff { Role: StaffRole.Superuser or StaffRole.Administrator } author)
            return ErrorList.Single(new ModelError.Forbidden());
        if (clientId.Value == Guid.Empty || commandId == Guid.Empty || author.Id.Value == Guid.Empty) return ErrorList.Single(new IdentifierError.Empty());
        var secret = OpaqueTokens.Create();
        var result = await store.ReplaceLinkAsync(clientId, commandId, author.Id, OpaqueTokens.Hash(secret), cancellationToken);
        if (result.IsFailure) return result.Error;
        return result.Value switch
        {
            ClientLinkWriteResult.Applied => new ClientLinkReplacement.Issued(new ClientLinkSecret(secret)),
            ClientLinkWriteResult.AlreadyApplied => new ClientLinkReplacement.AlreadyApplied()
        };
    }

    public async Task<UnitResult<ErrorList>> RevokeLinkAsync(ClientId clientId, Guid commandId, Actor actor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (actor is not Actor.Staff { Role: StaffRole.Superuser or StaffRole.Administrator } author)
            return UnitResult.Failure(ErrorList.Single(new ModelError.Forbidden()));
        if (clientId.Value == Guid.Empty || commandId == Guid.Empty || author.Id.Value == Guid.Empty)
            return UnitResult.Failure(ErrorList.Single(new IdentifierError.Empty()));
        var result = await store.ReplaceLinkAsync(clientId, commandId, author.Id, null, cancellationToken);
        return result.IsFailure ? UnitResult.Failure(result.Error) : UnitResult.Success<ErrorList>();
    }

    public async Task<ClientSessionCredentials?> SignInAsync(string? link, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OpaqueTokens.IsValid(link)) return null;
        var access = OpaqueTokens.Create();
        var refresh = OpaqueTokens.Create();
        var identity = await store.OpenSessionAsync(OpaqueTokens.Hash(link!), Guid.NewGuid(), OpaqueTokens.Hash(refresh), OpaqueTokens.Hash(access), cancellationToken);
        return identity is null ? null : new ClientSessionCredentials(access, refresh);
    }

    public async Task<ClientSessionCredentials?> RefreshAsync(string? refresh, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OpaqueTokens.IsValid(refresh)) return null;
        var access = OpaqueTokens.Create();
        var identity = await store.RefreshAsync(OpaqueTokens.Hash(refresh!), OpaqueTokens.Hash(access), cancellationToken);
        return identity is null ? null : new ClientSessionCredentials(access, refresh!);
    }

    public async Task<ClientSessionCredentials?> ActivateAsync(string? refresh, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OpaqueTokens.IsValid(refresh)) return null;
        var access = OpaqueTokens.Create();
        var identity = await store.ActivateAsync(OpaqueTokens.Hash(refresh!), OpaqueTokens.Hash(access), cancellationToken);
        return identity is null ? null : new ClientSessionCredentials(access, refresh!);
    }

    public Task<ClientSessionIdentity?> AuthenticateAsync(string? access, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return OpaqueTokens.IsValid(access) ? store.AuthenticateAsync(OpaqueTokens.Hash(access!), cancellationToken)
            : Task.FromResult<ClientSessionIdentity?>(null);
    }

    public Task SignOutAsync(ClientSessionIdentity identity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (identity.ClientId.Value == Guid.Empty || identity.SessionId == Guid.Empty)
            throw new ArgumentException("An authenticated client session is required.", nameof(identity));
        return store.RevokeSessionAsync(identity, cancellationToken);
    }
}

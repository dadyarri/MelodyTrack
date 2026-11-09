using System.Security.Cryptography;
using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain;

namespace MelodyTrack.Rewrite.Application;

public interface IStaffAccessTokenStore
{
    // Uses database time and rejects revoked sessions, blocked/archived accounts and expired tokens.
    Task<StaffSessionIdentity?> FindStaffAsync(string tokenHash, CancellationToken cancellationToken);
    Task<UnitResult<ErrorList>> AddAsync(string tokenHash, StaffId staffId, Guid sessionId,
        CancellationToken cancellationToken);
    Task RevokeSessionAsync(Guid sessionId, CancellationToken cancellationToken);
}

public sealed record StaffSessionIdentity(StaffId StaffId, Guid SessionId);

public sealed class IssuedStaffAccessToken(string value)
{
    public string Value { get; } = value;
    public TimeSpan Lifetime => TimeSpan.FromMinutes(5);
    public override string ToString() => nameof(IssuedStaffAccessToken);
}

public sealed class StaffAccessTokens(IStaffAccessTokenStore store)
{
    // Internal application boundary for a future completed password/passkey + required-factor flow.
    // No HTTP route accepts a staff ID or session ID as sufficient proof to call this operation.
    public async Task<Result<IssuedStaffAccessToken, ErrorList>> IssueAfterSignInAsync(StaffId staffId, Guid sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (staffId.Value == Guid.Empty || sessionId == Guid.Empty) return ErrorList.Single(new IdentifierError.Empty());
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var saved = await store.AddAsync(Hash(token), staffId, sessionId, cancellationToken);
        return saved.IsFailure ? saved.Error : new IssuedStaffAccessToken(token);
    }

    public Task<StaffSessionIdentity?> AuthenticateAsync(string token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // A canonical fixed-length representation prevents alternate spellings of the same secret.
        if (token.Length != 64 || token.Any(character => character is not (>= '0' and <= '9' or >= 'A' and <= 'F')))
            return Task.FromResult<StaffSessionIdentity?>(null);
        return store.FindStaffAsync(Hash(token), cancellationToken);
    }

    // Identity and session come from the authenticated token, never from a sign-out request body.
    public async Task<UnitResult<ErrorList>> SignOutAsync(StaffSessionIdentity identity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (identity.StaffId.Value == Guid.Empty || identity.SessionId == Guid.Empty)
            return UnitResult.Failure(ErrorList.Single(new IdentifierError.Empty()));
        await store.RevokeSessionAsync(identity.SessionId, cancellationToken);
        return UnitResult.Success<ErrorList>();
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(token)));
}

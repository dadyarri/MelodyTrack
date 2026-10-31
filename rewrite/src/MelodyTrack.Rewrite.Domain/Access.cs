using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain;

public enum StaffSignInMethod { Password, Passkey, RememberedSession }
public enum SecondFactorRequirement { None, AuthenticatorOrPasskey, Authenticator }
public sealed record StaffLogin
{
    internal StaffLogin(StaffId staffId, string login, bool hasPassword, bool authenticatorEnabled, ImmutableHashSet<string> passkeyIds, bool secondFactorEnabled)
    {
        StaffId = staffId; Login = login; HasPassword = hasPassword; AuthenticatorEnabled = authenticatorEnabled;
        PasskeyIds = passkeyIds; SecondFactorEnabled = secondFactorEnabled;
    }

    public StaffId StaffId { get; }

    public string Login { get; }

    public bool HasPassword { get; }

    public bool AuthenticatorEnabled { get; }

    public bool SecondFactorEnabled { get; }

    public ImmutableHashSet<string> PasskeyIds { get; }
}

public sealed record StaffInvitation
{
    internal StaffInvitation(StaffInvitationId id, StaffRole role) { Id = id; Role = role; }

    public StaffInvitationId Id { get; }

    public StaffRole Role { get; }

    public StaffId? AcceptedBy { get; internal init; }

    public DateTimeOffset? AcceptedAt { get; internal init; }
}

// These models store identities and revocation state, never raw links, tokens, passwords or TOTP secrets.
public sealed record StaffSession
{
    internal StaffSession(StaffSessionId id, StaffId staffId, DateTimeOffset now)
    { Id = id; StaffId = staffId; CreatedAt = now.ToUniversalTime(); RefreshExpiresAt = CreatedAt.AddDays(14); }

    public StaffSessionId Id { get; }

    public StaffId StaffId { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset RefreshExpiresAt { get; internal init; }

    public bool IsRevoked { get; internal init; }

    public static TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(5);
}

public sealed record ClientAccess
{
    internal ClientAccess(ClientAccessId id, ClientId clientId) { Id = id; ClientId = clientId; }

    public ClientAccessId Id { get; }

    public ClientId ClientId { get; }

    public bool IsRevoked { get; internal init; }
}

public sealed record ClientSession
{
    internal ClientSession(ClientSessionId id, ClientAccess access) { Id = id; AccessId = access.Id; ClientId = access.ClientId; }

    public ClientSessionId Id { get; }

    public ClientAccessId AccessId { get; }

    public ClientId ClientId { get; }

    public bool IsRevoked { get; internal init; }
}

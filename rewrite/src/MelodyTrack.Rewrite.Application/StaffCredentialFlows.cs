using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain;

namespace MelodyTrack.Rewrite.Application;

public sealed record StaffCredentialAccount(Guid Id, string Login, string? PasswordHash, string? Authenticator,
    bool SecondFactorEnabled, long LastAuthenticatorStep, StoredPasskey[] Passkeys, StaffRole Role,
    bool Blocked = false, bool Archived = false, long Revision = 0)
{
    public override string ToString() => nameof(StaffCredentialAccount);
}
public sealed record StaffCredentialLink(Guid Id, string Hash, StaffRole Role, Guid? StaffId, DateTimeOffset ExpiresAt, bool Consumed);
public sealed record StaffCredentialChallenge(string Hash, string Purpose, Guid? StaffId, long Revision, string Payload, DateTimeOffset ExpiresAt)
{
    public override string ToString() => nameof(StaffCredentialChallenge);
}
public sealed record StaffCredentialSession(Guid Id, Guid StaffId, DateTimeOffset CreatedAt, DateTimeOffset RefreshExpiresAt,
    bool FactorVerified, bool Revoked);
public closed record StaffCredentialResult
{
    public sealed override string ToString() => nameof(StaffCredentialResult);

    public sealed record Tokens(string AccessToken, string RefreshToken, Guid StaffId) : StaffCredentialResult;
    public sealed record LinkIssued(string Challenge) : StaffCredentialResult;
    public sealed record RegistrationOptions(string Challenge, string OptionsJson, string? AuthenticatorSecret) : StaffCredentialResult;
    public sealed record PasskeyOptions(string Challenge, string OptionsJson) : StaffCredentialResult;
    public sealed record AuthenticatorSetup(string Challenge, string AuthenticatorSecret) : StaffCredentialResult;
    public sealed record FactorRequired(string Challenge, string Factor, string? OptionsJson) : StaffCredentialResult;
    public sealed record Completed : StaffCredentialResult;
}
public sealed record StaffSecurityProfile(bool HasPassword, bool AuthenticatorEnabled, bool SecondFactorEnabled,
    IReadOnlyList<string> Passkeys, IReadOnlyList<StaffCredentialSession> Sessions);
public enum StaffSecurityOperation
{
    InvitationCreated, RecoveryStarted, CredentialsRegistered, PasswordChanged, AuthenticatorEnabled, AuthenticatorDisabled,
    PasskeyAdded, PasskeyRemoved, SecondFactorChanged, SessionStarted, SessionRefreshed, SessionsRevoked, AccountAccessChanged
}

public interface IStaffCredentialStore
{
    // Consuming a one-use proof remains committed on an authentication failure; exceptions roll back.
    Task<Result<T, ErrorList>> ExecuteAsync<T>(Func<IStaffCredentialTransaction, CancellationToken, Task<Result<T, ErrorList>>> action,
        CancellationToken cancellationToken);
}
public interface IStaffCredentialTransaction
{
    DateTimeOffset Now { get; }
    Task<StaffCredentialAccount?> AccountAsync(Guid? id, string? normalizedLogin, CancellationToken cancellationToken);
    Task<Guid?> PasskeyOwnerAsync(string credentialId, CancellationToken cancellationToken);
    Task SaveAccountAsync(StaffCredentialAccount account, bool create, CancellationToken cancellationToken);
    Task<StaffCredentialLink?> LinkAsync(string hash, CancellationToken cancellationToken);
    Task AddLinkAsync(StaffCredentialLink link, CancellationToken cancellationToken);
    Task ConsumeLinkAsync(Guid id, CancellationToken cancellationToken);
    Task<StaffCredentialChallenge?> TakeChallengeAsync(string hash, CancellationToken cancellationToken);
    Task AddChallengeAsync(StaffCredentialChallenge challenge, CancellationToken cancellationToken);
    Task<StaffCredentialSession> CreateSessionAsync(Guid staffId, string accessHash, string refreshHash,
        bool factorVerified, CancellationToken cancellationToken);
    Task<StaffCredentialSession?> RefreshSessionAsync(string refreshHash, CancellationToken cancellationToken);
    Task RotateRefreshAsync(Guid sessionId, string refreshHash, CancellationToken cancellationToken);
    Task VerifySessionFactorAsync(Guid sessionId, CancellationToken cancellationToken);
    Task<bool> IsActiveSessionAsync(Guid sessionId, CancellationToken cancellationToken);
    Task ActivateSessionAsync(Guid sessionId, CancellationToken cancellationToken);
    Task AddAccessAsync(Guid sessionId, string accessHash, CancellationToken cancellationToken);
    Task<IReadOnlyList<StaffCredentialSession>> SessionsAsync(Guid staffId, CancellationToken cancellationToken);
    Task RevokeAsync(Guid staffId, Guid? sessionId, CancellationToken cancellationToken);
    Task SetAccessAsync(Guid staffId, StaffRole? role, bool? blocked, CancellationToken cancellationToken);
    Task AuditAsync(Guid? authorId, Guid? targetId, StaffSecurityOperation operation, Guid? sessionId, CancellationToken cancellationToken);
}

public interface ICurrentStaffLogin
{
    Task<Result<string, ErrorList>> GetLoginAsync(StaffId staffId, CancellationToken cancellationToken);
}

public sealed class StaffCredentialFlows(IStaffCredentialStore store, ICredentialSecurity security) : ICurrentStaffLogin
{
    public Task<Result<string, ErrorList>> GetLoginAsync(StaffId staffId, CancellationToken cancellationToken) =>
        store.ExecuteAsync<string>(async (tx, ct) =>
        {
            var account = await tx.AccountAsync(staffId.Value, null, ct);
            return Active(account) && !string.IsNullOrWhiteSpace(account.Login) ? account.Login : Invalid<string>();
        }, cancellationToken);

    public Task<Result<StaffCredentialResult, ErrorList>> InviteAsync(Actor.Staff author, StaffRole role, Guid? recoveryStaff,
        CancellationToken cancellationToken) => store.ExecuteAsync<StaffCredentialResult>(async (tx, ct) =>
    {
        if (!await IsSuperAsync(tx, author.Id.Value, ct) || !Enum.IsDefined(role)) return Denied<StaffCredentialResult>();
        if (recoveryStaff is { } id)
        {
            var account = await tx.AccountAsync(id, null, ct);
            if (account is null) return Missing<StaffCredentialResult>();
            role = account.Role;
            await tx.RevokeAsync(id, null, ct);
            await tx.SaveAccountAsync(account with { PasswordHash = null, Authenticator = null, Passkeys = [],
                LastAuthenticatorStep = -1, SecondFactorEnabled = role != StaffRole.Teacher, Revision = account.Revision + 1 }, false, ct);
        }
        var secret = NewSecret();
        await tx.AddLinkAsync(new(Guid.CreateVersion7(), Hash(secret), role, recoveryStaff, tx.Now.AddHours(24), false), ct);
        await tx.AuditAsync(author.Id.Value, recoveryStaff, recoveryStaff is null ? StaffSecurityOperation.InvitationCreated
            : StaffSecurityOperation.RecoveryStarted, null, ct);
        return new StaffCredentialResult.LinkIssued(secret);
    }, cancellationToken);

    public Task<Result<StaffRole, ErrorList>> PreviewInvitationAsync(string link, CancellationToken cancellationToken) =>
        store.ExecuteAsync<StaffRole>(async (tx, ct) =>
        {
            var invitation = await LinkAsync(tx, link, ct);
            return invitation is null ? Invalid<StaffRole>() : invitation.Role;
        }, cancellationToken);

    public Task<Result<StaffCredentialResult, ErrorList>> BeginRegistrationAsync(string link, string login, string? password,
        bool authenticator, bool secondFactor, CancellationToken cancellationToken) => store.ExecuteAsync<StaffCredentialResult>(async (tx, ct) =>
    {
        var invitation = await LinkAsync(tx, link, ct);
        if (invitation is null || string.IsNullOrWhiteSpace(login) || login.Trim().Length > 256 || password is { Length: < 8 } or { Length: > 1024 })
            return Invalid<StaffCredentialResult>();
        var existing = await tx.AccountAsync(null, Normalize(login), ct);
        if (existing is not null && existing.Id != invitation.StaffId || invitation.ExpiresAt <= tx.Now) return Invalid<StaffCredentialResult>();
        var staffId = invitation.StaffId ?? Guid.CreateVersion7();
        var rawSecret = authenticator ? security.CreateAuthenticatorSecret() : null;
        var account = new StaffCredentialAccount(staffId, login.Trim(), password is null ? null : security.HashPassword(password),
            rawSecret is null ? null : security.ProtectAuthenticator(new(staffId), rawSecret), secondFactor || authenticator,
            -1, [], invitation.Role);
        var options = security.CreatePasskeyRegistration(new(staffId), login.Trim(), []);
        var payload = JsonSerializer.Serialize(new RegistrationPayload(invitation.Id, account, options, invitation.Hash));
        var challenge = await ChallengeAsync(tx, "register", staffId, 0, payload, ct);
        return new StaffCredentialResult.RegistrationOptions(challenge, options, rawSecret);
    }, cancellationToken);

    public Task<Result<StaffCredentialResult, ErrorList>> AdvanceRegistrationAuthenticatorAsync(string challenge,
        string? code, bool skip, CancellationToken cancellationToken) => store.ExecuteAsync<StaffCredentialResult>(async (tx, ct) =>
    {
        var pending = await TakeAsync(tx, challenge, "register", ct);
        if (pending is null) return Invalid<StaffCredentialResult>();
        var registration = JsonSerializer.Deserialize<RegistrationPayload>(pending.Payload)!;
        var invitation = await RegistrationLinkAsync(tx, registration, ct);
        if (invitation is null) return Invalid<StaffCredentialResult>();
        var account = registration.Account;
        if (invitation.StaffId is { } existingId)
        {
            var original = await tx.AccountAsync(existingId, null, ct);
            if (!Active(original)) return Invalid<StaffCredentialResult>();
            account = account with { Role = original.Role };
        }
        if (skip)
        {
            account = account with { Authenticator = null, LastAuthenticatorStep = -1, SecondFactorEnabled = false };
            if (account.PasswordHash is null && RequiresFactor(account)) return Invalid<StaffCredentialResult>();
        }
        else
        {
            var verified = VerifyAuthenticator(account, code, tx.Now);
            if (verified is null) return Invalid<StaffCredentialResult>();
            account = account with { LastAuthenticatorStep = verified.Value };
        }
        var expiresAt = pending.ExpiresAt < invitation.ExpiresAt ? pending.ExpiresAt : invitation.ExpiresAt;
        if (expiresAt <= tx.Now) return Invalid<StaffCredentialResult>();
        var next = NewSecret();
        var payload = JsonSerializer.Serialize(registration with { Account = account, AuthenticatorVerified = !skip });
        await tx.AddChallengeAsync(new(Hash(next), "register", account.Id, pending.Revision, payload, expiresAt), ct);
        return new StaffCredentialResult.PasskeyOptions(next, registration.Options);
    }, cancellationToken);

    public Task<Result<StaffCredentialResult, ErrorList>> CompleteRegistrationAsync(string challenge, string? authenticatorCode,
        string? passkeyResponse, CancellationToken cancellationToken) => store.ExecuteAsync<StaffCredentialResult>(async (tx, ct) =>
    {
        var pending = await TakeAsync(tx, challenge, "register", ct);
        if (pending is null) return Invalid<StaffCredentialResult>();
        var registration = JsonSerializer.Deserialize<RegistrationPayload>(pending.Payload)!;
        var account = registration.Account;
        var invitation = await RegistrationLinkAsync(tx, registration, ct);
        if (invitation is null) return Invalid<StaffCredentialResult>();
        var original = invitation.StaffId is { } existingId ? await tx.AccountAsync(existingId, null, ct) : null;
        if (invitation.StaffId is not null && original is null) return Invalid<StaffCredentialResult>();
        if (original is not null)
        {
            if (!Active(original)) return Denied<StaffCredentialResult>();
            account = account with { Role = original.Role };
        }
        if (passkeyResponse is not null)
        {
            var credential = await security.VerifyPasskeyRegistrationAsync(new(account.Id), registration.Options, passkeyResponse, ct);
            if (credential is null) return Invalid<StaffCredentialResult>();
            account = account with { Passkeys = [credential] };
        }
        var verified = registration.AuthenticatorVerified && account.Authenticator is not null && account.LastAuthenticatorStep >= 0
            ? account.LastAuthenticatorStep : VerifyAuthenticator(account, authenticatorCode, tx.Now);
        if (account.Authenticator is not null && verified is null || account.PasswordHash is null && account.Passkeys.Length == 0)
            return Invalid<StaffCredentialResult>();
        if (RequiresFactor(account) && account.Authenticator is null && (account.PasswordHash is null || account.Passkeys.Length == 0))
            return Invalid<StaffCredentialResult>();
        if (await tx.AccountAsync(null, Normalize(account.Login), ct) is { } duplicate && duplicate.Id != account.Id)
            return Invalid<StaffCredentialResult>();
        if (invitation.ExpiresAt <= tx.Now || pending.ExpiresAt <= tx.Now) return Invalid<StaffCredentialResult>();
        account = account with { LastAuthenticatorStep = verified ?? -1, Revision = (original?.Revision ?? -1) + 1 };
        await tx.SaveAccountAsync(account, original is null, ct);
        await tx.ConsumeLinkAsync(invitation.Id, ct);
        await tx.AuditAsync(account.Id, account.Id, StaffSecurityOperation.CredentialsRegistered, null, ct);
        // A newly registered passkey used as the factor must be asserted before issuing work access.
        return await AfterPrimaryAsync(tx, account, "password", account.Authenticator is not null, ct);
    }, cancellationToken);

    public Task<Result<StaffCredentialResult, ErrorList>> PasswordAsync(string login, string password,
        CancellationToken cancellationToken) => store.ExecuteAsync<StaffCredentialResult>(async (tx, ct) =>
    {
        var account = await tx.AccountAsync(null, Normalize(login), ct);
        if (!Active(account) || account!.PasswordHash is null || password.Length > 1024
            || !security.VerifyPassword(account.PasswordHash, password)) return Invalid<StaffCredentialResult>();
        return await AfterPrimaryAsync(tx, account, "password", false, ct);
    }, cancellationToken);

    public Task<Result<StaffCredentialResult, ErrorList>> BeginPasskeyAsync(CancellationToken cancellationToken) =>
        store.ExecuteAsync<StaffCredentialResult>(async (tx, ct) =>
    {
        var options = security.CreatePasskeyAssertion([]);
        return new StaffCredentialResult.PasskeyOptions(await ChallengeAsync(tx, "primary-passkey", null,
            0, options, ct), options);
    }, cancellationToken);

    public Task<Result<StaffCredentialResult, ErrorList>> CompletePasskeyAsync(string challenge, string response,
        CancellationToken cancellationToken) => store.ExecuteAsync<StaffCredentialResult>(async (tx, ct) =>
    {
        var pending = await TakeAsync(tx, challenge, "primary-passkey", ct);
        if (pending is null) return Invalid<StaffCredentialResult>();
        var identity = security.ReadPasskeyAssertionIdentity(response);
        if (identity is null) return Invalid<StaffCredentialResult>();
        var owner = await tx.PasskeyOwnerAsync(identity.CredentialId, ct);
        var account = owner is { } id ? await tx.AccountAsync(id, null, ct) : null;
        if (!Active(account) || pending.ExpiresAt <= tx.Now) return Invalid<StaffCredentialResult>();
        var credential = account.Passkeys.FirstOrDefault(key => key.CredentialId == identity.CredentialId);
        if (credential is null || !credential.UserHandle.SequenceEqual(identity.UserHandle)
            || !await VerifyPasskeyAsync(tx, account, pending.Payload, response, ct)) return Invalid<StaffCredentialResult>();
        return await AfterPrimaryAsync(tx, account with { Revision = account.Revision + 1 }, "passkey", false, ct);
    }, cancellationToken);

    public Task<Result<StaffCredentialResult, ErrorList>> FactorAsync(string challenge, string? code, string? passkeyResponse,
        CancellationToken cancellationToken) => store.ExecuteAsync<StaffCredentialResult>(async (tx, ct) =>
    {
        var pending = await TakeAsync(tx, challenge, "factor", ct);
        var account = await ChallengeAccountAsync(tx, pending, ct);
        if (account is null) return Invalid<StaffCredentialResult>();
        var factor = JsonSerializer.Deserialize<FactorPayload>(pending!.Payload)!;
        var step = VerifyAuthenticator(account, code, tx.Now);
        var verified = step is not null || factor.Method != "passkey" && passkeyResponse is not null && factor.Options is not null
            && await VerifyPasskeyAsync(tx, account, factor.Options, passkeyResponse, ct);
        if (!verified) return Invalid<StaffCredentialResult>();
        if (step is not null) await tx.SaveAccountAsync(account with { LastAuthenticatorStep = step.Value, Revision = account.Revision + 1 }, false, ct);
        if (factor.ResumeHash is not null)
        {
            var session = await tx.RefreshSessionAsync(factor.ResumeHash, ct);
            if (session is null || session.StaffId != account.Id || session.Revoked || session.RefreshExpiresAt <= tx.Now)
                return Invalid<StaffCredentialResult>();
            await tx.VerifySessionFactorAsync(session.Id, ct);
            return await RenewAsync(tx, account, session, ct, true);
        }
        return await IssueAsync(tx, account, true, ct);
    }, cancellationToken);

    public Task<Result<StaffCredentialResult, ErrorList>> RefreshAsync(string refresh, bool resume,
        CancellationToken cancellationToken) => store.ExecuteAsync<StaffCredentialResult>(async (tx, ct) =>
    {
        if (!Canonical(refresh)) return Invalid<StaffCredentialResult>();
        var session = await tx.RefreshSessionAsync(Hash(refresh), ct);
        if (session is null || session.Revoked || session.RefreshExpiresAt <= tx.Now) return Invalid<StaffCredentialResult>();
        var account = await tx.AccountAsync(session.StaffId, null, ct);
        if (!Active(account) || session.RefreshExpiresAt <= tx.Now) return Invalid<StaffCredentialResult>();
        if (resume && RequiresFactor(account!)) return await AfterPrimaryAsync(tx, account!, "remembered", false, ct, Hash(refresh));
        if (!resume && !await tx.IsActiveSessionAsync(session.Id, ct)) return Denied<StaffCredentialResult>();
        if (RequiresFactor(account!) && !session.FactorVerified) return Denied<StaffCredentialResult>();
        return await RenewAsync(tx, account!, session, ct, resume);
    }, cancellationToken);

    public Task<Result<StaffSecurityProfile, ErrorList>> ProfileAsync(StaffId staff, CancellationToken cancellationToken) =>
        store.ExecuteAsync<StaffSecurityProfile>(async (tx, ct) =>
    {
        var account = await tx.AccountAsync(staff.Value, null, ct);
        if (!Active(account)) return Denied<StaffSecurityProfile>();
        return new StaffSecurityProfile(account!.PasswordHash is not null, account.Authenticator is not null,
            account.SecondFactorEnabled, account.Passkeys.Select(key => key.CredentialId).ToArray(), await tx.SessionsAsync(account.Id, ct));
    }, cancellationToken);

    public Task<Result<StaffCredentialResult, ErrorList>> ChangePasswordAsync(StaffId staff, string? oldPassword, string newPassword,
        CancellationToken cancellationToken) => store.ExecuteAsync<StaffCredentialResult>(async (tx, ct) =>
    {
        var account = await tx.AccountAsync(staff.Value, null, ct);
        if (!Active(account) || account!.PasswordHash is not null && (oldPassword is null || !security.VerifyPassword(account.PasswordHash, oldPassword))
            || newPassword.Length is < 8 or > 1024) return Invalid<StaffCredentialResult>();
        await tx.SaveAccountAsync(account with { PasswordHash = security.HashPassword(newPassword), Revision = account.Revision + 1 }, false, ct);
        await tx.AuditAsync(account.Id, account.Id, StaffSecurityOperation.PasswordChanged, null, ct);
        return new StaffCredentialResult.Completed();
    }, cancellationToken);

    public Task<Result<StaffCredentialResult, ErrorList>> SetSecondFactorAsync(StaffId staff, bool enabled,
        CancellationToken cancellationToken) => store.ExecuteAsync<StaffCredentialResult>(async (tx, ct) =>
    {
        var account = await tx.AccountAsync(staff.Value, null, ct);
        if (!Active(account)) return Denied<StaffCredentialResult>();
        if (!enabled && (account!.Role != StaffRole.Teacher || account.Authenticator is not null)
            || enabled && account!.Authenticator is null && (account.PasswordHash is null || account.Passkeys.Length == 0))
            return Denied<StaffCredentialResult>();
        await tx.SaveAccountAsync(account! with { SecondFactorEnabled = enabled, Revision = account.Revision + 1 }, false, ct);
        await tx.AuditAsync(account.Id, account.Id, StaffSecurityOperation.SecondFactorChanged, null, ct);
        return new StaffCredentialResult.Completed();
    }, cancellationToken);

    public Task<Result<StaffCredentialResult, ErrorList>> BeginAuthenticatorAsync(StaffId staff, CancellationToken cancellationToken) =>
        store.ExecuteAsync<StaffCredentialResult>(async (tx, ct) =>
    {
        var account = await tx.AccountAsync(staff.Value, null, ct);
        if (!Active(account)) return Denied<StaffCredentialResult>();
        var secret = security.CreateAuthenticatorSecret();
        var challenge = await ChallengeAsync(tx, "enroll-totp", account!.Id, account.Revision,
            security.ProtectAuthenticator(staff, secret), ct);
        return new StaffCredentialResult.AuthenticatorSetup(challenge, secret);
    }, cancellationToken);

    public Task<Result<StaffCredentialResult, ErrorList>> ConfirmAuthenticatorAsync(StaffId staff, string challenge, string code,
        CancellationToken cancellationToken) => store.ExecuteAsync<StaffCredentialResult>(async (tx, ct) =>
    {
        var pending = await TakeAsync(tx, challenge, "enroll-totp", ct);
        var account = await ChallengeAccountAsync(tx, pending, ct);
        if (account is null || account.Id != staff.Value) return Invalid<StaffCredentialResult>();
        var secret = security.UnprotectAuthenticator(staff, pending!.Payload);
        var step = security.VerifyAuthenticator(secret, code, tx.Now);
        if (step is null) return Invalid<StaffCredentialResult>();
        await tx.SaveAccountAsync(account with { Authenticator = pending.Payload, SecondFactorEnabled = true,
            LastAuthenticatorStep = step.Value, Revision = account.Revision + 1 }, false, ct);
        await tx.AuditAsync(account.Id, account.Id, StaffSecurityOperation.AuthenticatorEnabled, null, ct);
        return new StaffCredentialResult.Completed();
    }, cancellationToken);

    public Task<Result<StaffCredentialResult, ErrorList>> DisableAuthenticatorAsync(StaffId staff, string code,
        CancellationToken cancellationToken) => store.ExecuteAsync<StaffCredentialResult>(async (tx, ct) =>
    {
        var account = await tx.AccountAsync(staff.Value, null, ct);
        if (!Active(account) || VerifyAuthenticator(account!, code, tx.Now) is null) return Invalid<StaffCredentialResult>();
        // Mandatory factor can be provided by passkey after password, but passkey-primary requires TOTP.
        if (account!.Role != StaffRole.Teacher && (account.PasswordHash is null || account.Passkeys.Length == 0))
            return Denied<StaffCredentialResult>();
        await tx.SaveAccountAsync(account with { Authenticator = null, LastAuthenticatorStep = -1,
            SecondFactorEnabled = account.Role != StaffRole.Teacher, Revision = account.Revision + 1 }, false, ct);
        await tx.AuditAsync(account.Id, account.Id, StaffSecurityOperation.AuthenticatorDisabled, null, ct);
        return new StaffCredentialResult.Completed();
    }, cancellationToken);

    public Task<Result<StaffCredentialResult, ErrorList>> BeginAddPasskeyAsync(StaffId staff, CancellationToken cancellationToken) =>
        store.ExecuteAsync<StaffCredentialResult>(async (tx, ct) =>
    {
        var account = await tx.AccountAsync(staff.Value, null, ct);
        if (!Active(account)) return Denied<StaffCredentialResult>();
        var options = security.CreatePasskeyRegistration(staff, account!.Login, account.Passkeys);
        return new StaffCredentialResult.PasskeyOptions(await ChallengeAsync(tx, "enroll-passkey", account.Id, account.Revision, options, ct), options);
    }, cancellationToken);

    public Task<Result<StaffCredentialResult, ErrorList>> CompleteAddPasskeyAsync(StaffId staff, string challenge, string response,
        CancellationToken cancellationToken) => store.ExecuteAsync<StaffCredentialResult>(async (tx, ct) =>
    {
        var pending = await TakeAsync(tx, challenge, "enroll-passkey", ct);
        var account = await ChallengeAccountAsync(tx, pending, ct);
        if (account is null || account.Id != staff.Value) return Invalid<StaffCredentialResult>();
        var key = await security.VerifyPasskeyRegistrationAsync(staff, pending!.Payload, response, ct);
        if (key is null || account.Passkeys.Any(old => old.CredentialId == key.CredentialId)) return Invalid<StaffCredentialResult>();
        await tx.SaveAccountAsync(account with { Passkeys = [.. account.Passkeys, key], Revision = account.Revision + 1 }, false, ct);
        await tx.AuditAsync(account.Id, account.Id, StaffSecurityOperation.PasskeyAdded, null, ct);
        return new StaffCredentialResult.Completed();
    }, cancellationToken);

    public Task<Result<StaffCredentialResult, ErrorList>> RemovePasskeyAsync(StaffId staff, string credentialId,
        CancellationToken cancellationToken) => store.ExecuteAsync<StaffCredentialResult>(async (tx, ct) =>
    {
        var account = await tx.AccountAsync(staff.Value, null, ct);
        if (!Active(account) || !account!.Passkeys.Any(key => key.CredentialId == credentialId)) return Invalid<StaffCredentialResult>();
        var keys = account.Passkeys.Where(key => key.CredentialId != credentialId).ToArray();
        if (keys.Length == 0 && (account.PasswordHash is null || RequiresFactor(account) && account.Authenticator is null))
            return Denied<StaffCredentialResult>();
        await tx.SaveAccountAsync(account with { Passkeys = keys, Revision = account.Revision + 1 }, false, ct);
        await tx.AuditAsync(account.Id, account.Id, StaffSecurityOperation.PasskeyRemoved, null, ct);
        return new StaffCredentialResult.Completed();
    }, cancellationToken);

    public Task<Result<StaffCredentialResult, ErrorList>> RevokeAsync(StaffId staff, Guid? session, CancellationToken cancellationToken) =>
        store.ExecuteAsync<StaffCredentialResult>(async (tx, ct) =>
    {
        if (!Active(await tx.AccountAsync(staff.Value, null, ct))) return Denied<StaffCredentialResult>();
        if (session is { } id && !(await tx.SessionsAsync(staff.Value, ct)).Any(value => value.Id == id))
            return Missing<StaffCredentialResult>();
        await tx.RevokeAsync(staff.Value, session, ct);
        await tx.AuditAsync(staff.Value, staff.Value, StaffSecurityOperation.SessionsRevoked, session, ct);
        return new StaffCredentialResult.Completed();
    }, cancellationToken);

    public Task<Result<StaffCredentialResult, ErrorList>> SetAccessAsync(Actor.Staff author, StaffId target, StaffRole? role,
        bool? blocked, CancellationToken cancellationToken) => store.ExecuteAsync<StaffCredentialResult>(async (tx, ct) =>
    {
        if (!await IsSuperAsync(tx, author.Id.Value, ct)) return Denied<StaffCredentialResult>();
        if (role is { } value && !Enum.IsDefined(value) || role is null && blocked is null) return Invalid<StaffCredentialResult>();
        if (await tx.AccountAsync(target.Value, null, ct) is null) return Missing<StaffCredentialResult>();
        await tx.SetAccessAsync(target.Value, role, blocked, ct);
        if (blocked == true) await tx.RevokeAsync(target.Value, null, ct);
        await tx.AuditAsync(author.Id.Value, target.Value, StaffSecurityOperation.AccountAccessChanged, null, ct);
        return new StaffCredentialResult.Completed();
    }, cancellationToken);

    private async Task<StaffCredentialResult> IssueAsync(IStaffCredentialTransaction tx, StaffCredentialAccount account, bool verified, CancellationToken ct)
    {
        var access = NewSecret();
        var refresh = NewSecret();
        var session = await tx.CreateSessionAsync(account.Id, Hash(access), Hash(refresh), verified, ct);
        await tx.AuditAsync(account.Id, account.Id, StaffSecurityOperation.SessionStarted, session.Id, ct);
        return new StaffCredentialResult.Tokens(access, refresh, account.Id);
    }
    private async Task<Result<StaffCredentialResult, ErrorList>> AfterPrimaryAsync(IStaffCredentialTransaction tx,
        StaffCredentialAccount account, string method, bool verified, CancellationToken ct, string? resumeHash = null)
    {
        if (!RequiresFactor(account) || verified) return await IssueAsync(tx, account, verified, ct);
        var options = method == "passkey" || account.Passkeys.Length == 0 ? null : security.CreatePasskeyAssertion(account.Passkeys);
        var payload = JsonSerializer.Serialize(new FactorPayload(method, options, resumeHash));
        return new StaffCredentialResult.FactorRequired(await ChallengeAsync(tx, "factor", account.Id, account.Revision, payload, ct),
            method == "passkey" ? "authenticator" : "authenticatorOrPasskey", options);
    }
    private static async Task<StaffCredentialResult> RenewAsync(IStaffCredentialTransaction tx, StaffCredentialAccount account,
        StaffCredentialSession session, CancellationToken ct, bool activate = false)
    {
        var access = NewSecret();
        var refresh = NewSecret();
        await tx.RotateRefreshAsync(session.Id, Hash(refresh), ct);
        await tx.AddAccessAsync(session.Id, Hash(access), ct);
        if (activate) await tx.ActivateSessionAsync(session.Id, ct);
        await tx.AuditAsync(account.Id, account.Id, StaffSecurityOperation.SessionRefreshed, session.Id, ct);
        return new StaffCredentialResult.Tokens(access, refresh, account.Id);
    }
    private long? VerifyAuthenticator(StaffCredentialAccount account, string? code, DateTimeOffset now)
    {
        if (account.Authenticator is null || code is null) return null;
        var step = security.VerifyAuthenticator(security.UnprotectAuthenticator(new(account.Id), account.Authenticator), code, now);
        return step > account.LastAuthenticatorStep ? step : null;
    }
    private async Task<bool> VerifyPasskeyAsync(IStaffCredentialTransaction tx, StaffCredentialAccount account, string options,
        string response, CancellationToken ct)
    {
        foreach (var key in account.Passkeys)
        {
            var counter = await security.VerifyPasskeyAssertionAsync(new(account.Id), options, response, key, ct);
            if (counter is null) continue;
            var keys = account.Passkeys.Select(old => old.CredentialId == key.CredentialId ? old with { Counter = counter.Value } : old).ToArray();
            await tx.SaveAccountAsync(account with { Passkeys = keys, Revision = account.Revision + 1 }, false, ct);
            return true;
        }
        return false;
    }
    private static bool Active([NotNullWhen(true)] StaffCredentialAccount? account) => account is { Blocked: false, Archived: false };
    private static bool RequiresFactor(StaffCredentialAccount account) => account.Role != StaffRole.Teacher || account.SecondFactorEnabled;
    private static string Normalize(string login) => login.Trim().ToUpperInvariant();
    private static string NewSecret() => OpaqueTokens.Create();
    private static string Hash(string secret) => OpaqueTokens.Hash(secret);
    private static bool Canonical(string value) => OpaqueTokens.IsValid(value);
    private static Result<T, ErrorList> Invalid<T>() => ErrorList.Single(new ModelError.InvalidValue(), "credentials");
    private static Result<T, ErrorList> Denied<T>() => ErrorList.Single(new ModelError.Forbidden());
    private static Result<T, ErrorList> Missing<T>() => ErrorList.Single(new ModelError.NotFound());
    private static async Task<bool> IsSuperAsync(IStaffCredentialTransaction tx, Guid id, CancellationToken ct) =>
        await tx.AccountAsync(id, null, ct) is { Role: StaffRole.Superuser, Blocked: false, Archived: false };
    private static async Task<StaffCredentialLink?> LinkAsync(IStaffCredentialTransaction tx, string secret, CancellationToken ct)
    {
        if (!Canonical(secret)) return null;
        var link = await tx.LinkAsync(Hash(secret), ct);
        return link is { Consumed: false } && link.ExpiresAt > tx.Now ? link : null;
    }
    private static async Task<StaffCredentialLink?> RegistrationLinkAsync(IStaffCredentialTransaction tx,
        RegistrationPayload registration, CancellationToken ct)
    {
        var link = await tx.LinkAsync(registration.LinkHash, ct);
        return link is { Consumed: false } && link.Id == registration.LinkId && link.ExpiresAt > tx.Now ? link : null;
    }
    private static async Task<string> ChallengeAsync(IStaffCredentialTransaction tx, string purpose, Guid? staffId,
        long revision, string payload, CancellationToken ct)
    {
        var secret = NewSecret();
        await tx.AddChallengeAsync(new(Hash(secret), purpose, staffId, revision, payload, tx.Now.AddMinutes(5)), ct);
        return secret;
    }
    private static async Task<StaffCredentialChallenge?> TakeAsync(IStaffCredentialTransaction tx, string secret, string purpose, CancellationToken ct)
    {
        if (!Canonical(secret)) return null;
        var challenge = await tx.TakeChallengeAsync(Hash(secret), ct);
        return challenge is not null && challenge.Purpose == purpose && challenge.ExpiresAt > tx.Now ? challenge : null;
    }
    private static async Task<StaffCredentialAccount?> ChallengeAccountAsync(IStaffCredentialTransaction tx,
        StaffCredentialChallenge? challenge, CancellationToken ct)
    {
        if (challenge?.StaffId is not { } id) return null;
        var account = await tx.AccountAsync(id, null, ct);
        return Active(account) && account!.Revision == challenge.Revision && challenge.ExpiresAt > tx.Now ? account : null;
    }
    private sealed record RegistrationPayload(Guid LinkId, StaffCredentialAccount Account, string Options, string LinkHash = "",
        bool AuthenticatorVerified = false);
    private sealed record FactorPayload(string Method, string? Options, string? ResumeHash = null);
}

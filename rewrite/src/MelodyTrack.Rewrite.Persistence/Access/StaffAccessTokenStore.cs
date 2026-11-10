using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;

namespace MelodyTrack.Rewrite.Persistence.Access;

/// <summary>Stores only token digests and resolves identity against current database time and account state.</summary>
public sealed class StaffAccessTokenStore(Database database, IBrowserAccessContext browser) : IStaffAccessTokenStore
{
    /// <summary>Resolves a staff identity from a token digest using database time and current session and account state.</summary>
    /// <returns>The staff and session identity, or <see langword="null"/> if the token is invalid, expired, not yet valid, or access is revoked, blocked, or archived.</returns>
    public async Task<StaffSessionIdentity?> FindStaffAsync(string tokenHash, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ValidHash(tokenHash)) return null;
        var identity = await database.Query("""
            SELECT s.staff_id AS "StaffId", s.id AS "SessionId"
            FROM public.staff_access_tokens t
            JOIN public.staff_sessions s ON s.id=t.session_id
            JOIN public.staff_access_state a ON a.staff_id=s.staff_id
            LEFT JOIN public.staff_credentials c ON c.staff_id=s.staff_id
            LEFT JOIN public.browser_access_contexts b ON b.id=s.browser_context_id
            WHERE t.token_hash=@hash AND t.issued_at <= clock_timestamp() AND t.expires_at > clock_timestamp()
                AND s.revoked_at IS NULL AND NOT a.is_blocked AND NOT a.is_archived
                AND (s.factor_verified OR (a.role='teacher' AND NOT COALESCE(c.second_factor_enabled,false)))
                AND ((@trusted AND s.browser_context_id IS NULL)
                    OR (b.id=@browser AND b.active_kind='staff' AND b.active_session_id=s.id))
            """).WithParameter("hash", tokenHash).WithParameter("trusted", browser.IsTrustedInternal)
                .WithParameter("browser", browser.ContextId, System.Data.DbType.Guid).GetSingleOrDefaultAsync<IdentityRow>(cancellationToken);
        return identity is null ? null : new StaffSessionIdentity(new(identity.StaffId), identity.SessionId);
    }

    /// <summary>For a completed trusted sign-in only; no public caller may select a staff identity.</summary>
    public async Task<UnitResult<ErrorList>> AddAsync(string tokenHash, StaffId staffId, Guid sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ValidHash(tokenHash)) return Fail(new ModelError.InvalidValue(), "tokenHash");
        if (staffId.Value == Guid.Empty || sessionId == Guid.Empty) return Fail(new IdentifierError.Empty());
        await using var transaction = await database.BeginTransactionAsync(cancellationToken);
        if (!await LockBrowserAsync(transaction, cancellationToken)) return Fail(new ModelError.Forbidden());
        // Match identity-first ordering used by staff policy writes. Revocation never locks identity afterwards.
        var active = await transaction.Query("""
            SELECT staff_id FROM public.staff_access_state
            WHERE staff_id=@staff AND NOT is_blocked AND NOT is_archived FOR SHARE
            """).WithParameter("staff", staffId.Value).GetSingleOrDefaultAsync<Guid?>(cancellationToken);
        if (active is null) return Fail(new ModelError.Forbidden());
        await LockSessionAsync(transaction, sessionId, cancellationToken);
        await transaction.Query("""
            INSERT INTO public.staff_sessions(id, staff_id, factor_verified,browser_context_id)
            VALUES (@session, @staff, true,@browser) ON CONFLICT (id) DO NOTHING
            """).WithParameter("session", sessionId).WithParameter("staff", staffId.Value)
                .WithParameter("browser", browser.ContextId, System.Data.DbType.Guid).ExecuteAsync(cancellationToken);
        var session = await transaction.Query("""
            SELECT staff_id AS "StaffId", revoked_at AS "RevokedAt",browser_context_id AS "BrowserContextId"
            FROM public.staff_sessions WHERE id=@session FOR UPDATE
            """).WithParameter("session", sessionId).GetSingleAsync<SessionRow>(cancellationToken);
        if (session.StaffId != staffId.Value || session.RevokedAt is not null || session.BrowserContextId != browser.ContextId)
            return Fail(new ModelError.Forbidden());
        var inserted = await transaction.Query("""
            WITH instant AS (SELECT clock_timestamp() AS now)
            INSERT INTO public.staff_access_tokens(token_hash, session_id, issued_at, expires_at)
            SELECT @hash, @session, now, now + INTERVAL '5 minutes' FROM instant
            ON CONFLICT (token_hash) DO NOTHING RETURNING token_hash
            """).WithParameter("hash", tokenHash).WithParameter("session", sessionId)
            .GetSingleOrDefaultAsync<string>(cancellationToken);
        if (inserted is null) return Fail(new ModelError.Duplicate(), "tokenHash");
        if (browser.ContextId is not null)
            await transaction.Query("UPDATE public.browser_access_contexts SET active_kind='staff',active_session_id=@session WHERE id=@browser")
                .WithParameter("session", sessionId).WithParameter("browser", browser.ContextId, System.Data.DbType.Guid).ExecuteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return UnitResult.Success<ErrorList>();
    }

    /// <summary>Revokes the session and all its access tokens while preserving any existing revocation timestamp.</summary>
    public async Task RevokeSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (sessionId == Guid.Empty) return;
        await using var transaction = await database.BeginTransactionAsync(cancellationToken);
        if (!await LockBrowserAsync(transaction, cancellationToken)) return;
        await LockSessionAsync(transaction, sessionId, cancellationToken);
        await transaction.Query("""
            UPDATE public.staff_sessions SET revoked_at=COALESCE(revoked_at, clock_timestamp())
            WHERE id=@session AND (browser_context_id=@browser OR (@trusted AND browser_context_id IS NULL))
            """).WithParameter("session", sessionId).WithParameter("browser", browser.ContextId, System.Data.DbType.Guid)
                .WithParameter("trusted", browser.IsTrustedInternal).ExecuteAsync(cancellationToken);
        if (browser.ContextId is not null)
            await transaction.Query("""
                UPDATE public.browser_access_contexts SET active_kind=NULL,active_session_id=NULL
                WHERE id=@browser AND active_kind='staff' AND active_session_id=@session
                """).WithParameter("browser", browser.ContextId, System.Data.DbType.Guid).WithParameter("session", sessionId).ExecuteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<bool> LockBrowserAsync(DatabaseTransaction transaction, CancellationToken cancellationToken)
    {
        if (browser.IsTrustedInternal) return true;
        if (browser.ContextId is not { } id) return false;
        return await transaction.Query("SELECT id FROM public.browser_access_contexts WHERE id=@id FOR UPDATE")
            .WithParameter("id", id).GetSingleOrDefaultAsync<Guid?>(cancellationToken) is not null;
    }

    private static Task LockSessionAsync(DatabaseTransaction transaction, Guid sessionId, CancellationToken cancellationToken) =>
        transaction.Query("SELECT pg_advisory_xact_lock(hashtextextended(CAST(@session AS text), 0))")
            .WithParameter("session", sessionId).ExecuteAsync(cancellationToken);

    private static bool ValidHash(string value) => value.Length == 64
        && value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');

    private static UnitResult<ErrorList> Fail(DomainError error, string? field = null) =>
        UnitResult.Failure(ErrorList.Single(error, field));

    private sealed class SessionRow
    {
        public Guid StaffId { get; init; }
        public DateTime? RevokedAt { get; init; }
        public Guid? BrowserContextId { get; init; }
    }

    private sealed class IdentityRow
    {
        public Guid StaffId { get; init; }
        public Guid SessionId { get; init; }
    }
}

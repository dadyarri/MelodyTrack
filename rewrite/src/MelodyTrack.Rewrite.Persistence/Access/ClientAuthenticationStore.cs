using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;

namespace MelodyTrack.Rewrite.Persistence.Access;

/// <summary>Persists client link and session allowlists without storing raw credentials.</summary>
public sealed class ClientAuthenticationStore(Database database, IBrowserAccessContext browser) : IClientAuthenticationStore
{
    /// <summary>Replaces or revokes a link and all its sessions after locking the current staff role and client.</summary>
    public async Task<Result<ClientLinkWriteResult, ErrorList>> ReplaceLinkAsync(ClientId clientId, Guid commandId, StaffId authorId, string? linkHash, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (clientId.Value == Guid.Empty || commandId == Guid.Empty || authorId.Value == Guid.Empty || linkHash is not null && !OpaqueTokens.IsValid(linkHash))
            return ErrorList.Single(new ModelError.InvalidValue());
        await using var transaction = await database.BeginTransactionAsync(cancellationToken);
        if (!await LockBrowserAsync(transaction, cancellationToken)) return ErrorList.Single(new ModelError.Forbidden());
        var role = await transaction.Query("""
            SELECT role::text FROM public.staff_access_state
            WHERE staff_id=@id AND NOT is_blocked AND NOT is_archived FOR SHARE
            """).WithParameter("id", authorId.Value).GetSingleOrDefaultAsync<string>(cancellationToken);
        if (role is not ("administrator" or "superuser")) return ErrorList.Single(new ModelError.Forbidden());
        await transaction.Query("SELECT pg_advisory_xact_lock(hashtextextended(CAST(@id AS text), 1))")
            .WithParameter("id", commandId).ExecuteAsync(cancellationToken);
        var archived = await LockClientAsync(transaction, clientId.Value, cancellationToken);
        if (archived is null) return ErrorList.Single(new ModelError.NotFound(), "clientId");
        if (linkHash is not null && archived.Value) return ErrorList.Single(new ModelError.InvalidState(), "clientId");
        var operation = linkHash is null ? "revoke" : "replace";
        var previous = await transaction.Query("""
            SELECT client_id AS "ClientId", author_staff_id AS "AuthorId", operation AS "Operation"
            FROM public.client_access_commands WHERE command_id=@id
            """).WithParameter("id", commandId).GetSingleOrDefaultAsync<ReceiptRow>(cancellationToken);
        if (previous is not null)
            return previous.ClientId == clientId.Value && previous.AuthorId == authorId.Value && previous.Operation == operation
                ? new ClientLinkWriteResult.AlreadyApplied() : ErrorList.Single(new ModelError.InvalidState(), "commandId");
        await transaction.Query("""
            UPDATE public.client_access_links SET revoked_at=COALESCE(revoked_at, clock_timestamp()) WHERE client_id=@id;
            UPDATE public.client_access_sessions s SET revoked_at=COALESCE(s.revoked_at, clock_timestamp())
            FROM public.client_access_links l WHERE s.link_hash=l.link_hash AND l.client_id=@id;
            """).WithParameter("id", clientId.Value).ExecuteAsync(cancellationToken);
        if (linkHash is not null)
            await transaction.Query("INSERT INTO public.client_access_links(link_hash, client_id, issued_by) VALUES (@hash,@client,@author)")
                .WithParameter("hash", linkHash).WithParameter("client", clientId.Value).WithParameter("author", authorId.Value).ExecuteAsync(cancellationToken);
        await transaction.Query("""
            INSERT INTO public.client_access_commands(command_id,client_id,author_staff_id,operation)
            VALUES (@id,@client,@author,@operation)
            """).WithParameter("id", commandId).WithParameter("client", clientId.Value).WithParameter("author", authorId.Value)
            .WithParameter("operation", operation).ExecuteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ClientLinkWriteResult.Applied();
    }

    /// <summary>Creates an independent remembered device session from an active personal link.</summary>
    public async Task<ClientSessionIdentity?> OpenSessionAsync(string linkHash, Guid sessionId, string refreshHash, string accessHash, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OpaqueTokens.IsValid(linkHash) || !OpaqueTokens.IsValid(refreshHash) || !OpaqueTokens.IsValid(accessHash) || sessionId == Guid.Empty) return null;
        await using var transaction = await database.BeginTransactionAsync(cancellationToken);
        if (!await LockBrowserAsync(transaction, cancellationToken)) return null;
        var clientId = await transaction.Query("SELECT client_id FROM public.client_access_links WHERE link_hash=@hash")
            .WithParameter("hash", linkHash).GetSingleOrDefaultAsync<Guid?>(cancellationToken);
        if (clientId is null || await LockClientAsync(transaction, clientId.Value, cancellationToken) is not false) return null;
        var current = await transaction.Query("SELECT client_id FROM public.client_access_links WHERE link_hash=@hash AND revoked_at IS NULL FOR UPDATE")
            .WithParameter("hash", linkHash).GetSingleOrDefaultAsync<Guid?>(cancellationToken);
        if (current != clientId) return null;
        await transaction.Query("INSERT INTO public.client_access_sessions(id,link_hash,refresh_hash,browser_context_id) VALUES (@id,@link,@refresh,@browser)")
            .WithParameter("id", sessionId).WithParameter("link", linkHash).WithParameter("refresh", refreshHash)
            .WithParameter("browser", browser.ContextId, System.Data.DbType.Guid).ExecuteAsync(cancellationToken);
        await AddAccessAsync(transaction, sessionId, accessHash, cancellationToken);
        await ActivateAsync(transaction, sessionId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(new(clientId.Value), sessionId);
    }

    /// <summary>Issues another five-minute access token while the remembered session remains in the allowlist.</summary>
    public Task<ClientSessionIdentity?> RefreshAsync(string refreshHash, string accessHash, CancellationToken cancellationToken) =>
        ContinueAsync(refreshHash, accessHash, false, cancellationToken);

    /// <summary>Activates a remembered client in the same browser after validating its persistent proof.</summary>
    public Task<ClientSessionIdentity?> ActivateAsync(string refreshHash, string accessHash, CancellationToken cancellationToken) =>
        ContinueAsync(refreshHash, accessHash, true, cancellationToken);

    private async Task<ClientSessionIdentity?> ContinueAsync(string refreshHash, string accessHash, bool activate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OpaqueTokens.IsValid(refreshHash) || !OpaqueTokens.IsValid(accessHash)) return null;
        await using var transaction = await database.BeginTransactionAsync(cancellationToken);
        if (!await LockBrowserAsync(transaction, cancellationToken)) return null;
        var candidate = await transaction.Query("""
            SELECT l.client_id AS "ClientId", s.id AS "SessionId" FROM public.client_access_sessions s
            JOIN public.client_access_links l ON l.link_hash=s.link_hash WHERE s.refresh_hash=@hash
            """).WithParameter("hash", refreshHash).GetSingleOrDefaultAsync<IdentityRow>(cancellationToken);
        if (candidate is null || await LockClientAsync(transaction, candidate.ClientId, cancellationToken) is not false) return null;
        var active = await transaction.Query("""
            SELECT s.id FROM public.client_access_sessions s JOIN public.client_access_links l ON l.link_hash=s.link_hash
            WHERE s.id=@id AND s.refresh_hash=@hash AND s.revoked_at IS NULL AND l.revoked_at IS NULL
                AND (s.browser_context_id=@browser OR (@trusted AND s.browser_context_id IS NULL)) FOR UPDATE OF l,s
            """).WithParameter("id", candidate.SessionId).WithParameter("hash", refreshHash)
            .WithParameter("browser", browser.ContextId, System.Data.DbType.Guid).WithParameter("trusted", browser.IsTrustedInternal)
            .GetSingleOrDefaultAsync<Guid?>(cancellationToken);
        if (active is null) return null;
        if (!activate && !browser.IsTrustedInternal && !await IsActiveAsync(transaction, candidate.SessionId, cancellationToken)) return null;
        await AddAccessAsync(transaction, candidate.SessionId, accessHash, cancellationToken);
        if (activate) await ActivateAsync(transaction, candidate.SessionId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(new(candidate.ClientId), candidate.SessionId);
    }

    /// <summary>Resolves only an active stored token, session, link and unarchived client using database time.</summary>
    public async Task<ClientSessionIdentity?> AuthenticateAsync(string accessHash, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OpaqueTokens.IsValid(accessHash)) return null;
        var identity = await database.Query("""
            SELECT c.id AS "ClientId", s.id AS "SessionId" FROM public.client_access_tokens t
            JOIN public.client_access_sessions s ON s.id=t.session_id
            JOIN public.client_access_links l ON l.link_hash=s.link_hash
            JOIN public.clients c ON c.id=l.client_id
            LEFT JOIN public.browser_access_contexts b ON b.id=s.browser_context_id
            WHERE t.token_hash=@hash AND t.issued_at<=clock_timestamp() AND t.expires_at>clock_timestamp()
                AND s.revoked_at IS NULL AND l.revoked_at IS NULL AND NOT c.is_archived
                AND ((@trusted AND s.browser_context_id IS NULL)
                    OR (b.id=@browser AND b.active_kind='client' AND b.active_session_id=s.id))
            """).WithParameter("hash", accessHash).WithParameter("browser", browser.ContextId, System.Data.DbType.Guid)
            .WithParameter("trusted", browser.IsTrustedInternal).GetSingleOrDefaultAsync<IdentityRow>(cancellationToken);
        return identity is null ? null : new(new(identity.ClientId), identity.SessionId);
    }

    /// <summary>Revokes only the authenticated client's current browser session without affecting its link or other devices.</summary>
    public async Task RevokeSessionAsync(ClientSessionIdentity identity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var transaction = await database.BeginTransactionAsync(cancellationToken);
        if (!await LockBrowserAsync(transaction, cancellationToken)) return;
        await transaction.Query("""
            UPDATE public.client_access_sessions s SET revoked_at=COALESCE(s.revoked_at,clock_timestamp())
            FROM public.client_access_links l WHERE s.link_hash=l.link_hash AND s.id=@session AND l.client_id=@client
                AND (s.browser_context_id=@browser OR (@trusted AND s.browser_context_id IS NULL))
            """).WithParameter("session", identity.SessionId).WithParameter("client", identity.ClientId.Value)
            .WithParameter("browser", browser.ContextId, System.Data.DbType.Guid).WithParameter("trusted", browser.IsTrustedInternal).ExecuteAsync(cancellationToken);
        if (browser.ContextId is not null)
            await transaction.Query("""
                UPDATE public.browser_access_contexts SET active_kind=NULL,active_session_id=NULL
                WHERE id=@browser AND active_kind='client' AND active_session_id=@session
                """).WithParameter("browser", browser.ContextId, System.Data.DbType.Guid).WithParameter("session", identity.SessionId).ExecuteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static Task<bool?> LockClientAsync(DatabaseTransaction transaction, Guid clientId, CancellationToken cancellationToken) =>
        transaction.Query("SELECT is_archived FROM public.clients WHERE id=@id FOR UPDATE")
            .WithParameter("id", clientId).GetSingleOrDefaultAsync<bool?>(cancellationToken);

    private async Task<bool> LockBrowserAsync(DatabaseTransaction transaction, CancellationToken ct)
    {
        if (browser.IsTrustedInternal) return true;
        if (browser.ContextId is not { } id) return false;
        return await transaction.Query("SELECT id FROM public.browser_access_contexts WHERE id=@id FOR UPDATE")
            .WithParameter("id", id).GetSingleOrDefaultAsync<Guid?>(ct) is not null;
    }

    private Task<bool> IsActiveAsync(DatabaseTransaction transaction, Guid sessionId, CancellationToken ct) =>
        transaction.Query("""
            SELECT EXISTS(SELECT 1 FROM public.browser_access_contexts
                WHERE id=@browser AND active_kind='client' AND active_session_id=@session)
            """).WithParameter("browser", browser.ContextId, System.Data.DbType.Guid).WithParameter("session", sessionId).GetSingleAsync<bool>(ct);

    private async Task ActivateAsync(DatabaseTransaction transaction, Guid sessionId, CancellationToken ct)
    {
        if (browser.IsTrustedInternal) return;
        await transaction.Query("UPDATE public.browser_access_contexts SET active_kind='client',active_session_id=@session WHERE id=@browser")
            .WithParameter("session", sessionId).WithParameter("browser", browser.ContextId, System.Data.DbType.Guid).ExecuteAsync(ct);
    }

    private static Task AddAccessAsync(DatabaseTransaction transaction, Guid sessionId, string accessHash, CancellationToken cancellationToken) =>
        transaction.Query("""
            WITH instant AS (SELECT clock_timestamp() AS now)
            INSERT INTO public.client_access_tokens(token_hash,session_id,issued_at,expires_at)
            SELECT @hash,@id,now,now+INTERVAL '5 minutes' FROM instant
            """).WithParameter("hash", accessHash).WithParameter("id", sessionId).ExecuteAsync(cancellationToken);

    private sealed class ReceiptRow
    {
        public Guid ClientId { get; init; }
        public Guid AuthorId { get; init; }
        public string Operation { get; init; } = "";
    }

    private sealed class IdentityRow
    {
        public Guid ClientId { get; init; }
        public Guid SessionId { get; init; }
    }
}

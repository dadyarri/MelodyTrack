using System.Text.Json;
using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using Npgsql;

namespace MelodyTrack.Rewrite.Persistence.Access;

/// <summary>Provides one database transaction for each credential flow and durable one-use proof consumption.</summary>
public sealed class StaffCredentialStore(Database database, IBrowserAccessContext browser) : IStaffCredentialStore
{
    /// <summary>Commits authentication attempts, including consumed failed proofs; exceptions roll back all changes.</summary>
    public async Task<Result<T, ErrorList>> ExecuteAsync<T>(Func<IStaffCredentialTransaction, CancellationToken, Task<Result<T, ErrorList>>> action,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var transaction = await database.BeginTransactionAsync(cancellationToken);
                if (browser.ContextId is { } contextId)
                    await transaction.Query("SELECT id FROM public.browser_access_contexts WHERE id=@id FOR UPDATE")
                        .WithParameter("id", contextId).GetSingleAsync<Guid>(cancellationToken);
                else if (!browser.IsTrustedInternal)
                    return ErrorList.Single(new ModelError.Forbidden());
                var instant = await transaction.Query("SELECT clock_timestamp()").GetSingleAsync<DateTime>(cancellationToken);
                var result = await action(new Transaction(transaction, new DateTimeOffset(instant, TimeSpan.Zero), browser), cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.DeadlockDetected && attempt < 2)
            {
                // The callback has only database writes and local credential verification; the failed transaction was rolled back.
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(5, 25) * (attempt + 1)), cancellationToken);
            }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.DeadlockDetected)
            { return ErrorList.Single(new ModelError.InvalidState(), "credentials"); }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.UniqueViolation)
            { return ErrorList.Single(new ModelError.Duplicate(), "credentials"); }
        }
    }

    private sealed class Transaction(DatabaseTransaction database, DateTimeOffset now, IBrowserAccessContext browser) : IStaffCredentialTransaction
    {
        public DateTimeOffset Now { get; private set; } = now;

        private async Task RefreshClockAsync(CancellationToken ct) =>
            Now = Utc(await database.Query("SELECT clock_timestamp()").GetSingleAsync<DateTime>(ct));

        public async Task<StaffCredentialAccount?> AccountAsync(Guid? id, string? normalizedLogin, CancellationToken ct)
        {
            var row = await database.Query("""
                SELECT s.staff_id AS "Id", COALESCE(c.login, '') AS "Login", c.password_hash AS "PasswordHash",
                    c.protected_authenticator AS "Authenticator", COALESCE(c.second_factor_enabled,false) AS "SecondFactorEnabled",
                    COALESCE(c.last_authenticator_step,-1) AS "LastAuthenticatorStep", s.role::text AS "Role",
                    s.is_blocked AS "Blocked", s.is_archived AS "Archived", COALESCE(c.revision,0) AS "Revision"
                FROM public.staff_access_state s LEFT JOIN public.staff_credentials c ON c.staff_id=s.staff_id
                WHERE (@id IS NOT NULL AND s.staff_id=@id) OR (@login IS NOT NULL AND c.normalized_login=@login)
                FOR UPDATE OF s
                """).WithParameter("id", id, System.Data.DbType.Guid).WithParameter("login", normalizedLogin, System.Data.DbType.String)
                .GetSingleOrDefaultAsync<AccountRow>(ct);
            await RefreshClockAsync(ct);
            if (row is null || !Enum.TryParse<StaffRole>(row.Role, true, out var role)) return null;
            var keys = await database.Query("""
                SELECT credential_id AS "CredentialId", public_key AS "PublicKey", signature_counter AS "Counter", user_handle AS "UserHandle"
                FROM public.staff_passkeys WHERE staff_id=@id ORDER BY credential_id
                """).WithParameter("id", row.Id).GetAllAsync<PasskeyRow>(ct);
            return new(row.Id, row.Login, row.PasswordHash, row.Authenticator, row.SecondFactorEnabled, row.LastAuthenticatorStep,
                keys.Select(key => new StoredPasskey(key.CredentialId, key.PublicKey, checked((uint)key.Counter), key.UserHandle)).ToArray(),
                role, row.Blocked, row.Archived, row.Revision);
        }

        public async Task SaveAccountAsync(StaffCredentialAccount account, bool create, CancellationToken ct)
        {
            if (create)
                await database.Query("INSERT INTO public.staff_access_state(staff_id,role) VALUES (@id,CAST(@role AS public.staff_access_role))")
                    .WithParameter("id", account.Id).WithParameter("role", account.Role.ToString().ToLowerInvariant()).ExecuteAsync(ct);
            await database.Query("""
                INSERT INTO public.staff_credentials(staff_id,login,normalized_login,password_hash,protected_authenticator,
                    second_factor_enabled,last_authenticator_step,revision)
                VALUES (@id,@login,@normalized,@password,@authenticator,@factor,@step,@revision)
                ON CONFLICT (staff_id) DO UPDATE SET login=EXCLUDED.login,normalized_login=EXCLUDED.normalized_login,
                    password_hash=EXCLUDED.password_hash,protected_authenticator=EXCLUDED.protected_authenticator,
                    second_factor_enabled=EXCLUDED.second_factor_enabled,last_authenticator_step=EXCLUDED.last_authenticator_step,
                    revision=EXCLUDED.revision
                """).WithParameter("id", account.Id).WithParameter("login", account.Login).WithParameter("normalized", account.Login.ToUpperInvariant())
                .WithParameter("password", account.PasswordHash, System.Data.DbType.String)
                .WithParameter("authenticator", account.Authenticator, System.Data.DbType.String)
                .WithParameter("factor", account.SecondFactorEnabled).WithParameter("step", account.LastAuthenticatorStep)
                .WithParameter("revision", account.Revision).ExecuteAsync(ct);
            await database.Query("DELETE FROM public.staff_passkeys WHERE staff_id=@id").WithParameter("id", account.Id).ExecuteAsync(ct);
            foreach (var key in account.Passkeys)
                await database.Query("""
                    INSERT INTO public.staff_passkeys(credential_id,staff_id,public_key,signature_counter,user_handle)
                    VALUES (@key,@id,@public,@counter,@handle)
                    """).WithParameter("key", key.CredentialId).WithParameter("id", account.Id).WithParameter("public", key.PublicKey)
                    .WithParameter("counter", (long)key.Counter).WithParameter("handle", key.UserHandle).ExecuteAsync(ct);
        }

        public async Task<StaffCredentialLink?> LinkAsync(string hash, CancellationToken ct)
        {
            var row = await database.Query("""
                SELECT id AS "Id", token_hash AS "Hash", role::text AS "Role", staff_id AS "StaffId",
                    expires_at AS "ExpiresAt", consumed AS "Consumed" FROM public.staff_credential_links WHERE token_hash=@hash FOR UPDATE
                """).WithParameter("hash", hash).GetSingleOrDefaultAsync<LinkRow>(ct);
            await RefreshClockAsync(ct);
            return row is null || !Enum.TryParse<StaffRole>(row.Role, true, out var role) ? null
                : new(row.Id,row.Hash,role,row.StaffId,Utc(row.ExpiresAt),row.Consumed);
        }
        public async Task AddLinkAsync(StaffCredentialLink link, CancellationToken ct)
        {
            if (link.StaffId is not null)
                await database.Query("UPDATE public.staff_credential_links SET consumed=true WHERE staff_id=@id AND NOT consumed")
                    .WithParameter("id", link.StaffId).ExecuteAsync(ct);
            await database.Query("""
                INSERT INTO public.staff_credential_links(id,token_hash,role,staff_id,expires_at)
                VALUES (@id,@hash,CAST(@role AS public.staff_access_role),@staff,@expires)
                """).WithParameter("id", link.Id).WithParameter("hash", link.Hash).WithParameter("role", link.Role.ToString().ToLowerInvariant())
                .WithParameter("staff", link.StaffId, System.Data.DbType.Guid).WithParameter("expires", link.ExpiresAt).ExecuteAsync(ct);
        }
        public async Task ConsumeLinkAsync(Guid id, CancellationToken ct) =>
            await database.Query("UPDATE public.staff_credential_links SET consumed=true WHERE id=@id").WithParameter("id", id).ExecuteAsync(ct);
        public async Task<StaffCredentialChallenge?> TakeChallengeAsync(string hash, CancellationToken ct)
        {
            var row = await database.Query("""
                DELETE FROM public.staff_credential_challenges WHERE token_hash=@hash
                    AND (browser_context_id=@browser OR (@trusted AND browser_context_id IS NULL))
                RETURNING token_hash AS "Hash", purpose AS "Purpose", staff_id AS "StaffId", credential_revision AS "Revision",
                    payload::text AS "Payload", expires_at AS "ExpiresAt"
                """).WithParameter("hash", hash).WithParameter("browser", browser.ContextId, System.Data.DbType.Guid)
                    .WithParameter("trusted", browser.IsTrustedInternal).GetSingleOrDefaultAsync<ChallengeRow>(ct);
            await RefreshClockAsync(ct);
            return row is null ? null : new(row.Hash,row.Purpose,row.StaffId,row.Revision,row.Payload,Utc(row.ExpiresAt));
        }
        public async Task AddChallengeAsync(StaffCredentialChallenge challenge, CancellationToken ct) =>
            await database.Query("""
                INSERT INTO public.staff_credential_challenges(token_hash,purpose,staff_id,credential_revision,payload,expires_at,browser_context_id)
                VALUES (@hash,@purpose,@staff,@revision,@payload,@expires,@browser)
                """).WithParameter("hash", challenge.Hash).WithParameter("purpose", challenge.Purpose).WithParameter("staff", challenge.StaffId, System.Data.DbType.Guid)
                .WithParameter("revision", challenge.Revision).WithParameter("payload", challenge.Payload).WithParameter("expires", challenge.ExpiresAt)
                .WithParameter("browser", browser.ContextId, System.Data.DbType.Guid).ExecuteAsync(ct);

        public async Task<StaffCredentialSession> CreateSessionAsync(Guid staffId, string accessHash, string refreshHash,
            bool factorVerified, CancellationToken ct)
        {
            await RefreshClockAsync(ct);
            var id = Guid.CreateVersion7();
            await database.Query("""
                INSERT INTO public.staff_sessions(id,staff_id,refresh_hash,refresh_expires_at,factor_verified,browser_context_id)
                VALUES (@id,@staff,@hash,@expires,@factor,@browser)
                """).WithParameter("id", id).WithParameter("staff", staffId).WithParameter("hash", refreshHash)
                .WithParameter("expires", Now.AddDays(14)).WithParameter("factor", factorVerified)
                .WithParameter("browser", browser.ContextId, System.Data.DbType.Guid).ExecuteAsync(ct);
            await AddAccessAsync(id,accessHash,ct);
            await ActivateSessionAsync(id,ct);
            return new(id,staffId,Now,Now.AddDays(14),factorVerified,false);
        }
        public async Task<StaffCredentialSession?> RefreshSessionAsync(string refreshHash, CancellationToken ct)
        {
            // Identity locks precede session locks, matching issuance and current-authority checks.
            var staff = await database.Query("SELECT staff_id FROM public.staff_sessions WHERE refresh_hash=@hash")
                .WithParameter("hash",refreshHash).GetSingleOrDefaultAsync<Guid?>(ct);
            if (staff is null) return null;
            if (await AccountAsync(staff.Value,null,ct) is null) return null;
            var row = await database.Query("""
                SELECT id AS "Id",staff_id AS "StaffId",created_at AS "CreatedAt",refresh_expires_at AS "RefreshExpiresAt",
                    factor_verified AS "FactorVerified",revoked_at IS NOT NULL AS "Revoked"
                FROM public.staff_sessions WHERE refresh_hash=@hash
                    AND (browser_context_id=@browser OR (@trusted AND browser_context_id IS NULL)) FOR UPDATE
                """).WithParameter("hash",refreshHash).WithParameter("browser", browser.ContextId, System.Data.DbType.Guid)
                    .WithParameter("trusted", browser.IsTrustedInternal).GetSingleOrDefaultAsync<SessionRow>(ct);
            await RefreshClockAsync(ct);
            return row is null ? null : Session(row);
        }
        public async Task RotateRefreshAsync(Guid sessionId, string refreshHash, CancellationToken ct) =>
            await database.Query("UPDATE public.staff_sessions SET refresh_hash=@hash,refresh_expires_at=@expires WHERE id=@id AND revoked_at IS NULL")
                .WithParameter("id",sessionId).WithParameter("hash",refreshHash).WithParameter("expires",Now.AddDays(14)).ExecuteAsync(ct);
        public async Task VerifySessionFactorAsync(Guid sessionId, CancellationToken ct) =>
            await database.Query("UPDATE public.staff_sessions SET factor_verified=true WHERE id=@id AND revoked_at IS NULL")
                .WithParameter("id", sessionId).ExecuteAsync(ct);
        public async Task<bool> IsActiveSessionAsync(Guid sessionId, CancellationToken ct) => browser.IsTrustedInternal
            || await database.Query("""
                SELECT EXISTS(SELECT 1 FROM public.browser_access_contexts
                    WHERE id=@browser AND active_kind='staff' AND active_session_id=@session)
                """).WithParameter("browser", browser.ContextId, System.Data.DbType.Guid).WithParameter("session", sessionId).GetSingleAsync<bool>(ct);
        public async Task ActivateSessionAsync(Guid sessionId, CancellationToken ct)
        {
            if (browser.IsTrustedInternal) return;
            await database.Query("""
                UPDATE public.browser_access_contexts b SET active_kind='staff',active_session_id=@session
                WHERE b.id=@browser AND EXISTS(SELECT 1 FROM public.staff_sessions s
                    WHERE s.id=@session AND s.browser_context_id=b.id AND s.revoked_at IS NULL)
                """).WithParameter("browser", browser.ContextId, System.Data.DbType.Guid).WithParameter("session", sessionId).ExecuteAsync(ct);
        }
        public async Task AddAccessAsync(Guid sessionId, string accessHash, CancellationToken ct) =>
            await database.Query("""
                INSERT INTO public.staff_access_tokens(token_hash,session_id,issued_at,expires_at)
                VALUES (@hash,@session,@now,@expires)
                """).WithParameter("hash",accessHash).WithParameter("session",sessionId).WithParameter("now",Now)
                .WithParameter("expires",Now.AddMinutes(5)).ExecuteAsync(ct);
        public async Task<IReadOnlyList<StaffCredentialSession>> SessionsAsync(Guid staffId, CancellationToken ct) =>
            (await database.Query("""
                SELECT id AS "Id",staff_id AS "StaffId",created_at AS "CreatedAt",COALESCE(refresh_expires_at,created_at) AS "RefreshExpiresAt",
                    factor_verified AS "FactorVerified",revoked_at IS NOT NULL AS "Revoked"
                FROM public.staff_sessions WHERE staff_id=@id ORDER BY created_at,id
                """).WithParameter("id",staffId).GetAllAsync<SessionRow>(ct)).Select(Session).ToArray();
        public async Task RevokeAsync(Guid staffId, Guid? sessionId, CancellationToken ct)
        {
            await database.Query("""
                UPDATE public.staff_sessions SET revoked_at=COALESCE(revoked_at,@now)
                WHERE staff_id=@staff AND (@session IS NULL OR id=@session)
                """).WithParameter("staff",staffId).WithParameter("session",sessionId,System.Data.DbType.Guid).WithParameter("now",Now).ExecuteAsync(ct);
            if (browser.ContextId is not null)
                await database.Query("""
                    UPDATE public.browser_access_contexts SET active_kind=NULL,active_session_id=NULL
                    WHERE id=@browser AND active_kind='staff' AND active_session_id IN
                        (SELECT id FROM public.staff_sessions WHERE staff_id=@staff AND revoked_at IS NOT NULL)
                    """).WithParameter("browser", browser.ContextId, System.Data.DbType.Guid).WithParameter("staff", staffId).ExecuteAsync(ct);
        }
        public async Task SetAccessAsync(Guid staffId, StaffRole? role, bool? blocked, CancellationToken ct) =>
            await database.Query("""
                UPDATE public.staff_access_state SET role=COALESCE(CAST(@role AS public.staff_access_role),role),is_blocked=COALESCE(@blocked,is_blocked)
                WHERE staff_id=@id
                """).WithParameter("id",staffId).WithParameter("role",role?.ToString().ToLowerInvariant(), System.Data.DbType.String)
                .WithParameter("blocked",blocked,System.Data.DbType.Boolean).ExecuteAsync(ct);
        public async Task AuditAsync(Guid? authorId, Guid? targetId, StaffSecurityOperation operation, Guid? sessionId, CancellationToken ct) =>
            await database.Query("""
                INSERT INTO public.staff_security_events(id,author_staff_id,target_staff_id,operation,session_id,occurred_at)
                VALUES (@id,@author,@target,@operation,@session,@now)
                """).WithParameter("id", Guid.CreateVersion7()).WithParameter("author", authorId, System.Data.DbType.Guid)
                .WithParameter("target", targetId, System.Data.DbType.Guid).WithParameter("operation", operation.ToString())
                .WithParameter("session", sessionId, System.Data.DbType.Guid).WithParameter("now", Now).ExecuteAsync(ct);
        private static DateTimeOffset Utc(DateTime value) => new(value,TimeSpan.Zero);
        private static StaffCredentialSession Session(SessionRow row) => new(row.Id,row.StaffId,Utc(row.CreatedAt),Utc(row.RefreshExpiresAt),row.FactorVerified,row.Revoked);
    }
    private sealed class AccountRow
    {
        public Guid Id { get; init; } public string Login { get; init; } = ""; public string? PasswordHash { get; init; }
        public string? Authenticator { get; init; } public bool SecondFactorEnabled { get; init; } public long LastAuthenticatorStep { get; init; }
        public string Role { get; init; } = ""; public bool Blocked { get; init; } public bool Archived { get; init; } public long Revision { get; init; }
    }
    private sealed class PasskeyRow
    { public string CredentialId { get; init; } = ""; public byte[] PublicKey { get; init; } = []; public long Counter { get; init; } public byte[] UserHandle { get; init; } = []; }
    private sealed class LinkRow
    { public Guid Id { get; init; } public string Hash { get; init; } = ""; public string Role { get; init; } = ""; public Guid? StaffId { get; init; } public DateTime ExpiresAt { get; init; } public bool Consumed { get; init; } }
    private sealed class ChallengeRow
    { public string Hash { get; init; } = ""; public string Purpose { get; init; } = ""; public Guid? StaffId { get; init; } public long Revision { get; init; } public string Payload { get; init; } = ""; public DateTime ExpiresAt { get; init; } }
    private sealed class SessionRow
    { public Guid Id { get; init; } public Guid StaffId { get; init; } public DateTime CreatedAt { get; init; } public DateTime RefreshExpiresAt { get; init; } public bool FactorVerified { get; init; } public bool Revoked { get; init; } }
}

using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Persistence;
using Microsoft.Extensions.Configuration;

namespace MelodyTrack.Rewrite.Init;

// This entry point is intentionally available only to the server-side Init executable.
internal static class AuthBootstrapCommand
{
    internal static async Task<int> RunAsync(IConfiguration configuration, Database database, CancellationToken cancellationToken)
    {
        var target = configuration["Auth:RecoverSuperuser"];
        if (target is not null && (!Guid.TryParse(target, out var id) || id == Guid.Empty)) return 2;
        if (!Uri.TryCreate(configuration["Auth:SetupUrl"], UriKind.Absolute, out var setup)
            || !(setup.Scheme == Uri.UriSchemeHttps || setup.Scheme == Uri.UriSchemeHttp && setup.IsLoopback)
            || !string.IsNullOrEmpty(setup.Fragment) || !string.IsNullOrEmpty(setup.Query)) return 2;
        var recoveryId = target is null ? (Guid?)null : Guid.Parse(target);
        var result = await CreateAsync(database, recoveryId, cancellationToken);
        if (result.IsFailure) return 2;
        // Functional secret output for the operator, never sent through the application's logger.
        await Console.Out.WriteLineAsync($"{setup.AbsoluteUri}#invitation={result.Value}");
        return 0;
    }

    internal static async Task<Result<string, ErrorList>> CreateAsync(Database database, Guid? recoveryId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (recoveryId == Guid.Empty) return ErrorList.Single(new IdentifierError.Empty());
        await using var transaction = await database.BeginTransactionAsync(cancellationToken);
        if (recoveryId is { } staff)
        {
            var authorized = await transaction.Query("""
                SELECT s.staff_id FROM public.staff_access_state s
                JOIN public.staff_credentials c ON c.staff_id=s.staff_id
                WHERE s.staff_id=@id AND s.role='superuser' AND NOT s.is_archived
                FOR UPDATE OF s
                """).WithParameter("id", staff).GetSingleOrDefaultAsync<Guid?>(cancellationToken);
            if (authorized is null) return ErrorList.Single(new ModelError.NotFound());
            await transaction.Query("UPDATE public.staff_access_state SET is_blocked=false WHERE staff_id=@id")
                .WithParameter("id", staff).ExecuteAsync(cancellationToken);
            await transaction.Query("""
                UPDATE public.staff_credentials SET password_hash=NULL,protected_authenticator=NULL,
                    second_factor_enabled=true,last_authenticator_step=-1,revision=revision+1 WHERE staff_id=@id
                """).WithParameter("id", staff).ExecuteAsync(cancellationToken);
            await transaction.Query("DELETE FROM public.staff_passkeys WHERE staff_id=@id")
                .WithParameter("id", staff).ExecuteAsync(cancellationToken);
            await transaction.Query("DELETE FROM public.staff_credential_challenges WHERE staff_id=@id")
                .WithParameter("id", staff).ExecuteAsync(cancellationToken);
            await transaction.Query("UPDATE public.staff_sessions SET revoked_at=COALESCE(revoked_at,clock_timestamp()) WHERE staff_id=@id")
                .WithParameter("id", staff).ExecuteAsync(cancellationToken);
            await transaction.Query("UPDATE public.staff_credential_links SET consumed=true WHERE staff_id=@id")
                .WithParameter("id", staff).ExecuteAsync(cancellationToken);
        }
        var secret = OpaqueTokens.Create();
        await transaction.Query("""
            INSERT INTO public.staff_credential_links(id,token_hash,role,staff_id,expires_at,consumed)
            VALUES (@id,@hash,'superuser',@staff,clock_timestamp()+INTERVAL '24 hours',false)
            """).WithParameter("id", Guid.CreateVersion7()).WithParameter("hash", OpaqueTokens.Hash(secret))
            .WithParameter("staff", recoveryId, System.Data.DbType.Guid).ExecuteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return secret;
    }
}

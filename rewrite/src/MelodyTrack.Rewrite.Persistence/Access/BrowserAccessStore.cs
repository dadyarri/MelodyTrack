using MelodyTrack.Rewrite.Application;

namespace MelodyTrack.Rewrite.Persistence.Access;

/// <summary>Resolves the browser allowlist without storing its raw secret or any role claims.</summary>
public sealed class BrowserAccessStore(Database database) : IBrowserAccessStore
{
    /// <summary>Finds the context for a canonical browser-secret digest.</summary>
    public Task<Guid?> ResolveAsync(string secretHash, CancellationToken cancellationToken) =>
        database.Query("SELECT id FROM public.browser_access_contexts WHERE secret_hash=@hash")
            .WithParameter("hash", secretHash).GetSingleOrDefaultAsync<Guid?>(cancellationToken);

    /// <summary>Creates an empty context before any account has been activated.</summary>
    public async Task<Guid> CreateAsync(string secretHash, CancellationToken cancellationToken)
    {
        if (!OpaqueTokens.IsValid(secretHash)) throw new ArgumentException("Invalid browser digest.", nameof(secretHash));
        var id = Guid.CreateVersion7();
        await database.Query("INSERT INTO public.browser_access_contexts(id,secret_hash) VALUES (@id,@hash)")
            .WithParameter("id", id).WithParameter("hash", secretHash).ExecuteAsync(cancellationToken);
        return id;
    }
}

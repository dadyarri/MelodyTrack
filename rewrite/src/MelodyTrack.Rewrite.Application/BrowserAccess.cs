namespace MelodyTrack.Rewrite.Application;

public interface IBrowserAccessContext
{
    Guid? ContextId { get; }
    bool IsTrustedInternal => false;
}

// Explicit seam for server-only provisioning and isolated persistence fixtures, never registered in the API.
public sealed class TrustedInternalBrowserAccessContext : IBrowserAccessContext
{
    public Guid? ContextId => null;
    public bool IsTrustedInternal => true;
}

public interface IBrowserAccessStore
{
    Task<Guid?> ResolveAsync(string secretHash, CancellationToken cancellationToken);
    Task<Guid> CreateAsync(string secretHash, CancellationToken cancellationToken);
}

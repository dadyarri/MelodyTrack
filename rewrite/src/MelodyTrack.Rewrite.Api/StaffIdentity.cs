using System.Security.Claims;
using MelodyTrack.Rewrite.Domain;

namespace MelodyTrack.Rewrite.Api;

internal static class StaffIdentity
{
    internal const string ActorKindClaim = "melodytrack.actor_kind";

    internal static bool TryRead(ClaimsPrincipal principal, out StaffId staffId)
    {
        staffId = default;
        var identities = principal.Identities.Where(identity => identity.IsAuthenticated).ToArray();
        if (identities.Length != 1) return false;
        var kinds = identities[0].FindAll(ActorKindClaim).ToArray();
        var ids = identities[0].FindAll(ClaimTypes.NameIdentifier).ToArray();
        if (kinds.Length != 1 || kinds[0].Value != "staff" || ids.Length != 1 || !Guid.TryParse(ids[0].Value, out var id) || id == Guid.Empty)
            return false;
        staffId = new(id);
        return true;
    }
}

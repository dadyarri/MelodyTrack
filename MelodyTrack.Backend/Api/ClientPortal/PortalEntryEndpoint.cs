using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.ClientPortal;

// Browser navigation lives outside the secured /api group and the generated API contract.
public static class PortalEntryEndpoint
{
    public static async Task<RedirectHttpResult> HandleAsync(
        string? token,
        AppDbContext db,
        ClientPortalSessionService sessionService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (token is null || token.Length is < 16 or > 128 || token.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            return TypedResults.Redirect("/portal/error");
        }

        var tokenHash = UserUtils.HashOpaqueToken(token);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Rotation/revocation takes the same row lock, so a stale link cannot create a session afterward.
        var link = await db.ClientPortalLoginLinks
            .FromSqlInterpolated($"SELECT * FROM \"ClientPortalLoginLinks\" WHERE \"TokenHash\" = {tokenHash} FOR UPDATE")
            .Include(item => item.User)
                .ThenInclude(item => item.Role)
            .SingleOrDefaultAsync(ct);
        if (link is null || link.RevokedAtUtc is not null || !link.User.Role.RoleName.IsClient() || link.User.ClientId is null)
        {
            return TypedResults.Redirect("/portal/error");
        }

        var session = await sessionService.CreateAsync(link.User, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        sessionService.IssueCookie(httpContext.Response, session);
        return TypedResults.Redirect("/portal");
    }
}

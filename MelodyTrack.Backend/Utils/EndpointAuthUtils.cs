using System.Security.Claims;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Extensions;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Utils;

public static class EndpointAuthUtils
{
    public sealed class CurrentUserContext
    {
        public required Ulid Id { get; init; }
        public required string Email { get; init; }
        public required UserRoles Role { get; init; }
        public Ulid? LinkedClientId { get; init; }
    }

    public static async Task<UserRoles?> GetCurrentUserRoleAsync(ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        return (await GetCurrentUserContextAsync(principal, db, ct))?.Role;
    }

    public static async Task<CurrentUserContext?> GetCurrentUserContextAsync(ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var email = principal.Claims.FirstOrDefault(e => e.Type == ClaimTypes.Name)?.Value;
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return await db.Users
            .AsNoTracking()
            .WhereEmailMatches(email)
            .Select(user => new CurrentUserContext
            {
                Id = user.Id,
                Email = user.Email,
                Role = user.Role.RoleName,
                LinkedClientId = user.ClientId
            })
            .FirstOrDefaultAsync(ct);
    }
}

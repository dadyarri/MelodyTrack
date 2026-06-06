using System.Security.Claims;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Utils;

public static class EndpointAuthUtils
{
    public static async Task<UserRoles?> GetCurrentUserRoleAsync(ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var email = principal.Claims.FirstOrDefault(e => e.Type == ClaimTypes.Name)?.Value;
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return await db.Users
            .AsNoTracking()
            .Where(user => user.Email == email)
            .Select(user => (UserRoles?)user.Role.RoleName)
            .FirstOrDefaultAsync(ct);
    }
}

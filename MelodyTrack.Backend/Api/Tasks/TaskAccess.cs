using System.Security.Claims;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Tasks;

internal static class TaskAccess
{
    public static async Task<User?> GetCurrentUserAsync(ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var email = principal.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.Name)?.Value;

        if (email is null)
        {
            return null;
        }

        return await db.Users
            .Include(user => user.Role)
            .FirstOrDefaultAsync(user => user.Email == email, ct);
    }

    public static bool CanAccessTasks(User user)
    {
        return user.Role.RoleName.IsAnyAdmin();
    }
}

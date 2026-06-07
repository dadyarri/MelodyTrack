using System.Security.Claims;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Extensions;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Dashboard;

internal static class DashboardAccess
{
    public static async Task<User?> GetCurrentUserAsync(ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var email = principal.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.Name)?.Value;

        if (email is null)
        {
            return null;
        }

        return await db.Users
            .AsNoTracking()
            .Include(user => user.Role)
            .WhereEmailMatches(email)
            .FirstOrDefaultAsync(ct);
    }

    public static bool CanViewDashboardAnalytics(User user)
    {
        return user.Role.RoleName.IsAnyAdmin();
    }

    public static bool IsProviderScoped(User user)
    {
        return !user.Role.RoleName.IsAnyAdmin();
    }
}

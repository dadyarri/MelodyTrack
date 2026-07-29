using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.Dashboard;

internal static class DashboardAccess
{
    public static bool CanViewDashboardAnalytics(User user)
    {
        return user.Role.RoleName.IsAnyAdmin();
    }

    public static bool IsProviderScoped(User user)
    {
        return !user.Role.RoleName.IsAnyAdmin();
    }
}

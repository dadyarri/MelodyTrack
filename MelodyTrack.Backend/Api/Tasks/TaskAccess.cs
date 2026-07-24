using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.Tasks;

internal static class TaskAccess
{
    public static bool CanAccessTasks(User user)
    {
        return user.Role.RoleName.IsAnyAdmin();
    }
}

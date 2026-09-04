using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.Notifications;

internal readonly record struct NotificationPrincipal(Ulid? UserId, Ulid? ClientId)
{
    public static NotificationPrincipal From(User user)
    {
        return user.Role.RoleName.IsClient()
            ? new NotificationPrincipal(null, user.ClientId)
            : new NotificationPrincipal(user.Id, null);
    }

    public bool IsValid => (UserId is null) != (ClientId is null);
}

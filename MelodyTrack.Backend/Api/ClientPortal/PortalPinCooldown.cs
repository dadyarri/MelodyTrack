namespace MelodyTrack.Backend.Api.ClientPortal;

public static class PortalPinCooldown
{
    public static DateTime? GetBlockedUntilUtc(int failedAttempts, DateTime? lastFailedAtUtc)
    {
        if (failedAttempts < 3 || lastFailedAtUtc is null)
        {
            return null;
        }

        var delay = failedAttempts switch
        {
            3 => TimeSpan.FromSeconds(30),
            4 => TimeSpan.FromMinutes(1),
            5 => TimeSpan.FromMinutes(5),
            6 => TimeSpan.FromMinutes(15),
            _ => TimeSpan.FromHours(1)
        };
        return DateTime.SpecifyKind(lastFailedAtUtc.Value, DateTimeKind.Utc).Add(delay);
    }
}

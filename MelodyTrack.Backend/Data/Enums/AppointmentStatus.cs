namespace MelodyTrack.Backend.Data.Enums;

public enum AppointmentStatus
{
    Planned = 0,
    Completed = 1,
    Cancelled = 2,
    Burned = 3
}

public static class AppointmentStatusExtensions
{
    extension(AppointmentStatus status)
    {
        public bool CountsAsRevenue()
        {
            return status is AppointmentStatus.Completed or AppointmentStatus.Burned;
        }

        public bool IsUpcoming()
        {
            return status == AppointmentStatus.Planned;
        }

        public string ToApiKey()
        {
            return status switch
            {
                AppointmentStatus.Completed => "completed",
                AppointmentStatus.Cancelled => "cancelled",
                AppointmentStatus.Burned => "burned",
                _ => "planned"
            };
        }
    }

    public static bool TryParseApiKey(string? value, out AppointmentStatus status)
    {
        status = AppointmentStatus.Planned;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "planned":
                status = AppointmentStatus.Planned;
                return true;
            case "completed":
                status = AppointmentStatus.Completed;
                return true;
            case "cancelled":
                status = AppointmentStatus.Cancelled;
                return true;
            case "burned":
                status = AppointmentStatus.Burned;
                return true;
            default:
                return false;
        }
    }
}

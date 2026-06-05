namespace MelodyTrack.Backend.Data.Enums;

public enum RecurringTaskStatus
{
    Completed = 0,
    Cancelled = 1,
    Delayed = 2
}

public static class RecurringTaskStatusExtensions
{
    public static string ToApiKey(this RecurringTaskStatus status)
    {
        return status switch
        {
            RecurringTaskStatus.Completed => "completed",
            RecurringTaskStatus.Cancelled => "cancelled",
            RecurringTaskStatus.Delayed => "delayed",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
    }
}

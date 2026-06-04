namespace MelodyTrack.Backend.Data.Enums;

public enum RecurringTaskStatus
{
    Completed = 0,
    Skipped = 1
}

public static class RecurringTaskStatusExtensions
{
    public static string ToApiKey(this RecurringTaskStatus status)
    {
        return status switch
        {
            RecurringTaskStatus.Completed => "completed",
            RecurringTaskStatus.Skipped => "skipped",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
    }
}

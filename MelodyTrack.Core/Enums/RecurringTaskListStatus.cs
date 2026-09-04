namespace MelodyTrack.Backend.Data.Enums;

public enum RecurringTaskListStatus
{
    Open = 0,
    Completed = 1,
    Cancelled = 2,
    Delayed = 3
}

public static class RecurringTaskListStatusExtensions
{
    public static bool TryParseApiKey(string? value, out RecurringTaskListStatus status)
    {
        status = RecurringTaskListStatus.Open;

        switch (value?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case "open":
                status = RecurringTaskListStatus.Open;
                return true;
            case "completed":
                status = RecurringTaskListStatus.Completed;
                return true;
            case "cancelled":
            case "skipped":
                status = RecurringTaskListStatus.Cancelled;
                return true;
            case "delayed":
                status = RecurringTaskListStatus.Delayed;
                return true;
            default:
                return false;
        }
    }
}

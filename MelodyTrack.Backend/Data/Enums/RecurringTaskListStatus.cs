namespace MelodyTrack.Backend.Data.Enums;

public enum RecurringTaskListStatus
{
    Open = 0,
    Completed = 1,
    Skipped = 2
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
            case "skipped":
                status = RecurringTaskListStatus.Skipped;
                return true;
            default:
                return false;
        }
    }
}

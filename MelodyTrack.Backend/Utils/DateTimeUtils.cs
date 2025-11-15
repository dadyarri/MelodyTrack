namespace MelodyTrack.Backend.Utils;

/// <summary>
///     Date time utilities
/// </summary>
public static class DateTimeUtils
{
    /// <summary>
    ///     Convert datetime to specified timezone
    /// </summary>
    /// <param name="date">Datetime</param>
    /// <param name="timeZoneId">Timezone</param>
    /// <returns>Local datetime</returns>
    public static DateTime ConvertDateToTimezone(DateTime date, string timeZoneId)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        if (date.Kind == DateTimeKind.Unspecified)
            date = DateTime.SpecifyKind(date, DateTimeKind.Utc);
        else if (date.Kind == DateTimeKind.Local) date = date.ToUniversalTime();

        return TimeZoneInfo.ConvertTimeFromUtc(date, tz);
    }
}
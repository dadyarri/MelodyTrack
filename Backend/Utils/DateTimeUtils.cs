namespace Backend.Utils;

/// <summary>
/// Утилиты даты/времени
/// </summary>
public static class DateTimeUtils
{
    /// <summary>
    /// Сконвертировать дату/время в указанную таймзону
    /// </summary>
    /// <param name="date">Дата</param>
    /// <param name="tz">Таймзона</param>
    /// <returns>Локальное время</returns>
    public static DateTime ConvertDateToTimezone(DateTime date, TimeZoneInfo tz)
    {
        if (date.Kind == DateTimeKind.Unspecified)
        {
            date = DateTime.SpecifyKind(date, DateTimeKind.Utc);
        }
        else if (date.Kind == DateTimeKind.Local)
        {
            date = date.ToUniversalTime();
        }

        return TimeZoneInfo.ConvertTimeFromUtc(date, tz);
    }
}
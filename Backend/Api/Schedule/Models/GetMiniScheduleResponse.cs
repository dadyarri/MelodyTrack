namespace Backend.Api.Schedule.Models;

/// <summary>
/// Ответ на запрос мини-расписания
/// </summary>
public class GetMiniScheduleResponse
{
    /// <summary>
    /// Элементы мини-расписания
    /// </summary>
    public required Dictionary<string, List<MiniScheduleItem>> Items { get; set; }
}

/// <summary>
/// Элементы мини-расписания
/// </summary>
public class MiniScheduleItem
{
    /// <summary>
    /// Имя
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Услуга
    /// </summary>
    public required string Service { get; set; }

    /// <summary>
    /// Время
    /// </summary>
    public required DateTime Time { get; set; }
}
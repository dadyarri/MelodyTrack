namespace Backend.Api.Schedule.Models;

/// <summary>
/// Тело запроса мини расписания
/// </summary>
public class GetMiniScheduleRequest
{
    /// <summary>
    /// Часовой пояс
    /// </summary>
    public string Timezone { get; set; }
}
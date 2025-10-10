using System.ComponentModel.DataAnnotations;

namespace Backend.Api.Schedule.Models;

/// <summary>
/// Запрос расписания на указанные даты
/// </summary>
public class GetScheduleRequest
{
    /// <summary>
    /// Дата начала
    /// </summary>
    [Required]
    public DateTime StartDate { get; set; }

    /// <summary>
    /// Дата окончания
    /// </summary>
    [Required]
    public DateTime EndDate { get; set; }
}
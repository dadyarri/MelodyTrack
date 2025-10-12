namespace Backend.Api.Schedule.Models;

/// <summary>
///     Тело запроса на создание записи услуги
/// </summary>
public class CreateServiceScheduleRequest
{
    /// <summary>
    ///     Идентификатор клиента
    /// </summary>
    public long ClientId { get; set; }

    /// <summary>
    ///     Идентификатор услуги
    /// </summary>
    public long ServiceId { get; set; }

    /// <summary>
    ///     Слот времени
    /// </summary>
    public DateTime Start { get; set; }
}
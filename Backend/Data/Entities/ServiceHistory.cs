namespace Backend.Data.Entities;

/// <summary>
/// История оказывания услуг
/// </summary>
public class ServiceHistory : BaseModel
{
    /// <summary>
    /// Услуга
    /// </summary>
    public required Service Service { get; set; } = null!;

    /// <summary>
    /// Клиент
    /// </summary>
    public required Client Client { get; set; } = null!;

    /// <summary>
    /// Дата начала
    /// </summary>
    public required DateTime StartDate { get; set; }

    /// <summary>
    /// Дата конца
    /// </summary>
    public required DateTime EndDate { get; set; }

    /// <summary>
    /// Выполнение
    /// </summary>
    public bool Completed { get; set; } = false;
}
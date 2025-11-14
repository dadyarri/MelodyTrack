namespace MelodyTrack.Migrator.OldData.Entities;

public class OldServiceHistory : OldBaseModel
{
    /// <summary>
    ///     Услуга
    /// </summary>
    public required OldService Service { get; set; } = null!;

    /// <summary>
    ///     Клиент
    /// </summary>
    public required OldClient Client { get; set; } = null!;

    /// <summary>
    ///     Дата начала
    /// </summary>
    public required DateTime StartDate { get; set; }

    /// <summary>
    ///     Дата конца
    /// </summary>
    public required DateTime EndDate { get; set; }

    /// <summary>
    ///     Выполнение
    /// </summary>
    public bool Completed { get; set; } = false;
}
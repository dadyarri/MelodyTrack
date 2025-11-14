namespace MelodyTrack.Migrator.OldData.Entities;

public class OldServicePriceHistory : OldBaseModel
{
    /// <summary>
    ///     Услуга
    /// </summary>
    public required OldService Service { get; set; }

    /// <summary>
    ///     Цена
    /// </summary>
    public required decimal Price { get; set; }

    /// <summary>
    ///     Дата вступления цены в силу
    /// </summary>
    public required DateTime EffectiveDate { get; set; }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{Service.Name} (Starting {EffectiveDate:dd.MM.yyyy} - {Price:C})";
    }
}
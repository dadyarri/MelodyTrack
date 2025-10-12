namespace Backend.Data.Entities;

/// <summary>
///     История цен на услугу
/// </summary>
public class ServicePriceHistory : BaseModel
{
    /// <summary>
    ///     Услуга
    /// </summary>
    public required Service Service { get; set; }

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
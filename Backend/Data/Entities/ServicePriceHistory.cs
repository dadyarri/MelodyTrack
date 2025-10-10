namespace Backend.Data.Entities;

public class ServicePriceHistory : BaseModel
{
    public required Service Service { get; set; }

    public required decimal Price { get; set; }

    public required DateTime EffectiveDate { get; set; }

    public override string ToString()
    {
        return $"{Service.Name} (Starting {EffectiveDate:dd.MM.yyyy} - {Price:C})";
    }
}
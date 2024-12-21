using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Common;

public class ServicePriceHistory : BaseModel
{
    public required Service Service { get; set; }

    [Range(0, (double)decimal.MaxValue)] public required decimal Price { get; set; }

    public required DateTime EffectiveDate { get; set; }

    public override string ToString()
    {
        return $"{Service.Name} (Starting {EffectiveDate:dd.MM.yyyy} - {Price:C})";
    }
}
using System.ComponentModel.DataAnnotations;

namespace Backend.Data.Entities;

public class Service : BaseModel
{
    [MaxLength(200)] public required string Name { get; set; }

    public string Description { get; set; } = string.Empty;

    public required User Provider { get; set; }
}
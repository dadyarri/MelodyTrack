using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Backend.Data.Models;

public class Service : BaseModel
{
    [MaxLength(200)] public required string Name { get; set; }

    [MaxLength(200)] public string? Description { get; set; }
}
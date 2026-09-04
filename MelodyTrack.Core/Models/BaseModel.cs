using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Backend.Data.Models;

public class BaseModel
{
    [Key]
    public Ulid Id { get; set; }
}
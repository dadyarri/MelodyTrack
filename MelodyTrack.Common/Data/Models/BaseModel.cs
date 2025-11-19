using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Common.Data.Models;

public class BaseModel
{
    [Key]
    public Ulid Id { get; set; }
}
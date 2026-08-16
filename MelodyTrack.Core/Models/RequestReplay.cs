using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Backend.Data.Models;

public class RequestReplay : BaseModel
{
    [MaxLength(100)]
    public required string Endpoint { get; set; }

    [MaxLength(100)]
    public required string ReplayKey { get; set; }

    public required Ulid CallerId { get; set; }

    [MaxLength(64)]
    public required string RequestFingerprint { get; set; }

    public Ulid? ResponseEntityId { get; set; }

    public required DateTime CreatedAtUtc { get; set; }
}

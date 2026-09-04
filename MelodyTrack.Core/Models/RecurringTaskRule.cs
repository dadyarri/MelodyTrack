using System.ComponentModel.DataAnnotations;
using MelodyTrack.Backend.Data.Enums;

namespace MelodyTrack.Backend.Data.Models;

public class RecurringTaskRule : BaseModel
{
    [MaxLength(200)]
    public required string Name { get; set; }

    public required RecurringTaskType Type { get; set; }

    public required bool IsEnabled { get; set; }

    [MaxLength(1000)]
    public required string MessageTemplate { get; set; }

    public int? OffsetMinutes { get; set; }

    public int? CooldownDays { get; set; }

    public required DateTime CreatedAtUtc { get; set; }

    public required DateTime UpdatedAtUtc { get; set; }
}

using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.Tasks.Requests;

public class UpdateRecurringTaskRuleRequest : IValidatableRequest
{
    [JsonIgnore]
    public Ulid Id { get; set; }
    public Ulid? ExpectedActivityId { get; set; }
    public required bool IsEnabled { get; set; }
    public required string MessageTemplate { get; set; }
    public int? OffsetMinutes { get; set; }
    public int? CooldownDays { get; set; }
}

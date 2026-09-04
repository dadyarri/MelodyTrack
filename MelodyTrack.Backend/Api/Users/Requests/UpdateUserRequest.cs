using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.Users.Requests;

public class UpdateUserRequest : IValidatableRequest
{
    [JsonIgnore]
    public Ulid Id { get; set; }
    public Ulid? ExpectedActivityId { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? Phone { get; set; }
    public string? Telegram { get; set; }
    public string? Vk { get; set; }
}

using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Backend.Api.Notifications.Requests;

public sealed class NotificationIdRequest
{
    [FromRoute(Name = "id")]
    [JsonIgnore]
    public Ulid Id { get; set; }
}

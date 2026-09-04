
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Backend.Api.Services.Requests;

public class UpdateServicePriceRequest
{
    [JsonIgnore]
    public Ulid Id { get; set; }
    public Ulid? ExpectedActivityId { get; set; }
    public decimal Price { get; set; }
}

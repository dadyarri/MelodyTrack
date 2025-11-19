using FastEndpoints;

namespace MelodyTrack.Common.Api.Services.Requests;

public class UpdateServicePriceRequest
{
    [BindFrom("id")]
    public Ulid Id { get; set; }
    public decimal Price { get; set; }
}
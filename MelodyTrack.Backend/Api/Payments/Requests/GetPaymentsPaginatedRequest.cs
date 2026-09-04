using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Attributes;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.Payments.Requests;

public class GetPaymentsPaginatedRequest : PaginatedRequest
{
    [FromQuery(Name = "search")]
    public string? Search { get; set; }

    [FuzzyPath(typeof(Payment), "Client.FirstName")]
    public string? FirstName { get; set; }

    [FuzzyPath(typeof(Payment), "Client.LastName")]
    public string? LastName { get; set; }

    [FromQuery(Name = "clientId")]
    public Ulid? ClientId { get; set; }

    [FromQuery(Name = "serviceId")]
    public Ulid? ServiceId { get; set; }

    [FromQuery(Name = "start")]
    public DateTime? Start { get; set; }

    [FromQuery(Name = "end")]
    public DateTime? End { get; set; }
}

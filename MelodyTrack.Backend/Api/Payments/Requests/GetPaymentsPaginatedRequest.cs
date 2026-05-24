using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Attributes;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.Payments.Requests;

public class GetPaymentsPaginatedRequest : PaginatedRequest
{
    [BindFrom("search")]
    public string? Search { get; set; }

    [FuzzyPath(typeof(Payment), "Client.FirstName")]
    public string? FirstName { get; set; }

    [FuzzyPath(typeof(Payment), "Client.LastName")]
    public string? LastName { get; set; }

    [BindFrom("clientId")]
    public Ulid? ClientId { get; set; }

    [BindFrom("serviceId")]
    public Ulid? ServiceId { get; set; }

    [BindFrom("start")]
    public DateTime? Start { get; set; }

    [BindFrom("end")]
    public DateTime? End { get; set; }
}

using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Attributes;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.Payments.Requests;

public class GetPaymentsPaginatedRequest : PaginatedRequest
{
    [FuzzyPath(typeof(Payment), "Client.FirstName")]
    public string? FirstName { get; set; }
    [FuzzyPath(typeof(Payment), "Client.LastName")]
    public string? LastName { get; set; }
}
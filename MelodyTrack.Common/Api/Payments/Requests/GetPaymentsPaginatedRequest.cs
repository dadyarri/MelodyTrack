using MelodyTrack.Common.Api.Common.Requests;
using MelodyTrack.Common.Attributes;
using MelodyTrack.Common.Data.Models;

namespace MelodyTrack.Common.Api.Payments.Requests;

public class GetPaymentsPaginatedRequest : PaginatedRequest
{
    [FuzzyPath(typeof(Payment), "Client.FirstName")]
    public string? FirstName { get; set; }
    [FuzzyPath(typeof(Payment), "Client.LastName")]
    public string? LastName { get; set; }
}
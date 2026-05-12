using MelodyTrack.Backend.Api.Common.Responses;

namespace MelodyTrack.Backend.Api.Payments.Responses;

public class GetPaymentsResponse : PaginatedResponse<GetPaymentsDto>
{
    public required MoneyListSummaryDto Summary { get; set; }
}

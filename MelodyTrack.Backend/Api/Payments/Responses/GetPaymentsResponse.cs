using MelodyTrack.Backend.Api.Common.Responses;

namespace MelodyTrack.Backend.Api.Payments.Responses;

public class GetPaymentsResponse : PaginatedResponse<GetPaymentsDto>
{
    public required PaymentsSummaryDto Summary { get; set; }
}

public class PaymentsSummaryDto
{
    public decimal TotalAmount { get; set; }
    public int ItemsCount { get; set; }
    public DateTime? FirstPaymentAtUtc { get; set; }
    public DateTime? LastPaymentAtUtc { get; set; }
}

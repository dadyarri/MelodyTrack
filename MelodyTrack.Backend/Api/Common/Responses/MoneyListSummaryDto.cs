namespace MelodyTrack.Backend.Api.Common.Responses;

public class MoneyListSummaryDto
{
    public decimal TotalAmount { get; set; }
    public int ItemsCount { get; set; }
    public DateTime? FirstItemAtUtc { get; set; }
    public DateTime? LastItemAtUtc { get; set; }
}

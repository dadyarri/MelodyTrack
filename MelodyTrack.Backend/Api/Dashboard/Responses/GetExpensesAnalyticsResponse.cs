namespace MelodyTrack.Backend.Api.Dashboard.Responses;

public class GetExpensesAnalyticsResponse
{
    public required DateTime StartDate { get; set; }
    public required DateTime EndDate { get; set; }
    public required string GroupBy { get; set; }
    public required decimal TotalExpenses { get; set; }
    public required decimal TotalRevenue { get; set; }
    public decimal? ExpenseToRevenueRatio { get; set; }
    public required int ExpensesCount { get; set; }
    public required List<ExpenseCategoryAnalyticsDto> Categories { get; set; }
    public required List<ExpenseDynamicsBucketDto> Dynamics { get; set; }
}

public class ExpenseCategoryAnalyticsDto
{
    public Ulid? CategoryId { get; set; }
    public required string CategoryName { get; set; }
    public required decimal Amount { get; set; }
    public decimal? Share { get; set; }
}

public class ExpenseDynamicsBucketDto
{
    public required DateTime StartDate { get; set; }
    public required DateTime EndDate { get; set; }
    public required decimal Expenses { get; set; }
    public decimal? ChangeFromPrevious { get; set; }
    public decimal? ChangePercentFromPrevious { get; set; }
}

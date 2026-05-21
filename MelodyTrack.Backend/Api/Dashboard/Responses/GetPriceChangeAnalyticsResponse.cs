namespace MelodyTrack.Backend.Api.Dashboard.Responses;

public class GetPriceChangeAnalyticsResponse
{
    public required DateTime StartDate { get; set; }
    public required DateTime EndDate { get; set; }
    public required int WindowDays { get; set; }
    public required int TotalChanges { get; set; }
    public required int PriceIncreasesCount { get; set; }
    public required int PriceDecreasesCount { get; set; }
    public required int PositiveRevenueImpactCount { get; set; }
    public required int NegativeDemandImpactCount { get; set; }
    public required List<PriceChangeAnalyticsDto> Changes { get; set; }
}

public class PriceChangeAnalyticsDto
{
    public required Ulid ServiceId { get; set; }
    public required string ServiceName { get; set; }
    public required DateTime EffectiveDate { get; set; }
    public required decimal OldPrice { get; set; }
    public required decimal NewPrice { get; set; }
    public required decimal PriceChange { get; set; }
    public decimal? PriceChangePercent { get; set; }
    public required int AffectedAppointmentsCount { get; set; }
    public required decimal RevenueBefore { get; set; }
    public required decimal RevenueAfter { get; set; }
    public required decimal RevenueChange { get; set; }
    public decimal? RevenueChangePercent { get; set; }
    public required int AppointmentsBefore { get; set; }
    public required int AppointmentsAfter { get; set; }
    public required int AppointmentChange { get; set; }
    public decimal? AppointmentChangePercent { get; set; }
    public required int CompletedAppointmentsBefore { get; set; }
    public required int CompletedAppointmentsAfter { get; set; }
    public decimal? CancellationShareBefore { get; set; }
    public decimal? CancellationShareAfter { get; set; }
    public decimal? BurnedShareBefore { get; set; }
    public decimal? BurnedShareAfter { get; set; }
    public decimal? AverageReceiptBefore { get; set; }
    public decimal? AverageReceiptAfter { get; set; }
    public required decimal ExpensesBefore { get; set; }
    public required decimal ExpensesAfter { get; set; }
    public required decimal NetProfitBefore { get; set; }
    public required decimal NetProfitAfter { get; set; }
    public required decimal ProfitImpact { get; set; }
    public decimal? PriceElasticity { get; set; }
    public decimal? AdditionalRevenue { get; set; }
    public required List<PriceChangeTeacherImpactDto> Teachers { get; set; }
}

public class PriceChangeTeacherImpactDto
{
    public Ulid? TeacherId { get; set; }
    public required string TeacherDisplayName { get; set; }
    public required decimal RevenueBefore { get; set; }
    public required decimal RevenueAfter { get; set; }
    public required int AppointmentsBefore { get; set; }
    public required int AppointmentsAfter { get; set; }
    public decimal? AverageReceiptBefore { get; set; }
    public decimal? AverageReceiptAfter { get; set; }
    public decimal? CancellationShareBefore { get; set; }
    public decimal? CancellationShareAfter { get; set; }
    public decimal? BurnedShareBefore { get; set; }
    public decimal? BurnedShareAfter { get; set; }
}

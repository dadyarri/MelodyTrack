namespace MelodyTrack.Backend.Api.Dashboard.Responses;

public class GetRevenueAnalyticsResponse
{
    public required DateTime StartDate { get; set; }
    public required DateTime EndDate { get; set; }
    public required decimal TotalRevenue { get; set; }
    public required decimal PlannedRevenue { get; set; }
    public required decimal TotalExpenses { get; set; }
    public required decimal NetProfit { get; set; }
    public decimal? AverageReceipt { get; set; }
    public required int RevenueCountedAppointmentsCount { get; set; }
    public required int PlannedAppointmentsCount { get; set; }
    public required List<TeacherRevenueAnalyticsDto> Teachers { get; set; }
}

public class TeacherRevenueAnalyticsDto
{
    public Ulid? TeacherId { get; set; }
    public required string TeacherDisplayName { get; set; }
    public required decimal Revenue { get; set; }
    public decimal? RevenueShare { get; set; }
    public decimal? AverageReceipt { get; set; }
    public required int RevenueCountedAppointmentsCount { get; set; }
    public required int CompletedAppointmentsCount { get; set; }
    public required int BurnedAppointmentsCount { get; set; }
    public required int ServicesProvidedCount { get; set; }
}

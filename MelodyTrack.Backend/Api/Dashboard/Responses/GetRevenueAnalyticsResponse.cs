namespace MelodyTrack.Backend.Api.Dashboard.Responses;

public class GetRevenueAnalyticsResponse
{
    public required DateTime StartDate { get; set; }
    public required DateTime EndDate { get; set; }
    public required string GroupBy { get; set; }
    public required decimal TotalRevenue { get; set; }
    public required decimal PlannedRevenue { get; set; }
    public required decimal TotalExpenses { get; set; }
    public required decimal NetProfit { get; set; }
    public decimal? AverageReceipt { get; set; }
    public required int RevenueCountedAppointmentsCount { get; set; }
    public required int PlannedAppointmentsCount { get; set; }
    public required List<TeacherRevenueAnalyticsDto> Teachers { get; set; }
    public required List<ClientRevenueAnalyticsDto> Clients { get; set; }
    public required List<ServiceRevenueAnalyticsDto> Services { get; set; }
    public required List<NetProfitBucketDto> NetProfitDynamics { get; set; }
    public required List<NetProfitBucketDto> MostProfitablePeriods { get; set; }
    public required List<NetProfitBucketDto> UnprofitablePeriods { get; set; }
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

public class ClientRevenueAnalyticsDto
{
    public required Ulid ClientId { get; set; }
    public required string ClientDisplayName { get; set; }
    public required decimal Revenue { get; set; }
    public decimal? RevenueShare { get; set; }
    public decimal? AverageReceipt { get; set; }
    public required int RevenueCountedAppointmentsCount { get; set; }
}

public class ServiceRevenueAnalyticsDto
{
    public required Ulid ServiceId { get; set; }
    public required string ServiceName { get; set; }
    public required decimal Revenue { get; set; }
    public decimal? RevenueShare { get; set; }
    public decimal? AverageReceipt { get; set; }
    public required int RevenueCountedAppointmentsCount { get; set; }
    public required int CompletedAppointmentsCount { get; set; }
    public required int BurnedAppointmentsCount { get; set; }
}

public class NetProfitBucketDto
{
    public required DateTime StartDate { get; set; }
    public required DateTime EndDate { get; set; }
    public required decimal Revenue { get; set; }
    public required decimal Expenses { get; set; }
    public required decimal NetProfit { get; set; }
    public decimal? ChangeFromPrevious { get; set; }
    public decimal? ChangePercentFromPrevious { get; set; }
    public decimal? LossPercentageRelativeToRevenue { get; set; }
}

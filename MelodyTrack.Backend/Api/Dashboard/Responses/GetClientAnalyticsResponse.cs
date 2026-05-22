namespace MelodyTrack.Backend.Api.Dashboard.Responses;

public class GetClientAnalyticsResponse
{
    public required DateTime StartDate { get; set; }
    public required DateTime EndDate { get; set; }
    public required DateTime PreviousPeriodStartDate { get; set; }
    public required DateTime PreviousPeriodEndDate { get; set; }
    public required int TotalClientsCount { get; set; }
    public required int ActiveNowClientsCount { get; set; }
    public required int InactiveClientsCount { get; set; }
    public required int ActiveClientsCount { get; set; }
    public required int PreviousPeriodActiveClientsCount { get; set; }
    public required int RetainedClientsCount { get; set; }
    public decimal? RetentionRate { get; set; }
    public required int NewClientsCount { get; set; }
    public required int ReturnedClientsCount { get; set; }
    public decimal? ReturningClientsShare { get; set; }
    public required int LostClientsCount { get; set; }
    public decimal? LostShare { get; set; }
    public required int AtRiskClientsCount { get; set; }
    public decimal? AverageIntervalDays { get; set; }
    public decimal? AverageLifetimeValue { get; set; }
    public decimal? AverageClientLifetimeDays { get; set; }
    public required int VipClientsCount { get; set; }
    public required int RegularClientsCount { get; set; }
    public required int SingleTimeClientsCount { get; set; }
    public required int DebtorsCount { get; set; }
    public required List<ClientSourceAnalyticsDto> Sources { get; set; }
    public required List<ClientAnalyticsDto> Clients { get; set; }
    public required List<ClientRfmAnalyticsDto> RfmClients { get; set; }
}

public class ClientSourceAnalyticsDto
{
    public required string SourceName { get; set; }
    public required int ClientsCount { get; set; }
    public required int ActiveClientsCount { get; set; }
    public required int PreviousPeriodActiveClientsCount { get; set; }
    public required int RetainedClientsCount { get; set; }
    public decimal? RetentionRate { get; set; }
    public required int NewClientsCount { get; set; }
    public decimal? NewClientsShare { get; set; }
    public required int LostClientsCount { get; set; }
    public decimal? LostShare { get; set; }
    public required decimal Revenue { get; set; }
    public decimal? AverageLifetimeValue { get; set; }
}

public class ClientAnalyticsDto
{
    public required Ulid ClientId { get; set; }
    public required string ClientDisplayName { get; set; }
    public required string SourceName { get; set; }
    public required decimal LifetimeValue { get; set; }
    public required int RevenueCountedAppointmentsCount { get; set; }
    public required int CompletedAppointmentsCount { get; set; }
    public decimal? AverageIntervalDays { get; set; }
    public decimal? LifetimeDays { get; set; }
    public int? DaysSinceLastAppointment { get; set; }
    public required DateTime CreatedAtUtc { get; set; }
    public DateTime? FirstAppointmentAtUtc { get; set; }
    public DateTime? LastAppointmentAtUtc { get; set; }
    public required decimal Debt { get; set; }
    public required bool IsLost { get; set; }
    public required bool IsAtRisk { get; set; }
    public required bool IsVip { get; set; }
    public required bool IsRegular { get; set; }
    public required bool IsSingleTime { get; set; }
    public required bool IsDebtor { get; set; }
    public required bool IsNew { get; set; }
    public required bool IsReturned { get; set; }
}

public class ClientRfmAnalyticsDto
{
    public required Ulid ClientId { get; set; }
    public required string ClientDisplayName { get; set; }
    public required string SourceName { get; set; }
    public int? RecencyDays { get; set; }
    public required int Frequency { get; set; }
    public required decimal Monetary { get; set; }
    public required int RecencyScore { get; set; }
    public required int FrequencyScore { get; set; }
    public required int MonetaryScore { get; set; }
    public required string RfmScore { get; set; }
    public required string Segment { get; set; }
}

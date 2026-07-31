namespace MelodyTrack.Backend.Api.Reports.Responses;

public sealed class ReportContextDto
{
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
    public required string Timezone { get; init; }
    public Ulid? ProviderId { get; init; }
    public required string ScopeLabel { get; init; }
    public required string GroupBy { get; init; }
    public required List<ReportProviderDto> Providers { get; init; }
}

public sealed class ReportProviderDto
{
    public required Ulid Id { get; init; }
    public required string DisplayName { get; init; }
}

public sealed class WorkReportResponse
{
    public required ReportContextDto Context { get; init; }
    public required WorkReportSummaryDto Summary { get; init; }
    public required List<WorkStatusDto> Statuses { get; init; }
    public required List<WorkTrendDto> Trend { get; init; }
    public required List<WorkProviderDto> Providers { get; init; }
    public required List<WorkServiceDto> Services { get; init; }
    public required List<WorkHourDto> BusyHours { get; init; }
}

public sealed class WorkReportSummaryDto
{
    public required int Appointments { get; init; }
    public required int Completed { get; init; }
    public required int Burned { get; init; }
    public required decimal WorkingCapacityHours { get; init; }
    public required decimal OccupiedWorkingHours { get; init; }
    public required decimal FreeWorkingHours { get; init; }
    public decimal? UtilizationPercent { get; init; }
    public decimal? CancellationPercent { get; init; }
}

public sealed class WorkStatusDto
{
    public required string Status { get; init; }
    public required int Count { get; init; }
    public decimal? SharePercent { get; init; }
}

public sealed class WorkTrendDto
{
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
    public required int Appointments { get; init; }
    public required int Completed { get; init; }
    public required int Cancelled { get; init; }
    public required int Burned { get; init; }
    public required decimal WorkingCapacityHours { get; init; }
    public required decimal OccupiedWorkingHours { get; init; }
    public required decimal FreeWorkingHours { get; init; }
    public decimal? UtilizationPercent { get; init; }
}

public sealed class WorkProviderDto
{
    public Ulid? ProviderId { get; init; }
    public required string ProviderName { get; init; }
    public required int Appointments { get; init; }
    public required int Completed { get; init; }
    public required int Cancelled { get; init; }
    public required int Burned { get; init; }
    public required decimal WorkingCapacityHours { get; init; }
    public required decimal OccupiedWorkingHours { get; init; }
    public required decimal FreeWorkingHours { get; init; }
    public decimal? UtilizationPercent { get; init; }
}

public sealed class WorkServiceDto
{
    public required Ulid ServiceId { get; init; }
    public required string ServiceName { get; init; }
    public required int Appointments { get; init; }
    public required int Completed { get; init; }
    public required int Burned { get; init; }
    public required decimal Revenue { get; init; }
}

public sealed class WorkHourDto
{
    public required int Hour { get; init; }
    public required int Appointments { get; init; }
    public required int Completed { get; init; }
    public required int Cancelled { get; init; }
}

public sealed class FinanceReportResponse
{
    public required ReportContextDto Context { get; init; }
    public required FinanceReportSummaryDto Summary { get; init; }
    public required List<FinanceTrendDto> Trend { get; init; }
    public required List<FinanceExpenseCategoryDto> ExpenseCategories { get; init; }
    public required List<FinanceDebtorDto> Debtors { get; init; }
    public required List<FinanceServiceDto> Services { get; init; }
}

public sealed class FinanceReportSummaryDto
{
    public required decimal Revenue { get; init; }
    public decimal? Payments { get; init; }
    public decimal? Expenses { get; init; }
    public decimal? NetProfit { get; init; }
    public decimal? OutstandingDebt { get; init; }
    public decimal? AverageRevenuePerVisit { get; init; }
    public required int RevenueAppointments { get; init; }
    public required bool OrganizationOnlyFiguresAvailable { get; init; }
}

public sealed class FinanceTrendDto
{
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
    public required decimal Revenue { get; init; }
    public decimal? Payments { get; init; }
    public decimal? Expenses { get; init; }
    public decimal? NetProfit { get; init; }
}

public sealed class FinanceExpenseCategoryDto
{
    public required string CategoryName { get; init; }
    public required decimal Amount { get; init; }
}

public sealed class FinanceDebtorDto
{
    public required Ulid ClientId { get; init; }
    public required string ClientName { get; init; }
    public required decimal Revenue { get; init; }
    public required decimal Payments { get; init; }
    public required decimal Debt { get; init; }
}

public sealed class FinanceServiceDto
{
    public required Ulid ServiceId { get; init; }
    public required string ServiceName { get; init; }
    public required int Appointments { get; init; }
    public required decimal Revenue { get; init; }
}

public sealed class ClientsReportResponse
{
    public required ReportContextDto Context { get; init; }
    public required ClientsReportSummaryDto Summary { get; init; }
    public required List<ClientActivityTrendDto> Trend { get; init; }
    public required List<ClientSourceReportDto> Sources { get; init; }
    public required List<ClientValueReportDto> Clients { get; init; }
}

public sealed class ClientsReportSummaryDto
{
    public required int AcquiredClients { get; init; }
    public required int ActiveClients { get; init; }
    public required int RetainedClients { get; init; }
    public decimal? RetentionPercent { get; init; }
    public required int AtRiskClients { get; init; }
    public required int LostClients { get; init; }
    public required int OnVacationClients { get; init; }
    public decimal? AverageVisitFrequency { get; init; }
    public decimal? AverageClientValue { get; init; }
}

public sealed class ClientActivityTrendDto
{
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
    public required int AcquiredClients { get; init; }
    public required int ActiveClients { get; init; }
    public required int Visits { get; init; }
}

public sealed class ClientSourceReportDto
{
    public required string SourceName { get; init; }
    public required int AcquiredClients { get; init; }
    public required int ActiveClients { get; init; }
    public required decimal ClientValue { get; init; }
}

public sealed class ClientValueReportDto
{
    public required Ulid ClientId { get; init; }
    public required string ClientName { get; init; }
    public required string SourceName { get; init; }
    public required int Visits { get; init; }
    public required decimal Value { get; init; }
    public decimal? AverageIntervalDays { get; init; }
    public DateTime? LastVisitAtUtc { get; init; }
    public required string ActivityState { get; init; }
}

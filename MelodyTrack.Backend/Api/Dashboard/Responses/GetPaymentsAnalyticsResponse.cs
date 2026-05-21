namespace MelodyTrack.Backend.Api.Dashboard.Responses;

public class GetPaymentsAnalyticsResponse
{
    public required DateTime StartDate { get; set; }
    public required DateTime EndDate { get; set; }
    public required int UnpaidAppointmentsCount { get; set; }
    public required int DebtorsCount { get; set; }
    public required decimal TotalDebt { get; set; }
    public decimal? AveragePaymentDelayDays { get; set; }
    public decimal? MedianPaymentDelayDays { get; set; }
    public decimal? MaxPaymentDelayDays { get; set; }
    public required List<ClientPaymentsAnalyticsDto> Clients { get; set; }
    public required List<TeacherPaymentsAnalyticsDto> Teachers { get; set; }
    public required List<ServicePaymentsAnalyticsDto> Services { get; set; }
}

public class ClientPaymentsAnalyticsDto
{
    public required Ulid ClientId { get; set; }
    public required string ClientDisplayName { get; set; }
    public required decimal TotalRevenue { get; set; }
    public required decimal TotalPayments { get; set; }
    public required decimal Balance { get; set; }
    public required decimal Debt { get; set; }
    public required int UnpaidAppointmentsCount { get; set; }
    public decimal? AveragePaymentDelayDays { get; set; }
    public decimal? MedianPaymentDelayDays { get; set; }
    public decimal? MaxPaymentDelayDays { get; set; }
}

public class TeacherPaymentsAnalyticsDto
{
    public Ulid? TeacherId { get; set; }
    public required string TeacherDisplayName { get; set; }
    public required decimal TotalRevenue { get; set; }
    public required decimal OutstandingDebt { get; set; }
    public required int UnpaidAppointmentsCount { get; set; }
    public decimal? AveragePaymentDelayDays { get; set; }
    public decimal? MedianPaymentDelayDays { get; set; }
    public decimal? MaxPaymentDelayDays { get; set; }
}

public class ServicePaymentsAnalyticsDto
{
    public required Ulid ServiceId { get; set; }
    public required string ServiceName { get; set; }
    public required decimal TotalRevenue { get; set; }
    public required decimal OutstandingDebt { get; set; }
    public required int UnpaidAppointmentsCount { get; set; }
    public decimal? AveragePaymentDelayDays { get; set; }
    public decimal? MedianPaymentDelayDays { get; set; }
    public decimal? MaxPaymentDelayDays { get; set; }
}

using Facet;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.Clients.Responses;

[Facet(typeof(ClientContacts), GenerateToSource = false, NullableProperties = true)]
public partial class ClientHistoryContactsDto;

public class ClientHistoryResponse
{
    public required ClientWithBalanceDto Client { get; set; }
    public required ClientHistorySummaryDto Summary { get; set; }
    public required List<ClientHistoryPaymentDto> RecentPayments { get; set; }
    public required PaginatedResponse<ClientHistoryAppointmentDto> Appointments { get; set; }
}

public class ClientHistorySummaryDto
{
    public decimal TotalPayments { get; set; }
    public int PaymentsCount { get; set; }
    public int CompletedAppointmentsCount { get; set; }
    public int UpcomingAppointmentsCount { get; set; }
    public DateTime? LastPaymentAtUtc { get; set; }
    public DateTime? LastVisitAtUtc { get; set; }
    public DateTime? NextAppointmentAtUtc { get; set; }
}

public class ClientHistoryPaymentDto
{
    public required Ulid Id { get; set; }
    public required decimal Amount { get; set; }
    public required DateTime Date { get; set; }
    public required string Description { get; set; }
    public string? ServiceName { get; set; }
}

public class ClientHistoryAppointmentDto
{
    public required Ulid Id { get; set; }
    public required DateTime StartDate { get; set; }
    public required DateTime EndDate { get; set; }
    public required string ServiceName { get; set; }
    public string? ProviderDisplayName { get; set; }
    public required string Status { get; set; }
    public Ulid? CourseThemeId { get; set; }
    public string? CourseThemeTitle { get; set; }
    public string? LessonNotes { get; set; }
}

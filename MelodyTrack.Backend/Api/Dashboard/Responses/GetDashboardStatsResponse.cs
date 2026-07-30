namespace MelodyTrack.Backend.Api.Dashboard.Responses;

public class GetDashboardStatsResponse
{
    public int PersonalClientsCount { get; set; }
    public decimal MonthIncome { get; set; }
    public required DashboardScheduleDayResponse Today { get; set; }
    public required DashboardScheduleDayResponse Tomorrow { get; set; }
    public OrganizationDashboardResponse? Organization { get; set; }
}

public class OrganizationDashboardResponse
{
    public int TotalClients { get; set; }
    public int DebtorsCount { get; set; }
    public decimal TotalDebt { get; set; }
    public decimal TotalPositiveBalance { get; set; }
    public int AppointmentsToday { get; set; }
    public int AppointmentsTomorrow { get; set; }
    public decimal MonthIncome { get; set; }
    public decimal MonthExpenses { get; set; }
    public decimal MonthNet { get; set; }
}

public class DashboardScheduleDayResponse
{
    public required DateOnly Date { get; set; }
    public int Count => Appointments.Count;
    public required List<DashboardAppointmentResponse> Appointments { get; set; }
}

public class DashboardAppointmentResponse
{
    public required Ulid Id { get; set; }
    public required DashboardClientResponse Client { get; set; }
    public required DashboardServiceResponse Service { get; set; }
    public required DateTime StartDate { get; set; }
    public required DateTime EndDate { get; set; }
    public required string Status { get; set; }
}

public class DashboardClientResponse
{
    public required Ulid Id { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public DashboardClientContactsResponse? Contacts { get; set; }
}

public class DashboardClientContactsResponse
{
    public string? Telegram { get; set; }
    public string? Vk { get; set; }
    public string? Phone { get; set; }
}

public class DashboardServiceResponse
{
    public required Ulid Id { get; set; }
    public required string Name { get; set; }
}

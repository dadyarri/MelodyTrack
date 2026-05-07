namespace MelodyTrack.Backend.Api.Dashboard.Responses;

public class GetDashboardStatsResponse
{
    public int TotalClients { get; set; }
    public int DebtorsCount { get; set; }
    public decimal TotalDebt { get; set; }
    public int AppointmentsToday { get; set; }
    public int AppointmentsTomorrow { get; set; }
    public decimal MonthIncome { get; set; }
    public decimal MonthExpenses { get; set; }
    public decimal MonthNet { get; set; }
}

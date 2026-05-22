namespace MelodyTrack.Backend.Api.Dashboard.Responses;

public class GetAppointmentsAnalyticsResponse
{
    public required DateTime StartDate { get; set; }
    public required DateTime EndDate { get; set; }
    public required int TotalAppointmentsCount { get; set; }
    public required int PlannedAppointmentsCount { get; set; }
    public required int CompletedAppointmentsCount { get; set; }
    public required int CancelledAppointmentsCount { get; set; }
    public required int BurnedAppointmentsCount { get; set; }
    public decimal? BurnedShare { get; set; }
    public decimal? CancellationShare { get; set; }
    public required decimal TotalRevenue { get; set; }
    public required decimal TakenHours { get; set; }
    public required decimal WorkedHours { get; set; }
    public required decimal AvailableHours { get; set; }
    public required decimal FreeHours { get; set; }
    public decimal? LoadPercentage { get; set; }
    public required int ActiveTeachersCount { get; set; }
    public decimal? AverageCompletedAppointmentsPerTeacher { get; set; }
    public decimal? AverageGapBetweenServicesHours { get; set; }
    public required List<AppointmentStatusCountDto> Statuses { get; set; }
    public required List<AppointmentLoadByDayDto> DailyLoad { get; set; }
    public required List<AppointmentHourAnalyticsDto> Hours { get; set; }
    public required List<TeacherAppointmentsAnalyticsDto> Teachers { get; set; }
    public required List<BurnedClientAnalyticsDto> BurnedClients { get; set; }
}

public class AppointmentStatusCountDto
{
    public required string Status { get; set; }
    public required int Count { get; set; }
    public decimal? Share { get; set; }
}

public class AppointmentLoadByDayDto
{
    public required DateTime Date { get; set; }
    public required int AppointmentsCount { get; set; }
    public required int ServicesProvidedCount { get; set; }
    public required int CompletedAppointmentsCount { get; set; }
    public required int CancelledAppointmentsCount { get; set; }
    public required int BurnedAppointmentsCount { get; set; }
    public required int UniqueClientsCount { get; set; }
    public required int CompletedUniqueClientsCount { get; set; }
    public required decimal Revenue { get; set; }
    public required decimal TakenHours { get; set; }
    public required decimal AvailableHours { get; set; }
    public required decimal FreeHours { get; set; }
    public decimal? LoadPercentage { get; set; }
    public decimal? BurnedShare { get; set; }
    public decimal? CancellationShare { get; set; }
}

public class AppointmentHourAnalyticsDto
{
    public required int Hour { get; set; }
    public required int AppointmentsCount { get; set; }
    public required int PlannedAppointmentsCount { get; set; }
    public required int CompletedAppointmentsCount { get; set; }
    public required int CancelledAppointmentsCount { get; set; }
    public required int BurnedAppointmentsCount { get; set; }
    public required int UniqueClientsCount { get; set; }
    public required decimal Revenue { get; set; }
    public required decimal TakenHours { get; set; }
    public required decimal AvailableHours { get; set; }
    public required decimal FreeHours { get; set; }
    public decimal? LoadPercentage { get; set; }
    public decimal? CancellationRate { get; set; }
    public decimal? BurnedShare { get; set; }
}

public class TeacherAppointmentsAnalyticsDto
{
    public Ulid? TeacherId { get; set; }
    public required string TeacherDisplayName { get; set; }
    public required int TotalAppointmentsCount { get; set; }
    public required int PlannedAppointmentsCount { get; set; }
    public required int CompletedAppointmentsCount { get; set; }
    public required int CancelledAppointmentsCount { get; set; }
    public required int BurnedAppointmentsCount { get; set; }
    public required int UniqueClientsCount { get; set; }
    public required int WorkingDaysCount { get; set; }
    public required decimal Revenue { get; set; }
    public required decimal WorkedHours { get; set; }
    public required decimal OccupiedHours { get; set; }
    public required decimal AvailableHours { get; set; }
    public required decimal FreeHours { get; set; }
    public decimal? LoadPercentage { get; set; }
    public decimal? DowntimeShare { get; set; }
    public decimal? CancellationShare { get; set; }
    public decimal? BurnedShare { get; set; }
    public decimal? RevenuePerWorkedHour { get; set; }
    public decimal? RevenuePerOccupiedHour { get; set; }
    public decimal? AverageCompletedAppointmentsPerWorkingDay { get; set; }
    public decimal? AverageGapBetweenServicesHours { get; set; }
    public required List<TeacherServiceAnalyticsDto> TopServices { get; set; }
}

public class TeacherServiceAnalyticsDto
{
    public required Ulid ServiceId { get; set; }
    public required string ServiceName { get; set; }
    public required int CompletedAppointmentsCount { get; set; }
    public required int RevenueCountedAppointmentsCount { get; set; }
    public required decimal Revenue { get; set; }
    public decimal? CompletedShare { get; set; }
}

public class BurnedClientAnalyticsDto
{
    public required Ulid ClientId { get; set; }
    public required string ClientDisplayName { get; set; }
    public required int TotalAppointmentsCount { get; set; }
    public required int BurnedAppointmentsCount { get; set; }
    public decimal? BurnedShare { get; set; }
}

using MelodyTrack.Backend.Api.Dashboard;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Reports.Reporting;

public sealed record ReportAppointment(
    Ulid Id,
    Ulid ClientId,
    string ClientName,
    string SourceName,
    Ulid ServiceId,
    string ServiceName,
    bool IsConsultation,
    Ulid? ProviderId,
    string ProviderName,
    DateTime StartUtc,
    DateTime EndUtc,
    DateTime StartLocal,
    DateTime EndLocal,
    AppointmentStatus Status,
    decimal Price)
{
    public decimal DurationHours => Convert.ToDecimal((EndUtc - StartUtc).TotalHours);
    public bool IsOccupied => Status is AppointmentStatus.Planned or AppointmentStatus.Completed or AppointmentStatus.Burned;
    public bool IsVisit => Status is AppointmentStatus.Completed or AppointmentStatus.Burned;
    public bool IsValueVisit => IsVisit && !IsConsultation;
}

public interface IReportAppointmentQuery
{
    Task<List<ReportAppointment>> LoadAsync(ReportContext context, DateTime startUtc, DateTime endExclusiveUtc, CancellationToken ct);
}

public sealed class ReportAppointmentQuery(AppDbContext db) : IReportAppointmentQuery
{
    public async Task<List<ReportAppointment>> LoadAsync(ReportContext context, DateTime startUtc, DateTime endExclusiveUtc, CancellationToken ct)
    {
        var providerId = context.ProviderId;
        var rows = await db.Appointments
            .AsNoTracking()
            .Where(appointment => !appointment.IsDeleted
                                  && appointment.StartDate >= startUtc
                                  && appointment.StartDate < endExclusiveUtc
                                  && (providerId == null || appointment.Provider != null && appointment.Provider.Id == providerId))
            .Select(appointment => new AppointmentRow
            {
                Id = appointment.Id,
                ClientId = appointment.Client.Id,
                ClientName = (appointment.Client.LastName + " " + appointment.Client.FirstName).Trim(),
                SourceName = appointment.Client.Source != null ? appointment.Client.Source.Name : "Без источника",
                ServiceId = appointment.Service.Id,
                ServiceName = appointment.Service.Name,
                IsConsultation = appointment.Service.IsConsultation,
                ProviderId = appointment.Provider != null ? appointment.Provider.Id : null,
                ProviderName = appointment.Provider != null
                    ? (appointment.Provider.LastName + " " + appointment.Provider.FirstName).Trim()
                    : "Без преподавателя",
                StartUtc = appointment.StartDate,
                EndUtc = appointment.EndDate,
                Status = appointment.Status
            })
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return [];
        }

        var serviceIds = rows.Select(row => row.ServiceId).Distinct().ToList();
        var prices = await db.ServicePriceHistory
            .AsNoTracking()
            .Where(price => serviceIds.Contains(price.Service.Id))
            .Select(price => new PriceRow(price.Service.Id, price.EffectiveDate, price.Price))
            .ToListAsync(ct);
        var pricesByService = prices
            .GroupBy(price => price.ServiceId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(price => price.EffectiveDate).ToList());

        return rows.Select(row => new ReportAppointment(
                row.Id,
                row.ClientId,
                row.ClientName,
                row.SourceName,
                row.ServiceId,
                row.ServiceName,
                row.IsConsultation,
                row.ProviderId,
                row.ProviderName,
                row.StartUtc,
                row.EndUtc,
                TimeZoneInfo.ConvertTimeFromUtc(row.StartUtc, context.Timezone),
                TimeZoneInfo.ConvertTimeFromUtc(row.EndUtc, context.Timezone),
                row.Status,
                DashboardPriceResolver.ResolveAppointmentPrice(
                    row.ServiceId,
                    row.StartUtc,
                    pricesByService,
                    price => price.EffectiveDate,
                    price => price.Price)))
            .ToList();
    }

    private sealed class AppointmentRow
    {
        public required Ulid Id { get; init; }
        public required Ulid ClientId { get; init; }
        public required string ClientName { get; init; }
        public required string SourceName { get; init; }
        public required Ulid ServiceId { get; init; }
        public required string ServiceName { get; init; }
        public required bool IsConsultation { get; init; }
        public Ulid? ProviderId { get; init; }
        public required string ProviderName { get; init; }
        public required DateTime StartUtc { get; init; }
        public required DateTime EndUtc { get; init; }
        public required AppointmentStatus Status { get; init; }
    }

    private sealed record PriceRow(Ulid ServiceId, DateTime EffectiveDate, decimal Price);
}

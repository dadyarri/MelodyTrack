using Facet;
using Facet.Mapping;
using MelodyTrack.Backend.Api.Clients;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Responses;

[Facet(typeof(Client), nameof(Client.Contacts))]
public partial class ClientWithBalanceDto
{
    public decimal Balance { get; set; }
    public string? Telegram { get; set; }
    public string? Vk { get; set; }
    public string? Phone { get; set; }
    public string? SourceName { get; set; }
    public DateTime? LastAppointmentAtUtc { get; set; }
    public DateTime? NextAppointmentAtUtc { get; set; }
    public ClientLifecycleStatus LifecycleStatus { get; set; }
    public RecordActivityDto? LastActivity { get; set; }
}

public class ClientToClientWithBalanceDtoMapConfig(AppDbContext db)
    : IFacetMapConfigurationAsyncInstance<Client, ClientWithBalanceDto>
{
    public async Task MapAsync(Client source, ClientWithBalanceDto target,
        CancellationToken cancellationToken = default)
    {
        var totalPayments = await db.Payments
            .Where(e => e.Client.Id == source.Id)
            .SumAsync(e => e.Amount, cancellationToken);

        var appointments = await db.Appointments
            .Where(e => e.Client.Id == source.Id
                        && (e.Status == AppointmentStatus.Completed || e.Status == AppointmentStatus.Burned)
                        && !e.IsDeleted)
            .Select(e => new
            {
                ServiceId = e.Service.Id,
                e.StartDate
            })
            .ToListAsync(cancellationToken);

        var serviceIds = appointments.Select(appointment => appointment.ServiceId).Distinct().ToList();
        var priceLookup = await db.ServicePriceHistory
            .Where(e => serviceIds.Contains(e.Service.Id))
            .Select(e => new
            {
                ServiceId = e.Service.Id,
                e.EffectiveDate,
                e.Price
            })
            .ToListAsync(cancellationToken);

        var groupedPriceLookup = priceLookup
            .GroupBy(price => price.ServiceId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(price => new ServicePriceSnapshot(price.EffectiveDate, price.Price))
                    .ToList());

        var totalServiceCost = ClientBalanceCalculator.CalculateServiceCost(
            appointments.Select(appointment => (appointment.ServiceId, appointment.StartDate)),
            groupedPriceLookup);

        target.Balance = totalPayments - totalServiceCost;
        target.Telegram = source.Contacts.Telegram;
        target.Vk = source.Contacts.Vk;
        target.Phone = source.Contacts.Phone;
        target.SourceId = source.SourceId;
        target.DateOfBirth = source.DateOfBirth;
        target.SourceName = source.Source?.Name;
        target.LastAppointmentAtUtc = await db.Appointments
            .Where(e => e.Client.Id == source.Id
                        && (e.Status == AppointmentStatus.Completed || e.Status == AppointmentStatus.Burned)
                        && !e.IsDeleted)
            .OrderByDescending(e => e.StartDate)
            .Select(e => (DateTime?)e.StartDate)
            .FirstOrDefaultAsync(cancellationToken);
        target.NextAppointmentAtUtc = await db.Appointments
            .Where(e => e.Client.Id == source.Id
                        && e.Status == AppointmentStatus.Planned
                        && !e.IsDeleted
                        && e.StartDate >= DateTime.UtcNow)
            .OrderBy(e => e.StartDate)
            .Select(e => (DateTime?)e.StartDate)
            .FirstOrDefaultAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var hasFutureRegularAppointment = await db.Appointments.AnyAsync(e =>
            e.Client.Id == source.Id && !e.IsDeleted && e.Status == AppointmentStatus.Planned && e.StartDate >= now && !e.Service.IsConsultation,
            cancellationToken);
        var hasCompletedConsultation = await db.Appointments.AnyAsync(e =>
            e.Client.Id == source.Id && !e.IsDeleted && e.Status == AppointmentStatus.Completed && e.Service.IsConsultation,
            cancellationToken);
        var hasPlannedConsultation = await db.Appointments.AnyAsync(e =>
            e.Client.Id == source.Id && !e.IsDeleted && e.Status == AppointmentStatus.Planned && e.Service.IsConsultation,
            cancellationToken);
        target.LifecycleStatus = ClientLifecycleResolver.Resolve(
            source.IsLeadClosed,
            hasFutureRegularAppointment,
            hasCompletedConsultation,
            hasPlannedConsultation);
    }
}

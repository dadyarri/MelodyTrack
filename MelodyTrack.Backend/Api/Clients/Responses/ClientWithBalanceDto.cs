using Facet;
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

public sealed class ClientWithBalanceDtoMapper(AppDbContext db, TimeProvider timeProvider)
{
    public async Task<List<ClientWithBalanceDto>> MapAsync(
        IReadOnlyCollection<Client> clients,
        CancellationToken cancellationToken = default)
    {
        if (clients.Count == 0)
        {
            return [];
        }

        var clientIds = clients.Select(client => client.Id).ToArray();
        var paymentsByClient = await db.Payments
            .AsNoTracking()
            .Where(payment => clientIds.Contains(payment.Client.Id))
            .GroupBy(payment => payment.Client.Id)
            .Select(group => new ClientPaymentTotal(group.Key, group.Sum(payment => payment.Amount)))
            .ToDictionaryAsync(item => item.ClientId, item => item.Amount, cancellationToken);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var appointments = await db.Appointments
            .AsNoTracking()
            .Where(appointment => clientIds.Contains(appointment.Client.Id)
                && !appointment.IsDeleted
                && (appointment.Status == AppointmentStatus.Planned
                    || appointment.Status == AppointmentStatus.Completed
                    || appointment.Status == AppointmentStatus.Burned))
            .Select(appointment => new ClientAppointment(
                appointment.Client.Id,
                appointment.Service.Id,
                appointment.StartDate,
                appointment.Status,
                appointment.Service.IsConsultation))
            .ToListAsync(cancellationToken);

        var serviceIds = appointments
            .Where(IsBillable)
            .Select(appointment => appointment.ServiceId)
            .Distinct()
            .ToArray();
        var priceLookup = await db.ServicePriceHistory
            .AsNoTracking()
            .Where(price => serviceIds.Contains(price.Service.Id))
            .Select(price => new ServicePriceRow(
                price.Service.Id,
                price.EffectiveDate,
                price.Price))
            .ToListAsync(cancellationToken);

        var groupedPriceLookup = priceLookup
            .GroupBy(price => price.ServiceId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(price => new ServicePriceSnapshot(price.EffectiveDate, price.Price))
                    .ToList());
        var appointmentsByClient = appointments
            .GroupBy(appointment => appointment.ClientId)
            .ToDictionary(group => group.Key, group => group.ToList());

        return clients.Select(client => MapClient(
            client,
            paymentsByClient.GetValueOrDefault(client.Id),
            appointmentsByClient.GetValueOrDefault(client.Id) ?? [],
            groupedPriceLookup,
            nowUtc)).ToList();
    }

    private static ClientWithBalanceDto MapClient(
        Client source,
        decimal totalPayments,
        IReadOnlyCollection<ClientAppointment> appointments,
        IReadOnlyDictionary<Ulid, List<ServicePriceSnapshot>> priceLookup,
        DateTime nowUtc)
    {
        var target = new ClientWithBalanceDto(source);
        var billableAppointments = appointments.Where(IsBillable).ToList();
        var completedConsultations = appointments
            .Where(appointment => appointment.Status == AppointmentStatus.Completed && appointment.IsConsultation)
            .ToList();

        var totalServiceCost = ClientBalanceCalculator.CalculateServiceCost(
            billableAppointments.Select(appointment => (appointment.ServiceId, appointment.StartDate)),
            priceLookup);
        var hasFutureRegularAppointment = appointments.Any(appointment =>
            appointment.Status == AppointmentStatus.Planned
            && appointment.StartDate >= nowUtc
            && !appointment.IsConsultation);
        var hasCompletedConsultation = completedConsultations.Count > 0;
        var hasPaidAppointmentAfterConsultation = billableAppointments.Any(appointment =>
            !appointment.IsConsultation
            && completedConsultations.Any(consultation => consultation.StartDate < appointment.StartDate));
        var hasPlannedConsultation = appointments.Any(appointment =>
            appointment.Status == AppointmentStatus.Planned && appointment.IsConsultation);

        target.Balance = totalPayments - totalServiceCost;
        target.Telegram = source.Contacts.Telegram;
        target.Vk = source.Contacts.Vk;
        target.Phone = source.Contacts.Phone;
        target.SourceId = source.SourceId;
        target.DateOfBirth = source.DateOfBirth;
        target.SourceName = source.Source?.Name;
        target.LastAppointmentAtUtc = billableAppointments.Count == 0
            ? null
            : billableAppointments.Max(appointment => appointment.StartDate);
        target.NextAppointmentAtUtc = appointments
            .Where(appointment => appointment.Status == AppointmentStatus.Planned && appointment.StartDate >= nowUtc)
            .Select(appointment => (DateTime?)appointment.StartDate)
            .Min();
        target.LifecycleStatus = ClientLifecycleResolver.Resolve(
            source.IsLeadClosed,
            hasFutureRegularAppointment,
            hasCompletedConsultation,
            hasPaidAppointmentAfterConsultation,
            hasPlannedConsultation);
        return target;
    }

    private static bool IsBillable(ClientAppointment appointment) =>
        appointment.Status is AppointmentStatus.Completed or AppointmentStatus.Burned;

    private sealed record ClientPaymentTotal(Ulid ClientId, decimal Amount);
    private sealed record ClientAppointment(
        Ulid ClientId,
        Ulid ServiceId,
        DateTime StartDate,
        AppointmentStatus Status,
        bool IsConsultation);
    private sealed record ServicePriceRow(Ulid ServiceId, DateTime EffectiveDate, decimal Price);
}

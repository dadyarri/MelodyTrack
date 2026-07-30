using MelodyTrack.Backend.Api.Dashboard.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Dashboard;

public interface IPersonalDashboardQueryService
{
    Task<GetDashboardStatsResponse> GetAsync(
        Ulid providerId,
        TimeZoneInfo timezone,
        DateTime nowUtc,
        bool includeOrganization,
        CancellationToken ct);
}

internal sealed class PersonalDashboardQueryService(
    AppDbContext db,
    IRecurringAppointmentMaterializer recurringAppointmentMaterializer) : IPersonalDashboardQueryService
{
    public async Task<GetDashboardStatsResponse> GetAsync(
        Ulid providerId,
        TimeZoneInfo timezone,
        DateTime nowUtc,
        bool includeOrganization,
        CancellationToken ct)
    {
        var period = PersonalDashboardPeriod.Create(timezone, nowUtc);
        await recurringAppointmentMaterializer.EnsureAppointmentsGeneratedAsync(
            period.TodayStartUtc,
            period.DayAfterTomorrowStartUtc.AddTicks(-1),
            ct);

        var agendaAppointments = await db.Appointments
            .AsNoTracking()
            .AsSplitQuery()
            .Include(appointment => appointment.Client)
            .ThenInclude(client => client.Contacts)
            .Include(appointment => appointment.Client)
            .ThenInclude(client => client.Vacations)
            .Include(appointment => appointment.Service)
            .Where(appointment =>
                !appointment.IsDeleted
                && appointment.Provider != null
                && appointment.Provider.Id == providerId
                && appointment.Status == AppointmentStatus.Planned
                && appointment.StartDate >= period.TodayStartUtc
                && appointment.StartDate < period.DayAfterTomorrowStartUtc
                && appointment.EndDate > nowUtc)
            .OrderBy(appointment => appointment.StartDate)
            .ThenBy(appointment => appointment.Client.LastName)
            .ThenBy(appointment => appointment.Client.FirstName)
            .ToListAsync(ct);

        var visibleAppointments = agendaAppointments
            .Where(appointment => !IsClientVacation(appointment, timezone))
            .ToList();
        var todayAppointments = MapAppointments(
            visibleAppointments.Where(appointment => appointment.StartDate < period.TomorrowStartUtc),
            timezone);
        var tomorrowAppointments = MapAppointments(
            visibleAppointments.Where(appointment => appointment.StartDate >= period.TomorrowStartUtc),
            timezone);

        var personalClientsCount = await db.Appointments
            .AsNoTracking()
            .Where(appointment =>
                !appointment.IsDeleted
                && appointment.Provider != null
                && appointment.Provider.Id == providerId)
            .Select(appointment => appointment.Client.Id)
            .Distinct()
            .CountAsync(ct);

        var incomeAppointments = await db.Appointments
            .AsNoTracking()
            .Where(appointment =>
                !appointment.IsDeleted
                && appointment.Provider != null
                && appointment.Provider.Id == providerId
                && appointment.StartDate >= period.MonthStartUtc
                && appointment.StartDate < period.NextMonthStartUtc
                && (appointment.Status == AppointmentStatus.Completed || appointment.Status == AppointmentStatus.Burned))
            .Select(appointment => new IncomeAppointment(appointment.Service.Id, appointment.StartDate))
            .ToListAsync(ct);
        var monthIncome = await CalculateIncomeAsync(incomeAppointments, ct);

        return new GetDashboardStatsResponse
        {
            PersonalClientsCount = personalClientsCount,
            MonthIncome = monthIncome,
            Today = new DashboardScheduleDayResponse
            {
                Date = period.Today,
                Appointments = todayAppointments
            },
            Tomorrow = new DashboardScheduleDayResponse
            {
                Date = period.Tomorrow,
                Appointments = tomorrowAppointments
            },
            Organization = includeOrganization
                ? await GetOrganizationDashboardAsync(period, nowUtc, ct)
                : null
        };
    }

    private async Task<OrganizationDashboardResponse> GetOrganizationDashboardAsync(
        PersonalDashboardPeriod period,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var plannedAppointments = db.Appointments
            .AsNoTracking()
            .Where(appointment => appointment.Status == AppointmentStatus.Planned && !appointment.IsDeleted);
        var appointmentsToday = await plannedAppointments.CountAsync(appointment =>
            appointment.StartDate >= period.TodayStartUtc
            && appointment.StartDate < period.TomorrowStartUtc, ct);
        var appointmentsTomorrow = await plannedAppointments.CountAsync(appointment =>
            appointment.StartDate >= period.TomorrowStartUtc
            && appointment.StartDate < period.DayAfterTomorrowStartUtc
            && appointment.EndDate > nowUtc, ct);
        var totalClients = await db.Clients.AsNoTracking().CountAsync(ct);

        var incomeAppointments = await db.Appointments
            .AsNoTracking()
            .Where(appointment =>
                !appointment.IsDeleted
                && appointment.StartDate >= period.MonthStartUtc
                && appointment.StartDate < period.NextMonthStartUtc
                && (appointment.Status == AppointmentStatus.Completed || appointment.Status == AppointmentStatus.Burned))
            .Select(appointment => new IncomeAppointment(appointment.Service.Id, appointment.StartDate))
            .ToListAsync(ct);
        var monthIncome = await CalculateIncomeAsync(incomeAppointments, ct);
        var monthExpenses = await db.Expenses
            .AsNoTracking()
            .Where(expense => expense.Date >= period.MonthStartUtc && expense.Date < period.NextMonthStartUtc)
            .SumAsync(expense => expense.Amount, ct);

        var paymentsByClient = await db.Payments
            .AsNoTracking()
            .GroupBy(payment => payment.Client.Id)
            .Select(group => new ClientAmount(group.Key, group.Sum(payment => payment.Amount)))
            .ToListAsync(ct);
        var serviceCostAppointments = await db.Appointments
            .AsNoTracking()
            .Where(appointment =>
                !appointment.IsDeleted
                && (appointment.Status == AppointmentStatus.Completed || appointment.Status == AppointmentStatus.Burned))
            .Select(appointment => new ClientServiceAppointment(
                appointment.Client.Id,
                appointment.Service.Id,
                appointment.StartDate))
            .ToListAsync(ct);
        var prices = await LoadPricesAsync(serviceCostAppointments.Select(appointment => appointment.ServiceId), ct);
        var serviceCostsByClient = serviceCostAppointments
            .GroupBy(appointment => appointment.ClientId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(appointment => ResolvePrice(appointment.ServiceId, appointment.StartDate, prices)));
        var payments = paymentsByClient.ToDictionary(item => item.ClientId, item => item.Amount);
        var balances = payments.Keys
            .Union(serviceCostsByClient.Keys)
            .Select(clientId => payments.GetValueOrDefault(clientId) - serviceCostsByClient.GetValueOrDefault(clientId))
            .ToList();
        var debts = balances.Where(balance => balance < 0).ToList();

        return new OrganizationDashboardResponse
        {
            TotalClients = totalClients,
            DebtorsCount = debts.Count,
            TotalDebt = Math.Abs(debts.Sum()),
            TotalPositiveBalance = balances.Where(balance => balance > 0).Sum(),
            AppointmentsToday = appointmentsToday,
            AppointmentsTomorrow = appointmentsTomorrow,
            MonthIncome = monthIncome,
            MonthExpenses = monthExpenses,
            MonthNet = monthIncome - monthExpenses
        };
    }

    private async Task<decimal> CalculateIncomeAsync(IReadOnlyCollection<IncomeAppointment> appointments, CancellationToken ct)
    {
        if (appointments.Count == 0)
        {
            return 0m;
        }

        var prices = await LoadPricesAsync(appointments.Select(appointment => appointment.ServiceId), ct);
        return appointments.Sum(appointment => ResolvePrice(appointment.ServiceId, appointment.StartDate, prices));
    }

    private async Task<IReadOnlyDictionary<Ulid, List<ServicePriceRow>>> LoadPricesAsync(
        IEnumerable<Ulid> appointmentServiceIds,
        CancellationToken ct)
    {
        var serviceIds = appointmentServiceIds.Distinct().ToArray();
        if (serviceIds.Length == 0)
        {
            return new Dictionary<Ulid, List<ServicePriceRow>>();
        }

        var prices = await db.ServicePriceHistory
            .AsNoTracking()
            .Where(price => serviceIds.Contains(price.Service.Id))
            .Select(price => new ServicePriceRow(price.Service.Id, price.EffectiveDate, price.Price))
            .ToListAsync(ct);
        return prices
            .GroupBy(price => price.ServiceId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(price => price.EffectiveDate).ToList());
    }

    private static decimal ResolvePrice(
        Ulid serviceId,
        DateTime appointmentStart,
        IReadOnlyDictionary<Ulid, List<ServicePriceRow>> prices) =>
        DashboardPriceResolver.ResolveAppointmentPrice(
            serviceId,
            appointmentStart,
            prices,
            price => price.EffectiveDate,
            price => price.Price);

    private static bool IsClientVacation(Appointment appointment, TimeZoneInfo timezone)
    {
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(appointment.StartDate), timezone));
        return appointment.Client.Vacations.Any(vacation => vacation.StartDate <= localDate && vacation.EndDate >= localDate);
    }

    private static List<DashboardAppointmentResponse> MapAppointments(IEnumerable<Appointment> appointments, TimeZoneInfo timezone)
    {
        return appointments.Select(appointment => new DashboardAppointmentResponse
        {
            Id = appointment.Id,
            Client = new DashboardClientResponse
            {
                Id = appointment.Client.Id,
                FirstName = appointment.Client.FirstName,
                LastName = appointment.Client.LastName,
                Contacts = appointment.Client.Contacts is null
                    ? null
                    : new DashboardClientContactsResponse
                    {
                        Phone = appointment.Client.Contacts.Phone,
                        Telegram = appointment.Client.Contacts.Telegram,
                        Vk = appointment.Client.Contacts.Vk
                    }
            },
            Service = new DashboardServiceResponse
            {
                Id = appointment.Service.Id,
                Name = appointment.Service.Name
            },
            StartDate = TimeZoneInfo.ConvertTimeFromUtc(AsUtc(appointment.StartDate), timezone),
            EndDate = TimeZoneInfo.ConvertTimeFromUtc(AsUtc(appointment.EndDate), timezone),
            Status = appointment.Status.ToApiKey()
        }).ToList();
    }

    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Utc
        ? value
        : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private sealed record IncomeAppointment(Ulid ServiceId, DateTime StartDate);
    private sealed record ClientAmount(Ulid ClientId, decimal Amount);
    private sealed record ClientServiceAppointment(Ulid ClientId, Ulid ServiceId, DateTime StartDate);
    private sealed record ServicePriceRow(Ulid ServiceId, DateTime EffectiveDate, decimal Price);
}

internal sealed record PersonalDashboardPeriod(
    DateOnly Today,
    DateOnly Tomorrow,
    DateTime TodayStartUtc,
    DateTime TomorrowStartUtc,
    DateTime DayAfterTomorrowStartUtc,
    DateTime MonthStartUtc,
    DateTime NextMonthStartUtc)
{
    public static PersonalDashboardPeriod Create(TimeZoneInfo timezone, DateTime nowUtc)
    {
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            nowUtc.Kind == DateTimeKind.Utc ? nowUtc : DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc),
            timezone));
        var tomorrow = localToday.AddDays(1);
        var dayAfterTomorrow = localToday.AddDays(2);
        var monthStart = new DateOnly(localToday.Year, localToday.Month, 1);
        var nextMonthStart = monthStart.AddMonths(1);

        return new PersonalDashboardPeriod(
            localToday,
            tomorrow,
            ToUtc(localToday, timezone),
            ToUtc(tomorrow, timezone),
            ToUtc(dayAfterTomorrow, timezone),
            ToUtc(monthStart, timezone),
            ToUtc(nextMonthStart, timezone));
    }

    private static DateTime ToUtc(DateOnly date, TimeZoneInfo timezone) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), timezone);
}

using FastEndpoints;
using MelodyTrack.Backend.Api.Dashboard;
using MelodyTrack.Backend.Api.Dashboard.Requests;
using MelodyTrack.Backend.Api.Dashboard.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Dashboard.Endpoints;

public class GetDashboardStatsEndpoint(AppDbContext db, IRecurringAppointmentMaterializer recurringAppointmentMaterializer)
    : Ep.Req<GetDashboardStatsRequest>.Res<Results<Ok<GetDashboardStatsResponse>, UnauthorizedHttpResult, ProblemDetails>>
{
    public override void Configure()
    {
        Get("/dashboard/stats");
    }

    public override async Task<Results<Ok<GetDashboardStatsResponse>, UnauthorizedHttpResult, ProblemDetails>> ExecuteAsync(
        GetDashboardStatsRequest req,
        CancellationToken ct)
    {
        var currentUser = await DashboardAccess.GetCurrentUserAsync(User, db, ct);

        if (currentUser is null)
        {
            return TypedResults.Unauthorized();
        }

        TimeZoneInfo timezone;
        try
        {
            timezone = TimeZoneInfo.FindSystemTimeZoneById(req.Timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            AddError(r => r.Timezone, "Часовой пояс не найден");
            return new ProblemDetails(ValidationFailures);
        }
        catch (InvalidTimeZoneException)
        {
            AddError(r => r.Timezone, "Часовой пояс недоступен");
            return new ProblemDetails(ValidationFailures);
        }

        var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timezone).Date;
        var tomorrow = today.AddDays(1);
        var dayAfterTomorrow = today.AddDays(2);
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var nextMonthStart = monthStart.AddMonths(1);

        var todayStartUtc = TimeZoneInfo.ConvertTimeToUtc(today, timezone);
        var tomorrowStartUtc = TimeZoneInfo.ConvertTimeToUtc(tomorrow, timezone);
        var dayAfterTomorrowStartUtc = TimeZoneInfo.ConvertTimeToUtc(dayAfterTomorrow, timezone);
        var monthStartUtc = TimeZoneInfo.ConvertTimeToUtc(monthStart, timezone);
        var nextMonthStartUtc = TimeZoneInfo.ConvertTimeToUtc(nextMonthStart, timezone);
        var materializationEndUtc = (nextMonthStartUtc > dayAfterTomorrowStartUtc ? nextMonthStartUtc : dayAfterTomorrowStartUtc).AddTicks(-1);

        await recurringAppointmentMaterializer.EnsureAppointmentsGeneratedAsync(monthStartUtc, materializationEndUtc, ct);

        var appointmentsQuery = db.Appointments
            .AsNoTracking()
            .Where(appointment => appointment.Status == AppointmentStatus.Planned && !appointment.IsDeleted);

        if (DashboardAccess.IsProviderScoped(currentUser))
        {
            appointmentsQuery = appointmentsQuery.Where(appointment => appointment.Provider != null && appointment.Provider.Id == currentUser.Id);
        }

        var appointmentsToday = await appointmentsQuery
            .AsNoTracking()
            .CountAsync(e => e.StartDate >= todayStartUtc
                             && e.StartDate < tomorrowStartUtc, ct);

        var totalClients = await db.Clients
            .AsNoTracking()
            .CountAsync(ct);

        var appointmentsTomorrow = await appointmentsQuery
            .CountAsync(e => e.StartDate >= tomorrowStartUtc
                             && e.StartDate < dayAfterTomorrowStartUtc
                             && e.EndDate > DateTime.UtcNow, ct);

        var incomeAppointmentsThisMonth = await db.Appointments
            .AsNoTracking()
            .Where(e => e.StartDate >= monthStartUtc
                        && e.StartDate < nextMonthStartUtc
                        && (e.Status == AppointmentStatus.Completed || e.Status == AppointmentStatus.Burned)
                        && !e.IsDeleted)
            .Select(e => new
            {
                ServiceId = e.Service.Id,
                e.StartDate
            })
            .ToListAsync(ct);

        var servicePrices = await db.ServicePriceHistory
            .AsNoTracking()
            .Select(e => new
            {
                ServiceId = e.Service.Id,
                e.EffectiveDate,
                e.Price
            })
            .ToListAsync(ct);

        var monthIncome = incomeAppointmentsThisMonth.Sum(appointment =>
            servicePrices
                .Where(price => price.ServiceId == appointment.ServiceId
                                && price.EffectiveDate <= appointment.StartDate)
                .OrderByDescending(price => price.EffectiveDate)
                .Select(price => price.Price)
                .FirstOrDefault());

        var monthExpenses = await db.Expenses
            .AsNoTracking()
            .Where(e => e.Date >= monthStartUtc && e.Date < nextMonthStartUtc)
            .SumAsync(e => e.Amount, ct);

        var paymentsByClient = await db.Payments
            .AsNoTracking()
            .GroupBy(e => e.Client.Id)
            .Select(e => new
            {
                ClientId = e.Key,
                Amount = e.Sum(payment => payment.Amount)
            })
            .ToListAsync(ct);

        var serviceCostsByClient = await db.Appointments
            .AsNoTracking()
            .Where(e => (e.Status == AppointmentStatus.Completed || e.Status == AppointmentStatus.Burned) && !e.IsDeleted)
            .Join(db.ServicePriceHistory,
                appointment => appointment.Service.Id,
                price => price.Service.Id,
                (appointment, price) => new
                {
                    ClientId = appointment.Client.Id,
                    price.Price
                })
            .GroupBy(e => e.ClientId)
            .Select(e => new
            {
                ClientId = e.Key,
                Amount = e.Sum(service => service.Price)
            })
            .ToListAsync(ct);

        var payments = paymentsByClient.ToDictionary(e => e.ClientId, e => e.Amount);
        var serviceCosts = serviceCostsByClient.ToDictionary(e => e.ClientId, e => e.Amount);
        var clientIds = payments.Keys.Union(serviceCosts.Keys);
        var balances = clientIds.Select(clientId =>
            payments.GetValueOrDefault(clientId) - serviceCosts.GetValueOrDefault(clientId));
        var debts = balances.Where(e => e < 0).ToList();

        return TypedResults.Ok(new GetDashboardStatsResponse
        {
            TotalClients = totalClients,
            DebtorsCount = debts.Count,
            TotalDebt = Math.Abs(debts.Sum()),
            AppointmentsToday = appointmentsToday,
            AppointmentsTomorrow = appointmentsTomorrow,
            MonthIncome = monthIncome,
            MonthExpenses = monthExpenses,
            MonthNet = monthIncome - monthExpenses
        });
    }
}

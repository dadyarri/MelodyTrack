using MelodyTrack.Backend.Api.Reports.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Reports.Reporting;

public interface IFinanceReportQueryService
{
    Task<FinanceReportResponse> GetAsync(ReportContext context, CancellationToken ct);
}

public sealed class FinanceReportQueryService(
    AppDbContext db,
    IReportContextFactory contextFactory,
    IReportAppointmentQuery appointmentQuery,
    IRecurringAppointmentMaterializer materializer) : IFinanceReportQueryService
{
    public async Task<FinanceReportResponse> GetAsync(ReportContext context, CancellationToken ct)
    {
        await materializer.EnsureAppointmentsGeneratedAsync(context.StartUtc, context.EndExclusiveUtc.AddTicks(-1), ct);
        var appointments = await appointmentQuery.LoadAsync(context, context.StartUtc, context.EndExclusiveUtc, ct);
        var revenueAppointments = appointments.Where(appointment => appointment.IsValueVisit).ToList();
        var plannedIncomeAppointments = appointments.Where(appointment => appointment.IsPlannedIncomeValue).ToList();
        var includesOrganizationFigures = context.ProviderId is null;

        var payments = includesOrganizationFigures
            ? await db.Payments.AsNoTracking()
                .Where(payment => payment.Date >= context.StartUtc && payment.Date < context.EndExclusiveUtc)
                .Select(payment => new PaymentRow(payment.Client.Id, payment.Date, payment.Amount))
                .ToListAsync(ct)
            : [];
        var expenses = includesOrganizationFigures
            ? await db.Expenses.AsNoTracking()
                .Where(expense => expense.Date >= context.StartUtc && expense.Date < context.EndExclusiveUtc)
                .Select(expense => new ExpenseRow(
                    expense.Date,
                    expense.Amount,
                    expense.Category != null ? expense.Category.Name : "Без статьи"))
                .ToListAsync(ct)
            : [];

        var debt = includesOrganizationFigures
            ? await BuildDebtorsAsync(context, ct)
            : DebtResult.Empty;
        var revenue = revenueAppointments.Sum(appointment => appointment.Price);
        var forecastIncome = plannedIncomeAppointments.Sum(appointment => appointment.Price);
        var paymentTotal = payments.Sum(payment => payment.Amount);
        var expenseTotal = expenses.Sum(expense => expense.Amount);

        return new FinanceReportResponse
        {
            Context = await contextFactory.CreateDtoAsync(context, ct),
            Summary = new FinanceReportSummaryDto
            {
                Revenue = revenue,
                ForecastIncome = forecastIncome,
                ForecastAppointments = plannedIncomeAppointments.Count,
                Payments = includesOrganizationFigures ? paymentTotal : null,
                Expenses = includesOrganizationFigures ? expenseTotal : null,
                NetProfit = includesOrganizationFigures ? revenue - expenseTotal : null,
                OutstandingDebt = includesOrganizationFigures ? debt.Total : null,
                AverageRevenuePerVisit = revenueAppointments.Count == 0 ? null : revenue / revenueAppointments.Count,
                RevenueAppointments = revenueAppointments.Count,
                OrganizationOnlyFiguresAvailable = includesOrganizationFigures
            },
            Trend = BuildTrend(context, revenueAppointments, payments, expenses, includesOrganizationFigures),
            ExpenseCategories = expenses
                .GroupBy(expense => expense.CategoryName)
                .Select(group => new FinanceExpenseCategoryDto
                {
                    CategoryName = group.Key,
                    Amount = group.Sum(expense => expense.Amount)
                })
                .OrderByDescending(item => item.Amount)
                .ThenBy(item => item.CategoryName)
                .ToList(),
            Debtors = debt.Rows,
            Services = revenueAppointments
                .GroupBy(appointment => new { appointment.ServiceId, appointment.ServiceName })
                .Select(group => new FinanceServiceDto
                {
                    ServiceId = group.Key.ServiceId,
                    ServiceName = group.Key.ServiceName,
                    Appointments = group.Count(),
                    Revenue = group.Sum(appointment => appointment.Price)
                })
                .OrderByDescending(item => item.Revenue)
                .ThenBy(item => item.ServiceName)
                .ToList()
        };
    }

    private async Task<DebtResult> BuildDebtorsAsync(ReportContext context, CancellationToken ct)
    {
        var historicalAppointments = await appointmentQuery.LoadAsync(
            context,
            DateTime.UnixEpoch,
            context.EndExclusiveUtc,
            ct);
        var revenueByClient = historicalAppointments
            .Where(appointment => appointment.IsValueVisit)
            .GroupBy(appointment => new { appointment.ClientId, appointment.ClientName })
            .ToDictionary(
                group => group.Key,
                group => group.Sum(appointment => appointment.Price));
        if (revenueByClient.Count == 0)
        {
            return DebtResult.Empty;
        }

        var clientIds = revenueByClient.Keys.Select(key => key.ClientId).ToList();
        var paymentsByClient = await db.Payments.AsNoTracking()
            .Where(payment => clientIds.Contains(payment.Client.Id) && payment.Date < context.EndExclusiveUtc)
            .GroupBy(payment => payment.Client.Id)
            .Select(group => new { ClientId = group.Key, Amount = group.Sum(payment => payment.Amount) })
            .ToDictionaryAsync(item => item.ClientId, item => item.Amount, ct);

        var debtors = revenueByClient
            .Select(pair => new FinanceDebtorDto
            {
                ClientId = pair.Key.ClientId,
                ClientName = pair.Key.ClientName,
                Revenue = pair.Value,
                Payments = paymentsByClient.GetValueOrDefault(pair.Key.ClientId),
                Debt = Math.Max(0m, pair.Value - paymentsByClient.GetValueOrDefault(pair.Key.ClientId))
            })
            .Where(item => item.Debt > 0m)
            .OrderByDescending(item => item.Debt)
            .ThenBy(item => item.ClientName)
            .ToList();

        return new DebtResult(debtors.Sum(item => item.Debt), debtors.Take(100).ToList());
    }

    private static List<FinanceTrendDto> BuildTrend(
        ReportContext context,
        IReadOnlyList<ReportAppointment> appointments,
        IReadOnlyList<PaymentRow> payments,
        IReadOnlyList<ExpenseRow> expenses,
        bool includesOrganizationFigures)
    {
        return ReportBuckets.Starts(context).Select(bucketStart =>
        {
            var bucketEndExclusive = ReportBuckets.EndExclusive(bucketStart, context.GroupBy);
            var revenue = appointments
                .Where(appointment => appointment.StartLocal >= bucketStart && appointment.StartLocal < bucketEndExclusive)
                .Sum(appointment => appointment.Price);
            var paymentTotal = payments
                .Where(payment => ToLocal(payment.DateUtc, context).Date >= bucketStart && ToLocal(payment.DateUtc, context).Date < bucketEndExclusive)
                .Sum(payment => payment.Amount);
            var expenseTotal = expenses
                .Where(expense => ToLocal(expense.DateUtc, context).Date >= bucketStart && ToLocal(expense.DateUtc, context).Date < bucketEndExclusive)
                .Sum(expense => expense.Amount);
            return new FinanceTrendDto
            {
                StartDate = bucketStart < context.StartLocal ? context.StartLocal : bucketStart,
                EndDate = (bucketEndExclusive > context.EndExclusiveLocal ? context.EndExclusiveLocal : bucketEndExclusive).AddDays(-1),
                Revenue = revenue,
                Payments = includesOrganizationFigures ? paymentTotal : null,
                Expenses = includesOrganizationFigures ? expenseTotal : null,
                NetProfit = includesOrganizationFigures ? revenue - expenseTotal : null
            };
        }).ToList();
    }

    private static DateTime ToLocal(DateTime value, ReportContext context) => TimeZoneInfo.ConvertTimeFromUtc(value, context.Timezone);

    private sealed record PaymentRow(Ulid ClientId, DateTime DateUtc, decimal Amount);
    private sealed record ExpenseRow(DateTime DateUtc, decimal Amount, string CategoryName);
    private sealed record DebtResult(decimal Total, List<FinanceDebtorDto> Rows)
    {
        public static DebtResult Empty { get; } = new(0m, []);
    }
}

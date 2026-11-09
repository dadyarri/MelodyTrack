using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class MonthlyForecastTests
{
    [Fact]
    public void Create_RecordedPrepaidResult_PreservesStoredValuation()
    {
        var appointment = Ok(MakeAppointment(100, Utc(5, 15)).Complete(Utc(5, 17)));
        var settlement = Ok(AppointmentSettlementRules.Create(new(Guid(101)), appointment,
            new SettlementSource.Prepaid(new(Guid(102)), Ok(Money.Create(900))), Utc(5, 17), []));

        var forecast = Ok(MonthlyForecastRules.Create(2026, 1, [appointment], [MakeService(1500)], [settlement], Utc(6, 0), []));

        Assert.Equal(900m, forecast.KnownValue());
        Assert.True(forecast.IsComplete());
    }

    [Fact]
    public void Create_FuturePaidReserve_ReportsUnresolvedValuation()
    {
        var payment = Ok(PaymentRules.Create(new(Guid(101)), TestData.ClientId, MakeService(), 2, Now, null, null, [], []));

        var forecast = Ok(MonthlyForecastRules.Create(2026, 1, [MakeAppointment(100, Utc(5, 15))],
            [MakeService(1500)], [], Now, [], [payment]));

        Assert.Equal(ForecastUncertainty.PaidReserveValuation, Assert.Single(forecast.Lines).Uncertainty);
        Assert.False(forecast.IsComplete());
    }

    [Fact]
    public void Create_ConsultationWithHistoricalPrice_PreservesRecordedCashAndZerosNewForecast()
    {
        var service = Ok(ServiceRules.Restore(TestData.ServiceId, "Консультация", Duration(60), true,
            [Ok(ServicePrice.Create(Now, 100m))]));
        var past = Ok(MakeAppointment(100, Utc(5, 15)).Complete(Utc(5, 17)));
        var future = MakeAppointment(101, Utc(10, 15));
        var historical = Ok(AppointmentSettlementRules.Create(new(Guid(102)), past,
            new SettlementSource.Cash(Ok(Money.Create(100)), null), Utc(5, 17), []));

        var forecast = Ok(MonthlyForecastRules.Create(2026, 1, [past, future], [service], [historical], Utc(6, 0), [future.Id]));

        Assert.Equal(100m, forecast.Lines.Single(line => line.AppointmentId == past.Id).Value!.Amount);
        Assert.Equal(0m, forecast.Lines.Single(line => line.AppointmentId == future.Id).Value!.Amount);
        Assert.True(forecast.IsComplete());
    }

    [Theory]
    [InlineData(2026, 2, 2026, 1, 31)]
    [InlineData(2027, 1, 2026, 12, 31)]
    public void Create_MoscowMonthBoundary_SelectsOnlyStartsInRequestedYearAndMonth(
        int year, int month, int previousYear, int previousMonth, int previousDay)
    {
        var lastPreviousStart = new DateTimeOffset(previousYear, previousMonth, previousDay, 20, 0, 0, TimeSpan.Zero);
        var crossingMidnight = MakeAppointment(100, lastPreviousStart);
        var firstInMonth = MakeAppointment(101, lastPreviousStart.AddHours(1));
        var nextMonth = MakeAppointment(102, new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1));
        var previousYearAppointment = MakeAppointment(103, new DateTimeOffset(year - 1, month, 2, 0, 0, 0, TimeSpan.Zero));

        var forecast = Ok(MonthlyForecastRules.Create(year, month,
            [crossingMidnight, firstInMonth, nextMonth, previousYearAppointment], [MakeService()], [], Now, []));

        Assert.Equal(firstInMonth.Id, Assert.Single(forecast.Lines).AppointmentId);
        Assert.Equal(1000m, forecast.KnownValue());
    }

    [Fact]
    public void Create_LessonEndsInNextMoscowMonth_IncludesItInStartMonth()
    {
        var appointment = MakeAppointment(100, new DateTimeOffset(2026, 1, 31, 20, 0, 0, TimeSpan.Zero));

        var forecast = Ok(MonthlyForecastRules.Create(2026, 1, [appointment], [MakeService()], [], Now, []));

        Assert.Equal(appointment.Id, Assert.Single(forecast.Lines).AppointmentId);
        Assert.Equal(1000m, forecast.KnownValue());
    }

    [Theory]
    [InlineData(0, 1500)]
    [InlineData(1, 1000)]
    public void Create_PriceBoundary_UsesPriceAtLessonStart(int effectiveHoursAfterStart, int expectedAmount)
    {
        var appointment = MakeAppointment(100, Utc(5, 15));
        var service = Ok(MakeService().SchedulePrice(Now,
            Ok(ServicePrice.Create(appointment.StartsAt.AddHours(effectiveHoursAfterStart), 1500m))));

        var forecast = Ok(MonthlyForecastRules.Create(2026, 1, [appointment], [service], [], Now, []));

        Assert.Equal((decimal)expectedAmount, Assert.Single(forecast.Lines).Value!.Amount);
        Assert.True(forecast.IsComplete());
    }

    [Fact]
    public void Create_ChargeRecordedInNextMonth_PreservesAmountAndStartMonth()
    {
        var appointment = Ok(MakeAppointment(100, Utc(5, 15)).Complete(Utc(5, 17)));
        var charge = Ok(AppointmentSettlementRules.Create(new(Guid(101)), appointment,
            new SettlementSource.Cash(Ok(Money.Create(700m)), null), new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), []));

        var forecast = Ok(MonthlyForecastRules.Create(2026, 1, [appointment], [MakeService(5000m)], [charge], charge.RecordedAt, []));

        Assert.Equal(700m, Assert.Single(forecast.Lines).Value!.Amount);
        Assert.Equal(700m, forecast.KnownValue());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Create_PastLessonWithoutResult_ExcludesCostRegardlessOfPlannedCoverage(bool covered)
    {
        var appointment = MakeAppointment(100, Utc(5, 15));
        AppointmentId[] coveredAppointments = covered ? [appointment.Id] : [];

        var forecast = Ok(MonthlyForecastRules.Create(2026, 1, [appointment], [MakeService()], [],
            appointment.EndsAt, coveredAppointments));

        Assert.Empty(forecast.Lines);
        Assert.Equal(0m, forecast.KnownValue());
        Assert.True(forecast.IsComplete());
        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
    }

    [Fact]
    public void Create_PastAndFutureLessons_KeepsRecordedResultAndFuturePlan()
    {
        var pastWithoutResult = MakeAppointment(100, Utc(5, 15));
        var completed = Ok(MakeAppointment(101, Utc(5, 17)).Complete(Utc(5, 19)));
        var future = MakeAppointment(102, Utc(7, 15));
        var charge = Ok(AppointmentSettlementRules.Create(new(Guid(103)), completed,
            new SettlementSource.Cash(Ok(Money.Create(700m)), null), Utc(6, 0), []));

        var forecast = Ok(MonthlyForecastRules.Create(2026, 1, [pastWithoutResult, completed, future],
            [MakeService()], [charge], Utc(6, 0), []));

        Assert.Equal(new[] { completed.Id, future.Id }, forecast.Lines.Select(line => line.AppointmentId));
        Assert.Equal(1700m, forecast.KnownValue());
        Assert.True(forecast.IsComplete());
    }
}

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

        var forecast = Ok(MonthlyForecastRules.Create(2026, 1, [appointment], [MakeService(1500)], [settlement], Utc(6, 0)));

        Assert.Equal(900m, forecast.KnownValue());
        Assert.True(forecast.IsComplete());
    }

    [Fact]
    public void Create_FuturePaidReserve_UsesStoredPriceBeforeCurrentTariff()
    {
        var payment = Ok(PaymentRules.Create(new(Guid(101)), TestData.ClientId, MakeService(), 2, Now, null, null, [], []));

        var forecast = Ok(MonthlyForecastRules.Create(2026, 1, [MakeAppointment(100, Utc(5, 15))],
            [MakeService(1500)], [], Now, [payment]));

        Assert.Equal(1000m, Assert.IsType<ForecastLine.Known>(Assert.Single(forecast.Lines)).Value.Amount);
        Assert.Equal(1000m, forecast.KnownValue());
        Assert.True(forecast.IsComplete());
        Assert.Equal(2, payment.RemainingLessons([]));
    }

    [Fact]
    public void Create_ConsultationWithHistoricalPrice_PreservesRecordedCashAndZerosNewForecast()
    {
        var service = Ok(ServiceRules.Restore(TestData.ServiceId, "Консультация", Duration(60), true,
            [Ok(ServicePrice.Create(Now, 100m))]));
        var past = Ok(MakeAppointment(100, Utc(5, 15)).Complete(Utc(5, 17)));
        var future = MakeAppointment(101, Utc(10, 15));
        var historical = Ok(AppointmentSettlementRules.Create(new(Guid(102)), past,
            new SettlementSource.Uncovered(Ok(Money.Create(100)), null), Utc(5, 17), []));

        var forecast = Ok(MonthlyForecastRules.Create(2026, 1, [past, future], [service], [historical], Utc(6, 0)));

        Assert.Equal(100m, Assert.IsType<ForecastLine.Known>(forecast.Lines.Single(line => line.AppointmentId == past.Id)).Value.Amount);
        Assert.Equal(0m, Assert.IsType<ForecastLine.Known>(forecast.Lines.Single(line => line.AppointmentId == future.Id)).Value.Amount);
        Assert.True(forecast.IsComplete());
    }

    [Fact]
    public void Create_MultipleReserves_ProjectsOldestFirstThenKnownTariffRegardlessOfInputOrder()
    {
        var first = Ok(PaymentRules.Create(new(Guid(200)), TestData.ClientId, MakeService(800), 1, Now, null, null, [], []));
        var second = Ok(PaymentRules.Create(new(Guid(201)), TestData.ClientId, MakeService(1000), 1, Utc(2, 0), null, null, [first], []));
        var early = MakeAppointment(100, Utc(5, 15));
        var middle = MakeAppointment(101, Utc(6, 15));
        var last = MakeAppointment(102, Utc(7, 15));

        var forecast = Ok(MonthlyForecastRules.Create(2026, 1, [last, early, middle], [MakeService(1500)], [],
            Utc(3, 0), [second, first]));

        Assert.Equal(new[] { early.Id, middle.Id, last.Id }, forecast.Lines.Select(line => line.AppointmentId));
        Assert.Equal(new[] { 800m, 1000m, 1500m }, Values(forecast));
        Assert.Equal(3300m, forecast.KnownValue());
        Assert.True(forecast.IsComplete());
        Assert.Equal(1, first.RemainingLessons([]));
        Assert.Equal(1, second.RemainingLessons([]));
    }

    [Fact]
    public void Create_PlanBeforeTargetMonth_ConsumesReserveOnlyInProjection()
    {
        var payment = Ok(PaymentRules.Create(new(Guid(200)), TestData.ClientId, MakeService(1000), 1, Now, null, null, [], []));
        var january = MakeAppointment(100, Utc(30, 15));
        var february = MakeAppointment(101, new DateTimeOffset(2026, 2, 3, 15, 0, 0, TimeSpan.Zero));

        var forecast = Ok(MonthlyForecastRules.Create(2026, 2, [february, january], [MakeService(1500)], [],
            Utc(25, 0), [payment]));

        Assert.Equal(february.Id, Assert.Single(forecast.Lines).AppointmentId);
        Assert.Equal(1500m, forecast.KnownValue());
        Assert.Equal(1, payment.RemainingLessons([]));
        Assert.Equal(AppointmentStatus.Scheduled, january.Status);
    }

    [Fact]
    public void Create_RecordedUsage_ProjectsOnlyRemainingUnitsAndPreservesActualValue()
    {
        var payment = Ok(PaymentRules.Create(new(Guid(200)), TestData.ClientId, MakeService(800), 2, Now, null, null, [], []));
        var past = Ok(MakeAppointment(100, Utc(2, 15)).Complete(Utc(2, 17)));
        var charge = Ok(AppointmentSettlementRules.Create(new(Guid(201)), past,
            new SettlementSource.Prepaid(payment.Id, Ok(Money.Create(800))), Utc(2, 17), []));

        var forecast = Ok(MonthlyForecastRules.Create(2026, 1,
            [past, MakeAppointment(101, Utc(5, 15)), MakeAppointment(102, Utc(6, 15))], [MakeService(1500)],
            [charge], Utc(3, 0), [payment]));

        Assert.Equal(new[] { 800m, 800m, 1500m }, Values(forecast));
        Assert.Equal(1, payment.RemainingLessons([charge]));
        Assert.False(charge.IsReversed);
    }

    [Fact]
    public void Create_ReversedPrepaidUsage_ReturnsOldPriceToProjectedReserve()
    {
        var payment = Ok(PaymentRules.Create(new(Guid(200)), TestData.ClientId, MakeService(800), 1, Now, null, null, [], []));
        var past = Ok(MakeAppointment(100, Utc(2, 15)).Complete(Utc(2, 17)));
        var reversed = Ok(AppointmentSettlementRules.Create(new(Guid(201)), past,
            new SettlementSource.Prepaid(payment.Id, Ok(Money.Create(800))), Utc(2, 17), [])).Reverse();

        var forecast = Ok(MonthlyForecastRules.Create(2026, 1, [MakeAppointment(101, Utc(5, 15))],
            [MakeService(1500)], [reversed], Utc(3, 0), [payment]));

        Assert.Equal(new[] { 800m }, Values(forecast));
        Assert.Equal(1, payment.RemainingLessons([reversed]));
    }

    [Fact]
    public void Create_PaymentCoversPastDebt_ReservesOnlyUnspentQuantity()
    {
        var past = Ok(MakeAppointment(100, Utc(2, 15)).Complete(Utc(2, 17)));
        var debt = Ok(AppointmentSettlementRules.Create(new(Guid(200)), past,
            new SettlementSource.Uncovered(Ok(Money.Create(800)), null), Utc(2, 17), []));
        var payment = Ok(PaymentRules.Create(new(Guid(201)), TestData.ClientId, MakeService(1000), 2, Utc(3, 0), null, null, [], [debt]));

        var forecast = Ok(MonthlyForecastRules.Create(2026, 1,
            [past, MakeAppointment(101, Utc(5, 15)), MakeAppointment(102, Utc(6, 15))], [MakeService(1500)],
            [debt], Utc(4, 0), [payment]));

        Assert.Equal(new[] { 800m, 1000m, 1500m }, Values(forecast));
        Assert.Equal(1, payment.RemainingLessons([debt]));
    }

    [Fact]
    public void Create_ReversedCoveredDebt_ProjectsReturnedUnitAtItsOriginalPrice()
    {
        var past = Ok(MakeAppointment(100, Utc(2, 15)).Complete(Utc(2, 17)));
        var debt = Ok(AppointmentSettlementRules.Create(new(Guid(200)), past,
            new SettlementSource.Uncovered(Ok(Money.Create(800)), null), Utc(2, 17), []));
        var payment = Ok(PaymentRules.Create(new(Guid(201)), TestData.ClientId, MakeService(1000), 2, Utc(3, 0), null, null, [], [debt]));
        var reversed = debt.Reverse();

        var forecast = Ok(MonthlyForecastRules.Create(2026, 1,
            [MakeAppointment(101, Utc(5, 15)), MakeAppointment(102, Utc(6, 15)), MakeAppointment(103, Utc(7, 15))],
            [MakeService(1500)], [reversed], Utc(4, 0), [payment]));

        Assert.Equal(new[] { 800m, 1000m, 1500m }, Values(forecast));
        Assert.Equal(2, payment.RemainingLessons([reversed]));
    }

    [Fact]
    public void Create_DiscountedReserve_UsesPaidAmountThenOrdinaryTariff()
    {
        var discount = Ok(Discount.Percentage.Create(12.5m));
        var payment = Ok(PaymentRules.Restore(new(Guid(200)), TestData.ClientId, Ok(Money.Create(1960)), Now, null,
            new(TestData.ServiceId, 2, Ok(Money.Create(1122)), new DiscountActivationId(Guid(201)), discount)));

        var forecast = Ok(MonthlyForecastRules.Create(2026, 1,
            [MakeAppointment(100, Utc(5, 15)), MakeAppointment(101, Utc(6, 15)), MakeAppointment(102, Utc(7, 15))],
            [MakeService(1500)], [], Now, [payment]));

        Assert.Equal(new[] { 980m, 980m, 1500m }, Values(forecast));
        Assert.Equal(3460m, forecast.KnownValue());
    }

    [Fact]
    public void Create_RecoveredUnitPaidDuringCurrentLesson_PreservesReturnedPrice()
    {
        var past = Ok(MakeAppointment(100, Utc(2, 15)).Complete(Utc(2, 17)));
        var debt = Ok(AppointmentSettlementRules.Create(new(Guid(200)), past,
            new SettlementSource.Uncovered(Ok(Money.Create(800)), null), Utc(2, 17), []));
        var payment = Ok(PaymentRules.Create(new(Guid(201)), TestData.ClientId, MakeService(), 1,
            Utc(5, 15).AddMinutes(10), null, null, [], [debt]));
        var current = MakeAppointment(101, Utc(5, 15));

        var forecast = Ok(MonthlyForecastRules.Create(2026, 1, [current], [MakeService(1500)],
            [debt.Reverse()], Utc(5, 15).AddMinutes(30), [payment]));

        Assert.Equal(new[] { 800m }, Values(forecast));
        Assert.Equal(AppointmentStatus.Scheduled, current.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Create_ReserveForDifferentClientOrService_DoesNotCoverAppointment(bool differentClient)
    {
        var payment = Ok(PaymentRules.Restore(new(Guid(200)), differentClient ? new ClientId(Guid(201)) : TestData.ClientId,
            Ok(Money.Create(800)), Now, null,
            new(differentClient ? TestData.ServiceId : new ServiceId(Guid(202)), 1, Ok(Money.Create(800)), null, null)));

        var forecast = Ok(MonthlyForecastRules.Create(2026, 1, [MakeAppointment(100, Utc(5, 15))],
            [MakeService(1500)], [], Now, [payment]));

        Assert.Equal(new[] { 1500m }, Values(forecast));
        Assert.True(forecast.IsComplete());
    }

    [Fact]
    public void Create_PaymentDatedAfterSnapshot_DoesNotAssumeFutureIncome()
    {
        var payment = Ok(PaymentRules.Create(new(Guid(200)), TestData.ClientId, MakeService(800), 1, Utc(3, 0), null, null, [], []));

        var forecast = Ok(MonthlyForecastRules.Create(2026, 1, [MakeAppointment(100, Utc(5, 15))],
            [MakeService(1500)], [], Now, [payment]));

        Assert.Equal(new[] { 1500m }, Values(forecast));
    }

    [Fact]
    public void Create_MissingPriceWithoutReserve_ReportsIncompleteData()
    {
        var forecast = Ok(MonthlyForecastRules.Create(2026, 1, [MakeAppointment(100, Utc(5, 15))], [], [], Now));

        Assert.Equal(ForecastUncertainty.MissingPrice, Assert.IsType<ForecastLine.Unresolved>(Assert.Single(forecast.Lines)).Reason);
        Assert.Equal(0m, forecast.KnownValue());
        Assert.False(forecast.IsComplete());
    }

    [Fact]
    public void Create_SubKopeckReserveAllocation_ReportsExistingAccountingLimitation()
    {
        var payment = Ok(PaymentRules.Restore(new(Guid(200)), TestData.ClientId, Ok(Money.Create(1000)), Now, null,
            new(TestData.ServiceId, 3, Ok(Money.Create(400)), null, null)));

        var forecast = Ok(MonthlyForecastRules.Create(2026, 1, [MakeAppointment(100, Utc(5, 15))],
            [MakeService(1500)], [], Now, [payment]));

        Assert.Equal(ForecastUncertainty.PaidReserveValuation, Assert.IsType<ForecastLine.Unresolved>(Assert.Single(forecast.Lines)).Reason);
        Assert.False(forecast.IsComplete());
    }

    [Fact]
    public void Create_ReservesWithSamePaymentTime_ReportsExistingAccountingLimitation()
    {
        var first = Ok(PaymentRules.Create(new(Guid(200)), TestData.ClientId, MakeService(800), 1, Now, null, null, [], []));
        var second = Ok(PaymentRules.Create(new(Guid(201)), TestData.ClientId, MakeService(1000), 1, Now, null, null, [first], []));

        var forecast = Ok(MonthlyForecastRules.Create(2026, 1, [MakeAppointment(100, Utc(5, 15))],
            [MakeService(1500)], [], Now, [second, first]));

        Assert.Equal(ForecastUncertainty.PaidReserveValuation, Assert.IsType<ForecastLine.Unresolved>(Assert.Single(forecast.Lines)).Reason);
        Assert.False(forecast.IsComplete());
    }

    [Fact]
    public void Create_PastUnmarkedAppointment_DoesNotConsumeProjectedReserve()
    {
        var payment = Ok(PaymentRules.Create(new(Guid(200)), TestData.ClientId, MakeService(800), 1, Now, null, null, [], []));
        var past = MakeAppointment(100, Utc(2, 15));
        var future = MakeAppointment(101, Utc(5, 15));

        var forecast = Ok(MonthlyForecastRules.Create(2026, 1, [past, future], [MakeService(1500)], [],
            Utc(3, 0), [payment]));

        Assert.Equal(future.Id, Assert.Single(forecast.Lines).AppointmentId);
        Assert.Equal(new[] { 800m }, Values(forecast));
    }

    private static decimal[] Values(MonthlyForecast forecast) => forecast.Lines
        .Select(line => Assert.IsType<ForecastLine.Known>(line).Value.Amount).ToArray();

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
            [crossingMidnight, firstInMonth, nextMonth, previousYearAppointment], [MakeService()], [], Now));

        Assert.Equal(firstInMonth.Id, Assert.Single(forecast.Lines).AppointmentId);
        Assert.Equal(1000m, forecast.KnownValue());
    }

    [Fact]
    public void Create_LessonEndsInNextMoscowMonth_IncludesItInStartMonth()
    {
        var appointment = MakeAppointment(100, new DateTimeOffset(2026, 1, 31, 20, 0, 0, TimeSpan.Zero));

        var forecast = Ok(MonthlyForecastRules.Create(2026, 1, [appointment], [MakeService()], [], Now));

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

        var forecast = Ok(MonthlyForecastRules.Create(2026, 1, [appointment], [service], [], Now));

        Assert.Equal((decimal)expectedAmount, Assert.IsType<ForecastLine.Known>(Assert.Single(forecast.Lines)).Value.Amount);
        Assert.True(forecast.IsComplete());
    }

    [Fact]
    public void Create_ChargeRecordedInNextMonth_PreservesAmountAndStartMonth()
    {
        var appointment = Ok(MakeAppointment(100, Utc(5, 15)).Complete(Utc(5, 17)));
        var charge = Ok(AppointmentSettlementRules.Create(new(Guid(101)), appointment,
            new SettlementSource.Uncovered(Ok(Money.Create(700m)), null), new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), []));

        var forecast = Ok(MonthlyForecastRules.Create(2026, 1, [appointment], [MakeService(5000m)], [charge], charge.RecordedAt));

        Assert.Equal(700m, Assert.IsType<ForecastLine.Known>(Assert.Single(forecast.Lines)).Value.Amount);
        Assert.Equal(700m, forecast.KnownValue());
    }

    [Fact]
    public void Create_PastLessonWithoutResult_ExcludesCost()
    {
        var appointment = MakeAppointment(100, Utc(5, 15));
        var forecast = Ok(MonthlyForecastRules.Create(2026, 1, [appointment], [MakeService()], [],
            appointment.EndsAt));

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
            new SettlementSource.Uncovered(Ok(Money.Create(700m)), null), Utc(6, 0), []));

        var forecast = Ok(MonthlyForecastRules.Create(2026, 1, [pastWithoutResult, completed, future],
            [MakeService()], [charge], Utc(6, 0)));

        Assert.Equal(new[] { completed.Id, future.Id }, forecast.Lines.Select(line => line.AppointmentId));
        Assert.Equal(1700m, forecast.KnownValue());
        Assert.True(forecast.IsComplete());
    }
}

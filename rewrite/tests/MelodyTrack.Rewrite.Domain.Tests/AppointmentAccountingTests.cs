using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class AppointmentAccountingTests
{
    private static Actor Super => new Actor.Staff(TestData.StaffId, StaffRole.Superuser);
    private static Payment Paid(int id, Service? service = null, int quantity = 2, int day = 1) =>
        Ok(PaymentRules.Create(new(Guid(id)), TestData.ClientId, service ?? MakeService(), quantity, Utc(day, 0), null, null, [], []));

    [Fact]
    public void SetResult_PastAppointmentWithoutComment_RejectsWithoutFinancialChange()
    {
        var appointment = MakeAppointment(100, Utc(5, 15));

        var result = AppointmentAccounting.SetResult(appointment, AppointmentStatus.Completed, new(Guid(101)),
            MakeService(), [], [], Super, Utc(5, 17));

        AssertError(new CatalogueError.Required("comment"), result);
        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
        Assert.Empty(appointment.StateHistory);
    }

    [Fact]
    public void SetResult_Consultation_DoesNotConsumePayment()
    {
        var payment = Paid(300);

        var change = Ok(AppointmentAccounting.SetResult(MakeAppointment(100, Utc(5, 15)), AppointmentStatus.Completed,
            new(Guid(101)), MakeService(0m, true), [payment], [], Super, Utc(5, 17), "Результат консультации"));

        var settlement = Assert.Single(change.SettlementChanges);
        Assert.Equal(new SettlementSource.Uncovered(Ok(Money.Create(0)), null), settlement.Source);
        Assert.Equal(2, payment.RemainingLessons([settlement]));
        Assert.Equal(TestData.StaffId, Assert.Single(change.Appointment.StateHistory).AuthorId);
    }

    [Fact]
    public void SetResult_PersonalOldPriceReserve_ConsumesOldestPaymentDespitePriceIncrease()
    {
        var oldPayment = Paid(300);
        var service = Ok(MakeService().SchedulePrice(Now, Ok(ServicePrice.Create(Utc(4, 0), 1500))));
        var newPayment = Paid(301, service, day: 4);

        var change = Ok(AppointmentAccounting.SetResult(MakeAppointment(100, Utc(5, 15)), AppointmentStatus.Completed,
            new(Guid(101)), service, [newPayment, oldPayment], [], Super, Utc(5, 17), "Результат занятия"));

        Assert.Equal(new SettlementSource.Prepaid(oldPayment.Id, Ok(Money.Create(1000))), Assert.Single(change.SettlementChanges).Source);
        Assert.Equal(1, oldPayment.RemainingLessons(change.SettlementChanges));
        Assert.Equal(2, newPayment.RemainingLessons(change.SettlementChanges));
    }

    [Fact]
    public void SetResult_PaymentsExhausted_UsesPriceAtStartAndCreatesServiceDebt()
    {
        var service = Ok(MakeService().SchedulePrice(Now, Ok(ServicePrice.Create(Utc(5, 16), 1500))));

        var change = Ok(AppointmentAccounting.SetResult(MakeAppointment(100, Utc(5, 15)), AppointmentStatus.Completed,
            new(Guid(101)), service, [], [], Super, Utc(10, 0), "Поздняя отметка"));

        Assert.Equal(new SettlementSource.Uncovered(Ok(Money.Create(1000)), null), Assert.Single(change.SettlementChanges).Source);
        Assert.Equal(-1000m, Ok(ClientBalance.CalculateForService(TestData.ClientId, TestData.ServiceId, [], change.SettlementChanges)).Amount);
    }

    [Fact]
    public void SetResult_RepeatedResult_PreservesRecordedSource()
    {
        var first = Ok(AppointmentAccounting.SetResult(MakeAppointment(100, Utc(5, 15)), AppointmentStatus.Completed,
            new(Guid(101)), MakeService(), [Paid(300)], [], Super, Utc(5, 17), "Результат занятия"));

        var repeated = Ok(AppointmentAccounting.SetResult(first.Appointment, AppointmentStatus.Completed, new(Guid(102)),
            MakeService(2000), [], first.SettlementChanges, Super, Utc(6, 17)));

        Assert.Equal(first.Appointment, repeated.Appointment);
        Assert.Empty(repeated.SettlementChanges);
    }

    [Fact]
    public void ClearResult_CashDebt_ReversesOnlySelectedResult()
    {
        var first = Ok(AppointmentAccounting.SetResult(MakeAppointment(100, Utc(5, 15)), AppointmentStatus.Completed,
            new(Guid(101)), MakeService(), [], [], Super, Utc(5, 17), "Результат занятия"));
        var second = Ok(AppointmentAccounting.SetResult(MakeAppointment(102, Utc(6, 15)), AppointmentStatus.Burned,
            new(Guid(103)), MakeService(), [], first.SettlementChanges, Super, Utc(6, 17), "Неявка"));

        var cleared = Ok(AppointmentAccounting.ClearResult(first.Appointment, first.SettlementChanges.AddRange(second.SettlementChanges),
            Super, Utc(7, 0), "Ошибка результата"));

        var reversed = Assert.Single(cleared.SettlementChanges);
        Assert.True(reversed.IsReversed);
        Assert.Equal(first.Appointment.Id, reversed.AppointmentId);
        Assert.False(second.SettlementChanges[0].IsReversed);
    }

    [Fact]
    public void ClearResult_PaidReserveAfterPriceIncrease_ReturnsOriginalServiceReserveWithoutChangingOtherResult()
    {
        var payment = Paid(300);
        var service = Ok(MakeService().SchedulePrice(Now, Ok(ServicePrice.Create(Utc(6, 0), 1500))));
        var first = Ok(AppointmentAccounting.SetResult(MakeAppointment(100, Utc(5, 15)), AppointmentStatus.Completed,
            new(Guid(101)), service, [payment], [], Super, Utc(5, 17), "Результат занятия"));
        var second = Ok(AppointmentAccounting.SetResult(MakeAppointment(102, Utc(6, 15)), AppointmentStatus.Completed,
            new(Guid(103)), service, [payment], first.SettlementChanges, Super, Utc(6, 17), "Результат занятия"));

        var cleared = Ok(AppointmentAccounting.ClearResult(first.Appointment,
            first.SettlementChanges.AddRange(second.SettlementChanges), Super, Utc(7, 0), "Ошибка результата"));

        var reversal = Assert.Single(cleared.SettlementChanges);
        Assert.True(reversal.IsReversed);
        Assert.Equal(first.SettlementChanges[0].Source, reversal.Source);
        Assert.Equal(new SettlementSource.Prepaid(payment.Id, Ok(Money.Create(1000))), reversal.Source);
        Assert.Equal(TestData.ServiceId, reversal.ServiceId);
        var currentHistory = cleared.SettlementChanges.AddRange(second.SettlementChanges);
        Assert.Equal(1, payment.RemainingLessons(currentHistory));
        Assert.False(second.SettlementChanges[0].IsReversed);
        var repeated = Ok(AppointmentAccounting.SetResult(cleared.Appointment, AppointmentStatus.Completed,
            new(Guid(104)), service, [payment], currentHistory, Super, Utc(7, 0), "Повторная отметка"));
        Assert.Equal(reversal.Source, Assert.Single(repeated.SettlementChanges).Source);
    }

    [Fact]
    public void ClearResult_ResultWithoutSettlement_RejectsInconsistentSnapshot()
    {
        var completed = Ok(MakeAppointment(100, Utc(5, 15)).Complete(StaffRole.Superuser, Utc(5, 17), "Результат занятия", TestData.StaffId));

        AssertError(new FinanceError.InconsistentSettlement(), AppointmentAccounting.ClearResult(completed, [], Super, Utc(5, 17), "Ошибка"));
    }

    [Fact]
    public void SetResult_ScheduledAppointmentWithActiveCharge_RejectsInconsistentSnapshot()
    {
        var appointment = MakeAppointment(100, Utc(5, 15));
        var first = Ok(AppointmentAccounting.SetResult(appointment, AppointmentStatus.Completed, new(Guid(101)),
            MakeService(), [], [], Super, Utc(5, 17), "Результат занятия"));

        AssertError(new FinanceError.InconsistentSettlement(), AppointmentAccounting.SetResult(appointment, AppointmentStatus.Completed,
            new(Guid(102)), MakeService(), [], first.SettlementChanges, Super, Utc(5, 17), "Результат занятия"));
    }

    [Fact]
    public void SetResult_Teacher_RejectsResultAndFinancialChange() => AssertError(new AppointmentError.Forbidden(),
        AppointmentAccounting.SetResult(MakeAppointment(100, Utc(5, 15)), AppointmentStatus.Completed, new(Guid(101)),
            MakeService(), [], [], new Actor.Staff(TestData.StaffId, StaffRole.Teacher), Utc(5, 17)));

    [Fact]
    public void SetResult_FutureAppointment_RejectsBeforeSettlement() => AssertError(new AppointmentError.ResultInFuture(),
        AppointmentAccounting.SetResult(MakeAppointment(100, Utc(5, 15)), AppointmentStatus.Completed, new(Guid(101)),
            MakeService(), [], [], Super, Utc(5, 15)));
}

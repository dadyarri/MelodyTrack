using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class PaymentTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Edit_PaymentHasHistoricalUse_RejectsChangingFact(bool reversed)
    {
        var service = MakeService();
        var payment = Ok(PaymentRules.Create(new(Guid(100)), TestData.ClientId, service, 2, Utc(1, 0), null, null, [], []));
        var completed = Ok(AppointmentAccounting.SetResult(MakeAppointment(101, Utc(5, 15)), AppointmentStatus.Completed,
            new(Guid(102)), service, [payment], [], Super, Utc(5, 17), "Результат"));
        var settlement = Assert.Single(completed.SettlementChanges);

        var result = payment.Edit(service, 3, Utc(1, 0), Utc(6, 0), "Исправление", null, [payment],
            [reversed ? settlement.Reverse() : settlement], Super);

        AssertError(new ModelError.InvalidState(), result);
        Assert.Contains(result.Error.Issues, issue => issue.Field == "usedPayment");
        Assert.Equal(2, payment.Conditions!.Quantity);
    }

    [Fact]
    public void Edit_UnusedPayment_PreservesOriginalInSubjectChange()
    {
        var service = MakeService();
        var payment = Ok(PaymentRules.Create(new(Guid(100)), TestData.ClientId, service, 2, Now, null, null, [], []));

        var change = Ok(payment.Edit(service, 3, Now, Utc(2, 0), "Исправление", null, [payment], [], Super));

        Assert.Equal(payment, change.Before);
        Assert.Equal(payment.Id, change.After.Id);
        Assert.Equal(3, change.After.Conditions!.Quantity);
        Assert.Equal(3000m, change.After.Amount.Amount);
        Assert.Equal(TestData.StaffId, change.AuthorId);
    }

    [Fact]
    public void Create_PersonalDeny_OverridesAdministratorRole()
    {
        var permissions = InitialPermissionPolicies.ForRole(StaffRole.Administrator);
        var actor = new Actor.Staff(TestData.StaffId, StaffRole.Administrator,
            permissions with { Grants = permissions.Grants.SetItem(StaffPermission.PaymentRecord, PermissionGrant.Denied) });

        AssertError(new ModelError.Forbidden(), PaymentOperations.Create(new(Guid(100)), TestData.ClientId, MakeService(), 2,
            Now, Now, null, null, [], [], actor));
    }

    [Fact]
    public void Create_GrantedTeacher_UsesCurrentPermission()
    {
        var permissions = InitialPermissionPolicies.ForRole(StaffRole.Teacher);
        var actor = new Actor.Staff(TestData.StaffId, StaffRole.Teacher,
            permissions with { Grants = permissions.Grants.SetItem(StaffPermission.PaymentRecord,
                new(true, PermissionDataScope.Available, PermissionTimeScope.Any)) });

        var payment = Ok(PaymentOperations.Create(new(Guid(100)), TestData.ClientId, MakeService(), 2,
            Now, Now, null, null, [], [], actor));

        Assert.Equal(2, payment.Conditions!.Quantity);
    }

    private static Actor Super => new Actor.Staff(TestData.StaffId, StaffRole.Superuser);
    private static DiscountActivation Activation(int id = 200, Discount? discount = null)
    {
        var campaign = Ok(MarketingCampaignOperations.Create(new(Guid(id)), "PAY", Period(Utc(1, 0), Utc(10, 0)),
            discount ?? new Discount.Fixed(Ok(Money.Create(100))), new DiscountTerms.Payment(), null, Super, []));
        return Ok(campaign.Activate(new(Guid(id + 1)), TestData.ClientId, TestData.ServiceId, Utc(2, 0), null, [], Super));
    }

    [Fact]
    public void Create_DiscountedPayment_StoresServiceQuantityAndRoundedTotal()
    {
        var activation = Activation(discount: Ok(Discount.Percentage.Create(12.5m)));
        var service = MakeService(2244);

        var payment = Ok(PaymentRules.Create(new(Guid(100)), TestData.ClientId, service, 1, Utc(3, 0), "Оплата", activation, [], []));

        Assert.Equal(1960m, payment.Amount.Amount);
        Assert.Equal(new PaymentConditions(service.Id, 1, Ok(Money.Create(2244)), activation.Id, activation.Discount), payment.Conditions);
        Assert.Equal("Оплата", payment.Comment);
    }

    [Fact]
    public void Create_SameActivationAlreadyUsed_RejectsReuse()
    {
        var activation = Activation();
        var first = Ok(PaymentRules.Create(new(Guid(100)), TestData.ClientId, MakeService(), 2, Utc(3, 0), null, activation, [], []));

        AssertError(new FinanceError.DiscountUnavailable(), PaymentRules.Create(new(Guid(101)), TestData.ClientId, MakeService(),
            2, Utc(4, 0), null, activation, [first], []));
    }

    [Fact]
    public void CalculateForService_TwoDiscountedPaymentsAndOrdinaryPayment_KeepsIndependentDiscountedRemainders()
    {
        var first = Ok(PaymentRules.Create(new(Guid(100)), TestData.ClientId, MakeService(), 2, Utc(3, 0), null, Activation(), [], []));
        var second = Ok(PaymentRules.Create(new(Guid(101)), TestData.ClientId, MakeService(), 2, Utc(4, 0), null, Activation(202), [first], []));
        var ordinary = Ok(PaymentRules.Create(new(Guid(102)), TestData.ClientId, MakeService(), 3, Utc(5, 0), null, null, [first, second], []));
        var change = Ok(AppointmentAccounting.SetResult(MakeAppointment(103, Utc(6, 15)), AppointmentStatus.Completed,
            new(Guid(104)), MakeService(), [ordinary, second, first], [], Super, Utc(6, 17), "Результат"));

        var balance = Ok(ClientBalance.CalculateForService(TestData.ClientId, TestData.ServiceId,
            [first, second, ordinary], change.SettlementChanges));

        Assert.Equal(new SettlementSource.Prepaid(first.Id, Ok(Money.Create(950))), Assert.Single(change.SettlementChanges).Source);
        Assert.Equal(3, balance.DiscountedLessons);
        Assert.Equal(6, balance.RemainingLessons);
        Assert.Equal(5850m, balance.Amount);
    }

    [Fact]
    public void Select_FractionalDistribution_RequiresDecisionWithoutChangingPayment()
    {
        var payment = Ok(PaymentRules.Create(new(Guid(100)), TestData.ClientId, MakeService(), 3, Utc(3, 0), null,
            Activation(), [], []));

        AssertError(new ModelError.DecisionRequired(), SettlementSelection.Select(MakeAppointment(101, Utc(5, 15)), MakeService(), [payment], [], Utc(5, 17)));
        Assert.Equal(2900m, payment.Amount.Amount);
        Assert.Equal(3, payment.RemainingLessons([]));
    }

    [Fact]
    public void Select_OldPaymentExhausted_ConsumesNextPayment()
    {
        var first = Ok(PaymentRules.Create(new(Guid(100)), TestData.ClientId, MakeService(), 1, Utc(3, 0), null, Activation(), [], []));
        var second = Ok(PaymentRules.Create(new(Guid(101)), TestData.ClientId, MakeService(), 1, Utc(4, 0), null, null, [first], []));
        var completed = Ok(AppointmentAccounting.SetResult(MakeAppointment(102, Utc(5, 15)), AppointmentStatus.Completed,
            new(Guid(103)), MakeService(), [second, first], [], Super, Utc(5, 17), "Проведено"));

        var next = Ok(AppointmentAccounting.SetResult(MakeAppointment(104, Utc(6, 15)), AppointmentStatus.Burned,
            new(Guid(105)), MakeService(), [second, first], completed.SettlementChanges, Super, Utc(6, 17), "Неявка"));

        Assert.Equal(new SettlementSource.Prepaid(second.Id, Ok(Money.Create(1000))), Assert.Single(next.SettlementChanges).Source);
        Assert.Equal(0, first.RemainingLessons(completed.SettlementChanges));
    }

    [Fact]
    public void Create_Consultation_DoesNotCreatePayment() => AssertError(new ModelError.InvalidValue(),
        PaymentRules.Create(new(Guid(100)), TestData.ClientId, MakeService(0, true), 1, Now, null, null, [], []));

    [Fact]
    public void Create_AdministratorBackdatedPayment_RejectsDate()
    {
        AssertError(new ModelError.Forbidden(), PaymentOperations.Create(new(Guid(100)), TestData.ClientId, MakeService(), 2,
            Utc(2, 0), Utc(3, 0), null, null, [], [], new Actor.Staff(TestData.StaffId, StaffRole.Administrator)));
    }

    [Fact]
    public void Create_ExistingServiceDebt_PaysDebtAndReservesRemainingLesson()
    {
        var debt = Ok(AppointmentAccounting.SetResult(MakeAppointment(100, Utc(5, 15)), AppointmentStatus.Completed,
            new(Guid(101)), MakeService(), [], [], Super, Utc(5, 17), "Результат"));

        var payment = Ok(PaymentOperations.Create(new(Guid(102)), TestData.ClientId, MakeService(), 2,
            Utc(6, 0), Utc(6, 0), null, null, [], debt.SettlementChanges, Super));

        var settlement = Assert.Single(debt.SettlementChanges);
        var coverage = Assert.Single(payment.DebtCoverage);
        Assert.Equal(settlement.Id, coverage.SettlementId);
        Assert.Equal(1000m, coverage.Amount.Amount);
        Assert.Equal(2000m, payment.Amount.Amount);
        Assert.Equal(2, payment.Conditions!.Quantity);
        Assert.Equal(TestData.ServiceId, payment.Conditions.ServiceId);
        Assert.Equal(1, payment.RemainingLessons(debt.SettlementChanges));
        Assert.IsType<SettlementSource.Cash>(settlement.Source);
    }
}

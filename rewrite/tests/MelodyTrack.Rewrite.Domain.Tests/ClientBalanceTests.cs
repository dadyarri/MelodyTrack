using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class ClientBalanceTests
{
    [Fact]
    public void Calculate_PurchaseInDebtAndActualRefund_ComputesNegativeBalance()
    {
        var payment = Ok(PaymentRules.Create(new(Guid(100)), TestData.ClientId, Ok(Money.Create(500)), Utc(1, 0), null));
        var subscription = Ok(SubscriptionRules.Purchase(new(Guid(101)), TestData.ClientId, SubscriptionSize.Four, Ok(Money.Create(2000)), Utc(1, 0)));
        var refund = Ok(RefundRules.Create(new(Guid(102)), TestData.ClientId, Ok(Money.Create(100)), Utc(1, 1), null));

        Assert.Equal(-1600m, ClientBalance.Calculate(TestData.ClientId, [payment], [refund], [subscription], []));
    }

    [Fact]
    public void Calculate_ReversedCharge_ExcludesChargeWithoutInventingPayment()
    {
        var appointment = Ok(MakeAppointment(100, Utc(5, 15)).Complete(StaffRole.Superuser, Utc(5, 17)));
        var charge = Ok(AppointmentSettlementRules.Create(new(Guid(101)), appointment, new SettlementSource.Cash(Ok(Money.Create(1000)), null), Utc(5, 17), []));

        Assert.Equal(-1000m, ClientBalance.Calculate(TestData.ClientId, [], [], [], [charge]));
        Assert.Equal(0m, ClientBalance.Calculate(TestData.ClientId, [], [], [], [charge.Reverse()]));
    }
}

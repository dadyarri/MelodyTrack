using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class ClientBalanceTests
{
    [Fact]
    public void CalculateForService_CreditAndOtherServiceDebt_KeepsBalancesIndependent()
    {
        var payment = Ok(PaymentRules.Create(new(Guid(100)), TestData.ClientId, MakeService(), 2, Utc(1, 0), null, null, [], []));
        var otherService = Ok(ServiceRules.Create(new(Guid(105)), "Вокал", Duration(60), false, Ok(ServicePrice.Create(Now, 1000))));
        var appointment = Ok(AppointmentRules.Create(new(Guid(101)), TestData.ClientId, otherService.Id, TestData.StaffId, null, Utc(5, 15), Duration(60)));
        var completed = Ok(appointment.Complete(StaffRole.Superuser, Utc(5, 17), "Результат", TestData.StaffId));
        var charge = Ok(AppointmentSettlementRules.Create(new(Guid(102)), completed, new SettlementSource.Uncovered(Ok(Money.Create(1000)), null), Utc(5, 17), []));

        var guitar = Ok(ClientBalance.CalculateForService(TestData.ClientId, TestData.ServiceId, [payment], [charge]));
        var singing = Ok(ClientBalance.CalculateForService(TestData.ClientId, otherService.Id, [payment], [charge]));

        Assert.Equal(2000m, guitar.Amount);
        Assert.Equal(2, guitar.RemainingLessons);
        Assert.Equal(-1000m, singing.Amount);
        Assert.Equal(0, singing.RemainingLessons);
        Assert.IsType<SettlementSource.Uncovered>(Ok(SettlementSelection.Select(completed, otherService, [payment], [], Utc(5, 17))));
    }

    [Fact]
    public void CalculateForService_HistoricalUnassignedPayment_RequiresMappingDecision()
    {
        var payment = Ok(PaymentRules.Restore(new(Guid(100)), TestData.ClientId, Ok(Money.Create(500)), Now, null));

        AssertError(new ModelError.DecisionRequired(), ClientBalance.CalculateForService(TestData.ClientId, TestData.ServiceId, [payment], []));
    }
}

using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class ServiceTests
{
    [Fact]
    public void PriceAt_NewPriceBoundary_UsesNewPriceAndPreservesHistory()
    {
        var service = MakeService();
        var updated = Ok(service.SchedulePrice(Utc(2, 0), Ok(ServicePrice.Create(Utc(10, 0), 1500m))));
        Assert.Equal(1000m, updated.PriceAt(Utc(10, 0).AddTicks(-1)));
        Assert.Equal(1500m, updated.PriceAt(Utc(10, 0)));
        Assert.Null(updated.PriceAt(Now.AddTicks(-1)));
        Assert.Equal(2, updated.Prices.Length);
        Assert.Single(service.Prices);
    }
    [Fact]
    public void SchedulePrice_PastEffectiveDate_RejectsPrice() => AssertError(new CatalogueError.PriceInPast(), MakeService().SchedulePrice(Utc(10, 0), Ok(ServicePrice.Create(Utc(9, 0), 100m))));
    [Fact]
    public void SchedulePrice_ExistingEffectiveDate_RejectsDuplicate() => AssertError(new CatalogueError.DuplicatePriceDate(), MakeService().SchedulePrice(Now, Ok(ServicePrice.Create(Now, 100m))));
    [Fact]
    public void Create_NegativePrice_RejectsAmount() => AssertError(new CatalogueError.NegativePrice(), ServicePrice.Create(Now, -1m));
    [Fact]
    public void Create_ZeroPrice_DoesNotImplyConsultation()
    {
        Assert.False(MakeService(0m).IsConsultation);
        Assert.True(MakeService(0m, true).IsConsultation);
    }

    [Fact]
    public void Create_PaidConsultation_RejectsPrice()
    {
        var result = ServiceRules.Create(TestData.ServiceId, "Консультация", Duration(60), true,
            Ok(ServicePrice.Create(Now, 100m)));

        AssertError(new ModelError.InvalidValue(), result);
    }

    [Fact]
    public void SchedulePrice_Consultation_RejectsPositivePrice()
    {
        var consultation = MakeService(0m, true);

        var result = consultation.SchedulePrice(Now, Ok(ServicePrice.Create(Utc(2, 0), 100m)));

        AssertError(new ModelError.InvalidValue(), result);
        Assert.Equal(0m, consultation.PriceAt(Utc(2, 0)));
    }

    [Fact]
    public void Restore_PaidConsultation_PreservesHistoricalPrices()
    {
        var first = Ok(ServicePrice.Create(Now, 100m));
        var second = Ok(ServicePrice.Create(Utc(2, 0), 200m));

        var consultation = Ok(ServiceRules.Restore(TestData.ServiceId, "Консультация", Duration(60), true,
            [second, first]));

        Assert.True(consultation.IsConsultation);
        Assert.Equal(new[] { first, second }, consultation.Prices);
        Assert.Equal(100m, consultation.PriceAt(Now));
        Assert.Equal(200m, consultation.PriceAt(Utc(2, 0)));
    }

    [Fact]
    public void Restore_EmptyPrices_RejectsHistory()
    {
        var result = ServiceRules.Restore(TestData.ServiceId, "Консультация", Duration(60), true, []);

        AssertError(new ModelError.InvalidValue(), result);
    }

    [Fact]
    public void Restore_DuplicatePriceDates_RejectsHistory()
    {
        var result = ServiceRules.Restore(TestData.ServiceId, "Консультация", Duration(60), true,
            [Ok(ServicePrice.Create(Now, 100m)), Ok(ServicePrice.Create(Now, 200m))]);

        AssertError(new CatalogueError.DuplicatePriceDate(), result);
    }
    [Fact]
    public void ChangeDuration_ExistingSeriesAndAppointments_KeepTheirSnapshots()
    {
        var service = MakeService();
        var series = MakeSeries();
        var appointment = MakeAppointment(20, Utc(5, 15));
        var updated = service.ChangeDuration(Duration(45));
        Assert.Equal(45, updated.Duration.Minutes);
        Assert.Equal(60, series.Duration.Minutes);
        Assert.Equal(60, appointment.Duration.Minutes);
    }
    [Fact]
    public void PublicVisibility_DoesNotArchiveServiceOrChangePrice()
    {
        var service = MakeService().SetPublic(true).SetPublic(false);
        Assert.False(service.IsArchived);
        Assert.Equal(1000m, service.PriceAt(Utc(5, 15)));
        Assert.Equal("Гитара", service.PublicName);
    }
    [Fact]
    public void Archive_FutureAppointments_RequiresExplicitResolution() => AssertError(new CatalogueError.FutureAppointmentsExist(), MakeService().Archive(NoUsage with { HasFutureAppointments = true }, [], []));
    [Fact]
    public void Archive_HistoricalUse_AllowsArchiveButPreventsDeletion()
    {
        var usage = NoUsage with { HasHistoricalUse = true };
        var service = Ok(MakeService().Archive(usage, [], []));
        Assert.True(service.IsArchived);
        Assert.False(usage.CanDelete());
        Assert.False(service.Unarchive().IsArchived);
    }
    [Fact]
    public void Archive_ActiveSeries_RejectsArchiveWithoutMaterializedAppointments() => AssertError(new CatalogueError.ActiveSeriesExist(), MakeService().Archive(NoUsage with { HasActiveSeries = true }, [], []));

    [Fact]
    public void Archive_UnusedPaidLessons_RejectsArchive()
    {
        var service = MakeService();
        var payment = Ok(PaymentRules.Create(new(Guid(100)), TestData.ClientId, service, 2, Now, null, null, [], []));

        AssertError(new ServiceArchiveError.UnusedPaidLessons(), service.Archive(NoUsage, [payment], []));
    }

    [Fact]
    public void Archive_PaymentFullyUsed_AllowsArchiveAndPreservesHistory()
    {
        var service = MakeService();
        var payment = Ok(PaymentRules.Create(new(Guid(100)), TestData.ClientId, service, 1, Now, null, null, [], []));
        var appointment = Ok(MakeAppointment(101, Utc(5, 15)).Complete(Utc(5, 17)));
        var settlement = Ok(AppointmentSettlementRules.Create(new(Guid(102)), appointment,
            new SettlementSource.Prepaid(payment.Id, payment.Amount), Utc(5, 17), []));

        var archived = Ok(service.Archive(NoUsage, [payment], [settlement]));

        Assert.True(archived.IsArchived);
        Assert.Equal(0, payment.RemainingLessons([settlement]));
        Assert.Equal(payment.Id, Assert.IsType<SettlementSource.Prepaid>(settlement.Source).Id);
    }

    [Fact]
    public void Archive_OnlyAnotherServiceHasUnusedLessons_AllowsArchive()
    {
        var service = MakeService();
        var another = Ok(ServiceRules.Create(new(Guid(103)), "Вокал", Duration(60), false, Ok(ServicePrice.Create(Now, 1000))));
        var payment = Ok(PaymentRules.Create(new(Guid(100)), TestData.ClientId, another, 1, Now, null, null, [], []));

        Assert.True(Ok(service.Archive(NoUsage, [payment], [])).IsArchived);
    }

    [Fact]
    public void Archive_ReversedUse_RetainsUnusedReserveAndRejectsArchive()
    {
        var service = MakeService();
        var payment = Ok(PaymentRules.Create(new(Guid(100)), TestData.ClientId, service, 1, Now, null, null, [], []));
        var appointment = Ok(MakeAppointment(101, Utc(5, 15)).Complete(Utc(5, 17)));
        var settlement = Ok(AppointmentSettlementRules.Create(new(Guid(102)), appointment,
            new SettlementSource.Prepaid(payment.Id, payment.Amount), Utc(5, 17), []));

        AssertError(new ServiceArchiveError.UnusedPaidLessons(), service.Archive(NoUsage, [payment], [settlement.Reverse()]));
    }
}

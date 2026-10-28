
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
        Assert.True(MakeService(100m, true).IsConsultation);
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
    public void Archive_FutureAppointments_RequiresExplicitResolution() => AssertError(new CatalogueError.FutureAppointmentsExist(), MakeService().Archive(NoUsage with { HasFutureAppointments = true }));
    [Fact]
    public void Archive_HistoricalUse_AllowsArchiveButPreventsDeletion()
    {
        var usage = NoUsage with { HasHistoricalUse = true };
        var service = Ok(MakeService().Archive(usage));
        Assert.True(service.IsArchived);
        Assert.False(usage.CanDelete);
        Assert.False(service.Unarchive().IsArchived);
    }
    [Fact]
    public void Archive_ActiveSeries_RejectsArchiveWithoutMaterializedAppointments() => AssertError(new CatalogueError.ActiveSeriesExist(), MakeService().Archive(NoUsage with { HasActiveSeries = true }));
}

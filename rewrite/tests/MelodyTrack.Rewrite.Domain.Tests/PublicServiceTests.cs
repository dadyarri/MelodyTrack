using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class PublicServiceTests
{
    [Fact]
    public void At_InternalPriceChanges_PreservesManualPublicPrice()
    {
        var service = Ok(MakeService().SetPublic(true).SetPublicPrice(900m));
        var updated = Ok(service.SchedulePrice(Now, Ok(ServicePrice.Create(Utc(2, 0), 1500m))));

        var before = PublicServiceRules.At(updated, Now);
        var after = PublicServiceRules.At(updated, Utc(2, 0));

        Assert.Equal(900m, before!.Price);
        Assert.Equal(900m, after!.Price);
        Assert.Equal(1500m, updated.PriceAt(Utc(2, 0)));
    }

    [Fact]
    public void At_NoManualPrice_DoesNotSubstituteInternalTariff()
    {
        var service = MakeService().SetPublic(true);

        var result = PublicServiceRules.At(service, Now);

        Assert.Null(result);
        Assert.Equal(1000m, service.PriceAt(Now));
    }

    [Fact]
    public void SetPublicPrice_ManualEdit_AppliesImmediatelyAndPreservesInternalPrice()
    {
        var service = Ok(MakeService().SetPublic(true).SetPublicPrice(900m));

        var updated = Ok(service.SetPublicPrice(800.50m));

        Assert.Equal(800.50m, PublicServiceRules.At(updated, Now)!.Price);
        Assert.Equal(1000m, updated.PriceAt(Now));
        Assert.Equal(900m, service.PublicPrice);
    }

    [Fact]
    public void SetPublicPrice_ZeroPrice_PublishesFreeService()
    {
        var service = Ok(MakeService(0m, true).SetPublic(true).SetPublicPrice(0m));

        var result = PublicServiceRules.At(service, Now);

        Assert.Equal(0m, result!.Price);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0.001)]
    public void SetPublicPrice_InvalidAmount_RejectsPrice(double amount)
    {
        var result = MakeService().SetPublicPrice((decimal)amount);

        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void At_HiddenOrArchivedService_OmitsPublicEntry(bool archived)
    {
        var service = Ok(MakeService().SetPublicPrice(900m));
        if (archived) service = Ok(service.SetPublic(true).Archive(NoUsage, [], []));

        var result = PublicServiceRules.At(service, Now);

        Assert.Null(result);
        Assert.Equal(1000m, service.PriceAt(Now));
    }

    [Fact]
    public void SetPublicPrice_Superuser_UpdatesPublicData()
    {
        var actor = new Actor.Staff(TestData.StaffId, StaffRole.Superuser);

        var result = Ok(MakeService().SetPublicPrice(900m, actor));

        Assert.Equal(900m, result.PublicPrice);
    }

    [Theory]
    [InlineData(StaffRole.Administrator)]
    [InlineData(StaffRole.Teacher)]
    public void SetPublicPrice_OtherStaff_RejectsEdit(StaffRole role)
    {
        var result = MakeService().SetPublicPrice(900m, new Actor.Staff(TestData.StaffId, role));

        AssertError(new ModelError.Forbidden(), result);
    }

    [Fact]
    public void SetPublicPrice_Client_RejectsEdit()
    {
        var result = MakeService().SetPublicPrice(900m, new Actor.Client(TestData.ClientId));

        AssertError(new ModelError.Forbidden(), result);
    }
}

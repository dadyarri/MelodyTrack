using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class MarketingCampaignServiceTests
{
    [Theory]
    [InlineData(StaffRole.Administrator)]
    [InlineData(StaffRole.Teacher)]
    public async Task CreateAsync_OtherStaff_DeniesBeforeStoreAccess(StaffRole role)
    {
        var store = new CollisionStore();
        var service = new MarketingCampaignService(store);

        var result = await service.CreateAsync(new(Guid(80)), "START", Period(Now, Utc(10, 0)),
            new Discount.Fixed(Ok(Money.Create(100))), new Actor.Staff(TestData.StaffId, role), TestContext.Current.CancellationToken);

        AssertError(new ModelError.Forbidden(), result);
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task CreateWithGeneratedCodeAsync_ConcurrentCodeCollision_SelectsAnotherFreeCode()
    {
        var store = new CollisionStore();
        var service = new MarketingCampaignService(store);

        var result = Ok(await service.CreateWithGeneratedCodeAsync(new(Guid(80)), Period(Now, Utc(10, 0)),
            new Discount.Fixed(Ok(Money.Create(100))), "AB", new Actor.Staff(TestData.StaffId, StaffRole.Superuser),
            TestContext.Current.CancellationToken));

        Assert.Equal(2, store.Attempted.Count);
        Assert.NotEqual(store.Attempted[0], result.Code);
        Assert.Equal(store.Attempted[1], result.Code);
        Assert.IsType<DiscountTerms.Payment>(result.Terms);
        Assert.Null(result.ReturningClientGap);
    }

    [Fact]
    public async Task CreateWithGeneratedCodeAsync_InvalidAlphabet_DeniesBeforeStoreAccess()
    {
        var store = new CollisionStore();

        var result = await new MarketingCampaignService(store).CreateWithGeneratedCodeAsync(new(Guid(80)), Period(Now, Utc(10, 0)),
            new Discount.Fixed(Ok(Money.Create(100))), "", new Actor.Staff(TestData.StaffId, StaffRole.Superuser),
            TestContext.Current.CancellationToken);

        AssertError(new ModelError.InvalidValue(), result);
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task CreateAsync_Cancelled_DoesNotAccessStore()
    {
        var store = new CollisionStore();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new MarketingCampaignService(store).CreateAsync(
            new(Guid(80)), "START", Period(Now, Utc(10, 0)), new Discount.Fixed(Ok(Money.Create(100))),
            new Actor.Staff(TestData.StaffId, StaffRole.Superuser), cancellation.Token));

        Assert.Equal(0, store.Calls);
    }

    private sealed class CollisionStore : IMarketingCampaignStore
    {
        internal int Calls;
        internal List<string> Attempted { get; } = [];

        public Task<Result<MarketingCampaign?, ErrorList>> FindAsync(MarketingCampaignId id, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Result.Success<MarketingCampaign?, ErrorList>(null));
        }

        public Task<IReadOnlyList<string>> GetCodesAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        public Task<Result<MarketingCampaign, ErrorList>> InsertAsync(MarketingCampaign campaign, CancellationToken cancellationToken)
        {
            Calls++;
            Attempted.Add(campaign.Code);
            return Task.FromResult(Attempted.Count == 1
                ? Result.Failure<MarketingCampaign, ErrorList>(ErrorList.Single(new ModelError.Duplicate(), "code"))
                : Result.Success<MarketingCampaign, ErrorList>(campaign));
        }
    }
}

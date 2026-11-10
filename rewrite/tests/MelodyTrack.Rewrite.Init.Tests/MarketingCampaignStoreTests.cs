using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;
using MelodyTrack.Rewrite.Persistence;
using MelodyTrack.Rewrite.Persistence.Marketing;
using MelodyTrack.Rewrite.Init.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace MelodyTrack.Rewrite.Init.Tests;

public sealed class MarketingCampaignStoreTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Start = new(2026, 11, 8, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public Task InsertAsync_ConcurrentExactCode_AllowsOneFact() => WithStore(async (store, database) =>
    {
        var campaigns = new[] { Campaign("START"), Campaign("start") };

        var results = await Task.WhenAll(campaigns.Select(campaign => store.InsertAsync(campaign, Token)));

        Assert.Single(results, result => result.IsSuccess);
        var failure = Assert.Single(results, result => result.IsFailure);
        Assert.Contains(failure.Error.Issues, issue => issue.Error is ModelError.Duplicate && issue.Field == "code");
        Assert.Equal(1L, await database.Query("SELECT count(*) FROM public.marketing_campaigns").GetSingleAsync<long>(Token));
    });

    [Fact]
    public Task InsertAsync_ConcurrentReplay_ReturnsOnePersistedFact() => WithStore(async (store, database) =>
    {
        var campaign = Campaign("START");

        var results = await Task.WhenAll(store.InsertAsync(campaign, Token), store.InsertAsync(campaign, Token));

        Assert.All(results, result => AssertCampaign(campaign, Ok(result)));
        Assert.Equal(1L, await database.Query("SELECT count(*) FROM public.marketing_campaigns").GetSingleAsync<long>(Token));
    });

    [Fact]
    public Task InsertAsync_StoppedExpiredCampaign_StillReservesCode() => WithStore(async (store, _) =>
    {
        var original = Ok(Campaign("START").Stop());
        Ok(await store.InsertAsync(original, Token));

        var result = await store.InsertAsync(Campaign("START"), Token);

        Assert.True(result.IsFailure);
        Assert.True(Ok(await store.FindAsync(original.Id, Token))!.IsStopped);
        Assert.Equal("START", Assert.Single(await store.GetCodesAsync(Token)));
    });

    [Fact]
    public Task InsertAsync_ReusedIdWithDifferentFact_RejectsEdit() => WithStore(async (store, _) =>
    {
        var original = Campaign("START");
        Ok(await store.InsertAsync(original, Token));
        var edited = Ok(MarketingCampaignRules.Create(original.Id, "OTHER", original.ActivationPeriod,
            original.Discount, new DiscountTerms.Payment(), null));

        var result = await store.InsertAsync(edited, Token);

        Assert.True(result.IsFailure);
        AssertCampaign(original, Ok(await store.FindAsync(original.Id, Token))!);
    });

    [Fact]
    public Task InsertAsync_EquivalentCodes_RejectsDuplicateAndPreservesOriginalRepresentation() => WithStore(async (store, _) =>
    {
        var original = Campaign("StArT");
        Ok(await store.InsertAsync(original, Token));

        var duplicate = await store.InsertAsync(Campaign("start"), Token);

        Assert.True(duplicate.IsFailure);
        Assert.Equal(new[] { "StArT" }, await store.GetCodesAsync(Token));
        Assert.Equal("StArT", Ok(await store.FindAsync(original.Id, Token))!.Code);
    });

    [Fact]
    public Task InsertAsync_ReplayedWithEquivalentCase_ReturnsOriginalRepresentation() => WithStore(async (store, _) =>
    {
        var original = Campaign("StArT");
        Ok(await store.InsertAsync(original, Token));
        var replay = Ok(MarketingCampaignRules.Create(original.Id, "start", original.ActivationPeriod,
            original.Discount, original.Terms, null));

        AssertCampaign(original, Ok(await store.InsertAsync(replay, Token)));
    });

    [Fact]
    public Task FindAsync_MismatchedNormalizedKey_ReturnsControlledFailure() => WithStore(async (store, database) =>
    {
        var original = Campaign("START");
        Ok(await store.InsertAsync(original, Token));
        await database.Query("UPDATE public.marketing_campaigns SET normalized_code = 'OTHER' WHERE id = @id")
            .WithParameter("id", original.Id.Value).ExecuteAsync(Token);

        var result = await store.FindAsync(original.Id, Token);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Error.Issues, issue => issue.Error is ModelError.InvalidState);
    });

    [Theory]
    [InlineData("payment")]
    [InlineData("lessons")]
    [InlineData("period")]
    public Task InsertAsync_HistoricalTerms_RoundTripsOriginalFacts(string kind) => WithStore(async (store, _) =>
    {
        DiscountTerms terms = kind switch
        {
            "payment" => new DiscountTerms.Payment(),
            "lessons" => Ok(DiscountTerms.Lessons.Create(3)),
            "period" => Ok(DiscountTerms.Period.Create(TimeSpan.FromTicks(123456789))),
            _ => throw new InvalidOperationException()
        };
        var original = Ok(MarketingCampaignRules.Restore(new(Guid.NewGuid()), "HISTORY",
            Ok(TimeInterval.Create(Start, Start.AddDays(1))), Ok(Discount.Percentage.Create(12.345m)), terms,
            TimeSpan.FromTicks(987654321)));
        original = Ok(original.Stop());

        Ok(await store.InsertAsync(original, Token));
        var restored = Ok(await store.FindAsync(original.Id, Token));

        AssertCampaign(original, restored!);
        Assert.Equal(terms, restored!.Terms);
    });

    [Fact]
    public Task FindAsync_StoredSubscriptionPurchase_RejectsUnsupportedTermsWithoutChangingData() => WithStore(async (store, database) =>
    {
        var original = Campaign("HISTORY");
        Ok(await store.InsertAsync(original, Token));
        await database.Query("""
            UPDATE public.marketing_campaigns SET terms_kind='purchase', purchase_sizes=ARRAY[4, 8]
            WHERE id=@id
            """).WithParameter("id", original.Id.Value).ExecuteAsync(Token);

        var result = await store.FindAsync(original.Id, Token);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Error.Issues, issue => issue.Error is ModelError.InvalidValue && issue.Field == "terms");
        Assert.Equal("purchase", await database.Query("SELECT terms_kind::text FROM public.marketing_campaigns WHERE id=@id")
            .WithParameter("id", original.Id.Value).GetSingleAsync<string>(Token));
        Assert.Equal(2, await database.Query("SELECT cardinality(purchase_sizes) FROM public.marketing_campaigns WHERE id=@id")
            .WithParameter("id", original.Id.Value).GetSingleAsync<int>(Token));
        Assert.Equal(new[] { "HISTORY" }, await store.GetCodesAsync(Token));
    });

    [Fact]
    public Task CreateWithGeneratedCodeAsync_ConcurrentRequests_PersistsDistinctFreeCodes() => WithStore(async (store, database) =>
    {
        var service = new MarketingCampaignService(store);
        var actor = new Actor.Staff(new(Guid.NewGuid()), StaffRole.Superuser);
        var period = Ok(TimeInterval.Create(Start, Start.AddDays(1)));
        var discount = new Discount.Fixed(Ok(Money.Create(100)));

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.CreateWithGeneratedCodeAsync(
            new(Guid.NewGuid()), period, discount, "AB", actor, Token)));

        Assert.All(results, result => Assert.Equal(5, Ok(result).Code.Length));
        Assert.Equal(8, results.Select(result => result.Value.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(8L, await database.Query("SELECT count(*) FROM public.marketing_campaigns").GetSingleAsync<long>(Token));
    });

    [Fact]
    public Task CreateWithGeneratedCodeAsync_ConcurrentReplay_ReturnsSameCode() => WithStore(async (store, database) =>
    {
        var service = new MarketingCampaignService(store);
        var actor = new Actor.Staff(new(Guid.NewGuid()), StaffRole.Superuser);
        var id = new MarketingCampaignId(Guid.NewGuid());
        var period = Ok(TimeInterval.Create(Start, Start.AddDays(1)));
        var discount = new Discount.Fixed(Ok(Money.Create(100)));

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.CreateWithGeneratedCodeAsync(
            id, period, discount, "AB", actor, Token)));

        Assert.All(results, result => Assert.Equal(results[0].Value.Code, Ok(result).Code));
        Assert.Equal(1L, await database.Query("SELECT count(*) FROM public.marketing_campaigns").GetSingleAsync<long>(Token));
    });

    private async Task WithStore(Func<MarketingCampaignStore, Database, Task> scenario)
    {
        await using var dataSource = NpgsqlDataSource.Create(await fixture.CreateDatabaseAsync(Token));
        var database = new Database(dataSource);
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Schema", "0005_CreateMarketingCampaigns.sql"), Token);
        await database.Query(sql).ExecuteAsync(Token);
        await using (var connection = await dataSource.OpenConnectionAsync(Token))
        await using (var transaction = await connection.BeginTransactionAsync(Token))
        {
            await AddMarketingCodeKeys.RunAsync(new(connection, transaction, Token, NullLogger.Instance));
            await transaction.CommitAsync(Token);
        }
        await scenario(new MarketingCampaignStore(database), database);
    }

    private static MarketingCampaign Campaign(string code) => Ok(MarketingCampaignRules.Create(new(Guid.NewGuid()), code,
        Ok(TimeInterval.Create(Start, Start.AddDays(1))), new Discount.Fixed(Ok(Money.Create(100))), new DiscountTerms.Payment(), null));

    private static void AssertCampaign(MarketingCampaign expected, MarketingCampaign actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Code, actual.Code);
        Assert.Equal(expected.ActivationPeriod, actual.ActivationPeriod);
        Assert.Equal(expected.Discount, actual.Discount);
        Assert.Equal(expected.ReturningClientGap, actual.ReturningClientGap);
        Assert.Equal(expected.IsStopped, actual.IsStopped);
    }

    private static T Ok<T>(Result<T, ErrorList> result)
    {
        Assert.True(result.IsSuccess);
        return result.Value;
    }
}

using MelodyTrack.Rewrite.Domain.Rules;
using MelodyTrack.Rewrite.Init.Migrations;
using MelodyTrack.Rewrite.Persistence;
using MelodyTrack.Rewrite.Persistence.Marketing;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace MelodyTrack.Rewrite.Init.Tests;

public sealed class MarketingCodeMigrationTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public Task RunAsync_RawHistoricalCode_RoundTripsAndReplaysWithoutTrimming() => WithDatabase(async dataSource =>
    {
        var database = new Database(dataSource);
        const string code = "  StArT  ";
        var id = await InsertOriginal(database, code);

        await ApplyMigration(dataSource);
        var store = new MarketingCampaignStore(database);
        var found = await store.FindAsync(new(id), Token);

        Assert.True(found.IsSuccess);
        var original = Assert.IsType<MelodyTrack.Rewrite.Domain.MarketingCampaign>(found.Value);
        Assert.Equal(code, original.Code);
        var replay = await store.InsertAsync(original, Token);
        Assert.True(replay.IsSuccess);
        Assert.Equal(code, replay.Value.Code);
        var row = Assert.Single(await database.Query("SELECT code AS \"Code\", normalized_code AS \"Key\" FROM public.marketing_campaigns")
            .GetAllAsync<CodeRow>(Token));
        Assert.Equal(code, row.Code);
        Assert.Equal(MarketingCodeRules.Normalize(code), row.Key);
    });

    [Fact]
    public Task RunAsync_ExistingDistinctCodes_BackfillsKeysWithoutChangingOriginalCodes() => WithDatabase(async dataSource =>
    {
        var database = new Database(dataSource);
        await InsertOriginal(database, "StArT");
        await InsertOriginal(database, "Проба");

        await ApplyMigration(dataSource);

        var rows = await database.Query("SELECT code AS \"Code\", normalized_code AS \"Key\" FROM public.marketing_campaigns")
            .GetAllAsync<CodeRow>(Token);
        Assert.Contains(rows, row => row.Code == "StArT" && row.Key == MarketingCodeRules.Normalize("StArT"));
        Assert.Contains(rows, row => row.Code == "Проба" && row.Key == MarketingCodeRules.Normalize("Проба"));
    });

    [Theory]
    [InlineData("START", "start")]
    [InlineData("Проба", "ПРОБА")]
    public Task RunAsync_ExistingCaseCollision_RejectsWithoutMergingOrLosingFacts(string first, string second) => WithDatabase(async dataSource =>
    {
        var database = new Database(dataSource);
        await InsertOriginal(database, first);
        await InsertOriginal(database, second);

        var error = await Assert.ThrowsAsync<InvalidMigrationException>(() => ApplyMigration(dataSource));

        Assert.DoesNotContain(first, error.Message);
        Assert.DoesNotContain(second, error.Message);
        var codes = await database.Query("SELECT code FROM public.marketing_campaigns").GetAllAsync<string>(Token);
        Assert.Equal(2, codes.Count);
        Assert.Contains(first, codes);
        Assert.Contains(second, codes);
        Assert.Equal(0, await database.Query("""
            SELECT count(*)::integer FROM information_schema.columns
            WHERE table_schema='public' AND table_name='marketing_campaigns' AND column_name='normalized_code'
            """).GetSingleAsync<int>(Token));
    });

    private static async Task ApplyMigration(NpgsqlDataSource dataSource)
    {
        await using var connection = await dataSource.OpenConnectionAsync(Token);
        await using var transaction = await connection.BeginTransactionAsync(Token);
        await AddMarketingCodeKeys.RunAsync(new(connection, transaction, Token, NullLogger.Instance));
        await transaction.CommitAsync(Token);
    }

    private static Task<Guid> InsertOriginal(Database database, string code) => database.Query("""
        INSERT INTO public.marketing_campaigns(id, code, activation_starts_at, activation_ends_at,
            discount_kind, discount_value, terms_kind, is_stopped)
        VALUES (@id, @code, '2026-11-08T00:00:00Z', '2026-11-09T00:00:00Z', 'fixed', 100, 'payment', false)
        RETURNING id
        """).WithParameter("id", Guid.NewGuid()).WithParameter("code", code).GetSingleAsync<Guid>(Token);

    private async Task WithDatabase(Func<NpgsqlDataSource, Task> scenario)
    {
        await using var dataSource = NpgsqlDataSource.Create(await fixture.CreateDatabaseAsync(Token));
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Schema", "0005_CreateMarketingCampaigns.sql"), Token);
        await new Database(dataSource).Query(sql).ExecuteAsync(Token);
        await scenario(dataSource);
    }

    private sealed class CodeRow
    {
        public string Code { get; init; } = "";
        public string Key { get; init; } = "";
    }
}

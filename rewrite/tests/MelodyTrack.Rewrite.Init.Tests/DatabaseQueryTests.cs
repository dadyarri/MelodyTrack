using System.Data;
using MelodyTrack.Rewrite.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace MelodyTrack.Rewrite.Init.Tests;

public sealed class DatabaseQueryTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    public sealed record Row(int Id, string? Name);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<ServiceProvider> CreateServicesAsync()
    {
        var settings = new NpgsqlConnectionStringBuilder(await fixture.CreateDatabaseAsync(Token))
        {
            Pooling = true,
            MaxPoolSize = 1
        };
        var services = new ServiceCollection();
        services.AddSingleton<NpgsqlDataSource>(_ => NpgsqlDataSource.Create(settings.ConnectionString));
        services.AddDatabase();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task GetAll_FluentParameters_MapsRowsAndMaterializesBeforeConnectionCloses()
    {
        await using var services = await CreateServicesAsync();
        var database = services.GetRequiredService<Database>();
        var query = database.Query("""
            SELECT id AS "Id", CAST(@name AS text) AS "Name"
            FROM generate_series(1, CAST(@count AS integer)) AS id
            ORDER BY id
            """)
            .WithParameter("name", "Пример")
            .WithParameter("count", 2, DbType.Int32);

        var rows = await query.GetAllAsync<Row>(Token);
        var next = await database.Query("SELECT 42").GetSingleAsync<int>(Token);

        Assert.Equal(new[] { new Row(1, "Пример"), new Row(2, "Пример") }, rows);
        Assert.Equal(42, next);
    }

    [Fact]
    public async Task Execute_ParameterizedInsert_PersistsDataWithoutReturningAffectedCount()
    {
        await using var services = await CreateServicesAsync();
        var database = services.GetRequiredService<Database>();
        await database.Query("CREATE TABLE query_probe (id integer PRIMARY KEY, name text NOT NULL)").ExecuteAsync(Token);

        await database.Query("INSERT INTO query_probe (id, name) VALUES (@id, @name)")
            .WithParameter("id", 1)
            .WithParameter("name", "Гитара")
            .ExecuteAsync(Token);

        var row = await database.Query("SELECT id AS \"Id\", name AS \"Name\" FROM query_probe WHERE id = @id")
            .WithParameter("id", 1)
            .GetSingleAsync<Row>(Token);
        Assert.Equal(new Row(1, "Гитара"), row);
    }

    [Fact]
    public async Task GetSingle_OneRow_MapsModel()
    {
        await using var services = await CreateServicesAsync();
        var database = services.GetRequiredService<Database>();

        var row = await database.Query("SELECT 7 AS \"Id\", CAST(@name AS text) AS \"Name\"")
            .WithParameter("name", "Вокал")
            .GetSingleAsync<Row>(Token);

        Assert.Equal(new Row(7, "Вокал"), row);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task GetSingle_UnexpectedCardinality_ThrowsAndReleasesConnection(int count)
    {
        await using var services = await CreateServicesAsync();
        var database = services.GetRequiredService<Database>();
        var query = database.Query("SELECT id FROM generate_series(1, CAST(@count AS integer)) AS id").WithParameter("count", count);

        await Assert.ThrowsAsync<InvalidOperationException>(() => query.GetSingleAsync<int>(Token));

        Assert.Equal(42, await database.Query("SELECT 42").GetSingleAsync<int>(Token));
    }

    [Fact]
    public async Task GetSingleOrDefault_EmptyResult_ReturnsNull()
    {
        await using var services = await CreateServicesAsync();
        var database = services.GetRequiredService<Database>();

        var row = await database.Query("SELECT 1 AS \"Id\", 'unused' AS \"Name\" WHERE false").GetSingleOrDefaultAsync<Row>(Token);

        Assert.Null(row);
    }

    [Fact]
    public async Task GetSingleOrDefault_OneRow_ReturnsModel()
    {
        await using var services = await CreateServicesAsync();
        var database = services.GetRequiredService<Database>();

        var row = await database.Query("SELECT 1 AS \"Id\", 'Гитара' AS \"Name\"").GetSingleOrDefaultAsync<Row>(Token);

        Assert.Equal(new Row(1, "Гитара"), row);
    }

    [Fact]
    public async Task GetSingleOrDefault_NullableScalar_DistinguishesMissingRowFromZero()
    {
        await using var services = await CreateServicesAsync();
        var database = services.GetRequiredService<Database>();

        var missing = await database.Query("SELECT 0 WHERE false").GetSingleOrDefaultAsync<int?>(Token);
        var zero = await database.Query("SELECT 0").GetSingleOrDefaultAsync<int?>(Token);

        Assert.Null(missing);
        Assert.Equal(0, zero);
    }

    [Fact]
    public async Task GetSingleOrDefault_MultipleRows_RejectsAmbiguousResult()
    {
        await using var services = await CreateServicesAsync();
        var database = services.GetRequiredService<Database>();
        var query = database.Query("SELECT id AS \"Id\", 'Гитара' AS \"Name\" FROM generate_series(1, 2) AS id");

        await Assert.ThrowsAsync<InvalidOperationException>(() => query.GetSingleOrDefaultAsync<Row>(Token));
    }

    [Fact]
    public async Task WithParameter_SqlInValue_ReturnsLiteralValue()
    {
        await using var services = await CreateServicesAsync();
        var database = services.GetRequiredService<Database>();
        const string value = "'; SELECT 1; --";

        var actual = await database.Query("SELECT CAST(@value AS text)").WithParameter("value", value).GetSingleAsync<string>(Token);

        Assert.Equal(value, actual);
    }

    [Fact]
    public async Task WithParameter_TypedNull_MapsDatabaseNull()
    {
        await using var services = await CreateServicesAsync();
        var database = services.GetRequiredService<Database>();

        var row = await database.Query("SELECT 1 AS \"Id\", CAST(@name AS text) AS \"Name\"")
            .WithParameter("name", null, DbType.String)
            .GetSingleAsync<Row>(Token);

        Assert.Equal(new Row(1, null), row);
    }

    [Fact]
    public async Task WithParameter_ChangedBranch_DoesNotMutateOriginalQuery()
    {
        await using var services = await CreateServicesAsync();
        var database = services.GetRequiredService<Database>();
        var original = database.Query("SELECT CAST(@value AS integer)").WithParameter("value", 1);
        var changed = original.WithParameter("@value", 2);

        var values = await Task.WhenAll(original.GetSingleAsync<int>(Token), changed.GetSingleAsync<int>(Token), original.GetSingleAsync<int>(Token));

        Assert.Equal(new[] { 1, 2, 1 }, values);
    }

    [Fact]
    public async Task GetSingle_SeparateQueriesOnSingletonDatabase_KeepParametersIndependent()
    {
        await using var services = await CreateServicesAsync();
        var database = services.GetRequiredService<Database>();
        Assert.Same(database, services.GetRequiredService<Database>());

        var first = database.Query("SELECT CAST(@value AS integer)").WithParameter("value", 10);
        var second = database.Query("SELECT CAST(@value AS integer)").WithParameter("value", 20);
        var values = await Task.WhenAll(first.GetSingleAsync<int>(Token), second.GetSingleAsync<int>(Token));

        Assert.Equal(new[] { 10, 20 }, values);
    }

    [Fact]
    public async Task Execute_CancelledToken_DoesNotExecuteSql()
    {
        await using var services = await CreateServicesAsync();
        var database = services.GetRequiredService<Database>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => database.Query("CREATE TABLE cancelled_probe (id integer)").ExecuteAsync(cancellation.Token));

        Assert.True(await database.Query("SELECT to_regclass('public.cancelled_probe') IS NULL").GetSingleAsync<bool>(Token));
    }

    [Fact]
    public async Task GetAll_CancelledToken_PropagatesCancellation()
    {
        await using var services = await CreateServicesAsync();
        var database = services.GetRequiredService<Database>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => database.Query("SELECT 1").GetAllAsync<int>(cancellation.Token));
    }
}

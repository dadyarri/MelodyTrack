using MelodyTrack.Rewrite.Persistence;
using Npgsql;
using Xunit;

namespace MelodyTrack.Rewrite.Init.Tests;

public sealed class DatabaseTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<NpgsqlDataSource> CreateSourceAsync()
    {
        var settings = new NpgsqlConnectionStringBuilder(await fixture.CreateDatabaseAsync(Token))
        {
            Pooling = true,
            MaxPoolSize = 1
        };
        return NpgsqlDataSource.Create(settings.ConnectionString);
    }

    [Fact]
    public async Task GetSingle_ParameterizedQuery_ReturnsValueAndReleasesConnection()
    {
        await using var source = await CreateSourceAsync();
        var database = new Database(source);

        var value = await database.Query("SELECT CAST(@value AS integer)")
            .WithParameter("value", 42)
            .GetSingleAsync<int>(Token);
        var next = await database.Query("SELECT 43").GetSingleAsync<int>(Token);

        Assert.Equal(42, value);
        Assert.Equal(43, next);
    }

    [Fact]
    public async Task GetSingle_SqlError_ReleasesConnectionAndPreservesPostgresError()
    {
        await using var source = await CreateSourceAsync();
        var database = new Database(source);

        var error = await Assert.ThrowsAsync<PostgresException>(() => database.Query("SELECT 1 / 0").GetSingleAsync<int>(Token));

        Assert.Equal(PostgresErrorCodes.DivisionByZero, error.SqlState);
        Assert.Equal(42, await database.Query("SELECT 42").GetSingleAsync<int>(Token));
    }

    [Fact]
    public async Task GetSingle_CancelledToken_PropagatesCancellationAndReleasesConnection()
    {
        await using var source = await CreateSourceAsync();
        var database = new Database(source);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => database.Query("SELECT 42").GetSingleAsync<int>(cancellation.Token));

        Assert.Equal(42, await database.Query("SELECT 42").GetSingleAsync<int>(Token));
    }

    [Fact]
    public async Task BeginTransaction_UncommittedDdl_RollsBackSchemaChanges()
    {
        await using var source = await CreateSourceAsync();
        var database = new Database(source);

        await using (var transaction = await database.BeginTransactionAsync(Token))
        {
            await transaction.Query("CREATE TABLE transaction_probe (value integer)").ExecuteAsync(Token);
        }
        var absent = await database.Query("SELECT to_regclass('public.transaction_probe') IS NULL").GetSingleAsync<bool>(Token);

        Assert.True(absent);
    }
}

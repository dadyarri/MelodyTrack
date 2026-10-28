using MelodyTrack.Rewrite.Persistence;
using Npgsql;
using Xunit;

namespace MelodyTrack.Rewrite.Init.Tests;

public sealed class DatabaseTransactionTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<NpgsqlDataSource> CreateSourceAsync(int maxPoolSize = 1)
    {
        var settings = new NpgsqlConnectionStringBuilder(await fixture.CreateDatabaseAsync(Token))
        {
            Pooling = true,
            MaxPoolSize = maxPoolSize
        };
        return NpgsqlDataSource.Create(settings.ConnectionString);
    }

    private static Task CreateTableAsync(Database database) => database.Query("CREATE TABLE transaction_probe (id integer PRIMARY KEY)").ExecuteAsync(Token);

    [Fact]
    public async Task Commit_MultipleFluentQueries_PersistsAllChanges()
    {
        await using var source = await CreateSourceAsync();
        var database = new Database(source);
        await CreateTableAsync(database);

        await using (var transaction = await database.BeginTransactionAsync(Token))
        {
            var insert = transaction.Query("INSERT INTO transaction_probe (id) VALUES (@id)");
            await insert.WithParameter("id", 1).ExecuteAsync(Token);
            await insert.WithParameter("id", 2).ExecuteAsync(Token);
            var pending = await transaction.Query("SELECT id FROM transaction_probe ORDER BY id").GetAllAsync<int>(Token);
            Assert.Equal(new[] { 1, 2 }, pending);

            await transaction.CommitAsync(Token);
        }

        var committed = await database.Query("SELECT id FROM transaction_probe ORDER BY id").GetAllAsync<int>(Token);
        Assert.Equal(new[] { 1, 2 }, committed);
    }

    [Fact]
    public async Task Dispose_WithoutCommit_RollsBackAllChanges()
    {
        await using var source = await CreateSourceAsync();
        var database = new Database(source);
        await CreateTableAsync(database);

        await using (var transaction = await database.BeginTransactionAsync(Token))
        {
            await transaction.Query("INSERT INTO transaction_probe (id) VALUES (@id)").WithParameter("id", 1).ExecuteAsync(Token);
            await transaction.Query("INSERT INTO transaction_probe (id) VALUES (@id)").WithParameter("id", 2).ExecuteAsync(Token);
        }

        Assert.Empty(await database.Query("SELECT id FROM transaction_probe").GetAllAsync<int>(Token));
    }

    [Fact]
    public async Task Rollback_ExplicitRequest_DiscardsChanges()
    {
        await using var source = await CreateSourceAsync();
        var database = new Database(source);
        await CreateTableAsync(database);

        await using (var transaction = await database.BeginTransactionAsync(Token))
        {
            await transaction.Query("INSERT INTO transaction_probe (id) VALUES (1)").ExecuteAsync(Token);
            await transaction.RollbackAsync(Token);
        }

        Assert.Empty(await database.Query("SELECT id FROM transaction_probe").GetAllAsync<int>(Token));
    }

    [Fact]
    public async Task Dispose_QueryFails_RollsBackAndReleasesConnection()
    {
        await using var source = await CreateSourceAsync();
        var database = new Database(source);
        await CreateTableAsync(database);

        var error = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var transaction = await database.BeginTransactionAsync(Token);
            await transaction.Query("INSERT INTO transaction_probe (id) VALUES (1)").ExecuteAsync(Token);
            await transaction.Query("INSERT INTO transaction_probe (id) VALUES (1)").ExecuteAsync(Token);
        });

        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Empty(await database.Query("SELECT id FROM transaction_probe").GetAllAsync<int>(Token));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetSingle_CompletedTransaction_RejectsPreviouslyBuiltQuery(bool commit)
    {
        await using var source = await CreateSourceAsync();
        var database = new Database(source);
        await using var transaction = await database.BeginTransactionAsync(Token);
        var query = transaction.Query("SELECT CAST(@value AS integer)").WithParameter("value", 42);

        if (commit)
        {
            await transaction.CommitAsync(Token);
        }
        else
        {
            await transaction.RollbackAsync(Token);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => query.GetSingleAsync<int>(Token));
        Assert.Throws<InvalidOperationException>(() => transaction.Query("SELECT 1"));
    }

    [Fact]
    public async Task Dispose_RepeatedCleanup_PreventsReuseAndReleasesConnection()
    {
        await using var source = await CreateSourceAsync();
        var database = new Database(source);
        await using var transaction = await database.BeginTransactionAsync(Token);
        var query = transaction.Query("SELECT 42");

        await transaction.DisposeAsync();
        await transaction.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => query.GetSingleAsync<int>(Token));
        Assert.Throws<ObjectDisposedException>(() => transaction.Query("SELECT 1"));
        Assert.Equal(42, await database.Query("SELECT 42").GetSingleAsync<int>(Token));
    }

    [Fact]
    public async Task BeginTransaction_CancelledToken_DoesNotHoldConnection()
    {
        await using var source = await CreateSourceAsync();
        var database = new Database(source);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => database.BeginTransactionAsync(cancellation.Token));

        Assert.Equal(42, await database.Query("SELECT 42").GetSingleAsync<int>(Token));
    }

    [Fact]
    public async Task Dispose_RequestCancelled_RollsBackWithIndependentCleanup()
    {
        await using var source = await CreateSourceAsync();
        var database = new Database(source);
        await CreateTableAsync(database);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);

        await using (var transaction = await database.BeginTransactionAsync(cancellation.Token))
        {
            await transaction.Query("INSERT INTO transaction_probe (id) VALUES (1)").ExecuteAsync(cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transaction.Query("SELECT id FROM transaction_probe").GetAllAsync<int>(cancellation.Token));
        }

        Assert.Empty(await database.Query("SELECT id FROM transaction_probe").GetAllAsync<int>(Token));
    }

    [Fact]
    public async Task BeginTransaction_IndependentScopes_KeepTheirChangesSeparate()
    {
        await using var source = await CreateSourceAsync(maxPoolSize: 2);
        var database = new Database(source);
        await CreateTableAsync(database);

        await using (var first = await database.BeginTransactionAsync(Token))
        await using (var second = await database.BeginTransactionAsync(Token))
        {
            await first.Query("INSERT INTO transaction_probe (id) VALUES (@id)").WithParameter("id", 1).ExecuteAsync(Token);
            await second.Query("INSERT INTO transaction_probe (id) VALUES (@id)").WithParameter("id", 2).ExecuteAsync(Token);

            await first.CommitAsync(Token);
            await second.RollbackAsync(Token);
        }

        Assert.Equal(new[] { 1 }, await database.Query("SELECT id FROM transaction_probe").GetAllAsync<int>(Token));
    }
}

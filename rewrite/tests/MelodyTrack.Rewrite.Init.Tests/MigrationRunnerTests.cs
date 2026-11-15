using System.Security.Cryptography;
using System.Text;
using MelodyTrack.Rewrite.Init;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace MelodyTrack.Rewrite.Init.Tests;

public sealed class MigrationRunnerTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static Migration Migration(long version, string sql) => new(version, $"{version:D4}_Test.sql",
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql))), context => SqlMigration.RunAsync(sql, context));
    private static Task Apply(NpgsqlConnection connection, Migration migration, CancellationToken? cancellationToken = null) =>
        MigrationRunner.ApplyAsync(connection, migration, NullLogger.Instance, cancellationToken ?? Token);

    private async Task WithDatabase(Func<NpgsqlConnection, Task> test)
    {
        var connectionString = await fixture.CreateDatabaseAsync(Token);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Token);
        await MigrationRunner.PrepareAsync(connection, Token);
        await test(connection);
    }

    private static async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(Token))!;
    }

    [Fact]
    public async Task Prepare_LegacyDatabase_RejectsBeforeCreatingTargetHistory()
    {
        var connectionString = await fixture.CreateDatabaseAsync(Token);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Token);
        await using (var command = new NpgsqlCommand("CREATE TABLE public.\"__EFMigrationsHistory\" (\"MigrationId\" text PRIMARY KEY)", connection))
            await command.ExecuteNonQueryAsync(Token);

        await Assert.ThrowsAsync<InvalidMigrationException>(() => MigrationRunner.PrepareAsync(connection, Token));

        Assert.True(await Scalar<bool>(connection, "SELECT to_regclass('public.schema_migrations') IS NULL"));
        Assert.True(await Scalar<bool>(connection, "SELECT to_regclass('public.\"__EFMigrationsHistory\"') IS NOT NULL"));
    }

    [Fact]
    public Task Apply_ValidSql_CommitsDataAndHistory() => WithDatabase(async connection =>
    {
        var step = Migration(1, "CREATE TABLE probe (value integer); INSERT INTO probe VALUES (42);");
        await Apply(connection, step);
        Assert.Equal(42, await Scalar<int>(connection, "SELECT value FROM probe"));
        await using var command = new NpgsqlCommand("SELECT version, name, checksum FROM public.schema_migrations", connection);
        await using var reader = await command.ExecuteReaderAsync(Token);
        Assert.True(await reader.ReadAsync(Token));
        Assert.Equal(step.Version, reader.GetInt64(0));
        Assert.Equal(step.Name, reader.GetString(1));
        Assert.Equal(step.Checksum, reader.GetString(2));
        Assert.False(await reader.ReadAsync(Token));
    });

    [Fact]
    public Task GetPending_RepeatedRun_DoesNotRepeatSql() => WithDatabase(async connection =>
    {
        Migration[] steps = [Migration(1, "CREATE TABLE probe (value integer); INSERT INTO probe VALUES (42);")];
        foreach (var step in await MigrationHistory.GetPendingAsync(connection, steps, Token)) await Apply(connection, step);
        Assert.Empty(await MigrationHistory.GetPendingAsync(connection, steps, Token));
        Assert.Equal(1L, await Scalar<long>(connection, "SELECT count(*) FROM probe"));
    });

    [Fact]
    public Task Apply_SqlFails_RollsBackDataAndHistory() => WithDatabase(async connection =>
    {
        var step = Migration(1, "CREATE TABLE probe (value integer); SELECT 1 / 0;");
        var error = await Assert.ThrowsAsync<MigrationFailedException>(() => Apply(connection, step));
        Assert.Equal(step.Name, error.MigrationName);
        Assert.Equal(PostgresErrorCodes.DivisionByZero, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        Assert.True(await Scalar<bool>(connection, "SELECT to_regclass('public.probe') IS NULL"));
        Assert.Equal(0L, await Scalar<long>(connection, "SELECT count(*) FROM public.schema_migrations"));
    });

    [Fact]
    public Task Apply_HistoryInsertFails_RollsBackSql() => WithDatabase(async connection =>
    {
        await Apply(connection, Migration(1, "SELECT 1;"));
        await Assert.ThrowsAsync<MigrationFailedException>(() => Apply(connection, Migration(1, "CREATE TABLE probe (value integer);")));
        Assert.True(await Scalar<bool>(connection, "SELECT to_regclass('public.probe') IS NULL"));
        Assert.Equal(1L, await Scalar<long>(connection, "SELECT count(*) FROM public.schema_migrations"));
    });

    [Fact]
    public Task GetPending_MissingAppliedFile_RejectsHistory() => WithDatabase(async connection =>
    {
        await Apply(connection, Migration(1, "SELECT 1;"));
        await Assert.ThrowsAsync<InvalidMigrationException>(() => MigrationHistory.GetPendingAsync(connection, [], Token));
    });

    [Fact]
    public Task GetPending_RenamedAppliedFile_RejectsHistory() => WithDatabase(async connection =>
    {
        var step = Migration(1, "SELECT 1;");
        await Apply(connection, step);
        await Assert.ThrowsAsync<InvalidMigrationException>(() => MigrationHistory.GetPendingAsync(connection, [step with { Name = "0001_Renamed.sql" }], Token));
    });

    [Fact]
    public Task GetPending_ChangedAppliedSql_RejectsHistory() => WithDatabase(async connection =>
    {
        await Apply(connection, Migration(1, "SELECT 1;"));
        await Assert.ThrowsAsync<InvalidMigrationException>(() => MigrationHistory.GetPendingAsync(connection, [Migration(1, "SELECT 2;")], Token));
    });

    [Fact]
    public Task GetPending_NewEarlierVersion_RejectsMigration() => WithDatabase(async connection =>
    {
        var latest = Migration(3, "SELECT 3;");
        await Apply(connection, latest);
        await Assert.ThrowsAsync<InvalidMigrationException>(() => MigrationHistory.GetPendingAsync(connection, [Migration(1, "SELECT 1;"), latest], Token));
    });

    [Fact]
    public Task Prepare_AnotherSessionHoldsLock_RejectsConcurrentInit() => WithDatabase(async connection =>
    {
        await using var competing = new NpgsqlConnection(fixture.ConnectionString(connection.Database));
        await competing.OpenAsync(Token);
        await Assert.ThrowsAsync<MigrationLockUnavailableException>(() => MigrationRunner.PrepareAsync(competing, Token));
    });

    [Fact]
    public Task Prepare_PreviousSessionClosed_ReleasesLock() => WithDatabase(async connection =>
    {
        await using var competing = new NpgsqlConnection(fixture.ConnectionString(connection.Database));
        await competing.OpenAsync(Token);
        await connection.CloseAsync();
        await MigrationRunner.PrepareAsync(competing, Token);
    });

    [Fact]
    public Task Apply_CancelledToken_DoesNotRecordMigration() => WithDatabase(async connection =>
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Apply(connection, Migration(1, "SELECT 1;"), cancelled.Token));
        Assert.Equal(0L, await Scalar<long>(connection, "SELECT count(*) FROM public.schema_migrations"));
    });

    [Fact]
    public Task Apply_GeneratedSqlAndCSharp_RunInSharedOrder() => WithDatabase(async connection =>
    {
        var steps = Fixtures.GeneratedMigrations.All;
        foreach (var step in await MigrationHistory.GetPendingAsync(connection, steps, Token)) await Apply(connection, step);
        await using (var command = new NpgsqlCommand("SELECT value FROM generated_probe ORDER BY id", connection))
        await using (var reader = await command.ExecuteReaderAsync(Token))
        {
            Assert.True(await reader.ReadAsync(Token));
            Assert.Equal("SQL \"quoted\" \\ путь", reader.GetString(0));
            Assert.True(await reader.ReadAsync(Token));
            Assert.Equal("C# через внешний модуль", reader.GetString(0));
            Assert.False(await reader.ReadAsync(Token));
        }
        Assert.Empty(await MigrationHistory.GetPendingAsync(connection, steps, Token));
    });

    [Fact]
    public Task Apply_ProductionMigrationChain_InstallsNameSimilarityExtensionThroughRunner() => WithDatabase(async connection =>
    {
        var steps = GeneratedMigrations.All;
        foreach (var step in await MigrationHistory.GetPendingAsync(connection, steps, Token)) await Apply(connection, step);

        Assert.Equal(1, await Scalar<int>(connection, "SELECT levenshtein('synthetic', 'synthetics')"));
        Assert.Equal(1L, await Scalar<long>(connection, "SELECT count(*) FROM public.schema_migrations WHERE version=15 AND name='0015_EnableClientNameSimilarity.sql'"));
        Assert.True(await Scalar<bool>(connection, "SELECT public.client_name_fragment_matches('богданова полина', 'багд')"));
        Assert.False(await Scalar<bool>(connection, "SELECT public.client_name_fragment_matches('богданова полина', 'xxxxанова')"));
        Assert.Equal(1L, await Scalar<long>(connection, "SELECT count(*) FROM public.schema_migrations WHERE version=17 AND name='0017_AddClientNameFragmentSearch.sql'"));
        Assert.Empty(await MigrationHistory.GetPendingAsync(connection, steps, Token));
    });

    [Fact]
    public Task Apply_CSharpHandlerFails_RollsBackChanges() => WithDatabase(async connection =>
    {
        var step = Migration(1, "unused") with
        {
            Name = "0001_FailingHandler.cs",
            RunAsync = async context =>
            {
                await SqlMigration.RunAsync("CREATE TABLE probe (value integer);", context);
                throw new InvalidOperationException("Test migration failed.");
            }
        };
        await Assert.ThrowsAsync<MigrationFailedException>(() => Apply(connection, step));
        Assert.True(await Scalar<bool>(connection, "SELECT to_regclass('public.probe') IS NULL"));
        Assert.Equal(0L, await Scalar<long>(connection, "SELECT count(*) FROM public.schema_migrations"));
    });

    [Fact]
    public Task Apply_CancelledAfterSql_RollsBackChanges() => WithDatabase(async connection =>
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var step = Migration(1, "unused") with
        {
            Name = "0001_CancelledHandler.cs",
            RunAsync = async context =>
            {
                await SqlMigration.RunAsync("CREATE TABLE probe (value integer);", context);
                entered.SetResult();
                await Task.Delay(Timeout.Infinite, context.CancellationToken);
            }
        };
        var execution = Apply(connection, step, cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
            Assert.True(await Scalar<bool>(connection, "SELECT to_regclass('public.probe') IS NULL"));
            Assert.Equal(0L, await Scalar<long>(connection, "SELECT count(*) FROM public.schema_migrations"));
        }
        finally
        {
            cancellation.Cancel();
            try { await execution; }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }
    });
}

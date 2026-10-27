namespace MelodyTrack.Rewrite.Init.Tests

open System
open System.Security.Cryptography
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging.Abstractions
open MelodyTrack.Rewrite.Init
open Npgsql
open Xunit

type MigrationRunnerTests(fixture: PostgreSqlFixture) =
    let token () = TestContext.Current.CancellationToken

    let migration (version: int64) sql =
        { Version = version
          Name = $"{version:D4}_Test.sql"
          Run = SqlMigration.run sql
          Checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql))) }

    let apply connection step cancellationToken =
        MigrationRunner.apply connection step NullLogger.Instance cancellationToken

    let withDatabase (test: NpgsqlConnection -> Task<unit>) =
        task {
            let! connectionString = fixture.CreateDatabase(token ())
            use connection = new NpgsqlConnection(connectionString)
            do! connection.OpenAsync(token ())
            do! MigrationRunner.prepare connection (token ())
            do! test connection
        }

    let scalar (connection: NpgsqlConnection) sql =
        task {
            use command = new NpgsqlCommand(sql, connection)
            return! command.ExecuteScalarAsync(token ())
        }

    [<Fact>]
    member _.Apply_ValidSql_CommitsDataAndHistory() =
        withDatabase (fun connection -> task {
            let step = migration 1L "CREATE TABLE probe (value integer); INSERT INTO probe VALUES (42);"

            do! apply connection step (token ())

            let! value = scalar connection "SELECT value FROM probe"
            Assert.Equal(42, unbox<int> value)
            use command = new NpgsqlCommand("SELECT version, name, checksum FROM public.schema_migrations", connection)
            use! reader = command.ExecuteReaderAsync(token ())
            let! hasRow = reader.ReadAsync(token ())
            Assert.True(hasRow)
            Assert.Equal(step.Version, reader.GetInt64(0))
            Assert.Equal(step.Name, reader.GetString(1))
            Assert.Equal(step.Checksum, reader.GetString(2))
            let! hasMore = reader.ReadAsync(token ())
            Assert.False(hasMore)
        })

    [<Fact>]
    member _.GetPending_RepeatedRun_DoesNotRepeatSql() =
        withDatabase (fun connection -> task {
            let steps = [| migration 1L "CREATE TABLE probe (value integer); INSERT INTO probe VALUES (42);" |]
            let! first = MigrationHistory.getPending connection steps (token ())
            for step in first do
                do! apply connection step (token ())

            let! second = MigrationHistory.getPending connection steps (token ())

            Assert.Empty(second)
            let! count = scalar connection "SELECT count(*) FROM probe"
            Assert.Equal(1L, unbox<int64> count)
        })

    [<Fact>]
    member _.Apply_SqlFails_RollsBackDataAndHistory() =
        withDatabase (fun connection -> task {
            let step = migration 1L "CREATE TABLE probe (value integer); SELECT 1 / 0;"

            let! error = Assert.ThrowsAsync<MigrationFailedException>(fun () -> apply connection step (token ()) :> Task)

            Assert.Equal(step.Name, error.MigrationName)
            let postgres = Assert.IsType<PostgresException>(error.InnerException)
            Assert.Equal(PostgresErrorCodes.DivisionByZero, postgres.SqlState)
            let! absent = scalar connection "SELECT to_regclass('public.probe') IS NULL"
            Assert.True(unbox<bool> absent)
            let! count = scalar connection "SELECT count(*) FROM public.schema_migrations"
            Assert.Equal(0L, unbox<int64> count)
        })

    [<Fact>]
    member _.Apply_HistoryInsertFails_RollsBackSql() =
        withDatabase (fun connection -> task {
            do! apply connection (migration 1L "SELECT 1;") (token ())
            let duplicate = migration 1L "CREATE TABLE probe (value integer);"

            let! _ = Assert.ThrowsAsync<MigrationFailedException>(fun () -> apply connection duplicate (token ()) :> Task)

            let! absent = scalar connection "SELECT to_regclass('public.probe') IS NULL"
            Assert.True(unbox<bool> absent)
            let! count = scalar connection "SELECT count(*) FROM public.schema_migrations"
            Assert.Equal(1L, unbox<int64> count)
        })

    [<Fact>]
    member _.GetPending_MissingAppliedFile_RejectsHistory() =
        withDatabase (fun connection -> task {
            do! apply connection (migration 1L "SELECT 1;") (token ())

            let! _ = Assert.ThrowsAsync<InvalidMigration>(fun () -> MigrationHistory.getPending connection [||] (token ()) :> Task)
            return ()
        })

    [<Fact>]
    member _.GetPending_RenamedAppliedFile_RejectsHistory() =
        withDatabase (fun connection -> task {
            let step = migration 1L "SELECT 1;"
            do! apply connection step (token ())
            let renamed = { step with Name = "0001_Renamed.sql" }

            let! _ = Assert.ThrowsAsync<InvalidMigration>(fun () -> MigrationHistory.getPending connection [| renamed |] (token ()) :> Task)
            return ()
        })

    [<Fact>]
    member _.GetPending_ChangedAppliedSql_RejectsHistory() =
        withDatabase (fun connection -> task {
            do! apply connection (migration 1L "SELECT 1;") (token ())

            let! _ = Assert.ThrowsAsync<InvalidMigration>(fun () -> MigrationHistory.getPending connection [| migration 1L "SELECT 2;" |] (token ()) :> Task)
            return ()
        })

    [<Fact>]
    member _.GetPending_NewEarlierVersion_RejectsMigration() =
        withDatabase (fun connection -> task {
            let latest = migration 3L "SELECT 3;"
            do! apply connection latest (token ())

            let! _ = Assert.ThrowsAsync<InvalidMigration>(fun () -> MigrationHistory.getPending connection [| migration 1L "SELECT 1;"; latest |] (token ()) :> Task)
            return ()
        })

    [<Fact>]
    member _.Prepare_AnotherSessionHoldsLock_RejectsConcurrentInit() =
        withDatabase (fun connection -> task {
            use competing = new NpgsqlConnection(fixture.ConnectionString(connection.Database))
            do! competing.OpenAsync(token ())

            let! _ = Assert.ThrowsAsync<MigrationLockUnavailable>(fun () -> MigrationRunner.prepare competing (token ()) :> Task)
            return ()
        })

    [<Fact>]
    member _.Prepare_PreviousSessionClosed_ReleasesLock() =
        withDatabase (fun connection -> task {
            use competing = new NpgsqlConnection(fixture.ConnectionString(connection.Database))
            do! competing.OpenAsync(token ())
            do! connection.CloseAsync()

            do! MigrationRunner.prepare competing (token ())
        })

    [<Fact>]
    member _.Apply_CancelledToken_DoesNotRecordMigration() =
        withDatabase (fun connection -> task {
            use cancelled = new CancellationTokenSource()
            cancelled.Cancel()

            let! _ = Assert.ThrowsAnyAsync<OperationCanceledException>(fun () -> apply connection (migration 1L "SELECT 1;") cancelled.Token :> Task)

            let! count = scalar connection "SELECT count(*) FROM public.schema_migrations"
            Assert.Equal(0L, unbox<int64> count)
        })

    [<Fact>]
    member _.Apply_GeneratedSqlAndFSharp_RunInSharedOrder() =
        withDatabase (fun connection -> task {
            let steps = Fixtures.GeneratedMigrations.all
            let! pending = MigrationHistory.getPending connection steps (token ())

            for step in pending do
                do! apply connection step (token ())

            use command = new NpgsqlCommand("SELECT value FROM generated_probe ORDER BY id", connection)
            use! reader = command.ExecuteReaderAsync(token ())
            let! first = reader.ReadAsync(token ())
            Assert.True(first)
            Assert.Equal("SQL \"quoted\" \\ путь", reader.GetString(0))
            let! second = reader.ReadAsync(token ())
            Assert.True(second)
            Assert.Equal("F# через внешний модуль", reader.GetString(0))
            let! more = reader.ReadAsync(token ())
            Assert.False(more)
            do! reader.DisposeAsync()
            let! again = MigrationHistory.getPending connection steps (token ())
            Assert.Empty(again)
        })

    [<Fact>]
    member _.Apply_FSharpHandlerFails_RollsBackChanges() =
        withDatabase (fun connection -> task {
            let step =
                { migration 1L "unused" with
                    Name = "0001_FailingHandler.fs"
                    Run = fun context -> task {
                        do! SqlMigration.run "CREATE TABLE probe (value integer);" context
                        failwith "Test migration failed."
                    } }

            let! _ = Assert.ThrowsAsync<MigrationFailedException>(fun () -> apply connection step (token ()) :> Task)

            let! absent = scalar connection "SELECT to_regclass('public.probe') IS NULL"
            Assert.True(unbox<bool> absent)
            let! count = scalar connection "SELECT count(*) FROM public.schema_migrations"
            Assert.Equal(0L, unbox<int64> count)
        })

    [<Fact>]
    member _.Apply_CancelledAfterSql_RollsBackChanges() =
        withDatabase (fun connection -> task {
            use cancellation = CancellationTokenSource.CreateLinkedTokenSource(token ())
            let entered = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
            let step =
                { migration 1L "unused" with
                    Name = "0001_CancelledHandler.fs"
                    Run = fun context -> task {
                        do! SqlMigration.run "CREATE TABLE probe (value integer);" context
                        entered.SetResult()
                        do! Task.Delay(Timeout.Infinite, context.CancellationToken)
                    } }

            let execution = apply connection step cancellation.Token
            try
                do! entered.Task.WaitAsync(TimeSpan.FromSeconds(10.0), token ())
                cancellation.Cancel()
                let! _ = Assert.ThrowsAnyAsync<OperationCanceledException>(fun () -> execution :> Task)

                let! absent = scalar connection "SELECT to_regclass('public.probe') IS NULL"
                Assert.True(unbox<bool> absent)
                let! count = scalar connection "SELECT count(*) FROM public.schema_migrations"
                Assert.Equal(0L, unbox<int64> count)
            finally
                cancellation.Cancel()
        })

    interface IClassFixture<PostgreSqlFixture>

namespace MelodyTrack.Rewrite.Init.Tests

open System
open System.Threading
open System.Threading.Tasks
open Npgsql
open Testcontainers.PostgreSql
open Xunit

type PostgreSqlFixture() =
    let container =
        new PostgreSqlBuilder("postgres:16-alpine@sha256:4e6e670bb069649261c9c18031f0aded7bb249a5b6664ddec29c013a89310d50")
        |> fun builder -> builder.Build()

    member _.ConnectionString(database: string) =
        let settings = NpgsqlConnectionStringBuilder(container.GetConnectionString())
        settings.Database <- database
        settings.Pooling <- false
        settings.Multiplexing <- false
        settings.ConnectionString

    member this.CreateDatabase(cancellationToken: CancellationToken) =
        task {
            let name = "migrations_" + Guid.NewGuid().ToString("N")
            use connection = new NpgsqlConnection(container.GetConnectionString())
            do! connection.OpenAsync(cancellationToken)
            use command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection)
            let! _ = command.ExecuteNonQueryAsync(cancellationToken)
            return this.ConnectionString(name)
        }

    interface IAsyncLifetime with
        member _.InitializeAsync() = ValueTask(container.StartAsync(TestContext.Current.CancellationToken))

    interface IAsyncDisposable with
        member _.DisposeAsync() = container.DisposeAsync()

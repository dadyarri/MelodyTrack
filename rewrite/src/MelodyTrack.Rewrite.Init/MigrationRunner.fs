namespace MelodyTrack.Rewrite.Init

open System
open System.Threading
open Npgsql

exception MigrationLockUnavailable

type MigrationFailedException(migrationName: string, innerException: exn) =
    inherit Exception($"Не удалось применить миграцию {migrationName}.", innerException)
    member _.MigrationName = migrationName

module MigrationRunner =
    // Stable session lock key: ASCII "MTRACKIN". All Init versions must use the same key.
    let private lockKey = 0x4D545241434B494EL

    let prepare (connection: NpgsqlConnection) (cancellationToken: CancellationToken) =
        task {
            use lockCommand = new NpgsqlCommand("SELECT pg_try_advisory_lock($1)", connection)
            lockCommand.Parameters.AddWithValue(lockKey) |> ignore
            let! acquired = lockCommand.ExecuteScalarAsync(cancellationToken)

            if not (unbox<bool> acquired) then
                raise MigrationLockUnavailable

            use historyCommand =
                new NpgsqlCommand(
                    """
                    CREATE TABLE IF NOT EXISTS public.schema_migrations (
                        version bigint PRIMARY KEY CHECK (version > 0),
                        name text NOT NULL,
                        checksum text NOT NULL CHECK (checksum ~ '^[0-9A-F]{64}$'),
                        applied_at timestamptz NOT NULL DEFAULT clock_timestamp()
                    );
                    """,
                    connection
                )

            let! _ = historyCommand.ExecuteNonQueryAsync(cancellationToken)
            return ()
        }

    let apply (connection: NpgsqlConnection) (migration: Migration) (cancellationToken: CancellationToken) =
        task {
            try
                use! transaction = connection.BeginTransactionAsync(cancellationToken)
                use sqlCommand = new NpgsqlCommand(migration.Sql, connection, transaction)
                let! _ = sqlCommand.ExecuteNonQueryAsync(cancellationToken)

                use historyCommand =
                    new NpgsqlCommand(
                        "INSERT INTO public.schema_migrations (version, name, checksum) VALUES ($1, $2, $3)",
                        connection,
                        transaction
                    )

                historyCommand.Parameters.AddWithValue(migration.Version) |> ignore
                historyCommand.Parameters.AddWithValue(migration.Name) |> ignore
                historyCommand.Parameters.AddWithValue(migration.Checksum) |> ignore
                let! _ = historyCommand.ExecuteNonQueryAsync(cancellationToken)
                do! transaction.CommitAsync(cancellationToken)
            with
            | :? OperationCanceledException as error -> return raise error
            | error -> return raise (MigrationFailedException(migration.Name, error))
        }

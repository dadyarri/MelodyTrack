namespace MelodyTrack.Rewrite.Init

open System.Threading
open Npgsql

exception MigrationLockUnavailable

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

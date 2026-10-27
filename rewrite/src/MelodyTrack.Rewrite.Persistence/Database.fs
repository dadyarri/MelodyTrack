namespace MelodyTrack.Rewrite.Persistence

open System.Threading
open Npgsql

module Database =
    let checkConnection (connection: NpgsqlConnection) (cancellationToken: CancellationToken) =
        task {
            use command = new NpgsqlCommand("SELECT 1", connection)
            let! _ = command.ExecuteScalarAsync(cancellationToken)
            return ()
        }

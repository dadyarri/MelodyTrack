namespace MelodyTrack.Rewrite.Init

open System.Threading.Tasks
open Npgsql

type Migration =
    { Version: int64
      Name: string
      Checksum: string
      Run: MigrationContext -> Task<unit> }

exception InvalidMigration of string

module SqlMigration =
    let run (sql: string) (context: MigrationContext) =
        task {
            use command = new NpgsqlCommand(sql, context.Connection, context.Transaction)
            let! _ = command.ExecuteNonQueryAsync(context.CancellationToken)
            return ()
        }

namespace MelodyTrack.Rewrite.Init

open System.Threading
open Microsoft.Extensions.Logging
open Npgsql

type MigrationContext =
    { Connection: NpgsqlConnection
      Transaction: NpgsqlTransaction
      CancellationToken: CancellationToken
      Logger: ILogger }

namespace MelodyTrack.Rewrite.Init

open System.Threading
open Npgsql

module MigrationHistory =
    type private AppliedMigration =
        { Version: int64
          Name: string
          Checksum: string }

    let private validate (migrations: Migration array) (applied: AppliedMigration array) =
        let byVersion = migrations |> Array.map (fun migration -> migration.Version, migration) |> Map.ofArray

        for previous in applied do
            match Map.tryFind previous.Version byVersion with
            | None ->
                raise (InvalidMigration $"Не найден файл применённой миграции {previous.Name}.")
            | Some current when current.Name <> previous.Name ->
                raise (InvalidMigration $"Применённая миграция {previous.Name} переименована в {current.Name}.")
            | Some current when current.Checksum <> previous.Checksum ->
                raise (InvalidMigration $"Изменено содержимое применённой миграции {previous.Name}.")
            | Some _ -> ()

        let appliedVersions = applied |> Array.map (fun migration -> migration.Version) |> Set.ofArray
        let pending = migrations |> Array.filter (fun migration -> not (Set.contains migration.Version appliedVersions))
        let latestApplied = applied |> Array.tryLast |> Option.map (fun migration -> migration.Version) |> Option.defaultValue 0L

        match pending |> Array.tryFind (fun migration -> migration.Version < latestApplied) with
        | Some migration ->
            raise (InvalidMigration $"Новая миграция {migration.Name} имеет номер ниже уже применённого {latestApplied}.")
        | None -> pending

    let getPending (connection: NpgsqlConnection) (migrations: Migration array) (cancellationToken: CancellationToken) =
        task {
            use command =
                new NpgsqlCommand(
                    "SELECT version, name, checksum FROM public.schema_migrations ORDER BY version",
                    connection
                )

            use! reader = command.ExecuteReaderAsync(cancellationToken)
            let applied = ResizeArray<AppliedMigration>()
            let mutable hasMore = true

            while hasMore do
                let! hasRow = reader.ReadAsync(cancellationToken)
                hasMore <- hasRow

                if hasRow then
                    applied.Add(
                        { Version = reader.GetInt64(0)
                          Name = reader.GetString(1)
                          Checksum = reader.GetString(2) }
                    )

            return validate migrations (applied.ToArray())
        }

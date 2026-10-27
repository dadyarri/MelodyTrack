namespace MelodyTrack.Rewrite.Init

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.RegularExpressions

type Migration =
    { Version: int64
      Name: string
      Sql: string
      Checksum: string }

exception InvalidMigration of string

module Migration =
    let private read (path: string) =
        let name = Path.GetFileName(path)
        let matched = Regex.Match(name, @"\A(?<version>[0-9]{4})_[A-Z][A-Za-z0-9]*\.sql\z")

        if not matched.Success then
            raise (InvalidMigration $"Неверное имя миграции {name}: ожидается 0001_CreateServices.sql.")

        let version = Int64.Parse(matched.Groups["version"].Value)

        if version = 0L then
            raise (InvalidMigration $"Номер миграции {name} должен быть больше нуля.")

        let bytes = File.ReadAllBytes(path)

        let sql =
            try
                UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF')
            with :? DecoderFallbackException ->
                raise (InvalidMigration $"Миграция {name} должна быть в кодировке UTF-8.")

        if String.IsNullOrWhiteSpace(sql) then
            raise (InvalidMigration $"Миграция {name} пуста.")

        { Version = version
          Name = name
          Sql = sql
          Checksum = Convert.ToHexString(SHA256.HashData(bytes)) }

    let load (directory: string) =
        if not (Directory.Exists(directory)) then
            raise (InvalidMigration "Не найден каталог SQL-миграций Migrations рядом с Init.")

        let migrations =
            Directory.GetFiles(directory, "*.sql", SearchOption.TopDirectoryOnly)
            |> Array.map read
            |> Array.sortBy (fun migration -> migration.Version)

        match migrations |> Array.pairwise |> Array.tryFind (fun (left, right) -> left.Version = right.Version) with
        | Some(left, right) ->
            raise (InvalidMigration $"Одинаковый номер миграций: {left.Name} и {right.Name}.")
        | None -> migrations

namespace MelodyTrack.Rewrite.Init.Tests

open System
open System.IO
open System.Text
open MelodyTrack.Rewrite.Init
open Xunit

type MigrationTests() =
    let directory = Directory.CreateTempSubdirectory("melodytrack-migrations-")

    let write (name: string) (sql: string) =
        File.WriteAllText(Path.Combine(directory.FullName, name), sql, UTF8Encoding(false))

    [<Fact>]
    member _.Load_FilesCreatedOutOfOrder_ReturnsVersionOrder() =
        write "0003_CreateServices.sql" "SELECT 3;"
        write "0001_CreateClients.sql" "SELECT 1;"

        let migrations = Migration.load directory.FullName

        Assert.Equal<int64>([| 1L; 3L |], migrations |> Array.map (fun migration -> migration.Version))
        Assert.Equal<string>(
            [| "0001_CreateClients.sql"; "0003_CreateServices.sql" |],
            migrations |> Array.map (fun migration -> migration.Name)
        )

    [<Fact>]
    member _.Load_DuplicateVersions_RejectsMigrations() =
        write "0001_CreateClients.sql" "SELECT 1;"
        write "0001_CreateServices.sql" "SELECT 2;"

        Assert.Throws<InvalidMigration>(fun () -> Migration.load directory.FullName |> ignore) |> ignore

    [<Fact>]
    member _.Load_Utf8BomAndCrLf_HashesOriginalBytes() =
        let bytes = Array.append [| 0xEFuy; 0xBBuy; 0xBFuy |] (Encoding.UTF8.GetBytes("SELECT 1;\r\n"))
        File.WriteAllBytes(Path.Combine(directory.FullName, "0001_CreateClients.sql"), bytes)

        let migration = Assert.Single<Migration>(Migration.load directory.FullName)

        Assert.Equal("SELECT 1;\r\n", migration.Sql)
        Assert.Equal("1FFC3C6ACDBB610BCC942C606C787270E7C9D149C473E9D9AAA4FE597D5E402D", migration.Checksum)

    interface IDisposable with
        member _.Dispose() = directory.Delete(true)

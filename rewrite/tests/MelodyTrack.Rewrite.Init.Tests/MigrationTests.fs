namespace MelodyTrack.Rewrite.Init.Tests

open System
open System.IO
open System.Text
open MelodyTrack.Rewrite.Build.MigrationGeneration
open Xunit

type MigrationTests() =
    let directory = Directory.CreateTempSubdirectory("melodytrack-migrations-")

    let write (name: string) (sql: string) =
        File.WriteAllText(Path.Combine(directory.FullName, name), sql, UTF8Encoding(false))

    [<Fact>]
    member _.Load_FilesCreatedOutOfOrder_ReturnsVersionOrder() =
        write "0003_CreateServices.sql" "SELECT 3;"
        write "0001_CreateClients.sql" "SELECT 1;"

        let migrations = load directory.FullName

        Assert.Equal<int64>([| 1L; 3L |], migrations |> Array.map (fun migration -> migration.Version))
        Assert.Equal<string>(
            [| "0001_CreateClients.sql"; "0003_CreateServices.sql" |],
            migrations |> Array.map (fun migration -> migration.Name)
        )

    [<Fact>]
    member _.Load_DuplicateVersions_RejectsMigrations() =
        write "0001_CreateClients.sql" "SELECT 1;"
        write "0001_CreateServices.sql" "SELECT 2;"

        Assert.Throws<InvalidMigrationSource>(fun () -> load directory.FullName |> ignore) |> ignore

    [<Fact>]
    member _.Load_Utf8BomAndCrLf_HashesOriginalBytes() =
        let bytes = Array.append [| 0xEFuy; 0xBBuy; 0xBFuy |] (Encoding.UTF8.GetBytes("SELECT 1;\r\n"))
        File.WriteAllBytes(Path.Combine(directory.FullName, "0001_CreateClients.sql"), bytes)

        let migration = Assert.Single<Source>(load directory.FullName)

        match migration.Body with
        | Sql sql -> Assert.Equal("SELECT 1;\r\n", sql)
        | FSharp _ -> Assert.Fail("Ожидался SQL-исходник.")
        Assert.Equal("1FFC3C6ACDBB610BCC942C606C787270E7C9D149C473E9D9AAA4FE597D5E402D", migration.Checksum)

    [<Theory>]
    [<InlineData("CreateClients.sql")>]
    [<InlineData("0000_CreateClients.sql")>]
    [<InlineData("0001_createClients.sql")>]
    [<InlineData("0001_Create-Clients.sql")>]
    [<InlineData("00001_CreateClients.sql")>]
    member _.Load_InvalidName_RejectsMigration(name: string) =
        write name "SELECT 1;"

        Assert.Throws<InvalidMigrationSource>(fun () -> load directory.FullName |> ignore) |> ignore

    [<Theory>]
    [<InlineData("")>]
    [<InlineData(" \r\n\t")>]
    member _.Load_EmptySql_RejectsMigration(sql: string) =
        write "0001_CreateClients.sql" sql

        Assert.Throws<InvalidMigrationSource>(fun () -> load directory.FullName |> ignore) |> ignore

    [<Fact>]
    member _.Load_InvalidUtf8_RejectsMigration() =
        File.WriteAllBytes(Path.Combine(directory.FullName, "0001_CreateClients.sql"), [| 0xC3uy; 0x28uy |])

        Assert.Throws<InvalidMigrationSource>(fun () -> load directory.FullName |> ignore) |> ignore

    [<Fact>]
    member _.Load_MissingDirectory_RejectsLoad() =
        let missing = Path.Combine(directory.FullName, "missing")

        Assert.Throws<InvalidMigrationSource>(fun () -> load missing |> ignore) |> ignore

    [<Fact>]
    member _.Load_EmptyDirectory_ReturnsNoMigrations() =
        Assert.Empty(load directory.FullName)

    [<Fact>]
    member _.Load_MixedKinds_ReturnsSharedVersionOrder() =
        write "0002_UpdateClients.fs" "module UpdateClients"
        write "0001_CreateClients.sql" "SELECT 1;"

        let sources = load directory.FullName

        Assert.Equal<int64>([| 1L; 2L |], sources |> Array.map (fun source -> source.Version))
        match sources[1].Body with
        | FSharp name -> Assert.Equal("UpdateClients", name)
        | Sql _ -> Assert.Fail("Ожидался F#-исходник.")

    [<Fact>]
    member _.Load_MixedKindsWithSameVersion_RejectsMigrations() =
        write "0001_CreateClients.sql" "SELECT 1;"
        write "0001_UpdateClients.fs" "module UpdateClients"

        Assert.Throws<InvalidMigrationSource>(fun () -> load directory.FullName |> ignore) |> ignore

    [<Fact>]
    member _.Generate_SourceRemoved_RemovesRegistryEntry() =
        write "0001_CreateClients.sql" "SELECT 1;"
        let output = Path.Combine(directory.FullName, "obj", "GeneratedMigrations.fs")
        generate directory.FullName output "Fixtures"
        Assert.Contains("0001_CreateClients.sql", File.ReadAllText(output))
        File.Delete(Path.Combine(directory.FullName, "0001_CreateClients.sql"))

        generate directory.FullName output "Fixtures"

        Assert.DoesNotContain("0001_CreateClients.sql", File.ReadAllText(output))

    [<Fact>]
    member _.Generate_FSharpSourceRemoved_RemovesCompilerInput() =
        write "0001_UpdateClients.fs" "module UpdateClients"
        let output = Path.Combine(directory.FullName, "obj", "GeneratedMigrations.fs")
        generate directory.FullName output "Fixtures"
        Assert.Contains("0001_UpdateClients.fs", File.ReadAllText(output + ".sources"))
        File.Delete(Path.Combine(directory.FullName, "0001_UpdateClients.fs"))

        generate directory.FullName output "Fixtures"

        Assert.Empty(File.ReadAllText(output + ".sources"))
        Assert.DoesNotContain("Migrations.UpdateClients.run", File.ReadAllText(output))

    interface IDisposable with
        member _.Dispose() = directory.Delete(true)

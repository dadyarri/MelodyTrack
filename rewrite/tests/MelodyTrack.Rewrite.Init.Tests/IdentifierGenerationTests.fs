namespace MelodyTrack.Rewrite.Init.Tests

open System
open System.IO
open System.Text
open MelodyTrack.Rewrite.Build.IdentifierGeneration
open MelodyTrack.Rewrite.Domain
open MelodyTrack.Rewrite.Domain.Scheduling
open Xunit

type IdentifierGenerationTests() =
    let directory = Directory.CreateTempSubdirectory("melodytrack-identifiers-")

    let write (name: string) (source: string) =
        let path = Path.Combine(directory.FullName, name)
        File.WriteAllText(path, source, UTF8Encoding(false))
        path

    [<Theory>]
    [<InlineData("GenerateIdentifier(typeof<Guid>)")>]
    [<InlineData("GenerateIdentifierAttribute(typeof<System.Guid>)")>]
    [<InlineData("MelodyTrack.Rewrite.Domain.GenerateIdentifier(typeof<Guid>)")>]
    member _.Load_AnnotatedModel_GeneratesMatchingIdentifier(attribute: string) =
        let path = write "Models.fs" $"namespace Example\n[<{attribute}>]\ntype Lesson = {{ Id: LessonId }}\n"

        let identifier = Assert.Single<Identifier>(load [| path |])

        Assert.Equal("Example", identifier.Namespace)
        Assert.Equal("LessonId", identifier.Name)

    [<Fact>]
    member _.Load_AttributeTextInCommentsAndStrings_DoesNotGenerateIdentifiers() =
        let path =
            write "Models.fs" """namespace Example
// [<GenerateIdentifier(typeof<Guid>)>]
type Plain = { Value: string }
module Text =
    let example = "[<GenerateIdentifier(typeof<Guid>)>] type Fake = { Id: FakeId }"
"""

        let identifiers = load [| path |]

        Assert.Empty(identifiers)

    [<Fact>]
    member _.Load_SameModelNameInDifferentNamespaces_KeepsDistinctIdentifiers() =
        let path =
            write "Models.fs" """namespace First
[<GenerateIdentifier(typeof<Guid>)>]
type Lesson = { Id: LessonId }
namespace Second
[<GenerateIdentifier(typeof<Guid>)>]
type Lesson = { Id: LessonId }
"""

        let identifiers = load [| path |]

        Assert.Equal<string>([| "First"; "Second" |], identifiers |> Array.map (fun identifier -> identifier.Namespace))

    [<Theory>]
    [<InlineData("int")>]
    [<InlineData("string")>]
    member _.Load_UnsupportedUnderlyingType_RejectsModel(typeName: string) =
        let path = write "Models.fs" $"namespace Example\n[<GenerateIdentifier(typeof<{typeName}>)>]\ntype Lesson = {{ Value: int }}\n"

        Assert.Throws<InvalidIdentifierSource>(fun () -> load [| path |] |> ignore) |> ignore

    [<Fact>]
    member _.Load_HandwrittenIdentifier_RejectsCollision() =
        let path =
            write "Models.fs" """namespace Example
type LessonId = LessonId of System.Guid
[<GenerateIdentifier(typeof<Guid>)>]
type Lesson = { Id: LessonId }
"""

        Assert.Throws<InvalidIdentifierSource>(fun () -> load [| path |] |> ignore) |> ignore

    [<Fact>]
    member _.Load_IdentifierInsideUnrelatedModule_DoesNotCollideWithGeneratedType() =
        let path =
            write "Models.fs" """namespace Example
module Other =
    type LessonId = LessonId of System.Guid
[<GenerateIdentifier(typeof<Guid>)>]
type Lesson = { Id: LessonId }
"""

        let identifier = Assert.Single<Identifier>(load [| path |])

        Assert.Equal("Example", identifier.Namespace)
        Assert.Equal("LessonId", identifier.Name)

    [<Fact>]
    member _.Load_DuplicateModelsAcrossFiles_RejectsCollision() =
        let source = "namespace Example\n[<GenerateIdentifier(typeof<Guid>)>]\ntype Lesson = { Id: LessonId }\n"
        let first = write "First.fs" source
        let second = write "Second.fs" source

        Assert.Throws<InvalidIdentifierSource>(fun () -> load [| first; second |] |> ignore) |> ignore

    [<Fact>]
    member _.Load_AnnotatedNestedModel_RejectsUnsupportedScope() =
        let path =
            write "Models.fs" """namespace Example
module Models =
    [<GenerateIdentifier(typeof<Guid>)>]
    type Lesson = { Id: LessonId }
"""

        Assert.Throws<InvalidIdentifierSource>(fun () -> load [| path |] |> ignore) |> ignore

    [<Fact>]
    member _.Load_AnnotatedGenericModel_RejectsModel() =
        let path = write "Models.fs" "namespace Example\n[<GenerateIdentifier(typeof<Guid>)>]\ntype Lesson<'T> = { Value: 'T }\n"

        Assert.Throws<InvalidIdentifierSource>(fun () -> load [| path |] |> ignore) |> ignore

    [<Fact>]
    member _.Load_InvalidSyntax_RejectsSource() =
        let path = write "Models.fs" "namespace Example\n[<GenerateIdentifier(typeof<Guid>)>]\ntype Lesson = {\n"

        Assert.Throws<InvalidIdentifierSource>(fun () -> load [| path |] |> ignore) |> ignore

    [<Fact>]
    member _.Generate_AttributeRemoved_RemovesGeneratedIdentifier() =
        let path = write "Models.fs" "namespace Example\n[<GenerateIdentifier(typeof<Guid>)>]\ntype Lesson = { Value: int }\n"
        let output = Path.Combine(directory.FullName, "obj", "GeneratedIdentifiers.fs")
        generate [| path |] output
        Assert.Contains("type LessonId", File.ReadAllText(output))
        write "Models.fs" "namespace Example\ntype Lesson = { Value: int }\n" |> ignore

        generate [| path |] output

        Assert.DoesNotContain("LessonId", File.ReadAllText(output))

    [<Fact>]
    member _.Generate_UnchangedSource_PreservesOutputTimestamp() =
        let path = write "Models.fs" "namespace Example\n[<GenerateIdentifier(typeof<Guid>)>]\ntype Lesson = { Value: int }\n"
        let output = Path.Combine(directory.FullName, "GeneratedIdentifiers.fs")
        generate [| path |] output
        File.SetLastWriteTimeUtc(output, DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc))
        let timestamp = File.GetLastWriteTimeUtc(output)

        generate [| path |] output

        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(output))

    [<Fact>]
    member _.Create_EmptyGuid_ReturnsError() =
        let result = SeriesId.create Guid.Empty

        Assert.Equal<Result<SeriesId, IdentifierError>>(Error IdentifierError.Empty, result)

    [<Fact>]
    member _.Create_NonEmptyGuid_RoundTripsThroughGeneratedIdentifier() =
        let value = Guid.Parse("019a2c63-8c00-7000-8000-000000000001")

        let result = SeriesId.create value |> Result.map SeriesId.value

        Assert.Equal<Result<Guid, IdentifierError>>(Ok value, result)

    interface IDisposable with
        member _.Dispose() = directory.Delete(true)

module MelodyTrack.Rewrite.Build.IdentifierGeneration

open System.IO
open System.Text
open System.Text.RegularExpressions
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Diagnostics
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text

type Identifier =
    { Namespace: string
      Name: string }

type private Declaration =
    { Namespace: string
      Name: string
      Generate: bool }

exception InvalidIdentifierSource of string

let private checker = lazy (FSharpChecker.Create())

let private longName (identifiers: Ident list) =
    identifiers |> List.map (fun identifier -> identifier.idText) |> String.concat "."

let private isMarker (attribute: SynAttribute) =
    let (SynLongIdent(identifiers, _, _)) = attribute.TypeName

    match longName identifiers with
    | "GenerateIdentifier"
    | "GenerateIdentifierAttribute"
    | "MelodyTrack.Rewrite.Domain.GenerateIdentifier"
    | "MelodyTrack.Rewrite.Domain.GenerateIdentifierAttribute" -> true
    | _ -> false

let rec private isGuidArgument expression =
    match expression with
    | SynExpr.Paren(expr = inner) -> isGuidArgument inner
    | SynExpr.TypeApp(expr = SynExpr.Ident name; typeArgs = [ SynType.LongIdent typeName ]) when name.idText = "typeof" ->
        let (SynLongIdent(identifiers, _, _)) = typeName
        let name = longName identifiers
        name = "Guid" || name = "System.Guid"
    | _ -> false

let private read (options: FSharpParsingOptions) (path: string) =
    let source = File.ReadAllText(path, UTF8Encoding(false, true)) |> SourceText.ofString
    let parsed = checker.Value.ParseFile(path, source, options) |> Async.RunSynchronously

    match parsed.Diagnostics |> Array.tryFind (fun diagnostic -> diagnostic.Severity = FSharpDiagnosticSeverity.Error) with
    | Some diagnostic ->
        raise (InvalidIdentifierSource $"Не удалось разобрать {path}:{diagnostic.StartLine}: {diagnostic.Message}")
    | None -> ()

    let rec declarations namespaceName inModule entries =
        entries
        |> List.collect (fun entry ->
            match entry with
            | SynModuleDecl.Types(typeDefns = types) ->
                types
                |> List.choose (fun (SynTypeDefn(typeInfo = info)) ->
                    let (SynComponentInfo(attributes = attributes; typeParams = typeParams; longId = name)) = info
                    let name = longName name
                    let markers = attributes |> List.collect (fun group -> group.Attributes) |> List.filter isMarker

                    match markers with
                    | [] -> ()
                    | [ marker ] ->
                        if inModule then
                            raise (InvalidIdentifierSource $"Модель {name} должна находиться непосредственно в пространстве имён.")

                        if typeParams.IsSome then
                            raise (InvalidIdentifierSource $"Генерация идентификатора для generic-модели {name} не поддерживается.")

                        if not (Regex.IsMatch(name, @"\A[A-Z][A-Za-z0-9]*\z")) then
                            raise (InvalidIdentifierSource $"Имя модели {name} должно использовать PascalCase латинскими буквами и цифрами.")

                        if not (isGuidArgument marker.ArgExpr) then
                            raise (InvalidIdentifierSource $"Модель {name}: ожидается GenerateIdentifier(typeof<Guid>); другие типы пока не поддерживаются.")
                    | _ -> raise (InvalidIdentifierSource $"Атрибут GenerateIdentifier указан у модели {name} несколько раз.")

                    if inModule then None
                    else
                        Some
                            { Namespace = namespaceName
                              Name = name
                              Generate = not (List.isEmpty markers) })
            | SynModuleDecl.NestedModule(decls = nested) -> declarations namespaceName true nested
            | SynModuleDecl.NamespaceFragment fragment -> fragmentDeclarations fragment
            | _ -> [])

    and fragmentDeclarations (SynModuleOrNamespace(longId = name; kind = kind; decls = entries)) =
        let inModule =
            match kind with
            | SynModuleOrNamespaceKind.DeclaredNamespace
            | SynModuleOrNamespaceKind.GlobalNamespace -> false
            | _ -> true

        declarations (longName name) inModule entries

    match parsed.ParseTree with
    | ParsedInput.ImplFile(ParsedImplFileInput(contents = contents)) -> contents |> List.collect fragmentDeclarations
    | _ -> raise (InvalidIdentifierSource $"Ожидается файл реализации F#: {path}.")

let load (sourcePaths: string array) =
    let options = { FSharpParsingOptions.Default with SourceFiles = sourcePaths }
    let declarations = sourcePaths |> Array.collect (read options >> List.toArray)
    let declaredNames = declarations |> Array.map (fun entry -> entry.Namespace, entry.Name) |> Set.ofArray

    let identifiers: Identifier array =
        declarations
        |> Array.filter (fun entry -> entry.Generate)
        |> Array.map (fun entry ->
            let name = entry.Name + "Id"

            if Set.contains (entry.Namespace, name) declaredNames then
                raise (InvalidIdentifierSource $"Тип {entry.Namespace}.{name} уже объявлен вручную.")

            { Namespace = entry.Namespace; Name = name })

    match identifiers |> Array.countBy (fun identifier -> identifier.Namespace, identifier.Name) |> Array.tryFind (fun (_, count) -> count > 1) with
    | Some((namespaceName, name), _) ->
        raise (InvalidIdentifierSource $"Идентификатор {namespaceName}.{name} генерируется несколько раз.")
    | None -> identifiers

let render (identifiers: Identifier array) =
    let namespaces =
        identifiers
        |> Array.groupBy (fun identifier -> identifier.Namespace)
        |> Array.map (fun (namespaceName, entries) ->
            let declarations =
                entries
                |> Array.map (fun identifier ->
                    let name = identifier.Name

                    String.concat "\n"
                        [ "[<Struct>]"
                          $"type {name} = private {name} of System.Guid"
                          ""
                          "[<RequireQualifiedAccess>]"
                          $"module {name} ="
                          "    let create (value: System.Guid) ="
                          "        if value = System.Guid.Empty then"
                          "            Error MelodyTrack.Rewrite.Domain.IdentifierError.Empty"
                          "        else"
                          $"            Ok({name} value)"
                          ""
                          $"    let value ({name} value) = value" ])

            let namespaceDeclaration =
                if namespaceName = "" then "namespace global" else $"namespace {namespaceName}"

            namespaceDeclaration + "\n\n" + String.concat "\n\n" declarations)

    let body =
        if Array.isEmpty namespaces then "namespace MelodyTrack.Rewrite.GeneratedIdentifiers"
        else String.concat "\n\n" namespaces

    "// <auto-generated/>\n" + body + "\n"

let generate sourcePaths output =
    let content = load sourcePaths |> render
    let outputDirectory = Path.GetDirectoryName(Path.GetFullPath(output)) |> nonNull
    Directory.CreateDirectory(outputDirectory) |> ignore

    if not (File.Exists(output)) || File.ReadAllText(output) <> content then
        File.WriteAllText(output, content, UTF8Encoding(false))
